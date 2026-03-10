# WebGPU Backend Process

This document describes the runtime flows used by `WebGPUDrawingBackend` for scene flushing and layer compositing.

## End-to-End Flow

```text
DrawingCanvasBatcher.Flush()
  -> IDrawingBackend.FlushCompositions(scene)
     -> if target has no NativeSurface: delegate to DefaultDrawingBackend directly
     -> capability checks
        -> TryGetCompositeTextureFormat<TPixel>
        -> AreAllCompositionBrushesSupported<TPixel>
        -> if unsupported: staging fallback (see Fallback Behavior)
     -> CompositionScenePlanner.CreatePreparedBatches(commands, targetBounds)
        -> clip each command to target bounds
        -> group contiguous commands by DefinitionKey
        -> keep prepared destination/source offsets
     -> compute scene command count + composition bounds
        -> if no visible commands: return
     -> acquire one WebGPUFlushContext for the scene
     -> TryRenderPreparedFlush
        -> ensure command encoder (single encoder reused for the scene)
        -> use target texture view directly as backdrop source (no copy)
        -> allocate transient output texture for composition bounds
        -> deduplicate coverage definitions across batches via CoverageDefinitionIdentity
           (keyed by path, interest, intersection rule, rasterization mode, sampling origin, antialias threshold)
        -> TryCreateEdgeBuffer (CPU-side edge preparation)
           -> for each unique coverage definition:
              -> path.Flatten() to iterate flattened vertices
              -> build fixed-point (24.8) GpuEdge via MemoryAllocator (IMemoryOwner<GpuEdge>)
              -> compute min_row/max_row per edge, clamped to interest
              -> build CSR (Compressed Sparse Row) band-to-edge mapping:
                 1) count edges per 16-row band
                 2) exclusive prefix sum over band counts
                 3) scatter edge indices into CSR index array
           -> merge per-definition edges into single buffer with metadata stamps
              -> single-definition fast path: stamp in-place
              -> multi-definition: merge via Span.CopyTo
           -> upload edge buffer via dirty-range detection (word-by-word diff)
           -> upload merged CSR offsets and indices via QueueWriteBuffer
        -> TryDispatchPreparedCompositeCommands
           -> build per-command PreparedCompositeParameters (destination, edge placement,
              brush type/color/region, blend mode, composition mode)
           -> upload parameters + dispatch config via QueueWriteBuffer
           -> single compute dispatch: CompositeComputeShader
              -> workgroup size: 16x16 (one tile per workgroup)
              -> dispatched as (tileCountX, tileCountY, 1)
              -> each workgroup:
                 -> loads backdrop pixel from target texture
                 -> for each command overlapping this tile:
                    -> clears workgroup shared memory (tile_cover, tile_area, tile_start_cover)
                    -> cooperatively rasterizes edges from CSR bands using fixed-point scanline math
                    -> X-range spatial filter: edges left of tile only update start_cover
                    -> barrier, then each thread accumulates its coverage from shared memory
                    -> applies fill rule (non-zero or even-odd)
                    -> if aliased mode: snaps coverage to binary using antialias threshold
                    -> samples brush (see Brush Types below)
                    -> composes pixel using Porter-Duff alpha composition + color blend mode
                 -> writes final pixel to output texture
        -> copy composited output texture region back into target texture
     -> TryFinalizeFlush
        -> finish encoder + single QueueSubmit (non-blocking)
     -> on any GPU failure: scene-scoped fallback (DefaultDrawingBackend)
```

## Stroke Processing

For stroke definitions (`CompositionCoverageDefinition.IsStroke`), the backend
performs stroke expansion on the GPU using `StrokeExpandComputeShader`:

1. **Dash splitting** (CPU): If the definition has a dash pattern, `SplitPathExtensions.GenerateDashes()`
   (shared with `DefaultDrawingBackend` in the core project) segments the centerline into
   open dash sub-paths before edge building.

2. **Centerline edge building** (CPU): `path.Flatten()` produces contour vertices.
   Centerline edges are built as `GpuEdge` structs with `StrokeEdgeFlags` indicating
   the edge type (`None` for side edges, `Join`, `CapStart`, `CapEnd`). Join edges
   carry adjacent vertex coordinates in `AdjX`/`AdjY`. Centerline edges are band-sorted
   with Y expansion of `halfWidth * max(miterLimit, 1)`.

3. **GPU stroke expansion**: One `StrokeExpandCommand` per band dispatches the compute
   shader. Each thread expands one centerline edge into outline edges written to
   per-band output slots via atomic counters. Output buffer size is computed by
   `ComputeOutlineEdgesPerCenterline()` which accounts for join/cap type and arc
   step count for round joins/caps.

4. **Rasterization**: The generated outline edges are band-sorted and rasterized
   by the composite shader's fill path (same fixed-point scanline rasterizer).

## GPU Buffer Layout

### Edge Buffer (`coverage-aggregated-edges`)

Each edge is a 32-byte `GpuEdge` struct (sequential layout):

| Field | Type | Description |
|---|---|---|
| X0, Y0 | i32 | Start point in 24.8 fixed-point |
| X1, Y1 | i32 | End point in 24.8 fixed-point |
| Flags | StrokeEdgeFlags | Stroke edge type (None/Join/CapStart/CapEnd) |
| AdjX, AdjY | i32 | Auxiliary coords (join adjacent vertex) |

### CSR Buffers

- `csr-offsets`: `array<u32>` - per-band prefix sum. `offsets[band]..offsets[band+1]` gives the range of edge indices for that 16-row band.
- `csr-indices`: `array<u32>` - edge indices within each band, ordered by band.

### Command Parameters

Each `PreparedCompositeParameters` struct (32 × u32 = 128 bytes) contains destination rectangle, edge placement (start, fill rule, CSR offsets start, band count), brush type and configuration (gp0–gp7), blend/composition mode, blend percentage, rasterization mode (0 = antialiased, 1 = aliased), antialias threshold (float as u32 bitcast), and color stop buffer references (stops_offset, stop_count).

### Dispatch Config

`PreparedCompositeDispatchConfig` contains target dimensions, tile counts, source/output origins, and command count.

## Brush Types

All brush types are handled natively on the GPU. The backend passes raw brush properties via gp0–gp7 fields; derived values (trig, distances, etc.) are computed per-pixel in the shader.

| Constant | Type | gp fields |
|---|---|---|
| `BRUSH_SOLID` (0) | Solid color | gp0–3 = RGBA |
| `BRUSH_IMAGE` (1) | Image texture | brush_origin/region fields + texture binding |
| `BRUSH_LINEAR_GRADIENT` (2) | Linear gradient | gp0–3 = start.xy, end.xy; gp4 = repetition mode |
| `BRUSH_RADIAL_GRADIENT` (3) | Radial (single circle) | gp0–1 = center; gp2 = radius; gp4 = repetition mode |
| `BRUSH_RADIAL_GRADIENT_TWO_CIRCLE` (4) | Radial (two circle) | gp0–3 = c0.xy, c1.xy; gp4–5 = r0, r1; gp6 = repetition mode |
| `BRUSH_ELLIPTIC_GRADIENT` (5) | Elliptic gradient | gp0–3 = center.xy, refEnd.xy; gp4 = axis ratio; gp5 = repetition mode |
| `BRUSH_SWEEP_GRADIENT` (6) | Sweep gradient | gp0–3 = center.xy, startAngleDeg, endAngleDeg; gp4 = repetition mode |
| `BRUSH_PATTERN` (7) | Pattern | gp0–1 = width, height; gp2–3 = origin; cells packed in color stops buffer |
| `BRUSH_RECOLOR` (8) | Recolor | gp0–3 = source RGBA; gp4–7 = target RGBA; stops_offset = threshold |

Gradient color stops are packed into a shared storage buffer (binding 7). Each stop is 5 × f32 (ratio, R, G, B, A). The `stops_offset` and `stop_count` fields in the command parameters index into this buffer. Color stop interpolation in the shader is an exact copy of the C# `GradientBrushApplicator.GetGradientSegment` + lerp logic - including unclamped `t` values, stable sort order, and per-mode repetition semantics.

Color stops are sorted by ratio in the `GradientBrush` constructor using a stable insertion sort to preserve the order of equal-ratio stops.

## Shader Bindings (CompositeComputeShader)

| Binding | Type | Description |
|---|---|---|
| 0 | `storage, read` | Edge buffer (`array<Edge>`) |
| 1 | `texture_2d` | Backdrop texture (target) |
| 2 | `texture_2d` | Brush texture (image brush or same as backdrop) |
| 3 | `texture_storage_2d, write` | Output texture |
| 4 | `storage, read` | Command parameters (`array<Params>`) |
| 5 | `uniform` | Dispatch config |
| 6 | `storage, read` | Band offsets (`array<u32>`) |
| 7 | `storage, read` | Color stops / pattern buffer (`array<ColorStop>`) |

## Context and Resource Lifetime

- `WebGPUFlushContext` is created once per `FlushCompositions` execution and disposed at the end.
- The same command encoder is reused across all GPU operations in that flush.
- Transient textures, texture views, buffers, and bind groups are tracked in the flush context and released on dispose.
- Source image texture views are cached within the flush context to avoid duplicate uploads.
- CPU-side edge geometry (`IMemoryOwner<GpuEdge>`) is allocated via `MemoryAllocator` and disposed within the flush.
- Shared GPU buffers (edge buffer, CSR buffers, params buffer, dispatch config buffer) are managed by `DeviceState` with grow-only reuse across flushes.
- Edge upload uses dirty-range detection: compares current data word-by-word against a cached copy, uploading only the changed byte range via `QueueWriteBuffer`.

## Destination Writeback

- `FlushCompositions` performs one command-buffer submission (`QueueSubmit`) per scene flush.
- One GPU-side `CommandEncoderCopyTextureToTexture` from output into the target at composition bounds. No CPU stall.
- The WebGPU backend only operates on native GPU surfaces. CPU-backed frames are handled entirely by `DefaultDrawingBackend`.

## Fallback Behavior

### FlushCompositions

1. **Non-native target**: If the target frame has no `NativeSurface`, delegate directly
   to `DefaultDrawingBackend.FlushCompositions` (no staging, no upload).
2. **Native target, unsupported scene**: Pixel format has no GPU mapping, or a command
   uses an unsupported brush (`PathGradientBrush`). Fallback: allocate a clean CPU
   staging `Buffer2D`, run `DefaultDrawingBackend.FlushCompositions` on it, then
   upload the staging region to the native target texture via `UploadTextureFromRegion`.
3. **GPU failure**: Any GPU operation fails during `TryRenderPreparedFlush` or
   `TryFinalizeFlush`. Same staging + upload fallback as (2).

### ComposeLayer

1. **Non-native destination**: Delegate directly to `DefaultDrawingBackend.ComposeLayer`.
2. **Native destination, GPU compose fails**: Read both destination and source textures
   from the GPU via `TryReadRegion`, compose on CPU via `DefaultDrawingBackend.ComposeLayer`,
   then upload the composited destination back to the native texture.

## Layer Compositing (ComposeLayer)

`SaveLayer` on `DrawingCanvas` calls `IDrawingBackend.CreateLayerFrame` to allocate the layer.
When the parent target is a native GPU surface, `WebGPUDrawingBackend` creates a GPU-resident
texture for the layer (zero-copy). For CPU-backed parents it falls back to `DefaultDrawingBackend`.
On `Restore`, `IDrawingBackend.ComposeLayer` blends the layer onto the parent target.

```text
DrawingCanvas.SaveLayer()
  -> IDrawingBackend.CreateLayerFrame(parentTarget, width, height)
     -> WebGPUDrawingBackend: GPU texture if parent is native, else CPU fallback
  -> redirect draw commands to layer frame

DrawingCanvas.Restore() / RestoreTo()
  -> CompositeAndPopLayer(layerState)
     -> Flush() current layer batcher
     -> pop LayerData from layerDataStack
     -> restore parent batcher
     -> IDrawingBackend.ComposeLayer(source=layerFrame, destination=parentFrame, offset, options)
        -> WebGPUDrawingBackend.ComposeLayer
           -> TryComposeLayerGpu (requires destination with NativeSurface)
              -> TryGetCompositeTextureFormat<TPixel>
              -> destination must expose NativeSurface with WebGPU capability
              -> TryAcquireSourceTexture: bind native GPU texture directly (zero-copy)
                 or upload CPU pixels to temporary GPU texture
              -> create output texture sized to composite region
              -> ComposeLayerComputeShader dispatch:
                 -> workgroup size 16x16
                 -> each thread reads backdrop at (out_x + offset, out_y + offset)
                 -> reads source at (out_x, out_y)
                 -> applies layer opacity, blend mode, alpha composition
                 -> writes result to output at (out_x, out_y)
              -> copy output back to destination texture at compositing offset
              -> QueueSubmit
           -> fallback: ComposeLayerFallback
              -> TryReadRegion(destination) -> CPU staging image
              -> TryReadRegion(source) -> CPU staging image
              -> DefaultDrawingBackend.ComposeLayer on staging frames
              -> UploadTextureFromRegion back to destination texture
     -> IDrawingBackend.ReleaseFrameResources(layerFrame)
        -> GPU frames: release texture + texture view
        -> CPU frames: dispose Buffer2D
```

### Shader Bindings (ComposeLayerComputeShader)

| Binding | Type | Description |
|---|---|---|
| 0 | `texture_2d` | Source layer texture (read) |
| 1 | `texture_2d` | Backdrop/destination texture (read) |
| 2 | `texture_storage_2d, write` | Output texture |
| 3 | `uniform` | `LayerConfig` (source dims, dest offset, blend mode, opacity) |

### LayerConfig Uniform

| Field | Type | Description |
|---|---|---|
| source_width, source_height | u32 | Source layer dimensions |
| dest_offset_x, dest_offset_y | i32 | Offset into backdrop texture |
| color_blend_mode | u32 | Porter-Duff color blend mode |
| alpha_composition_mode | u32 | Porter-Duff alpha composition mode |
| blend_percentage | u32 | Layer opacity (f32 bitcast to u32) |

### Coordinate Spaces

The output texture is sized to the composite region (intersection of source and
destination bounds). The shader uses local coordinates `(out_x, out_y)` for source
reads and output writes, and offset coordinates `(out_x + dest_offset, out_y + dest_offset)`
for backdrop reads. The final `CopyTextureRegion` places the output at the
compositing offset in the destination texture.

## Shared Composition Functions

`CompositionShaderSnippets.BlendAndCompose` contains shared WGSL functions used by both
`CompositeComputeShader` and `ComposeLayerComputeShader`:

- `unpremultiply(rgb, alpha)` — converts premultiplied alpha to straight alpha
- `blend_color(backdrop, source, mode)` — 8 color blend modes (Normal, Multiply, Add, Subtract, Screen, Darken, Lighten, Overlay, HardLight)
- `compose_pixel(backdrop, source, blend_mode, compose_mode)` — full Porter-Duff alpha composition with 12 modes (Clear, Src, Dest, SrcOver, DestOver, SrcIn, DestIn, SrcOut, DestOut, SrcAtop, DestAtop, Xor)

Both shaders include these functions via the `__BLEND_AND_COMPOSE__` template placeholder,
avoiding code duplication between the rasterize+composite and layer composite pipelines.

## Shader Source

`CompositeComputeShader` generates WGSL source per target texture format at runtime, substituting format-specific template tokens for texel decode/encode, backdrop/brush load, and output store. `ComposeLayerComputeShader` uses the same template approach with its own format traits. Generated source is cached by `TextureFormat` as null-terminated UTF-8 bytes.

The following static WGSL shaders exist for the legacy CSR GPU pipeline but are not used in the current dispatch path (CSR is computed on CPU):
- `CsrCountComputeShader`, `CsrScatterComputeShader`
- `CsrPrefixLocalComputeShader`, `CsrPrefixBlockScanComputeShader`, `CsrPrefixPropagateComputeShader`

## Performance Characteristics

Coverage rasterization and compositing are fused into a single compute dispatch. Each 16x16 tile workgroup computes coverage inline using a fixed-point scanline rasterizer ported from `DefaultRasterizer`, operating on workgroup shared memory with atomic accumulation. This eliminates the coverage texture, its allocation, write/read bandwidth, and the pass barrier that a separate coverage dispatch would require.

Edge preparation (path flattening, fixed-point conversion, CSR construction) runs on the CPU. The `path.Flatten()` cost is shared with the CPU rasterizer pipeline. CSR construction is three passes over the edge set: count, prefix sum, scatter.

Both the CPU and GPU backends use per-band parallel stroke expansion - the CPU
via `DefaultRasterizer.RasterizeStrokeRows` and the GPU via
`StrokeExpandComputeShader`. Both share the same `StrokeEdgeFlags` enum and
`SplitPathExtensions.GenerateDashes` (in the core project). The CPU backend fuses stroke expansion
directly into the rasterizer's band loop, while the GPU backend uses a separate
compute dispatch that writes outline edges into pre-allocated per-band output
slots sized by `ComputeOutlineEdgesPerCenterline()`.
