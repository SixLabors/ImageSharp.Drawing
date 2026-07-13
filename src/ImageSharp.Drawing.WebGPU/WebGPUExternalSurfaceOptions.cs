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
    public WebGPUPresentMode PresentMode { get; set; } = WebGPUPresentMode.Fifo;

    /// <summary>
    /// Gets or sets the swapchain texture format used by acquired frames.
    /// </summary>
    /// <remarks>
    /// The value must be one of the <see cref="WebGPUTextureFormat"/> members the backend can render
    /// into. <see cref="WebGPUTextureFormat.Bgra8Unorm"/> is the common swapchain format but requires
    /// the optional <c>bgra8unorm-storage</c> device feature, which the backend enables when the
    /// adapter reports it.
    /// </remarks>
    public WebGPUTextureFormat Format { get; set; } = WebGPUTextureFormat.Rgba8Unorm;

    /// <summary>
    /// Gets or sets how the native compositor interprets the surface alpha channel.
    /// </summary>
    public WebGPUCompositeAlphaMode AlphaMode { get; set; } = WebGPUCompositeAlphaMode.Auto;
}
