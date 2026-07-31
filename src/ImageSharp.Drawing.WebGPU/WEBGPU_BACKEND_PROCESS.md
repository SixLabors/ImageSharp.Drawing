# WebGPU Backend Docs

The WebGPU documentation is split into two newcomer-first documents:

- [`WEBGPU_BACKEND.md`](d:/GitHub/SixLabors/ImageSharp.Drawing/src/ImageSharp.Drawing.WebGPU/WEBGPU_BACKEND.md)
  Explains how `WebGPUEnvironment`, the public target types, and `WebGPUDrawingBackend` fit together, how retained scene creation reaches the GPU path (including the parallel and ordered encoders), where explicit support probing fits, how explicit layers and ordered Apply/scoped-layer scenes execute, how the scratch-growth retry loop and typed readback work, and how runtime/device-scoped state relates to flush-scoped work.

- [`WEBGPU_RASTERIZER.md`](d:/GitHub/SixLabors/ImageSharp.Drawing/src/ImageSharp.Drawing.WebGPU/WEBGPU_RASTERIZER.md)
  Explains the staged scene pipeline itself: scene encoding (including parallel partitioned encoding), packed scene format types, planning, resource creation, scheduling passes, GPU stroking in the flatten stage, fine rasterization, the scratch overflow protocol, chunked oversized-scene execution, and submission.

If you are new to the GPU path, read them in this order:

1. [`WEBGPU_BACKEND.md`](d:/GitHub/SixLabors/ImageSharp.Drawing/src/ImageSharp.Drawing.WebGPU/WEBGPU_BACKEND.md)
2. [`WEBGPU_RASTERIZER.md`](d:/GitHub/SixLabors/ImageSharp.Drawing/src/ImageSharp.Drawing.WebGPU/WEBGPU_RASTERIZER.md)
