# DefaultDrawingBackend

This document describes the CPU-based rasterization and composition pipeline implemented by `DefaultDrawingBackend`.

## Overview

`DefaultDrawingBackend` is a singleton (`DefaultDrawingBackend.Instance`) implementing `IDrawingBackend`. It performs all path rasterization, brush application, and pixel compositing on the CPU using fixed-point scanline math with band-based parallelism.

## End-to-End Flow

```text
DrawingCanvasBatcher.FlushCompositions()
  -> IDrawingBackend.FlushCompositions(configuration, target, scene)
     -> target.TryGetCpuRegion(out region)
     -> CompositionScenePlanner.CreatePreparedBatches(commands, targetBounds)
        -> clip each command to target bounds
        -> group contiguous commands by DefinitionKey
        -> keep prepared destination/source offsets
     -> for each CompositionBatch:
        -> FlushPreparedBatch(configuration, region, batch)
           -> create BrushApplicator[] for all commands in batch
           -> create RowOperation (scanline callback)
           -> if batch.Definition.IsStroke:
              -> DefaultRasterizer.RasterizeStrokeRows(definition, rowHandler, allocator)
           -> else:
              -> DefaultRasterizer.RasterizeRows(definition, rowHandler, allocator)
           -> dispose applicators
```

## Scene Planning (CompositionScenePlanner)

`CompositionScenePlanner.CreatePreparedBatches()` transforms raw `CompositionCommand` lists into `CompositionBatch` groups:

1. **Clip** each command's destination to target bounds; discard commands with zero-area overlap.
2. **Group** contiguous commands sharing the same `DefinitionKey` (path identity + rasterizer options hash) into a single batch. Commands with different definitions break the batch.
3. **Prepare** each command with clipped `DestinationRegion` and `SourceOffset` mapping rasterized coverage to the clipped region.

For stroke batches, after dash expansion grows the interest rect, `ReprepareBatchCommands()` re-clips all commands to the updated bounds.

## DefaultRasterizer

The rasterizer is a 3300-line fixed-point scanline engine in `DefaultRasterizer.cs`.

### Fixed-Point Representation

```
Format:     24.8 (8 fractional bits)
One:        256
Sub-pixels: 256 steps per pixel
Area scale: 512 (area-to-coverage shift = 9)
```

### Edge Table Construction

Input paths are flattened to line segments. Each segment is converted to fixed-point `EdgeData` (x0, y0, x1, y1) with:
- Vertical Liang-Barsky clipping to the band bounds
- Horizontal edges filtered out (fy0 == fy1)
- Y-ordering enforced (swap if y0 > y1)

### Execution Modes

```text
IF tileCount == 1 OR edgeCount <= 64
   -> RasterizeSingleTileDirect (no parallelism overhead)
ELSE IF ProcessorCount >= 2
   -> Parallel.For across tiles (band-sorted edges)
ELSE
   -> Sequential band loop with reusable scratch
```

**Parallel:** `MaxDegreeOfParallelism = min(12, ProcessorCount, tileCount)`. Each worker gets an isolated `WorkerScratch` instance. No shared mutable state during tile processing.

**Sequential:** Single reusable `WorkerScratch`, band loop with context reset between bands.

### Band-Based Processing

Tile height: 16 pixels. Each band processes only edges that intersect its Y range. Edges are duplicated across all touched bands during band-sort.

### Per-Band Scan Conversion (Context)

The `Context` ref struct holds per-band working memory:

| Buffer | Purpose |
|---|---|
| `bitVectors[]` | Sparse touched-column tracking (nuint[] per row) |
| `coverArea[]` | Signed area accumulation (2 ints per pixel: delta + area) |
| `startCover[]` | Carry cover from edges left of the band's X origin |
| `rowMinTouchedColumn[]` | Left bound per row for sparse iteration |
| `rowMaxTouchedColumn[]` | Right bound per row for sparse iteration |
| `rowHasBits[]`, `rowTouched[]` | Touch flags for sparse reset |
| `touchedRows[]` | Touched row indices for cleanup |
| `scanline[]` | Output coverage buffer (float per pixel) |

### Edge Rasterization

Bresenham-style line algorithm. For each cell an edge crosses:
- Register signed delta at cell entry X
- Register -delta at cell exit X
- Update area accumulation based on cell-fraction coverage
- Track touched rows and columns via bit vectors

### Row Coverage Emission

For each touched row:
1. Iterate only set bits in the touched-column bit vector (sparse).
2. Accumulate winding number from `startCover` + running `coverArea` deltas.
3. Apply fill rule:
   - **NonZero:** Clamp |winding| to [0, 1]
   - **EvenOdd:** `(winding & mask) > CoverageStepCount ? 0 : 1`
4. Apply antialiasing mode:
   - **Antialiased:** Coverage = area / 512, clamped to [0, 1]
   - **Aliased:** Coverage > threshold → 1.0, else 0.0
5. Coalesce consecutive cells with equal coverage into spans.
6. Emit span via `RasterizerCoverageRowHandler` callback.

### Memory Budget

```
Band memory budget: 64 MB
Bytes per row = (wordsPerRow × pointer_size) + (coverStride × 4) + 4
Max band rows = min(height, 64MB / bytesPerRow)
```

### Sparse Reset

After emitting all rows in a band, only touched rows are cleared—not the full scratch buffer. This avoids full-buffer clears when geometry is sparse relative to the interest rectangle.

## Stroke Processing

### Stroke Expansion

For each stroke definition, the rasterizer performs per-band parallel expansion:

1. **Centerline collection:** Flatten path into contours with open/closed tracking.
2. **Dash splitting (optional):** If the definition has a dash pattern, `SplitPathExtensions.GenerateDashes()` segments the centerline into open dash sub-paths.
3. **Stroke edge descriptors:** Each contour segment produces:
   - **Side edges** (Flags=None): Left/right offset by halfWidth along the perpendicular
   - **Join edges** (Flags=Join): Vertex + adjacent vertex for miter/round/bevel computation
   - **Cap edges** (CapStart/CapEnd): Endpoint + direction for butt/square/round caps

### Join Expansion

| Join Type | Algorithm |
|---|---|
| Miter | Intersection of offset lines; revert to bevel if miterDist > miterLimit × halfWidth |
| MiterRound | Blend bevel→miter at limit distance |
| Round | Circular arc subdivided at ~0.5 × halfWidth step density |
| Bevel | Straight diagonal between offset endpoints |

### Cap Expansion

| Cap Type | Algorithm |
|---|---|
| Butt | Single perpendicular edge at endpoint |
| Square | Rectangle extending halfWidth beyond endpoint |
| Round | Semicircular arc subdivided at ~0.5 × halfWidth |

### Band Sorting

`TryBuildBandSortedStrokeEdges()` duplicates stroke edges into all touched bands. Expansion Y bounds include halfWidth × max(miterLimit, 1). Each band expands and rasterizes independently.

## Brush Application

After rasterization emits a coverage scanline, `RowOperation.InvokeCoverageRow()` applies brushes:

```text
for each command overlapping this scanline row:
  -> clamp coverage region to command DestinationRegion
  -> compute destination pixel coordinates
  -> BrushApplicator[i].Apply(coverage.Slice(...), destX, destY)
```

Each `BrushApplicator<TPixel>` (abstract base in `BrushApplicator.cs`):
1. Samples brush color at destination coordinates
2. Multiplies by coverage
3. Composites into destination via `PixelBlender`

Concrete applicator types: Solid, LinearGradient, RadialGradient, EllipticGradient, SweepGradient, Pattern, Image, Recolor.

## ComposeLayer (CPU Path)

```text
IDrawingBackend.ComposeLayer(source, destination, offset, options)
  -> extract CPU regions from source and destination frames
  -> clamp compositing region to both bounds
  -> allocate float[] amounts filled with BlendPercentage
  -> for each row y in intersection:
     -> srcRow = sourceRegion[y].Slice(startX, width)
     -> dstRow = destRegion[dstY].Slice(dstX, width)
     -> PixelBlender.Blend(config, dstRow, dstRow, srcRow, amounts)
```

The `PixelBlender` implements the full Porter-Duff alpha composition with configurable color blend mode and per-pixel blend percentage (layer opacity).

## TryReadRegion

```text
IDrawingBackend.TryReadRegion(target, sourceRect, out image)
  -> target.TryGetCpuRegion(out region)
  -> create Image<TPixel> from region sub-rectangle
  -> copy pixel rows
```

Used by `DrawingCanvas.Process()` to read back pixels for image processing operations.

## Composition Pipeline

The full pixel composition formula (generalized Porter-Duff):

```
Cₒᵤₜ = αₛ × BlendMode(Cₛ, Cₐ) + αₐ × Cₐ × (1 - αₛ)
αₒᵤₜ = αₛ + αₐ × (1 - αₛ)
```

Where:
- αₛ = source alpha (brush alpha × coverage × blend percentage)
- αₐ = destination alpha
- Cₛ = source color (from brush)
- Cₐ = destination color (backdrop)
- BlendMode = color blend operation (Normal, Multiply, Screen, Overlay, etc.)

## Threading Model

| Condition | Path |
|---|---|
| 1 tile OR ≤64 edges | Single-tile direct (no overhead) |
| ProcessorCount ≥ 2, multiple tiles | Parallel.For, MaxDOP = min(12, cores, tiles) |
| Single core | Sequential band loop |

Worker isolation: Each parallel worker owns its own `WorkerScratch`. No synchronization during tile processing. Coverage emission callbacks are inherently thread-safe because each tile covers disjoint pixel rows.

## Key Data Structures

| Type | Purpose |
|---|---|
| `CompositionCommand` | Normalized draw instruction (path + brush + options) |
| `CompositionBatch` | Commands grouped by shared coverage definition |
| `PreparedCompositionCommand` | Command clipped to target bounds with source offset |
| `CompositionCoverageDefinition` | Stable identity for coverage reuse (path + rasterizer state) |
| `RasterizerOptions` | Interest rect, fill rule, rasterization mode, sampling origin, antialias threshold |
| `WorkerScratch` | Per-worker rasterization memory (bit vectors, area buffers, scanline) |

## Memory Management

- `WorkerScratch` allocated via `MemoryAllocator` with `IMemoryOwner<T>` for pool return.
- Sequential path reuses a single `WorkerScratch` across batches if dimensions are compatible.
- Parallel path creates fresh worker-local scratch per `Parallel.For` iteration (discarded after).
- `BrushApplicator` instances are disposed after each batch flush.
- 64 MB band memory budget caps per-band allocation.
