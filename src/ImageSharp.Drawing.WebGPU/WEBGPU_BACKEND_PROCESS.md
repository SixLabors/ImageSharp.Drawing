# WebGPU Backend Process

This document describes the runtime flow used by `WebGPUDrawingBackend` when flushing a `CompositionScene`.

## End-to-End Flow

```text
DrawingCanvasBatcher.Flush()
  -> IDrawingBackend.FlushCompositions(scene)
     -> CompositionScenePlanner.CreatePreparedBatches(scene.Commands)
     -> foreach prepared batch
        -> WebGPUDrawingBackend.FlushPreparedBatch(batch)
           -> validate brush support + pixel format support
           -> acquire WebGPUFlushContext
              -> shared session context when scene uses GPU-only brushes
              -> standalone context otherwise
           -> prepare/reuse GPU coverage texture for batch definition
           -> composite commands (tiled compute path)
              -> build tile ranges + tile command indices
              -> build brush/source layer payloads
              -> upload command/brush/tile buffers
              -> dispatch compute workgroups
           -> optional destination blit to target texture
           -> finalize
              -> submit GPU commands
              -> readback to CPU region when target requires readback
           -> on any GPU failure path: execute batch through DefaultDrawingBackend
```

## Context and Resource Lifetime

- `WebGPUFlushContext` owns per-flush transient resources:
  - command encoder
  - bind groups
  - transient buffers and textures
  - optional readback buffer mapping sequence
- shared flush sessions are keyed by scene flush id:
  - destination initialization is performed once
  - destination storage buffer is reused across all session batches
  - session is closed on final batch or on failure

## Fallback Behavior

Fallback is batch-scoped, not scene-scoped:

- if target exposes a CPU region:
  - run `DefaultDrawingBackend.FlushPreparedBatch(...)` directly
- if target is native-surface only:
  - rent CPU staging frame
  - run `DefaultDrawingBackend.FlushPreparedBatch(...)` on staging
  - upload staging pixels back to native target texture

## Shader Source and Null Terminator

All WGSL sources in this backend are stored as UTF-8 compile-time literals with an explicit trailing U+0000.

Reason:

- native WebGPU module creation consumes WGSL through a null-terminated pointer
- embedding the terminator in the literal avoids runtime append/copy work
- keeping shader bytes as static literal data removes per-call allocations
