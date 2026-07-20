# WebGPU Window Demo

`WebGPUWindowDemo` is the smallest end-to-end sample in the repo that renders `ImageSharp.Drawing` content directly into a native presentable window using the WebGPU backend.

It exists to show the intended shape of a real-time app:

- create a `WebGPUWindow`
- let the window own swapchain acquisition and presentation
- draw with the normal `DrawingCanvas` API
- present by ending the acquired frame

The sample opens an `800x600` window, draws a dark background, animates 1000 bouncing ellipses, scrolls a prepared rich-text document, and updates the window title with frame timing statistics.

## Why this sample matters

This demo is the clearest reference for the window-first WebGPU API surface:

- `WebGPUWindow` owns the OS window, WebGPU surface, adapter, device, queue, and swapchain configuration.
- `WebGPUSurfaceFrame` represents one acquired drawable frame.
- `WebGPUSurfaceFrame.Canvas` is the normal `DrawingCanvas` you already use elsewhere in ImageSharp.Drawing.
- disposing the frame renders pending canvas work, presents the surface texture, and releases the per-frame WebGPU handles.

That means sample code stays focused on drawing and animation instead of explicit texture acquisition, presentation, or interop setup.

## Running

```bash
dotnet run --project samples/WebGPUWindowDemo -c Debug
```

Requirements:

- .NET 8.0 SDK or later
- a WebGPU-capable desktop backend such as D3D12, Vulkan, or Metal
- adapter support for the storage-capable BGRA format selected by the sample

When the sample starts you should see:

- a native window titled `ImageSharp.Drawing WebGPU Demo`
- animated semi-transparent balls bouncing around the viewport
- a high-contrast scrolling rich-text document over a shader-accelerated frosted acrylic backdrop, with multiple sizes, bold and italic runs, multilingual fallback text, fills, outlines, and underline/overline/strikeout pens
- the title bar updating once per second with current frame time, current FPS, mean FPS, and FPS standard deviation

## Code Tour

Everything lives in [Program.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUWindowDemo/Program.cs).

### 1. Program startup

`Main()` creates the window and chooses the presentation mode:

```csharp
using WebGPUWindow window = new(new WebGPUWindowOptions
{
    Title = "ImageSharp.Drawing WebGPU Demo",
    Size = new Size(800, 600),
    Format = WebGPUTextureFormat.Bgra8Unorm,
    PresentMode = WebGPUPresentMode.Fifo,
});
```

Important details:

- `WebGPUTextureFormat.Bgra8Unorm` selects the swapchain format. The WebGPU factory creates the matching typed canvas internally.
- `WebGPUPresentMode.Fifo` gives normal v-synced presentation behavior.
- no manual WebGPU bootstrap code is needed in the sample; `WebGPUWindow` handles surface, adapter, device, queue, and swapchain setup internally.

### 2. DemoApp scene initialization

`DemoApp` owns the sample state:

- the window reference
- a deterministic `Random`
- the `Ball[]` animation state
- one prepared `TextBlock` and a caller-owned `DrawingTextCache` shared across frames
- FPS accumulation state

`InitializeScene()` does the expensive one-time work:

- creates a prepared `TextBlock` with several `RichTextRun` entries
- measures its initial wrapped height
- creates 1000 random balls sized and positioned for the current framebuffer

The important pattern is that text shaping is not done every frame. `DemoApp` also owns one `DrawingTextCache` and passes it to the window loop, allowing each short-lived frame canvas to reuse glyph and run geometry from previous frames.

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
this.window.Run(this.drawingOptions, this.textCache, this.OnRender);
```

`WebGPUWindow.Run(...)` acquires one `WebGPUSurfaceFrame` per render callback and disposes it automatically after your callback returns. In this sample that means you do not call `Flush()` yourself.

Inside `OnRender(...)` the sample:

1. grabs `DrawingCanvas canvas = frame.Canvas`
2. fills the full frame with a solid background color
3. fills one ellipse per ball
4. draws the scrolling text block inside a clipped `WebGPUBackdropAcrylicLayerEffect`
5. updates the window title once per second with timing statistics

The drawing code is intentionally plain `DrawingCanvas` API usage:

- `canvas.Fill(Brushes.Solid(...))` for the background
- `canvas.Fill(textBrush, path)` for text geometry
- `canvas.Fill(Brushes.Solid(ball.Color), ellipse)` for the balls

That is the point of the sample: the WebGPU path should feel like normal ImageSharp.Drawing usage, not a separate graphics API.

### 5. Prepared rich text and shared geometry

`DrawScrollingText(...)` shows the most important optimization in the sample.

Instead of reshaping text or rebuilding glyph paths every frame, it computes the wrapped vertical position and draws the prepared `TextBlock`. The text is isolated in a layer clipped to its visible bounds; `WebGPUBackdropAcrylicLayerEffect` blurs and tints the animated balls beneath that layer before the sharp text is composited over them. Rich runs demonstrate size and style changes, multilingual fallback, independent fills and outlines, and all three text decorations within one document. The caller-owned cache is deliberately independent of frame lifetime, so disposing a presented frame does not discard reusable text geometry.

## Frame lifetime and rendering

This sample uses the `Run(Action<WebGPUSurfaceFrame>)` overload, so frame lifetime is important:

1. the window acquires the current surface texture
2. the frame wraps that texture in a `DrawingCanvas`
3. your render callback queues draw operations
4. frame disposal renders the queued canvas work and presents the surface
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
if (window.TryAcquireFrame(out WebGPUSurfaceFrame? frame))
{
    using (frame)
    {
        DrawingCanvas canvas = frame.Canvas;
        canvas.Fill(Brushes.Solid(Color.Black));
        canvas.Fill(Brushes.Solid(Color.CornflowerBlue), new EllipsePolygon(200, 150, 80));
    }
}
```

Notes:

- a `false` result is normal retry behavior, not necessarily an error
- this can happen when the surface is outdated, lost, timed out, or the framebuffer is currently zero-sized
- disposing the frame renders queued canvas work, presents the surface, and releases per-frame resources

## Resize behavior

The sample shapes the scrolling text once. A resize changes only the wrapping length and measured height; it does not reshape the document.

As a result:

- the animation keeps working after resize because balls update against the current framebuffer size
- the text continues to render
- the rich text reflows to the current framebuffer width

## Files

- [Program.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUWindowDemo/Program.cs): the entire sample
- [WebGPUWindowDemo.csproj](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUWindowDemo/WebGPUWindowDemo.csproj): sample project file
- [README.md](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/WebGPUWindowDemo/README.md): this document
