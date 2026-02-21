// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Native WebGPU surface capability attached to <see cref="NativeSurface"/>.
/// </summary>
public sealed class WebGPUSurfaceCapability
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSurfaceCapability"/> class.
    /// </summary>
    /// <param name="device">Opaque <c>WGPUDevice*</c> handle.</param>
    /// <param name="queue">Opaque <c>WGPUQueue*</c> handle.</param>
    /// <param name="targetTexture">Opaque <c>WGPUTexture*</c> handle for the current frame when writable upload is supported.</param>
    /// <param name="targetTextureView">Opaque <c>WGPUTextureView*</c> handle for the current frame.</param>
    /// <param name="targetFormat">Native render target texture format.</param>
    /// <param name="width">Surface width in pixels.</param>
    /// <param name="height">Surface height in pixels.</param>
    /// <param name="isSrgb">Whether the target format is sRGB encoded.</param>
    /// <param name="isPremultipliedAlpha">Whether alpha is premultiplied in the target surface.</param>
    public WebGPUSurfaceCapability(
        nint device,
        nint queue,
        nint targetTexture,
        nint targetTextureView,
        TextureFormat targetFormat,
        int width,
        int height,
        bool isSrgb,
        bool isPremultipliedAlpha)
    {
        this.Device = device;
        this.Queue = queue;
        this.TargetTexture = targetTexture;
        this.TargetTextureView = targetTextureView;
        this.TargetFormat = targetFormat;
        this.Width = width;
        this.Height = height;
        this.IsSrgb = isSrgb;
        this.IsPremultipliedAlpha = isPremultipliedAlpha;
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
    /// Gets the native render target texture format.
    /// </summary>
    public TextureFormat TargetFormat { get; }

    /// <summary>
    /// Gets the surface width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the surface height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets a value indicating whether the target format is sRGB encoded.
    /// </summary>
    public bool IsSrgb { get; }

    /// <summary>
    /// Gets a value indicating whether the target uses premultiplied alpha.
    /// </summary>
    public bool IsPremultipliedAlpha { get; }
}
