# WebGPU Backend Docs

The WebGPU documentation is split into two newcomer-first documents:

- [`WEBGPU_BACKEND.md`](d:/GitHub/SixLabors/ImageSharp.Drawing/src/ImageSharp.Drawing.WebGPU/WEBGPU_BACKEND.md)
  Explains what `WebGPUDrawingBackend` owns, how a flush reaches the GPU path, where fallback lives, how layer composition fits in, and how runtime/device-scoped state relates to flush-scoped work.

- [`WEBGPU_RASTERIZER.md`](d:/GitHub/SixLabors/ImageSharp.Drawing/src/ImageSharp.Drawing.WebGPU/WEBGPU_RASTERIZER.md)
  Explains the staged scene pipeline itself: scene encoding, planning, resource creation, scheduling passes, fine rasterization, chunked oversized-scene execution, and submission.

If you are new to the GPU path, read them in this order:

1. [`WEBGPU_BACKEND.md`](d:/GitHub/SixLabors/ImageSharp.Drawing/src/ImageSharp.Drawing.WebGPU/WEBGPU_BACKEND.md)
2. [`WEBGPU_RASTERIZER.md`](d:/GitHub/SixLabors/ImageSharp.Drawing/src/ImageSharp.Drawing.WebGPU/WEBGPU_RASTERIZER.md)
