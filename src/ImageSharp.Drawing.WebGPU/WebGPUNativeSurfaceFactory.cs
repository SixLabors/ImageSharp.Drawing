// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Creates <see cref="NativeSurface"/> instances for externally-owned WebGPU targets.
/// </summary>
public static class WebGPUNativeSurfaceFactory
{
    /// <summary>
    /// Creates a WebGPU-backed <see cref="NativeSurface"/> from opaque native handles.
    /// </summary>
    /// <typeparam name="TPixel">Canvas pixel format.</typeparam>
    /// <param name="deviceHandle">Opaque <c>WGPUDevice*</c> handle.</param>
    /// <param name="queueHandle">Opaque <c>WGPUQueue*</c> handle.</param>
    /// <param name="targetTextureHandle">Opaque <c>WGPUTexture*</c> handle for writable uploads.</param>
    /// <param name="targetTextureViewHandle">Opaque <c>WGPUTextureView*</c> handle for render target binding.</param>
    /// <param name="targetFormat">Texture format identifier.</param>
    /// <param name="width">Surface width in pixels.</param>
    /// <param name="height">Surface height in pixels.</param>
    /// <param name="isSrgb">Whether the surface is sRGB encoded.</param>
    /// <param name="isPremultipliedAlpha">Whether surface alpha is premultiplied.</param>
    /// <param name="supportsTextureSampling">
    /// Whether <paramref name="targetTextureHandle"/> supports texture sampling.
    /// </param>
    /// <returns>A configured <see cref="NativeSurface"/> instance.</returns>
    public static NativeSurface Create<TPixel>(
        nint deviceHandle,
        nint queueHandle,
        nint targetTextureHandle,
        nint targetTextureViewHandle,
        WebGPUTextureFormatId targetFormat,
        int width,
        int height,
        bool isSrgb,
        bool isPremultipliedAlpha,
        bool supportsTextureSampling)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ValidateCommon(
            deviceHandle,
            queueHandle,
            targetTextureViewHandle,
            width,
            height);

        ValidatePixelCompatibility<TPixel>(targetFormat);

        NativeSurface nativeSurface = new(TPixel.GetPixelTypeInfo());
        nativeSurface.SetCapability(new WebGPUSurfaceCapability(
            deviceHandle,
            queueHandle,
            targetTextureHandle,
            targetTextureViewHandle,
            targetFormat,
            width,
            height,
            isSrgb,
            isPremultipliedAlpha,
            supportsTextureSampling));
        return nativeSurface;
    }

    private static void ValidateCommon(
        nint deviceHandle,
        nint queueHandle,
        nint targetTextureViewHandle,
        int width,
        int height)
    {
        if (deviceHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceHandle), "Device handle must be non-zero.");
        }

        if (queueHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queueHandle), "Queue handle must be non-zero.");
        }

        if (targetTextureViewHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetTextureViewHandle), "Texture view handle must be non-zero.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");
        }
    }

    private static void ValidatePixelCompatibility<TPixel>(WebGPUTextureFormatId targetFormat)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!WebGPUDrawingBackend.TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId expected))
        {
            throw new NotSupportedException($"Pixel type '{typeof(TPixel).Name}' is not supported by the WebGPU backend.");
        }

        if (expected != targetFormat)
        {
            throw new ArgumentException(
                $"Target format '{targetFormat}' is not compatible with pixel type '{typeof(TPixel).Name}' (expected '{expected}').",
                nameof(targetFormat));
        }
    }
}
