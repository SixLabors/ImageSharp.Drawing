# Reusable Backend Scene

## Goal

Canvas drawing should have one execution model.

`DrawingCanvas` records an ordered timeline. Disposing the root canvas renders that timeline once. A retained scene is a backend payload created from prepared draw commands; inserting that scene into a later canvas adds it to that later canvas timeline.

There is no separate immediate renderer. The non-retained path creates short-lived backend scenes during disposal, renders them once, and disposes them.

## Core Model

The canvas is the timeline container.

Backend scenes stay light:

- `DefaultDrawingBackendScene` owns CPU retained draw data.
- `WebGPUDrawingBackendScene` owns WebGPU encoded draw data and reusable render arenas.
- `DrawingBackendScene` is the public retained scene base.

Backend scenes do not contain the canvas timeline. They do not store apply barriers. They do not store child scenes.

## Terms

- **Draw command**: an existing canvas operation that becomes backend drawing work, for example fill, stroke, text, image draw, or layer markers.
- **Command range**: a contiguous run of draw commands sealed into the canvas timeline.
- **Backend scene**: a retained payload created by a backend from one prepared `DrawingCommandBatch`.
- **Inserted scene**: an existing `DrawingBackendScene` recorded through `DrawingCanvas.RenderScene(...)`.
- **Apply barrier**: a target-dependent timeline entry that reads the target, runs ImageSharp processors, and writes the processed snapshot back.
- **Flush barrier**: a command-range boundary created by `DrawingCanvas.Flush()`. It renders nothing by itself.

## Canvas Pipeline

`DrawingCanvas` remains the public recording API.

The pipeline is:

1. Draw operations append commands to `DrawingCanvasBatcher<TPixel>`.
2. `Flush()` seals the current commands into a command-range timeline entry.
3. `Apply(...)` seals current commands and appends an apply barrier.
4. `RenderScene(...)` seals current commands and appends an existing retained scene reference.
5. `Dispose()` restores open canvas state, seals and prepares commands, then replays the timeline once.

During disposal replay:

- command-range entries become short-lived backend scenes through `IDrawingBackend.CreateScene(...)`
- apply-barrier entries create a transient write-back command batch and render it through the same backend scene path
- inserted-scene entries call `IDrawingBackend.RenderScene(...)` on the retained scene supplied by the caller

`RenderScene(...)` is not a target write by itself. It records a retained scene reference at the current timeline position. The scene is rendered only when the canvas containing that entry is disposed.

`Flush()` is also not a target write by itself. It only makes the current command range explicit so later timeline entries cannot merge across that boundary.

```csharp
image.Mutate(x => x.Paint(canvas =>
{
    // These commands become the first command-range entry.
    canvas.Fill(backgroundBrush, backgroundPath);

    // Apply seals the first range and records a read/process/write barrier.
    canvas.Apply(effectPath, ctx => ctx.GaussianBlur(6F));

    // These commands become a later command-range entry.
    canvas.Draw(foregroundPen, foregroundPath);
}));
```

The callback above renders once during canvas disposal.

## Backend Contract

Scene creation is untyped and target-frame free. It only needs the active configuration, target bounds, prepared draw commands, and any resources that must remain alive with the scene.

```csharp
public interface IDrawingBackend
{
    public DrawingBackendScene CreateScene(
        Configuration configuration,
        Rectangle targetBounds,
        DrawingCommandBatch commandBatch,
        IReadOnlyList<IDisposable>? ownedResources = null);

    public void RenderScene<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        DrawingBackendScene scene)
        where TPixel : unmanaged, IPixel<TPixel>;

    public void ReadRegion<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        Buffer2DRegion<TPixel> destination)
        where TPixel : unmanaged, IPixel<TPixel>;
}
```

`CreateScene(...)` does not validate `TPixel`, because there is no `TPixel` at scene creation time.

`RenderScene<TPixel>(...)` is the typed target boundary. Backends validate the retained scene type, target capabilities, target bounds, and pixel format requirements there.

`ReadRegion<TPixel>(...)` is the typed readback boundary. Apply barriers use it to copy target pixels into a temporary `Image<TPixel>`.

## DrawingCommandBatch

`DrawingCommandBatch` is the backend handoff for one contiguous command range.

It contains:

- the prepared `CompositionSceneCommand` list
- the command count
- whether the range contains layer boundary commands

One command batch creates one backend scene.

## Apply Barrier

`ApplyBarrier` is shared canvas timeline data, not CPU or WebGPU scene data.

It stores the recorded processor operation and the geometry/state needed to reproduce the readback region at replay time:

- path
- drawing options
- clip paths
- canvas bounds
- target bounds
- destination offset
- layer state
- processor delegate

When disposal replay reaches an apply barrier:

1. The barrier computes the target-local source rectangle.
2. The backend reads that region into a temporary `Image<TPixel>`.
3. The processor delegate runs against the temporary image.
4. The barrier creates a transient image-brush draw command.
5. The canvas renders that command batch through `CreateScene(...)` and `RenderScene(...)`.
6. The temporary image is disposed.

The processed image is per-render temporary state. A retained backend scene never stores pixels produced by an apply barrier.

## CPU Scene

`DefaultDrawingBackendScene` stores a CPU `FlushScene`.

`DefaultDrawingBackend.CreateScene(...)` lowers a prepared command batch into `FlushScene` using target bounds and the active configuration. It does not need the target frame and does not require a `TPixel`.

`DefaultDrawingBackend.RenderScene<TPixel>(...)` acquires the CPU destination region from the target, validates the retained scene type and bounds, and executes the `FlushScene` into that destination.

## WebGPU Scene

`WebGPUDrawingBackendScene` stores a `WebGPUEncodedScene`.

It also owns:

- one reusable resource arena slot
- one reusable scheduling arena slot
- the retained scratch-size budget discovered across renders
- the backend that should receive cached arenas on disposal

`WebGPUDrawingBackend.CreateScene(...)` only encodes the command batch. It uses target bounds and the allocator, but it does not create a `WebGPUFlushContext`, does not validate `TPixel`, and does not store the target texture format.

`WebGPUDrawingBackend.RenderScene<TPixel>(...)` owns the render-scoped WebGPU work:

1. Resolve `TPixel` to the supported WebGPU texture format and required feature.
2. Validate that the target exposes a native WebGPU surface with matching bounds and device support.
3. Rent the retained scene arenas or backend arenas.
4. Create the staged scene resources for the current render.
5. Dispatch the staged GPU pipeline.
6. Update retained scratch sizes.
7. Return arenas to the scene or backend cache.

The retained WebGPU scene does not store texture format or required feature. Those belong to the typed render target boundary.

## Pixel Format Rule

`WebGPUTextureFormat` is the public closed set of supported WebGPU formats. Internal code does not repeatedly validate it.

`TPixel` is validated only when typed pixels are introduced:

- `RenderScene<TPixel>(...)`, when rendering to an `ICanvasFrame<TPixel>`
- `ReadRegion<TPixel>(...)`, when copying target pixels into CPU memory
- public typed readback helpers, through the backend readback path

## Ownership

Pending image resources move into the backend scene that references them.

Apply snapshots are not retained. They are created and disposed during barrier execution.

The retained scene owns:

- its backend payload
- resources captured by that payload
- reusable backend arenas or scratch state

Disposing the retained scene releases its backend payload and owned resources.

## Behavioral Summary

- `Dispose()` is the canvas operation that writes the recorded timeline to the target.
- `Flush()` seals pending commands into the recorded timeline and renders nothing.
- `Apply(...)` records a replay-time barrier and renders nothing immediately.
- `RenderScene(...)` records an existing retained scene reference and renders nothing immediately.
- `CreateScene(...)` creates a retained backend payload from prepared draw commands and renders nothing.
- `RenderScene<TPixel>(...)` is the typed target write boundary.
- One `DrawingCommandBatch` creates one backend scene.
- The canvas timeline preserves command ranges, apply barriers, and inserted retained scenes in recorded order.
