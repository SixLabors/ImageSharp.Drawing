// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace AvaloniaControlCatalog;

/// <summary>
/// Selects the rendering backend used by the ImageSharp.Drawing Avalonia sample.
/// </summary>
internal enum DrawingBackendMode
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
