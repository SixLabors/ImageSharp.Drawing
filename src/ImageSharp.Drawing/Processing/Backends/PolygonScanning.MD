# DefaultRasterizer (Fixed-Point, Tiled + Banded Fallback)

This document describes the current `DefaultRasterizer` implementation in
`src/ImageSharp.Drawing/Shapes/Rasterization/DefaultRasterizer.cs`.

The scanner is a fixed-point, area/cover rasterizer inspired by Blaze-style
scan conversion.

https://github.com/aurimasg/blaze (MIT-Licensed)

## Goals

- Robustly rasterize arbitrary tessellated polygon rings (including self intersections).
- Support `EvenOdd` and `NonZero` fill rules.
- Keep temporary memory bounded for large targets.
- Emit coverage spans efficiently for blending.

## High-Level Pipeline

### Fill Path (`RasterizeRows`)

```
IPath
  |
  v
TessellatedMultipolygon.Create(...)
  |
  v
Choose execution mode:
  |
  +--> Parallel row-tiles (default rasterizer path)
  |      |
  |      +--> Build edge table once (global local-space edges)
  |      +--> Assign edges to tile rows
  |      +--> Rasterize each tile in parallel using worker-local scratch
  |      +--> Emit tile outputs in deterministic top-to-bottom order
  |
  +--> Sequential band loop (scanline baseline + fallback)
         |
         +--> Build edge table once (shared with parallel path)
         +--> Assign edges to sequential bands
         +--> Reuse worker scratch across bands
         +--> Rasterize band-local edge subsets into cover/area accumulators
         +--> Convert accumulators to coverage scanlines
         +--> Invoke rasterizer callback per dirty row
```

### Stroke Path (`RasterizeStrokeRows`)

Stroke rasterization fuses stroke expansion with coverage rasterization so that
each parallel band only expands the centerline edges that overlap it. This avoids
the cost of a serial full-path `GenerateOutline()` call and eliminates the
intermediate `IPath` allocation for the expanded outline.

For dashed strokes, `SplitPathExtensions` splits the centerline into dash segments
on the CPU before passing the result through the same per-band stroke expansion
pipeline.

```
IPath (centerline)
  |
  +--> [if dashed] SplitPathExtensions.GenerateDashes(path, strokeWidth, pattern)
  |
  v
path.Flatten() -> List<ISimplePath> (preserving open/closed state)
  |
  v
BuildStrokeEdgeTable(contours) -> StrokeEdgeData[]
  |  For each contour:
  |    - Closed: N side edges + N join descriptors = 2N descriptors
  |    - Open:   (N-1) side edges + (N-2) joins + 2 caps = 2N-1 descriptors
  |  Each descriptor carries StrokeEdgeFlags (None/Join/CapStart/CapEnd)
  |
  v
TryBuildBandSortedStrokeEdges(edges, expansion)
  |  Band-sort with Y expansion = halfWidth * max(miterLimit, 1)
  |  to ensure join/cap geometry reaches all overlapping bands
  |
  v
Choose execution mode:
  |
  +--> Parallel row-tiles
  |      |
  |      +--> Per tile: ExpandStrokeEdges -> EmitOutlineEdge -> RasterizeLine
  |      +--> EmitCoverageRows -> ordered emit via output buffer
  |
  +--> Sequential band loop
         |
         +--> Per band: ExpandStrokeEdges -> EmitOutlineEdge -> RasterizeLine
         +--> EmitCoverageRows -> direct callback
```

#### Stroke Edge Expansion (`ExpandStrokeEdges`)

Each `StrokeEdgeData` descriptor is expanded into outline edges based on its
`StrokeEdgeFlags`, mirroring the GPU `StrokeExpandComputeShader`:

| Flag | Expansion | Outline edges |
|------|-----------|---------------|
| `None` (side) | Two edges offset by stroke normal | 2 |
| `Join` | Inner bevel + outer join (miter/round/bevel) | 2-N (round scales with width) |
| `CapStart`/`CapEnd` | Cap geometry (butt/square/round) | 1-N (round scales with width) |

Outline edges are converted from float to 24.8 fixed-point, clipped to band
bounds, and fed directly to `RasterizeLine` — no intermediate edge buffer.

## Coordinate System and Precision

- Geometry is transformed to scanner-local coordinates:
  - `xLocal = (x - interest.Left) + samplingOffsetX`
  - `yLocal = (y - interest.Top) + samplingOffsetY` (global local-space edge table)
  - Per tile/band pass uses `yLocal - currentBandTop`
- Scanner math uses signed 24.8 fixed point:
  - `FixedShift = 8`
  - `FixedOne = 256`
- Coverage is normalized to `[0..1]` with 256 steps:
  - `CoverageStepCount = 256`
  - `CoverageScale = 1 / 256`

This means 1 fixed unit in Y equals 1/256 pixel row resolution.

## Memory Model and Banded Scratch

The scanner bounds scratch memory with a per-band budget:

- `BandMemoryBudgetBytes = 64 MB`
- Rows per band are computed from per-row byte cost.

Per-row temporary storage:

```
bitVectors: wordsPerRow * sizeof(nuint)
coverArea : (width * 2) * sizeof(int)
startCover: 1 * sizeof(int)
```

Scratch buffers are reused per band/tile worker:

```
bitVectors  : [bandHeight][wordsPerRow]   // bitset marks touched columns
coverArea   : [bandHeight][width * 2]     // per x: [deltaCover, deltaArea]
startCover  : [bandHeight]                // carry-in cover at x=0
rowHasBits  : [bandHeight]                // fast "row touched" flag
scanline    : [width] float               // output coverage row
```

If width/height are too large for safe indexing math, rasterization throws
`ImageProcessingException`.

Parallel mode additionally buffers per-tile output coverage before ordered emit.
This path is capped by `ParallelOutputPixelBudget` to avoid pathological output
buffer growth.

## Edge Rasterization Stage

For each tessellated ring edge `(p0 -> p1)` during edge-table build:

1. Translate to local coordinates.
2. Reject non-finite coordinates.
3. Clip vertically to scanner bounds.
4. Record edge row range for tile assignment.

During tile/band rasterization:

1. Clip edge to current tile/band vertical bounds.
2. Convert endpoints to 24.8 fixed.
3. Skip horizontal edges (`fy0 == fy1`).
4. Route to directional line walkers (`LineDownR`, `LineUpL`, etc.).

The walkers decompose edges into affected cells and call:

- `Cell(...)` for general segments
- `CellVertical(...)` for vertical segments

Both end up in `AddCell(row, column, deltaCover, deltaArea)`.

`AddCell` updates:

- `coverArea[row, column * 2 + 0] += deltaCover`
- `coverArea[row, column * 2 + 1] += deltaArea`
- bit in `bitVectors[row]` for `column`
- `rowHasBits[row] = 1`

If `column < 0`, the contribution is folded into `startCover[row]` so coverage
to the left of the interest rectangle still influences pixels at `x >= 0`.

## Scanline Emission Stage

For each row in the current band:

1. Skip quickly if `startCover[row] == 0` and `rowHasBits[row] == 0`.
2. Iterate set bits in the row bitset (`TrailingZeroCount` walk).
3. Reconstruct area/cover state at each touched `x`.
4. Convert signed accumulated area to coverage via fill rule.
5. Coalesce equal coverage into spans.
6. Fill `scanline[start..end]` for each non-zero span.
7. Invoke callback for dirty rows only.

Core conversion:

```
area = coverArea[deltaArea] + (cover << 9)
```

`cover` is updated incrementally by `deltaCover`.

## Fill Rule Handling

### NonZero

```
absArea = abs(signedArea)
coverage = min(absArea, 256) / 256
```

### EvenOdd

```
wrapped = absArea & 511
if wrapped > 256: wrapped = 512 - wrapped
coverage = min(wrapped, 256) / 256
```

This is done in `AreaToCoverage(int area)`.

### Aliased Thresholding

When `RasterizationMode` is `Aliased`, the continuous coverage value computed
above is snapped to binary using `AntialiasThreshold`:

```
if coverage >= antialiasThreshold:
    coverage = 1.0
else:
    coverage = 0.0
```

Lower threshold values (e.g. 0.1) preserve more edge pixels and thin features;
higher values (e.g. 0.9) produce a tighter, more conservative fill. The default
threshold is 0.5.

## Why This Handles Self Intersections

The scanner does not require geometric boolean normalization first.
Overlaps are resolved by accumulated area/cover integration and final fill-rule
mapping (`EvenOdd` or `NonZero`), so winding/parity behavior is decided at
rasterization time.

## Fast Paths and Practical Optimizations

- One tessellation build per rasterization call.
- Parallel path builds a single edge table and reuses it across tiles.
- Worker-local scratch reuse avoids per-tile scratch allocations.
- Sequential path reuses band buffers across the full Y range.
- `rowHasBits` avoids scanning all words in empty rows.
- Bitset iteration visits only touched columns.
- Span coalescing reduces per-pixel operations before blending.

## Notes on Public Options

- `RasterizerOptions.RasterizationMode` controls whether scanner output is:
  - `Antialiased`: continuous coverage in `[0, 1]`
  - `Aliased`: binary coverage (`0` or `1`), thresholded in the scanner using `AntialiasThreshold`
- `RasterizerOptions.AntialiasThreshold` (0–1, default 0.5): the coverage cutoff
  used in `Aliased` mode. Pixels with coverage at or above this value become fully
  opaque; pixels below are discarded. Ignored in `Antialiased` mode.
- `RasterizerSamplingOrigin` affects both X and Y sample alignment (`PixelBoundary` vs `PixelCenter`).

## Data Flow Diagram (Row-Level)

```
             per-edge writes
                   |
                   v
        +----------------------+
        | coverArea[row][x,*]  |  deltaCover + deltaArea
        +----------------------+
                   |
                   +--> bitVectors[row] set bit x
                   |
                   +--> rowHasBits[row] = 1
                   |
                   +--> startCover[row] (for x < 0 contributions)

Then during emit:

bitVectors[row] -> touched x list -> accumulate cover/area -> coverage spans
                                                      |
                                                      v
                                                scanline[width]
                                                      |
                                                      v
                                               Rasterizer callback
```

## Failure Modes and Diagnostics

- Exception: interest too large for bounded scratch/output buffers or indexing.
- Symptoms like missing fill are usually from invalid input geometry (NaN/Inf) or
  ring construction upstream; scanner explicitly skips non-finite edges.
- Performance hotspots are typically in:
  - edge walking (`RasterizeLine` family),
  - fill-rule conversion (`EmitRowCoverage`),
  - downstream blending/compositing callbacks.
