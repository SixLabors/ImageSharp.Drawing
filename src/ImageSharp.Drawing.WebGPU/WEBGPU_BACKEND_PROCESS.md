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
     -> for each prepared batch
        -> ensure command encoder (single encoder reused for the scene)
        -> initialize destination storage buffer once per flush (premultiplied vec4<f32>)
           -> source = target view when sampleable
           -> else copy target region into transient composition texture, then sample that
           -> run CompositeDestinationInitShader compute pass
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
        -> composite commands into destination storage (PreparedCompositeComputeShader)
           -> solid brush uses Color.ToScaledVector4()
           -> image brush samples Image<TPixel> texture directly
        -> blit destination storage back to target (CompositeDestinationBlitShader)
           -> render pass uses LoadOp.Load + StoreOp.Store
           -> scissor limits writes to destination bounds
     -> finalize once
        -> finish encoder
        -> single queue submit for the flush context
        -> optional readback for CPU-region targets
     -> on any GPU failure path: scene-scoped fallback (DefaultDrawingBackend)
```

## Context and Resource Lifetime

- `WebGPUFlushContext` is created once per `FlushCompositions` execution.
- The same command encoder is reused across all batch passes in that flush.
- Destination storage (`CompositeDestinationPixelsBuffer`) is initialized once and reused across batches in the same flush.
- Transient textures/buffers/bind-groups are tracked in the flush context and released on dispose.
- Source image texture views are cached per flush context to avoid duplicate uploads.

## Destination Writeback and Flush Count

- `FlushCompositions` performs one command-buffer submission (`QueueSubmit`) per scene flush.
- Destination writeback to the render target occurs via destination blit pass(es) before final submit:
  - one blit per prepared batch in command order,
  - with destination storage preserved across batches.
- For scenes that plan to a single prepared batch (common case), this is one destination blit pass.

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
- composition shaders (`PreparedComposite`, `CompositeDestinationInit`, `CompositeDestinationBlit`)
