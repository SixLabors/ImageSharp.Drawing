# WebGPU Backend Process

This document describes the current runtime flow used by `WebGPUDrawingBackend` when flushing a `CompositionScene`.

## End-to-End Flow

```text
DrawingCanvasBatcher.Flush()
  -> IDrawingBackend.FlushCompositions(scene)
     -> capability checks first
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
     -> ensure command encoder (single encoder reused for the scene)
     -> resolve source backdrop texture view for composition bounds
        -> non-readback path: sample target view directly
        -> readback path: copy target region into transient source texture and sample that
     -> allocate transient output texture for composition
     -> build coverage texture from prepared geometry
        -> flatten prepared path geometry
        -> upload line/path/tile/segment buffers
        -> run compute sequence:
           1) PathCountSetup
           2) PathCount
           3) Backdrop
           4) SegmentAlloc
           5) PathTilingSetup
           6) PathTiling
           7) CoverageFine
     -> build one flush-scoped composite command parameter stream from prepared batches
     -> run composite dispatch sequence:
        1) PreparedCompositeBinning
        2) PreparedCompositeTileCount
        3) PreparedCompositeTilePrefix
        4) PreparedCompositeTileFill
        5) PreparedCompositeFine
        -> solid brush uses Color.ToScaledVector4()
        -> image brush samples Image<TPixel> texture directly
        -> writes composed pixels to one transient output texture
     -> copy output texture bounds back into the destination target once
     -> finalize once
        -> non-readback: finish encoder + single queue submit
        -> readback: encode texture->buffer copy, finish encoder + single queue submit, map/copy once
     -> on any GPU failure path: scene-scoped fallback (DefaultDrawingBackend)
```

## Context and Resource Lifetime

- `WebGPUFlushContext` is created once per `FlushCompositions` execution.
- The same command encoder is reused across all GPU passes in that flush.
- Transient textures/buffers/bind-groups are tracked in the flush context and released on dispose.
- Source image texture views are cached per flush context to avoid duplicate uploads.

## Destination Writeback and Flush Count

- `FlushCompositions` performs one command-buffer submission (`QueueSubmit`) per scene flush.
- Destination writeback to the render target is one copy from the fine output texture into composition bounds.
- No destination storage init/blit pass is used in the active flush path.
- CPU-region targets perform one additional texture->buffer copy and one map/read after the single submit.

## Fallback Behavior

Fallback is scene-scoped:

- if target exposes a CPU region:
  - run `DefaultDrawingBackend.FlushCompositions(...)` directly
- if target is native-surface only:
  - rent CPU staging frame
  - run `DefaultDrawingBackend.FlushCompositions(...)` on staging
  - upload staging pixels back to native target texture

## Shader Source and Null Terminator

Static WGSL shaders are stored as null-terminated UTF-8 bytes (`U+0000` terminator required at call site), including:

- coverage shaders: `PathCountSetup`, `PathCount`, `Backdrop`, `SegmentAlloc`, `PathTilingSetup`, `PathTiling`, `CoverageFine`
- prepared-composite shaders: `PreparedCompositeBinning`, `PreparedCompositeTileCount`, `PreparedCompositeTilePrefix`, `PreparedCompositeTileFill`

`PreparedCompositeFine` is generated per target texture format and emitted as null-terminated UTF-8 bytes at runtime.
