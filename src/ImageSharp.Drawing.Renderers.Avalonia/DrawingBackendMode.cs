// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Selects the rendering backend used by the ImageSharp.Drawing Avalonia renderer.
/// </summary>
public enum DrawingBackendMode
{
    /// <summary>
    /// Uses WebGPU when Avalonia exposes a compatible native surface; otherwise uses the CPU framebuffer.
    /// </summary>
    Auto,

    /// <summary>
    /// Uses the CPU framebuffer renderer.
    /// </summary>
    Cpu,

    /// <summary>
    /// Requires a WebGPU-compatible native surface.
    /// </summary>
    WebGpu
}
