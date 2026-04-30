# DrawingCanvas

`DrawingCanvas` is the high-level drawing surface used by ImageSharp.Drawing. It lets the library expose one drawing model while supporting very different execution targets:

- CPU rasterization into memory
- GPU execution through native surfaces
- backends that prefer their own internal representation, such as vector export

That unification is the hard part. The public API wants to feel immediate and simple: fill a path, draw text, save state, restore state, draw into a region, maybe draw into a layer. The backends, however, do not all want the same kind of work. A CPU rasterizer wants rows, spans, and direct pixel access. A GPU backend wants compact command data, stable batching, and a single handoff point. A vector exporter would want semantic geometry rather than already-rasterized pixels.

The architecture around `DrawingCanvas` and its typed implementation exists to absorb that mismatch.

This document explains that architecture from the outside in. The goal is to help a newcomer understand what problem each piece solves before diving into methods and types.

## The Main Problem

If the canvas executed every public call immediately, each backend would have to implement the entire public drawing model directly:

- save and restore state
- clip stacking
- layers and isolated composition
- text drawing
- image drawing with transforms
- region drawing
- brush and pen handling
- transform handling

That sounds straightforward until the differences between backends become obvious.

The CPU backend can cheaply mutate a memory buffer row by row. The GPU backend wants a larger batch of work so it can amortize setup, upload, and dispatch costs. A vector-style backend would ideally preserve geometry and draw intent for as long as possible. If each backend solved all of that from scratch, they would drift apart quickly and correctness bugs would multiply.

So the architecture chooses a different approach:

`DrawingCanvas` records drawing intent first, normalizes that intent at flush time, and only then hands the work to the backend.

That one decision explains most of the surrounding design.

## The Core Idea

The canvas is a deferred renderer.

Drawing calls do not rasterize immediately. They create `CompositionCommand` records and queue them into `DrawingCanvasBatcher<TPixel>`. The expensive normalization work happens later, during flush, when the batcher prepares those commands and passes a `CompositionScene` to the backend.

That gives the architecture three important benefits.

First, the public API stays backend-agnostic. A fill is a fill, whether the target is CPU memory or a GPU surface.

Second, the expensive work can happen once, in one shared place. Transform application, stroke expansion, clip application, and prepared-geometry setup are not reimplemented independently by every backend.

Third, the backend receives a much more stable handoff. Instead of reacting to a long stream of public API calls, it receives a prepared scene with consistent semantics.

## The Most Important Terms

Before looking at the flow, it helps to define the major terms in the sense used by this codebase.

### Canvas

`DrawingCanvas` is the public drawing facade. It owns the current drawing state, accepts commands, and decides when to flush.

It is not the rasterizer. It is the object that makes the public drawing model coherent.

Callers reach that model through `IImageProcessingContext.Paint(...)`.

`DrawingCanvas<TPixel>` is the typed implementation that carries the target pixel format for brush normalization,
readback, and backend execution. Factory methods return `DrawingCanvas` so CPU and GPU entry points expose the same
canvas-facing API while still constructing the typed implementation internally.

### Batcher

`DrawingCanvasBatcher<TPixel>` is the deferred command queue. It stores pending `CompositionCommand` values, prepares them during flush, builds a `CompositionScene`, and calls `IDrawingBackend.FlushCompositions(...)`.

It is the bridge between the immediate-looking public API and the deferred backend handoff.

### Command

`CompositionCommand` is the recorded unit of drawing intent. In the common case it means "fill this path with this brush under this state". The command stream also carries explicit layer boundaries through `BeginLayer` and `EndLayer`.

The command remains relatively close to the original user request. It may hold the original path, pen, brush, transform, and clip paths.

### Preparation

Preparation is the flush-time normalization step that turns recorded intent into backend-ready geometry and metadata. It is split across two stages:

1. `DrawingCanvasBatcher<TPixel>.PrepareCommands(...)` runs first and only when needed. It applies command transforms, expands strokes to fill paths, and applies clip paths so that clipped commands reach the backend as ordinary fills. It also expands dashed strokes when a stroke pattern is present.
2. The CPU backend then calls `FlushScene.Create(...)`, which lowers each command into a row-oriented retained representation through `TryPrepareFillPath` / `TryPrepareStrokePath`. That is where prepared rasterizable geometry is built, brushes are bound to the prepared coordinate space, and bounds and raster interest are recomputed.

Preparation is where the architecture moves from "what the caller asked for" to "what the backend can execute".

### Scene

`CompositionScene` is the prepared batch handed to the backend. It contains the command stream and scene-level facts such as whether layers are present.

It is the backend handoff boundary.

### Backend

`IDrawingBackend` is the execution engine behind the canvas. The important implementations are:

- `DefaultDrawingBackend` for CPU rendering
- `WebGPUDrawingBackend` for GPU rendering through native surfaces

The backend receives a scene and a target frame. It decides how to execute that prepared work.

There are two backend-selection paths in the architecture:

- direct `DrawingCanvas<TPixel>` construction resolves the backend from `Configuration`
- specialized infrastructure can construct a canvas with an explicit backend

The ordinary CPU entry point is `Paint(...)` on `IImageProcessingContext`, which routes into the typed
implementation internally.

That explicit-backend path matters for the WebGPU helpers. `WebGPUWindow`, `WebGPURenderTarget`, and `WebGPUDeviceContext` create canvases that point directly at their owned `WebGPUDrawingBackend` instance instead of storing that backend on the caller's `Configuration`.

### Frame

`ICanvasFrame<TPixel>` is the target abstraction that the backend renders into.

This is one of the terms that can be ambiguous without context, so it is worth being explicit. In this architecture, a canvas frame is not "a UI frame" or "one animation frame". It means "the destination surface for one canvas instance".

The important properties of a frame are:

- `Bounds`
- whether it exposes a CPU region through `TryGetCpuRegion(...)`
- whether it exposes a native surface through `TryGetNativeSurface(...)`

That abstraction lets the same canvas target:

- pure CPU memory with `MemoryCanvasFrame<TPixel>`
- a native or GPU surface with `NativeCanvasFrame<TPixel>`
- a combined CPU plus native target
- a clipped view over another frame with `CanvasRegionFrame<TPixel>`

The point is not to hide all differences. The point is to express the minimum target contract the backends need.

### Layer

A layer is isolated group rendering. In public API terms, it is created with `SaveLayer(...)` and later closed by `Restore()` or `RestoreTo(...)`.

In this architecture, a layer is recorded inline in the command stream as:

- `BeginLayer`
- commands inside the layer
- `EndLayer`

The backend is responsible for lowering those layer boundaries into the execution model it needs.

Layer semantics stay in the shared command model so every backend receives the same layer structure at the handoff boundary.

## The Big Picture Flow

The easiest way to understand the system is to follow one normal draw call all the way through.

### Step 1: The canvas records intent

A public method such as `Fill(...)`, `Draw(...)`, or `DrawText(...)` resolves the active state and creates one or more `CompositionCommand` values.

At this point the canvas is mostly recording:

- geometry references
- brushes or pens
- active transform
- clip paths
- graphics options
- target bounds relevant to this command

The canvas does not try to fully rasterize anything here.

### Step 2: The batcher owns the pending work

Commands go into `DrawingCanvasBatcher<TPixel>`.

The batcher exists so the canvas does not need to talk to the backend for every single API call. It accumulates work until a flush boundary is reached.

The flush boundary usually comes from:

- explicit `Flush()`
- `Process(...)`, which needs read-modify-write behavior
- disposal of the owning canvas

### Step 3: Flush prepares commands

When the batcher flushes, it prepares every command. This is where the architecture does the heavy shared work that would otherwise be duplicated across backends.

For a typical path-based command, preparation does the following in concept:

1. transform the source path into its final geometry space
2. if a pen is present, expand the stroke to fill geometry
3. apply clip paths
4. build or reuse prepared geometry
5. transform the brush into the same prepared geometry space
6. recompute bounds and raster interest

This is the architectural center of gravity. It is the shared normalization stage that makes the backends simpler.

### Step 4: The backend executes a prepared scene

After preparation, the batcher creates a `CompositionScene` and calls `backend.FlushCompositions(...)`.

From that point the CPU and GPU paths diverge.

The CPU backend lowers the scene into a row-oriented retained representation through `FlushScene` and then composites into memory.

The WebGPU backend lowers the same scene into its staged GPU representation, uploads resources, and dispatches GPU work.

The architecture is successful if both backends can differ dramatically here without needing the public canvas model itself to fork.

## Why State Is Snapshotted

Drawing APIs look stateful because they are stateful. The active transform, clips, graphics options, and layer information all affect future commands.

`DrawingCanvasState` exists so that state changes are cheap to reason about and cheap to attach to commands.

The state snapshot contains the active options and target information for subsequent commands, including:

- `Options`
- `ClipPaths`
- `IsLayer`
- layer-related graphics options and bounds
- current target bounds

The canvas treats this state as immutable snapshots on a stack. `Save()` pushes a copy. `Restore()` pops one. Drawing calls always read the current top-of-stack state.

That makes save and restore semantics predictable and backend-independent.

## How Layers Work In This Architecture

Layer terminology often causes confusion because different systems use it differently. In this codebase, the most useful mental model is:

"A layer is a nested composition scope recorded inline in the command stream."

When `SaveLayer(...)` is called, the canvas:

1. clamps the requested layer bounds to the canvas
2. converts them into absolute target bounds
3. records `BeginLayer`
4. pushes a state snapshot that marks the new layer scope

The layer bounds are expressed in the active local coordinate system, so the canvas
transform in effect at `SaveLayer(...)` time is applied when resolving the layer's
absolute target bounds. The resolved bounds limit isolation, allocation, and final
composition. They do not shift the canvas coordinate system; draw commands inside a
bounded layer still use the same local coordinates as the parent canvas.

When the layer is later closed through `Restore()` or `RestoreTo(...)`, the canvas records `EndLayer`.

The actual isolation is implemented later by the backend.

On the CPU backend, layer boundaries become temporary backing buffers during scene execution.

On the WebGPU backend, layer boundaries become explicit staged-scene operations inside the GPU-oriented pipeline.

The key architectural point is that the public canvas records one shared layer model and lets the backend lower it.

## Why Frames Exist

The frame abstraction solves another unification problem.

The canvas should be able to target a plain in-memory image, but that should not force the GPU backend to pretend everything is CPU memory. Likewise, GPU-native targets should not force the CPU path to know about native surfaces directly.

`ICanvasFrame<TPixel>` is the contract that keeps those concerns separated.

In this architecture, a frame means "the destination surface and its capabilities". That is why the interface exposes both:

- geometric bounds
- optional CPU access
- optional native-surface access

This lets the same canvas code target different kinds of surfaces without rewriting the command model.

`CanvasRegionFrame<TPixel>` extends that idea one step further by saying "treat this clipped rectangle inside another frame as the target". That is how region canvases can share the same backend and batcher model while still drawing into a sub-rectangle.

## What `CreateRegion(...)` Really Means

`CreateRegion(...)` does not create a new independent rendering universe. It creates a child canvas that views a clipped sub-region of the parent target.

The child:

- wraps the parent target in `CanvasRegionFrame<TPixel>`
- keeps using the same backend
- keeps using the same shared batcher
- keeps participating in the same deferred flush model

The child canvas has local coordinates starting at `(0, 0)`, but its frame bounds resolve to the correct absolute position inside the parent target.

That distinction matters. It means the region API is a coordinate-system convenience, not a request to fork rendering into a totally separate backend pipeline.

## Why `DrawImage(...)` Is Special

Most draw calls record intent and defer the heavy work.

`DrawImage(...)` is the notable exception.

Images behave differently from paths because the canvas cannot simply attach a transform and let the backend "figure it out later" in the same way. The code performs eager image work before the final command is queued.

The rough flow is:

1. crop and scale the source image if needed
2. if a canvas transform is active, bake that transform into the image pixels
3. align the transformed bitmap to integer canvas bounds
4. create an `ImageBrush`
5. queue the final fill command using that brush

This design avoids applying the canvas transform twice and keeps the later command model consistent with brush-based filling.

That is why `DrawImage(...)` should be understood as "prepare an image-backed brush, then queue a normal fill", not as a completely separate rasterization pipeline.

## What The CPU Backend Receives

Once the scene reaches `DefaultDrawingBackend`, the public drawing model is already normalized.

The CPU backend does not need to understand every public API call individually. It works with:

- prepared commands
- layer boundaries
- the destination frame

It lowers the scene into a retained row-oriented structure through `FlushScene`, allocates temporary backing buffers for layers when needed, and composites the final result into the target frame.

That is the payoff of the architecture: the CPU backend is solving a rendering problem, not a public-API interpretation problem.

## What The WebGPU Backend Receives

The WebGPU backend receives the same scene shape, but it lowers it into a staged GPU-oriented format.

That includes:

- encoding prepared scene data
- creating native resources
- planning dispatches
- executing the GPU pipeline

It benefits from the same canvas-level decisions:

- commands are already normalized
- layers already exist as explicit boundaries
- the frame already describes whether a native surface is available

The WebGPU public helpers reach this point in a target-first way:

- `WebGPUWindow` acquires a presentable native target per frame
- `WebGPURenderTarget` owns an offscreen native target and can pair it with CPU memory through hybrid frames
- `WebGPUDeviceContext` wraps shared or caller-owned device state and creates native-only or hybrid frames and canvases over native textures

Those helpers all create typed canvas instances with an explicit `WebGPUDrawingBackend`, so GPU execution stays attached to the WebGPU object that owns the native target and backend lifetime while callers work through `DrawingCanvas`.

The backend is free to choose a very different execution model because the canvas has already solved the shared semantics problem.

## The Practical Mental Model

If you are new to this code, the most useful mental model is:

`DrawingCanvas` is the stateful front end that records drawing intent, `DrawingCanvas<TPixel>` is the typed implementation, `DrawingCanvasBatcher<TPixel>` is the deferred handoff boundary, and the backend is the executor of a prepared scene.

Everything else serves that flow.

State snapshots exist so save and restore are precise.

Commands exist so public API calls can be deferred.

Preparation exists so backend-agnostic normalization happens once.

Frames exist so the same canvas can target memory, native surfaces, or sub-regions.

Layers exist as inline composition scopes in the command stream.

Once those ideas are clear, the code stops looking like a random collection of types and starts looking like one system with a clear division of responsibility.

## Reading Guide

If you want to move from the architecture into the code, this is the best order.

1. `DrawingCanvas.cs`
2. `DrawingCanvas{TPixel}.cs`
3. `DrawingCanvasFactoryExtensions.cs` and `DrawingCanvasShapeExtensions.cs`
4. `DrawingCanvasBatcher{TPixel}.cs`
5. `CompositionCommand.cs`
6. `DefaultDrawingBackend.cs`
7. `FlushScene.cs`
8. `WebGPUEnvironment.cs`
9. `WebGPUWindow.cs`, `WebGPURenderTarget.cs`, and `WebGPUDeviceContext.cs`
10. `WebGPUDrawingBackend` and its scene/dispatch types

That path follows the real runtime flow:

public API -> recorded command -> prepared scene -> backend selection -> backend execution

Following the code in that order is much easier than starting from the backend internals first.
