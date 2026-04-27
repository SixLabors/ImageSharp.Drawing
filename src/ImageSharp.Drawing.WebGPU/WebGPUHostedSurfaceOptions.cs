// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Options for creating a <see cref="WebGPUHostedSurface{TPixel}"/>.
/// </summary>
/// <remarks>
/// Only values that are meaningful for an externally-owned surface are exposed here.
/// Lifecycle, title, position, border, and state belong to the host application and its UI framework.
/// </remarks>
public sealed class WebGPUHostedSurfaceOptions
{
    /// <summary>
    /// Gets or sets how completed frames are queued for presentation to the display.
    /// </summary>
    public WebGPUPresentMode PresentMode { get; set; } = WebGPUPresentMode.Fifo;
}
