// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Options for creating a <see cref="WebGPUExternalSurface"/>.
/// </summary>
/// <remarks>
/// Only values that are meaningful for an externally-owned surface are exposed here.
/// Lifecycle, title, position, border, and state belong to the host application and its UI framework.
/// </remarks>
public sealed class WebGPUExternalSurfaceOptions
{
    /// <summary>
    /// Gets or sets how completed frames are queued for presentation to the display.
    /// </summary>
    /// <remarks>
    /// When the requested mode is unavailable, <see cref="WebGPUPresentMode.Fifo"/> is used.
    /// </remarks>
    public WebGPUPresentMode PresentMode { get; set; } = WebGPUPresentMode.Fifo;

    /// <summary>
    /// Gets or sets the swapchain texture format used by acquired frames.
    /// </summary>
    /// <remarks>
    /// The requested format is used when available. Otherwise, a compatible format is selected automatically.
    /// </remarks>
    public WebGPUTextureFormat Format { get; set; } = WebGPUTextureFormat.Rgba8Unorm;

    /// <summary>
    /// Gets or sets how the native compositor interprets the surface alpha channel.
    /// </summary>
    /// <remarks>
    /// The requested mode is used when available. Otherwise, a compatible mode is selected automatically.
    /// </remarks>
    public WebGPUCompositeAlphaMode AlphaMode { get; set; } = WebGPUCompositeAlphaMode.Auto;
}
