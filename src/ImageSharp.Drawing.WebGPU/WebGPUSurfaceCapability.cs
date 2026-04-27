// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Native WebGPU surface capability exposed by a <see cref="NativeSurface"/>.
/// </summary>
/// <remarks>
/// The backing WebGPU device, queue, texture, and texture view must remain valid while canvases target this surface.
/// </remarks>
internal sealed class WebGPUSurfaceCapability
{
    private readonly WebGPUDeviceHandle deviceHandle;
    private readonly WebGPUQueueHandle queueHandle;
    private readonly WebGPUTextureHandle targetTextureHandle;
    private readonly WebGPUTextureViewHandle targetTextureViewHandle;

    internal WebGPUSurfaceCapability(
        WebGPUDeviceHandle deviceHandle,
        WebGPUQueueHandle queueHandle,
        WebGPUTextureHandle targetTextureHandle,
        WebGPUTextureViewHandle targetTextureViewHandle,
        WebGPUTextureFormatId targetFormat,
        int width,
        int height)
    {
        Guard.NotNull(deviceHandle, nameof(deviceHandle));
        Guard.NotNull(queueHandle, nameof(queueHandle));
        Guard.NotNull(targetTextureHandle, nameof(targetTextureHandle));
        Guard.NotNull(targetTextureViewHandle, nameof(targetTextureViewHandle));

        this.deviceHandle = deviceHandle;
        this.queueHandle = queueHandle;
        this.targetTextureHandle = targetTextureHandle;
        this.targetTextureViewHandle = targetTextureViewHandle;
        this.TargetFormat = targetFormat;
        this.Width = width;
        this.Height = height;
    }

    /// <summary>
    /// Gets the wrapped device handle that owns the target texture.
    /// </summary>
    internal WebGPUDeviceHandle DeviceHandle => this.deviceHandle;

    /// <summary>
    /// Gets the wrapped queue handle used to submit work against the target texture.
    /// </summary>
    internal WebGPUQueueHandle QueueHandle => this.queueHandle;

    /// <summary>
    /// Gets the wrapped target texture handle exposed by this surface capability.
    /// </summary>
    internal WebGPUTextureHandle TargetTextureHandle => this.targetTextureHandle;

    /// <summary>
    /// Gets the wrapped target texture-view handle exposed by this surface capability.
    /// </summary>
    internal WebGPUTextureViewHandle TargetTextureViewHandle => this.targetTextureViewHandle;

    /// <summary>
    /// Gets the native render target texture format identifier.
    /// </summary>
    internal WebGPUTextureFormatId TargetFormat { get; }

    /// <summary>
    /// Gets the surface width in pixels.
    /// </summary>
    internal int Width { get; }

    /// <summary>
    /// Gets the surface height in pixels.
    /// </summary>
    internal int Height { get; }
}
