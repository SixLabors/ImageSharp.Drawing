// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Creates a bind group layout for WebGPU composition pipelines.
/// </summary>
/// <param name="api">The WebGPU API facade.</param>
/// <param name="device">The device used to create resources.</param>
/// <param name="bindGroupLayout">The created bind-group layout.</param>
/// <param name="error">The error message when creation fails.</param>
/// <returns><see langword="true"/> if the layout was created; otherwise <see langword="false"/>.</returns>
internal unsafe delegate bool WebGPUCompositeBindGroupLayoutFactory(
    WebGPU api,
    Device* device,
    out BindGroupLayout* bindGroupLayout,
    out string? error);
