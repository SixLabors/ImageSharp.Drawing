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

`DrawingCanvas` records drawing intent first, normalizes that intent when replaying or creating a retained scene, and only then hands the work to the backend.

That one decision explains most of the surrounding design.

## The Core Idea

The canvas is a deferred renderer.

Drawing calls do not rasterize immediately. They create command records and queue them into `DrawingCanvasBatcher<TPixel>`. The expensive normalization work happens later, during replay, when the batcher prepares those commands and hands `DrawingCommandBatch` ranges to the backend.

That gives the architecture three important benefits.

First, the public API stays backend-agnostic. A fill is a fill, whether the target is CPU memory or a GPU surface.

Second, the expensive shared command work can happen once, in one shared place. Dash expansion and brush-coordinate normalization are not reimplemented independently by every backend.

Third, the backend receives a much more stable handoff. Instead of reacting to a long stream of public API calls, it receives prepared command batches with consistent semantics.

## The Most Important Terms

Before looking at the flow, it helps to define the major terms in the sense used by this codebase.

### Canvas

`DrawingCanvas` is the public drawing facade. It owns the current drawing state, accepts commands, and decides when to flush.

It is not the rasterizer. It is the object that makes the public drawing model coherent.

Callers usually reach that model through `IImageProcessingContext.Paint(...)`.

When callers already have an `ImageFrame` or `ImageFrame<TPixel>`, the public `CreateCanvas(...)` frame extensions create a canvas directly over that frame. The caller owns the returned canvas and must dispose it to replay recorded work into the frame.

`DrawingCanvas<TPixel>` is the typed implementation that carries the target pixel format for brush normalization,
readback, and backend execution. Factory methods return `DrawingCanvas` so CPU and GPU entry points expose the same
canvas-facing API while still constructing the typed implementation internally.

### Options

`DrawingOptions` is the per-state option bundle. It carries four things:

- `GraphicsOptions` for blending, antialiasing, and composition
- `IntersectionRule` selecting non-zero or even-odd filling
- `Transform`, a `Matrix4x4` applied to subject geometry and brushes
- `TextContrast`, the perceptual coverage boost applied only to antialiased text

There is no separate shape-options type; the fill rule and transform live directly on `DrawingOptions`. Explicit path boolean operations (`BooleanOperation`) exist only on the `IPath` `Clip(...)` geometry extensions, not in the drawing options.

`TextContrast` counters the soft, washed-out look of small antialiased glyphs: partial coverage is remapped after fill-rule resolution and before compositing. Only the dedicated text command path (`CreateTextCompositionCommand`) forwards it into `RasterizerOptions.CoverageBoost`; plain fills, strokes, and clips always rasterize with a zero boost, and aliased text ignores it. Both backends apply the same curve (the CPU rasterizer in `AreaToCoverage`, the WebGPU fine shader in `fill_path`), so text weight matches across backends.

#### The TextContrast curve

With `a` the resolved coverage in `[0, 1]` and `k` the clamped `TextContrast`:

```
f(a) = a + k · a · (1 - a) · (2a - 1)
```

The perturbation term `a(1-a)(2a-1)` is negative below half coverage and positive above it, so mostly-empty pixels lighten while mostly-covered pixels darken: glyph stems solidify and counters stay bright, which reads as sharpening rather than the uniform darkening of a weight boost. `0`, `1/2`, and `1` are exact fixed points.

Expanding shows the family is a plain blend between identity and smoothstep, because `a + a(1-a)(2a-1) = 3a² - 2a³`:

```
f(a) = (1 - k) · a + k · smoothstep(a)
```

Properties that make it safe to apply per pixel:

- **Range preserving.** `f([0,1]) = [0,1]`; no clamp is needed after the remap.
- **Monotone.** `f′(a) = (1 - k) + 6k·a(1 - a)`, so the slope is smallest at the endpoints (`1 - k`) and largest at the midpoint (`1 + k/2`). For `k ≤ 1` the curve is monotone: coverage ordering is preserved and gradients cannot band or invert.
- **Bounded shift.** The perturbation peaks at `a = 1/2 ± √3/6` with magnitude `√3/18 ≈ 0.0962`, so no pixel's coverage moves by more than `0.0962·k` (about `±0.05` at the default).
- **Bounded erosion.** For `a → 0` the multiplicative factor tends to `(1 - k)` (the endpoint slope), so a faint antialiasing fringe keeps at least `(1 - k)` of its coverage; nothing is ever removed and no value crosses the `1/2` midpoint. At the default `k = 0.5` this gives a simple guarantee: no antialiased sample loses more than half its value. Erosion of sub-half-pixel features (hairline stems in very light faces at very small sizes) grows linearly with `k`; `k = 1` (pure smoothstep) attenuates faint fringes quadratically and is the setting to avoid if hairline preservation matters more than contrast.

The darkening-only alternative `a + k·a(1-a)` (Skia's `apply_contrast` from `SkMaskGamma.cpp`) adds weight but blurs dense glyphs; Skia's full remap (that contrast term followed by sRGB linear-blend compensation) was tested and rejected because it reproduces Skia's *unhinted* rendering, which is lighter and fuzzier than its familiar hinted output. The S-curve was chosen from side-by-side comparisons against Skia across Latin and CJK samples at 8-14px.

### Batcher

`DrawingCanvasBatcher<TPixel>` is the deferred command queue. It stores pending `CompositionSceneCommand` values, records the canvas replay timeline, prepares commands during replay, and creates `DrawingCommandBatch` values for command-range entries.

It is the bridge between the immediate-looking public API and the deferred backend handoff.

### Command

`CompositionCommand` is the recorded unit of drawing intent for fills. In the common case it means "fill this path with this brush under these options". The same command stream also carries:

- explicit layer boundaries through `BeginLayer` and `EndLayer`
- explicit clip scopes through `BeginClip` and `EndClip`, where each begin-clip carries a `DrawingClipDescriptor`
- `Apply` barriers that run an image processor over a path region

Stroked geometry is recorded through dedicated command types (`StrokePathCommand`, `StrokeLineSegmentCommand`, `StrokePolylineCommand`). The batcher stores all of these behind the shared `CompositionSceneCommand` wrapper so the stream stays ordered.

The command remains relatively close to the original user request. It may hold the original path, pen, brush, and drawing options including the transform.

### Preparation

Preparation is the normalization step that turns recorded intent into backend-ready commands.

`DrawingCanvasBatcher<TPixel>.PrepareCommands(...)` runs only when needed. It does two things, in parallel across the command buffer when the buffer is large enough:

1. expands dashed strokes into dash geometry (`GenerateDashes`), because dashing changes the subject geometry before the backend retains raster payload
2. bakes non-identity command transforms into brush coordinates (`Brush.Transform`), so CPU and WebGPU do not each bake the transform in their own way

Preparation deliberately does not transform subject geometry, expand strokes to fills, or resolve clips. Geometry keeps `DrawingOptions.Transform` so backends can flatten curves scale-aware; strokes stay stroke commands so each backend can expand them in its own execution model; and the ordered begin/end-clip command stream remains the single source of truth for clipping.

Preparation stops at `DrawingCommandBatch`. Backend-specific lowering happens after that, inside `IDrawingBackend.CreateScene(...)`.

### Command Batch

`DrawingCommandBatch` is the prepared command range handed to the backend. It contains the command stream for one contiguous range and scene-level facts about that range: `HasLayers`, `HasApply`, and `HasClipControls`.

It is the backend handoff boundary.

### Backend

`IDrawingBackend` is the execution engine behind the canvas. The important implementations are:

- `DefaultDrawingBackend` for CPU rendering
- `WebGPUDrawingBackend` for GPU rendering through native surfaces

The backend creates retained scenes from command batches and renders retained scenes into typed target frames.

There are two backend-selection paths in the architecture:

- direct `DrawingCanvas<TPixel>` construction resolves the backend from `Configuration`
- specialized infrastructure can construct a canvas with an explicit backend

The ordinary CPU entry point is `Paint(...)` on `IImageProcessingContext`, which routes into the typed
implementation internally. Public `ImageFrame` canvas extensions provide the lower-level frame entry point for callers that want to own the canvas lifetime directly.

That explicit-backend path matters for the WebGPU helpers. `WebGPUWindow`, `WebGPUExternalSurface`, and `WebGPURenderTarget` create canvases that point directly at their owned `WebGPUDrawingBackend` instance instead of storing that backend on the caller's `Configuration`.

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

A public method such as `Fill(...)`, `Draw(...)`, or `DrawText(...)` resolves the active state and creates one or more commands.

At this point the canvas is mostly recording:

- geometry references
- brushes or pens
- the active drawing options, including the transform
- rasterizer options such as the interest rectangle and fill rule
- target bounds and destination offset relevant to this command

The canvas does not try to fully rasterize anything here. Clip changes are not attached to draw commands; they were already recorded as `BeginClip`/`EndClip` commands in the same stream when `Clip(...)` was called.

### Step 2: The batcher owns the pending work

Commands go into `DrawingCanvasBatcher<TPixel>`.

The batcher exists so the canvas does not need to talk to the backend for every single API call. It accumulates work until a timeline boundary is reached.

The replay boundary usually comes from:

- explicit `Flush()`, which seals the current command range
- `CreateScene()`, which turns the recorded work into a retained backend scene
- `RenderScene(...)`, which inserts an existing retained scene into the timeline
- `CopyPixelsFrom(...)`, which materializes both timelines before copying pixels
- disposal of the root canvas

`Apply(...)` is not a replay boundary. It records an apply barrier command inline in the stream; the backend orders execution around it during scene execution.

Every seal boundary must leave the clip stream balanced: the canvas closes the currently open clip scopes before sealing and reopens them for subsequent commands, because a clip stream cannot span backend scene boundaries.

### Step 3: The batcher prepares commands

When the root canvas is disposed, or when the caller creates a retained scene, the batcher seals any pending commands and prepares the command buffer. This is where the shared normalization that would otherwise be duplicated across backends happens.

For a typical command, canvas preparation does the following in concept:

1. if a pen with a stroke pattern is present, expand the dashes into the subject geometry
2. if the command transform is not identity, bake that transform into the brush coordinates
3. leave subject geometry transformation, stroke expansion, and clip resolution to the backend

This is the architectural center of gravity: normalization that must be identical across backends happens once here, and everything that benefits from backend-specific execution is deferred. Clipping in particular is never rewritten into the subject geometry; the ordered clip command stream is consumed by each backend directly. Explicit path boolean operations stay on the geometry APIs.

### Step 4: The backend creates and renders scenes

After preparation, disposal replay walks the canvas timeline in order. Command-range entries become short-lived retained scenes through `backend.CreateScene(...)`, and those scenes are then rendered through `backend.RenderScene(...)`.

From that point the CPU and GPU paths diverge.

The CPU backend lowers each command batch into a row-oriented retained representation through `FlushScene` during `CreateScene(...)` and then composites into memory during `RenderScene(...)`.

The WebGPU backend encodes each command batch into its retained GPU representation during `CreateScene(...)`, then uploads render-scoped resources and dispatches GPU work during `RenderScene(...)`.

The architecture is successful if both backends can differ dramatically here without needing the public canvas model itself to fork.

## Why State Is Snapshotted

Drawing APIs look stateful because they are stateful. The active options, transform, clips, and layer information all affect future commands.

`DrawingCanvasState` exists so that state changes are cheap to reason about and cheap to attach to commands.

The state snapshot contains the active options and target information for subsequent commands:

- `Options`, the active `DrawingOptions` reference
- `ClipState`, the normalized clip stack for the state
- `TargetBounds`, the absolute target bounds for commands recorded in this state
- `DestinationOffset`, the absolute offset for paths recorded in local coordinates
- `IsLayer` and `Layer`, marking layer scopes

The canvas treats this state as immutable snapshots on a stack. `Save()` pushes a copy. `Restore()` pops one. Drawing calls always read the current top-of-stack state.

That makes save and restore semantics predictable and backend-independent.

## How Clipping Works

Clipping is the part of the model where the recorded command stream, not per-command state, is authoritative.

`Clip(...)` narrows the active clip. It supports `ClipOperation.Intersection` and `ClipOperation.Difference`, builds one `DrawingClipDescriptor` per clip path, and does two things:

1. appends `BeginClip` commands to the stream, each carrying its descriptor and the state's destination offset
2. replaces the top-of-stack state with one whose `DrawingClipState` includes the new descriptors, so later `Save`/`Restore` and layer logic can reason about the clip stack

Descriptors classify the clip so backends can pick fast paths: `Rectangle`, `IntegerRegion`, `Region`, or general `Path` geometry. Rectangle and region metadata is captured before the transform is applied so axis-aligned cases survive translation and scaling.

The ordered begin/end-clip command stream is the single source of truth. Draw commands do not carry clip state; backends resolve the active clip stack from the stream commands surrounding each draw. `Restore()` closes the scopes the restored state no longer holds by appending matching `EndClip` commands.

Clip descriptors are recorded in canvas-local coordinates and anchored at the recording state's destination offset. When drawing moves to a differently-offset context, for example a child region canvas inheriting parent clips, the canvas calls `DrawingCanvasBatcher<TPixel>.EnsureClipAnchors(...)` before recording draws: the open clip stack is closed and reopened anchored at the new offset. Per-glyph draw offsets do not participate; anchoring always follows the state's destination offset.

## How Apply Works

`Apply(...)` runs a caller-supplied `IImageProcessingContext` operation over a path region as part of the recorded timeline.

The canvas records an `ApplyBarrier` command inline in the stream. When the command is lowered, its drawing options are cloned through `CloneForClearOperation`, which forces `PixelAlphaCompositionMode.Src` at full blend percentage: the processed pixels replace the covered region outright, including transparency, instead of blending over it.

Because an apply must observe everything drawn beneath it, backends treat it as an ordering barrier during scene execution. If an apply is recorded inside a layer, the affected layers are marked as requiring scoped rendering so the processor sees the isolated layer content.

## How Layers Work In This Architecture

Layer terminology often causes confusion because different systems use it differently. In this codebase, the most useful mental model is:

"A layer is a nested composition scope recorded inline in the command stream."

When `SaveLayer(...)` is called, the canvas:

1. resolves the requested layer bounds against the active transform and target bounds
2. records `BeginLayer` carrying the absolute layer bounds and a shared layer state object
3. pushes a state snapshot that marks the new layer scope

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
- keeps participating in the same deferred replay model
- inherits the parent's clip state; the shared clip stream is re-anchored at the child's destination offset before the child records draws

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
5. queue the final fill command using that brush, with an identity transform so the canvas transform is not applied twice

This design avoids applying the canvas transform twice and keeps the later command model consistent with brush-based filling.

That is why `DrawImage(...)` should be understood as "prepare an image-backed brush, then queue a normal fill", not as a completely separate rasterization pipeline.

## What The CPU Backend Receives

Once a command batch reaches `DefaultDrawingBackend.CreateScene(...)`, the public drawing model is already normalized.

The CPU backend does not need to understand every public API call individually. It works with:

- prepared commands
- layer boundaries, clip scopes, and apply barriers as ordered stream commands
- target bounds during `CreateScene(...)`
- the destination frame during `RenderScene<TPixel>(...)`

It lowers each command batch into a retained row-oriented structure through `FlushScene`. Later, `RenderScene<TPixel>(...)` acquires the CPU destination frame, allocates temporary backing buffers for layers when needed, and composites the final result into the target frame.

That is the payoff of the architecture: the CPU backend is solving a rendering problem, not a public-API interpretation problem.

## What The WebGPU Backend Receives

The WebGPU backend receives the same command batch shape, but it splits retained scene creation from render-scoped GPU work.

`CreateScene(...)` handles:

- encoding prepared command data

`RenderScene<TPixel>(...)` handles:

- creating render-scoped native resources
- planning dispatches
- executing the GPU pipeline

It benefits from the same canvas-level decisions:

- commands are already normalized
- layers and clips already exist as explicit ordered boundaries
- the frame already describes whether a native surface is available

The WebGPU public helpers reach this point in a target-first way:

- `WebGPUWindow` acquires a presentable native target per frame
- `WebGPURenderTarget` owns an offscreen native target for GPU drawing and readback
- `WebGPUExternalSurface` attaches WebGPU drawing to a caller-owned native host

Those helpers all create typed canvas instances with an explicit `WebGPUDrawingBackend`, so GPU execution stays attached to the WebGPU object that owns the native target and backend lifetime while callers work through `DrawingCanvas`.

The backend is free to choose a very different execution model because the canvas has already solved the shared semantics problem.

## The Practical Mental Model

If you are new to this code, the most useful mental model is:

`DrawingCanvas` is the stateful front end that records drawing intent, `DrawingCanvas<TPixel>` is the typed implementation, `DrawingCanvasBatcher<TPixel>` is the deferred handoff boundary, and the backend creates and renders retained scenes from prepared command batches.

Everything else serves that flow.

State snapshots exist so save and restore are precise.

Commands exist so public API calls can be deferred.

Preparation exists so backend-agnostic normalization happens once.

Frames exist so the same canvas can target memory, native surfaces, or sub-regions.

Layers and clips exist as inline ordered scopes in the command stream.

Once those ideas are clear, the code stops looking like a random collection of types and starts looking like one system with a clear division of responsibility.

## Reading Guide

If you want to move from the architecture into the code, this is the best order.

1. `DrawingCanvas.cs`
2. `DrawingCanvas{TPixel}.cs`
3. `DrawingCanvasFactoryExtensions.cs` and `DrawingCanvas.Shapes.cs`
4. `DrawingCanvasBatcher{TPixel}.cs`
5. `CompositionCommand.cs` and `DrawingClipDescriptor.cs`
6. `DefaultDrawingBackend.cs`
7. `FlushScene.cs`
8. `WebGPUEnvironment.cs`
9. `WebGPUWindow.cs`, `WebGPUExternalSurface.cs`, and `WebGPURenderTarget.cs`
10. `WebGPUDrawingBackend` and its scene/dispatch types

That path follows the real runtime flow:

public API -> recorded command -> prepared command batch -> backend scene creation -> backend scene rendering

Following the code in that order is much easier than starting from the backend internals first.
