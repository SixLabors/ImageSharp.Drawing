# WebGPU Window Demo

`WebGPUWindowDemo` is the smallest end-to-end sample in the repo that renders `ImageSharp.Drawing` content directly into a native presentable window using the WebGPU backend.

It exists to show the intended shape of a real-time app:

- create a `WebGPUWindow<Bgra32>`
- let the window own swapchain acquisition and presentation
- draw with the normal `DrawingCanvas<Bgra32>` API
- present by ending the acquired frame

The sample opens an `800x600` window, draws a dark background, animates 1000 bouncing ellipses, scrolls a block of pre-shaped text, and updates the window title with frame timing statistics.

## Why this sample matters

This demo is the clearest reference for the window-first WebGPU API surface:

- `WebGPUWindow<TPixel>` owns the OS window, WebGPU surface, adapter, device, queue, and swapchain configuration.
- `WebGPUSurfaceFrame<TPixel>` represents one acquired drawable frame.
- `WebGPUSurfaceFrame<TPixel>.Canvas` is the normal `DrawingCanvas<TPixel>` you already use elsewhere in ImageSharp.Drawing.
- disposing the frame flushes pending canvas work, presents the surface texture, and releases the per-frame WebGPU handles.

That means sample code stays focused on drawing and animation instead of explicit texture acquisition, presentation, or interop setup.

## Running

```bash
dotnet run --project samples/WebGPUWindowDemo -c Debug
```

Requirements:

- .NET 8.0 SDK or later
- a WebGPU-capable desktop backend such as D3D12, Vulkan, or Metal
- adapter support for the storage-capable BGRA format required by `Bgra32`

When the sample starts you should see:

- a native window titled `ImageSharp.Drawing WebGPU Demo`
- animated semi-transparent balls bouncing around the viewport
- a large scrolling text block in the background
- the title bar updating once per second with current frame time, current FPS, mean FPS, and FPS standard deviation

## Code Tour

Everything lives in [Program.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUWindowDemo/Program.cs).

### 1. Program startup

`Main()` creates the window and chooses the presentation mode:

```csharp
using WebGPUWindow<Bgra32> window = new(new WebGPUWindowOptions
{
    Title = "ImageSharp.Drawing WebGPU Demo",
    Size = new Size(800, 600),
    PresentMode = WebGPUPresentMode.Fifo,
});
```

Important details:

- `Bgra32` is the pixel type for the canvas and must match the swapchain format expected by the WebGPU backend.
- `WebGPUPresentMode.Fifo` gives normal v-synced presentation behavior.
- no manual WebGPU bootstrap code is needed in the sample; `WebGPUWindow<TPixel>` handles surface, adapter, device, queue, and swapchain setup internally.

### 2. DemoApp scene initialization

`DemoApp` owns the sample state:

- the window reference
- a deterministic `Random`
- the `Ball[]` animation state
- cached text paths
- FPS accumulation state

`InitializeScene()` does the expensive one-time work:

- creates an `Arial` font at 24px
- builds `TextOptions` using the current framebuffer width
- shapes the scrolling text once with `TextBuilder.GeneratePaths(...)`
- measures the total text height with `TextMeasurer.MeasureSize(...)`
- creates 1000 random balls sized and positioned for the current framebuffer

The important pattern here is that text shaping is not done every frame. The sample converts the whole text block into vector paths once, then reuses that geometry as the text scrolls.

### 3. Update loop

`DemoApp` subscribes to `window.Update` in its constructor:

```csharp
this.window.Update += this.OnUpdate;
```

`OnUpdate(TimeSpan deltaTime)` performs simulation only:

- each ball advances by `velocity * dt`
- each ball reflects off the framebuffer edges
- the text scroll offset advances at `200` pixels per second

Separating animation from rendering keeps the sample structure close to a normal game or interactive tool.

### 4. Render loop

`Run()` calls:

```csharp
this.window.Run(this.OnRender);
```

`WebGPUWindow<TPixel>.Run(...)` acquires one `WebGPUSurfaceFrame<TPixel>` per render callback and disposes it automatically after your callback returns. In this sample that means you do not call `Flush()` yourself.

Inside `OnRender(...)` the sample:

1. grabs `DrawingCanvas<Bgra32> canvas = frame.Canvas`
2. fills the full frame with a solid background color
3. draws the scrolling text block
4. fills one ellipse per ball
5. updates the window title once per second with timing statistics

The drawing code is intentionally plain `DrawingCanvas` API usage:

- `canvas.Fill(Brushes.Solid(...))` for the background
- `canvas.Fill(textBrush, path)` for text geometry
- `canvas.Fill(Brushes.Solid(ball.Color), ellipse)` for the balls

That is the point of the sample: the WebGPU path should feel like normal ImageSharp.Drawing usage, not a separate graphics API.

### 5. Scrolling text path reuse

`DrawScrollingText(...)` shows the most important optimization in the sample.

Instead of rebuilding glyphs every frame, it:

- computes a wrapped vertical scroll offset
- builds a translation matrix for the current frame
- saves a transformed canvas state with `canvas.Save(translatedOptions)`
- culls any path whose translated bounds are outside the viewport
- fills only the visible paths
- restores the prior canvas state with `canvas.Restore()`

The culling is simple but effective: large amounts of off-screen text never get submitted for rasterization.

## Frame lifetime and flushing

This sample uses the `Run(Action<WebGPUSurfaceFrame<TPixel>>)` overload, so frame lifetime is important:

1. the window acquires the current surface texture
2. the frame wraps that texture in a `DrawingCanvas<TPixel>`
3. your render callback queues draw operations
4. frame disposal flushes the canvas and presents the surface
5. the frame releases the texture and texture view

Two practical consequences:

- you do not need to call `canvas.Flush()` in this sample
- manual frame loops should dispose each acquired frame exactly once

## What actually runs on the GPU

The sample renders into a real native presentable surface. The final destination is GPU-native, but the pipeline is still hybrid:

- vector scene preparation and coverage generation happen through the normal drawing backend flow
- the WebGPU backend uploads the prepared data to GPU resources
- final composition into the swapchain texture happens on the GPU through WebGPU compute work

So this demo is best understood as "ImageSharp.Drawing rendered into a native WebGPU window target" rather than "every drawing step is implemented as pure GPU vector rasterization."

## Manual frame loop option

If you want control over your own loop instead of `Run(...)`, use `TryAcquireFrame(...)`:

```csharp
if (window.TryAcquireFrame(out WebGPUSurfaceFrame<Bgra32>? frame))
{
    using (frame)
    {
        DrawingCanvas<Bgra32> canvas = frame.Canvas;
        canvas.Fill(Brushes.Solid(Color.Black));
        canvas.Fill(Brushes.Solid(Color.CornflowerBlue), new EllipsePolygon(200, 150, 80));
    }
}
```

Notes:

- a `false` result is normal retry behavior, not necessarily an error
- this can happen when the surface is outdated, lost, timed out, or the framebuffer is currently zero-sized
- disposing the frame flushes queued canvas work, presents the surface, and releases per-frame resources

## Resize behavior

The sample builds the scrolling text layout once from the startup framebuffer size. That keeps the demo simple and avoids reshaping text during the steady-state render loop.

As a result:

- the animation keeps working after resize because balls update against the current framebuffer size
- the text continues to render
- the text wrapping width is based on the initial framebuffer width, not a reflowed width after resize

That tradeoff is acceptable for a demo because the sample is trying to show rendering flow, cached path reuse, and frame presentation rather than full responsive layout management.

## Files

- [Program.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUWindowDemo/Program.cs): the entire sample
- [WebGPUWindowDemo.csproj](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUWindowDemo/WebGPUWindowDemo.csproj): sample project file
- [README.md](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUWindowDemo/README.md): this document
