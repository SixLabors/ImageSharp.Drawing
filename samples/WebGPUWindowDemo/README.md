# WebGPU Window Demo

A real-time sample application that renders directly to a native window swap chain using the ImageSharp.Drawing WebGPU backend. Bouncing ellipses and vertically scrolling text are composited each frame via the `DrawingCanvas<Bgra32>` API, with all composition performed by a WebGPU compute pipeline.

## What it demonstrates

- **Window-first rendering** - `WebGPUWindow<Bgra32>` owns the native window, per-frame setup, and presentation so the sample code stays focused on drawing.
- **Native surface rendering** - Each acquired swapchain texture is wrapped behind the window frame canvas. The backend composites directly into the swapchain target without CPU readback.
- **Pre-built text geometry** - `TextBuilder.GeneratePaths` shapes the text once at startup. Each frame translates the cached `IPathCollection` with a `Matrix3x2` without re-shaping or re-building glyph outlines.
- **Viewport culling** - Only glyph paths whose translated bounding boxes intersect the visible window are submitted for rasterization, keeping frame times low even with large text blocks.
- **Canvas API** - `Fill` for solid backgrounds, `Fill(IPath, Brush)` for ellipses and glyph outlines, `Flush` to submit all queued operations to the GPU compositor.

## Prerequisites

- .NET 8.0 SDK or later
- A GPU with Vulkan, Metal, or D3D12 support
- The adapter must support the `Bgra8UnormStorage` feature (most desktop GPUs do)

## Running

```bash
dotnet run --project samples/WebGPUWindowDemo -c Debug
```

An 800x600 window opens showing colored ellipses bouncing off the walls with scrolling descriptive text in the background. The window title displays the current FPS.

## Architecture

Each frame follows this sequence:

1. `WebGPUWindow<Bgra32>` acquires the next swapchain texture and view
2. `window.Run(...)` hands the sample a `DrawingCanvas<Bgra32>`
3. `canvas.Fill(...)` queues background, text glyphs, and ellipses
4. `canvas.Flush()` rasterizes coverage masks on CPU, uploads to GPU, and composites via compute shader
5. `WebGPUWindowFrame<TPixel>.Dispose()` flushes pending canvas work, presents the frame, and releases the per-frame WebGPU resources

For manual render loops, use `TryAcquireFrame(...)` instead of `Run(...)`. A `false` result means the frame should be retried later rather than treated as an exceptional failure. The returned frame is normally ended by disposing it; `Present()` is only needed if you want to present explicitly before disposal.

## Performance

| Scenario | FPS (typical) |
|---|---|
| Balls only (no text) | ~120 |
| Balls + scrolling text | 65-120 |

FPS varies with how much text is currently visible in the viewport.
