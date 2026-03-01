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
     -> acquire one WebGPUFlushContext for the scene
     -> ensure command encoder (single encoder reused for the scene)
     -> resolve source backdrop texture view for composition bounds
        -> source = target view when sampleable
        -> else copy target region into transient composition texture and sample that
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
    -> build one flush-scoped composite command stream
       -> command-parallel tile-pair init (sentinel)
       -> command-parallel tile-pair emit
       -> global tile-pair key sort by (tile_index, command_index)
       -> tile span build (tileStarts/tileCounts/tileCommandIndices)
     -> run one fine composite dispatch (PreparedCompositeFineComputeShader)
        -> solid brush uses Color.ToScaledVector4()
        -> image brush samples Image<TPixel> texture directly
        -> writes composed pixels to one transient output texture
     -> copy output texture bounds back into the destination target once
     -> finalize once
        -> finish encoder
        -> single queue submit for the flush context
        -> optional readback for CPU-region targets
     -> on any GPU failure path: scene-scoped fallback (DefaultDrawingBackend)
```

## Context and Resource Lifetime

- `WebGPUFlushContext` is created once per `FlushCompositions` execution.
- The same command encoder is reused across all batch passes in that flush.
- Transient textures/buffers/bind-groups are tracked in the flush context and released on dispose.
- Source image texture views are cached per flush context to avoid duplicate uploads.

## Destination Writeback and Flush Count

- `FlushCompositions` performs one command-buffer submission (`QueueSubmit`) per scene flush.
- Destination writeback to the render target is one copy from the fine output texture into composition bounds.
- No destination storage init/blit pass is used in the active flush path.

## Fallback Behavior

Fallback is scene-scoped:

- if target exposes a CPU region:
  - run `DefaultDrawingBackend.FlushCompositions(...)` directly
- if target is native-surface only:
  - rent CPU staging frame
  - run `DefaultDrawingBackend.FlushCompositions(...)` on staging
  - upload staging pixels back to native target texture

## Shader Source and Null Terminator

All static WGSL shader sources are stored as null-terminated UTF-8 bytes (`U+0000` terminator at call site requirement), including:

- coverage pipeline shaders (`PathCountSetup`, `PathCount`, `Backdrop`, `SegmentAlloc`, `PathTilingSetup`, `PathTiling`, `CoverageFine`)
- composition shaders (`PreparedCompositeTilePairInit`, `PreparedCompositeTileEmit`, `PreparedCompositeTilePairSort`, `PreparedCompositeTileBuild`, `PreparedCompositeFine`)
