# WebGPU Backend Process

This document describes the current runtime flow used by `WebGPUDrawingBackend` when flushing a `CompositionScene`.

## End-to-End Flow

```text
DrawingCanvasBatcher.Flush()
  -> IDrawingBackend.FlushCompositions(scene)
     -> capability checks
        -> TryGetCompositeTextureFormat<TPixel>
        -> AreAllCompositionBrushesSupported<TPixel>
        -> if unsupported: scene-scoped fallback (DefaultDrawingBackend)
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
                    -> samples brush (solid color or image texture)
                    -> composes pixel using Porter-Duff alpha composition + color blend mode
                 -> writes final pixel to output texture
        -> destination writeback:
           -> NativeSurface: copy output texture region into target texture
           -> CPU Region: set ReadbackSourceOverride to output texture (skip extra copy)
     -> TryFinalizeFlush
        -> NativeSurface: finish encoder + single QueueSubmit (non-blocking)
        -> CPU Region: encode texture->buffer copy, finish encoder, QueueSubmit,
           synchronous BufferMapAsync + poll wait, copy mapped bytes to CPU region
     -> on any GPU failure: scene-scoped fallback (DefaultDrawingBackend)
```

## GPU Buffer Layout

### Edge Buffer (`coverage-aggregated-edges`)

Each edge is a 32-byte `GpuEdge` struct (sequential layout):

| Field | Type | Description |
|---|---|---|
| X0, Y0 | i32 | Start point in 24.8 fixed-point |
| X1, Y1 | i32 | End point in 24.8 fixed-point |
| MinRow | i32 | First pixel row touched (clamped to interest) |
| MaxRow | i32 | Last pixel row touched (clamped to interest) |
| CsrBandOffset | u32 | Start index into CSR offsets for this definition |
| DefinitionEdgeStart | u32 | Edge index offset for this definition in merged buffer |

### CSR Buffers

- `csr-offsets`: `array<u32>` — per-band prefix sum. `offsets[band]..offsets[band+1]` gives the range of edge indices for that 16-row band.
- `csr-indices`: `array<u32>` — edge indices within each band, ordered by band.

### Command Parameters

Each `PreparedCompositeParameters` struct contains destination rectangle, edge placement (start, fill rule, CSR offsets start, band count), brush configuration, blend/composition mode, and blend percentage.

### Dispatch Config

`PreparedCompositeDispatchConfig` contains target dimensions, tile counts, source/output origins, and command count.

## Shader Bindings (CompositeComputeShader)

| Binding | Type | Description |
|---|---|---|
| 0 | `storage, read` | Edge buffer (`array<Edge>`) |
| 1 | `texture_2d` | Backdrop texture (target) |
| 2 | `texture_2d` | Brush texture (image brush or same as backdrop) |
| 3 | `texture_storage_2d, write` | Output texture |
| 4 | `storage, read` | Command parameters (`array<Params>`) |
| 5 | `uniform` | Dispatch config |
| 6 | `storage, read` | CSR offsets (`array<u32>`) |
| 7 | `storage, read` | CSR indices (`array<u32>`) |

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
- NativeSurface targets: one GPU-side `CommandEncoderCopyTextureToTexture` from output into the target at composition bounds. No CPU stall.
- CPU Region targets: readback from the output texture directly (skipping the output-to-target copy). Uses `CommandEncoderCopyTextureToBuffer`, `QueueSubmit`, synchronous `BufferMapAsync` with device polling, then copies mapped bytes to the CPU `Buffer2DRegion<TPixel>`.

## Fallback Behavior

Fallback is scene-scoped and triggered when:
- The pixel format has no supported WebGPU texture format mapping.
- Any command uses an unsupported brush type (only `SolidBrush` and `ImageBrush` are GPU-composable).
- Any GPU operation fails during the flush.

Fallback path:
- If target exposes a CPU region: run `DefaultDrawingBackend.FlushCompositions(...)` directly.
- If target is native-surface only: rent CPU staging frame, run fallback on staging, upload staging pixels back to native target texture.

## Shader Source

`CompositeComputeShader` generates WGSL source per target texture format at runtime, substituting format-specific template tokens for texel decode/encode, backdrop/brush load, and output store. Generated source is cached by `TextureFormat` as null-terminated UTF-8 bytes.

The following static WGSL shaders exist for the legacy CSR GPU pipeline but are not used in the current dispatch path (CSR is computed on CPU):
- `CsrCountComputeShader`, `CsrScatterComputeShader`
- `CsrPrefixLocalComputeShader`, `CsrPrefixBlockScanComputeShader`, `CsrPrefixPropagateComputeShader`

## Performance Characteristics

Coverage rasterization and compositing are fused into a single compute dispatch. Each 16x16 tile workgroup computes coverage inline using a fixed-point scanline rasterizer ported from `DefaultRasterizer`, operating on workgroup shared memory with atomic accumulation. This eliminates the coverage texture, its allocation, write/read bandwidth, and the pass barrier that a separate coverage dispatch would require.

Edge preparation (path flattening, fixed-point conversion, CSR construction) runs on the CPU. The `path.Flatten()` cost is shared with the CPU rasterizer pipeline. CSR construction is three passes over the edge set: count, prefix sum, scatter.

For the benchmark workload (7200x4800 US states GeoJSON polygon, 2px stroke, ~262K edges), NativeSurface performance is at parity with the CPU rasterizer (~28ms).
