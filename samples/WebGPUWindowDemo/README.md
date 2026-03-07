# WebGPU Window Demo

A real-time sample application that renders directly to a native window swap chain using the ImageSharp.Drawing WebGPU backend. Bouncing ellipses and vertically scrolling text are composited each frame via the `DrawingCanvas<Bgra32>` API, with all composition performed by a WebGPU compute pipeline.

## What it demonstrates

- **Native surface rendering** — The swap chain texture is wrapped as a `NativeSurface` and passed to the canvas via `ICanvasFrame<Bgra32>`. The backend composites directly into the swap chain target without CPU readback.
- **WebGPU bootstrap** — Full Silk.NET WebGPU initialization: instance → surface → adapter → device → queue, with `Bgra8UnormStorage` requested for compute storage writes to `Bgra8Unorm` textures.
- **Pre-built text geometry** — `TextBuilder.GeneratePaths` shapes the text once at startup. Each frame translates the cached `IPathCollection` with a `Matrix3x2` — no re-shaping or re-building of glyph outlines.
- **Viewport culling** — Only glyph paths whose translated bounding boxes intersect the visible window are submitted for rasterization, keeping frame times low even with large text blocks.
- **Canvas API** — `Fill` for solid backgrounds, `Fill(IPath, Brush)` for ellipses and glyph outlines, `Flush` to submit all queued operations to the GPU compositor.

## Prerequisites

- .NET 8.0 SDK or later
- A GPU with Vulkan, Metal, or D3D12 support
- The adapter must support the `Bgra8UnormStorage` feature (most desktop GPUs do)

## Running

```bash
dotnet run --project samples/WebGPUWindowDemo -c Debug
```

An 800×600 window opens showing colored ellipses bouncing off the walls with scrolling descriptive text in the background. The window title displays the current FPS.

## Architecture

Each frame follows this sequence:

1. `SurfaceGetCurrentTexture` — acquire the next swap chain texture
2. `WebGPUNativeSurfaceFactory.Create<Bgra32>(...)` — wrap it as a `NativeSurface`
3. `NativeSurfaceOnlyFrame<Bgra32>` — wrap as `ICanvasFrame` (GPU path only, no CPU region)
4. `new DrawingCanvas<Bgra32>(config, frame, options)` — create the canvas
5. `canvas.Fill(...)` — queue background, text glyphs, and ellipses
6. `canvas.Flush()` — rasterize coverage masks on CPU, upload to GPU, composite via compute shader, copy result to swap chain texture
7. `SurfacePresent()` — present the frame

## Performance

| Scenario | FPS (typical) |
|---|---|
| Balls only (no text) | ~120 |
| Balls + scrolling text | 65–120 |

FPS varies with how much text is currently visible in the viewport.
