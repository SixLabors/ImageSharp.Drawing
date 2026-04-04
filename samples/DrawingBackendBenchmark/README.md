# Drawing Backend Benchmark

`DrawingBackendBenchmark` is a small Windows sample that renders the same randomized line scene through several drawing backends and shows the last rendered frame alongside timing statistics.

It is based on the original ImageSharp benchmark workload from the `Csharp-Data-Visualization` repo:

https://swharden.com/csdv/platforms/compare/

## What it compares

Depending on what the machine can initialize, the sample can show:

- `CPU`: the default `ImageSharp.Drawing` CPU backend
- `SkiaSharp (CPU)`: Skia rasterized in CPU mode
- `SkiaSharp (GPU)`: Skia using a GPU-backed `GRContext` when the hidden GL host is available
- `WebGPU`: the offscreen `ImageSharp.Drawing.WebGPU` backend

The sample always starts with the CPU and Skia CPU paths. The WebGPU backend is added only when `WebGPUEnvironment.TryProbeComputePipelineSupport(...)` succeeds. The Skia GPU backend is added once the hidden `SKGLControl` has produced a usable `GRContext`.

## What the UI shows

The benchmark window contains:

- a backend selector
- an iteration count selector
- preset buttons for `10`, `1k`, `10k`, and `100k` lines
- a preview of the final rendered frame
- current render time, running mean, and standard deviation

The fixed benchmark surface is `600x400` pixels.

## Running

```powershell
dotnet run --project samples/DrawingBackendBenchmark -c Release
```

## How one benchmark run works

For each iteration the form:

1. generates a deterministic random line set for the requested line count
2. renders that scene through the selected backend
3. records the elapsed render time
4. updates the running mean and standard deviation
5. captures a preview image only on the last iteration

That last point matters: preview capture is intentionally outside the measured timing path. The reported time measures scene submission and backend flush work only. Any readback, clone, or bitmap conversion needed for the UI happens afterward.

## Timing model

All backends follow the same basic timing rule:

- start the stopwatch immediately before drawing the scene
- stop it immediately after the backend flush or submission boundary
- capture preview pixels only after the stopwatch stops

In practice that means:

- `CPU` measures drawing into the cached `Image<Bgra32>` through `DrawingCanvas.Flush()`
- `SkiaSharp` measures drawing through `SKCanvas.Flush()` and optional GPU context flush
- `WebGPU` measures drawing through `DrawingCanvas.Flush()` into the offscreen `WebGPURenderTarget<Bgra32>`

So the numbers are comparable as "render and submit" timings, not "render plus preview extraction" timings.

## Backend resource reuse

The sample uses a fixed benchmark size, so the backends keep their render targets alive across iterations instead of reallocating them every run:

- `CpuBenchmarkBackend` caches one `Image<Bgra32>`
- `SkiaSharpBenchmarkBackend` caches one `SKSurface`
- `WebGpuBenchmarkBackend` caches one `WebGPURenderTarget<Bgra32>`

This keeps the benchmark focused on scene rendering rather than repeated target allocation noise.

## WebGPU path

The WebGPU backend is intentionally small:

- it probes support up front with `WebGPUEnvironment.TryProbeComputePipelineSupport(...)`
- it renders into an owned offscreen `WebGPURenderTarget<Bgra32>`
- it draws through `CreateCanvas(...)`, not a hybrid CPU plus GPU canvas
- it reads back the final frame only when the UI requests the last-iteration preview

The status line also reports whether the last WebGPU flush completed on the staged GPU path or had to fall back to CPU execution.

## File guide

- [BenchmarkForm.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/DrawingBackendBenchmark/BenchmarkForm.cs): WinForms UI, backend selection, iteration loop, and preview display
- [CpuBenchmarkBackend.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/DrawingBackendBenchmark/CpuBenchmarkBackend.cs): ImageSharp CPU baseline backend
- [SkiaSharpBenchmarkBackend.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/DrawingBackendBenchmark/SkiaSharpBenchmarkBackend.cs): Skia CPU and GPU benchmark backend
- [WebGpuBenchmarkBackend.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/DrawingBackendBenchmark/WebGpuBenchmarkBackend.cs): offscreen WebGPU benchmark backend
- [VisualLine.cs](d:/GitHub/SixLabors/ImageSharp.Drawing/samples/DrawingBackendBenchmark/VisualLine.cs): shared randomized line scene description and canvas render helper
