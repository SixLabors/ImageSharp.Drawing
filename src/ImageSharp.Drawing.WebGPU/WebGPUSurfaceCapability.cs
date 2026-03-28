// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Native WebGPU surface capability attached to <see cref="NativeSurface"/>.
/// </summary>
/// <remarks>
/// The <see cref="Device"/> handle must remain valid for the lifetime of any
/// <see cref="IDrawingBackend"/> that processes frames using this capability.
/// The backend caches per-device GPU resources (pipelines, buffers) that reference
/// the device internally. Ensure the device is not released while any backend
/// instance may still reference it.
/// </remarks>
public sealed class WebGPUSurfaceCapability
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSurfaceCapability"/> class.
    /// </summary>
    /// <param name="device">Opaque <c>WGPUDevice*</c> handle. Must remain valid for the lifetime of any backend that uses this capability.</param>
    /// <param name="queue">Opaque <c>WGPUQueue*</c> handle.</param>
    /// <param name="targetTexture">Opaque <c>WGPUTexture*</c> handle for the current frame when writable upload is supported.</param>
    /// <param name="targetTextureView">Opaque <c>WGPUTextureView*</c> handle for the current frame.</param>
    /// <param name="targetFormat">Native render target texture format identifier.</param>
    /// <param name="width">Surface width in pixels.</param>
    /// <param name="height">Surface height in pixels.</param>
    public WebGPUSurfaceCapability(
        nint device,
        nint queue,
        nint targetTexture,
        nint targetTextureView,
        WebGPUTextureFormatId targetFormat,
        int width,
        int height)
    {
        this.Device = device;
        this.Queue = queue;
        this.TargetTexture = targetTexture;
        this.TargetTextureView = targetTextureView;
        this.TargetFormat = targetFormat;
        this.Width = width;
        this.Height = height;
    }

    /// <summary>
    /// Gets the opaque <c>WGPUDevice*</c> handle.
    /// </summary>
    public nint Device { get; }

    /// <summary>
    /// Gets the opaque <c>WGPUQueue*</c> handle.
    /// </summary>
    public nint Queue { get; }

    /// <summary>
    /// Gets the opaque <c>WGPUTexture*</c> handle for the current frame.
    /// </summary>
    public nint TargetTexture { get; }

    /// <summary>
    /// Gets the opaque <c>WGPUTextureView*</c> handle for the current frame.
    /// </summary>
    public nint TargetTextureView { get; }

    /// <summary>
    /// Gets the native render target texture format identifier.
    /// </summary>
    public WebGPUTextureFormatId TargetFormat { get; }

    /// <summary>
    /// Gets the surface width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the surface height in pixels.
    /// </summary>
    public int Height { get; }
}
