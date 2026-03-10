# DrawingCanvas

This document describes the architecture, state management, and object lifecycle of `DrawingCanvas<TPixel>`.

## Overview

`DrawingCanvas<TPixel>` is the high-level drawing API. It manages a state stack, command batching, layer compositing, and delegates rasterization to an `IDrawingBackend`. It implements a deferred command model—draw calls queue `CompositionCommand` objects in a batcher, which are flushed to the backend on `Flush()` or `Dispose()`.

## Class Structure

```text
DrawingCanvas<TPixel> : IDrawingCanvas, IDisposable
  Fields:
    configuration          : Configuration
    backend                : IDrawingBackend
    targetFrame            : ICanvasFrame<TPixel>        (root frame, immutable)
    batcher                : DrawingCanvasBatcher<TPixel> (reassigned on SaveLayer/Restore)
    savedStates            : Stack<DrawingCanvasState>    (min depth 1)
    layerDataStack         : Stack<LayerData<TPixel>>     (one per active SaveLayer)
    pendingImageResources  : List<Image<TPixel>>          (temp images awaiting flush)
    isRoot                 : bool                         (only root releases frame resources)
    isDisposed             : bool
```

## State Management

### DrawingCanvasState (Immutable Snapshot)

```text
DrawingCanvasState
  Options      : DrawingOptions   (by reference, not deep-cloned)
  ClipPaths    : IReadOnlyList<IPath>
  IsLayer      : bool             (init-only, default false)
  LayerOptions : GraphicsOptions? (init-only, set for layers)
  LayerBounds  : Rectangle?       (init-only, set for layers)
```

### Save / Restore

```text
Save()
  -> push new DrawingCanvasState(current.Options, current.ClipPaths)
  -> IsLayer = false (prevents spurious layer compositing on Restore)
  -> return SaveCount

Save(options, clipPaths)
  -> Save(), then replace top state with new options/clips
  -> return SaveCount

Restore()
  -> if SaveCount <= 1: no-op
  -> pop top state
  -> if state.IsLayer: CompositeAndPopLayer(state)

RestoreTo(saveCount)
  -> pop states until SaveCount == target
  -> each layer state triggers CompositeAndPopLayer
```

`ResolveState()` returns `savedStates.Peek()`—every drawing operation reads the active state from here.

## SaveLayer Lifecycle

### Purpose

SaveLayer enables group-level effects: rendering multiple draw commands to an isolated temporary surface, then compositing the result back with opacity, blend mode, or restricted bounds. This is the mechanism behind SVG `<g opacity="0.5">` and group blend modes.

### Push Phase

```text
SaveLayer(layerOptions, bounds)
  1. Flush() pending commands to current target
  2. Clamp bounds to canvas (min 1x1)
  3. Allocate transparent Image<TPixel>(width, height)
  4. Wrap in MemoryCanvasFrame<TPixel>
  5. Save current batcher in LayerData<TPixel>:
       LayerData(parentBatcher, layerImage, layerFrame, layerBounds)
  6. Push LayerData onto layerDataStack
  7. Create new batcher targeting layer frame
  8. Push layer state onto savedStates:
       DrawingCanvasState { IsLayer=true, LayerOptions, LayerBounds }
  9. Return SaveCount
```

### Draw Phase

All commands between SaveLayer() and Restore() target the layer batcher, which writes to the temporary layer image. Clip paths, transforms, and drawing options apply normally within the layer's local coordinate space.

### Pop Phase (Restore)

```text
CompositeAndPopLayer(layerState)
  1. Flush() pending commands to layer surface
  2. Pop LayerData from layerDataStack
  3. Restore parent batcher: this.batcher = layerData.ParentBatcher
  4. Get destination from parent batcher's target frame
  5. backend.ComposeLayer(source=layerFrame, destination, bounds.Location, layerOptions)
  6. layerData.Dispose() (releases temporary image)
```

### Nesting

Layers nest naturally via the `layerDataStack`. When compositing a nested layer, the parent batcher still targets the intermediate layer (not root). Each Restore pops one layer and composites it to its immediate parent.

### Object Lifecycle Diagram

```text
canvas.SaveLayer(options, bounds)
  ├─ Image<TPixel> layerImage ──────────────┐ (allocated)
  ├─ MemoryCanvasFrame layerFrame ──────────┤
  ├─ LayerData<TPixel> ────────────────────┤
  │   ├─ .ParentBatcher = old batcher      │
  │   ├─ .LayerImage = layerImage          │
  │   ├─ .LayerFrame = layerFrame          │
  │   └─ .LayerBounds = clampedBounds      │
  └─ new batcher → targets layerFrame      │
                                            │
  ... draw commands → layer batcher ...     │
                                            │
canvas.Restore()                            │
  ├─ flush layer batcher                   │
  ├─ pop LayerData                         │
  ├─ restore parent batcher                │
  ├─ ComposeLayer(layerFrame → parent)     │
  └─ layerData.Dispose() ─────────────────┘ (freed)
```

## The Batcher

`DrawingCanvasBatcher<TPixel>` queues `CompositionCommand` objects and flushes them to the backend.

```text
DrawingCanvasBatcher<TPixel>
  TargetFrame    : ICanvasFrame<TPixel>  (destination for this batcher)
  AddComposition(command)                (append to command list)
  FlushCompositions()                    (create scene, call backend, clear list)
```

**Flush behavior:**
1. If no commands, return (no-op).
2. Create `CompositionScene` wrapping the command list.
3. Call `backend.FlushCompositions(configuration, targetFrame, scene)`.
4. Clear commands in `finally` block (always, even on failure).

The canvas holds exactly one batcher at a time. SaveLayer swaps it for a new one targeting the layer frame; Restore swaps it back to the parent.

## Drawing Operations

### Fill

```text
Fill(brush, path)
  1. ResolveState() → options, clipPaths
  2. path.AsClosedPath()
  3. Apply transform: path.Transform(options.Transform), brush.Transform(options.Transform)
  4. Apply clips: path.Clip(shapeOptions, clipPaths)
  5. PrepareCompositionCore(path, brush, options, PixelBoundary)
     → batcher.AddComposition(CompositionCommand.Create(...))
```

### Stroke (Draw)

```text
Draw(pen, path)
  1. ResolveState() → options, clipPaths
  2. Apply transform to path
  3. Force NonZero winding rule (strokes self-overlap)
  4. If clip paths present:
     → expand stroke to outline on CPU, then clip, then fill
  5. If no clip paths:
     → PrepareStrokeCompositionCore(path, brush, strokeWidth, strokeOptions, ...)
     → defers stroke expansion to backend
  Uses PixelCenter sampling origin
```

### Clear

Executes a fill with modified `GraphicsOptions` (Src composition, no blending) inside a temporary state scope via `ExecuteWithTemporaryState()`.

### DrawImage

Three-phase pipeline:
1. **Source preparation:** Crop/scale source image to destination dimensions.
2. **Transform application:** Apply canvas transform via `image.Transform()` with composed matrix.
3. **Deferred execution:** Transfer temp image to `pendingImageResources`, create `ImageBrush`, fill destination path.

### DrawText

Renders text to glyph operations via `RichTextGlyphRenderer`, sorts by render pass (for color font layers), submits commands in sorted order.

### Process

Flushes current commands, reads back pixels via `backend.TryReadRegion()`, runs an `Action<IImageProcessingContext>` on the readback, then fills the result back to the canvas via `ImageBrush`.

## Frame Abstraction

```text
ICanvasFrame<TPixel>
  Bounds                    : Rectangle
  TryGetCpuRegion(out region)    : bool
  TryGetNativeSurface(out surface) : bool
```

| Implementation | CPU Region | Native Surface | Usage |
|---|---|---|---|
| `MemoryCanvasFrame<TPixel>` | Yes | No | CPU image buffers, layers |
| `NativeCanvasFrame<TPixel>` | No | Yes | GPU-backed surfaces |
| `CanvasRegionFrame<TPixel>` | Delegates | Delegates | Sub-region of parent frame |

`CanvasRegionFrame` wraps a parent frame with a clipped rectangle, used by `CreateRegion()`.

## Transform Handling

Transforms are stored in `DrawingOptions.Transform` as `Matrix4x4` (default: Identity). Applied per-operation, not cumulatively.

| Target | Method |
|---|---|
| Paths | `path.Transform(matrix)` |
| Brushes | `brush.Transform(matrix)` |
| Images | `image.Transform()` with composed source→destination→canvas matrix |

## Clipping

Clip paths are stored in `DrawingCanvasState.ClipPaths`. Applied during command preparation:

```csharp
effectivePath = subjectPath.Clip(shapeOptions, clipPaths);
```

For strokes with clip paths, the stroke is expanded to an outline first, then clipped, then filled—this prevents clip artifacts at stroke edges.

## CreateRegion

```text
CreateRegion(region)
  -> clamp to canvas bounds
  -> wrap frame in CanvasRegionFrame<TPixel>
  -> create child canvas:
     - shares backend and batcher with parent
     - snapshots current state
     - isRoot = false (no resource release on dispose)
     - local origin is (0,0) within clipped region
```

## Disposal

```text
Dispose()
  Phase 1: Pop all active layers
    -> for each layer in layerDataStack (LIFO):
       -> Flush() to layer surface
       -> restore parent batcher
       -> ComposeLayer(layer → parent) with default GraphicsOptions
       -> layerData.Dispose()

  Phase 2: Final flush
    -> batcher.FlushCompositions()

  Phase 3: Cleanup (in finally)
    -> DisposePendingImageResources()
    -> if isRoot: backend.ReleaseFrameResources(target)
    -> isDisposed = true
```

Active layers that were never explicitly Restored are composited with default `GraphicsOptions` during disposal. This ensures no resource leaks, though the compositing result may differ from explicit Restore with custom options.

## IDrawingBackend Interface

```text
IDrawingBackend
  IsSupported                → bool (default true)
  FlushCompositions<TPixel>  (configuration, target, scene)
  ComposeLayer<TPixel>       (configuration, source, destination, offset, options)
  TryReadRegion<TPixel>      (configuration, target, rect, out image) → bool
  ReleaseFrameResources<TPixel> (configuration, target)
```

`DefaultDrawingBackend` (singleton) handles all operations on CPU. `WebGPUDrawingBackend` accelerates FlushCompositions and ComposeLayer on GPU with CPU fallback.

## Command Flow Summary

```text
canvas.Fill(brush, path)
  → transform path + brush
  → clip path
  → CompositionCommand.Create(path, brush, options)
  → batcher.AddComposition(command)
  → ... more draw calls ...

canvas.Flush() or canvas.Dispose()
  → batcher.FlushCompositions()
    → CompositionScene(commands)
    → backend.FlushCompositions(config, frame, scene)
      → CompositionScenePlanner.CreatePreparedBatches()
      → for each batch: rasterize + apply brushes + composite
    → commands.Clear()
  → DisposePendingImageResources()
```
