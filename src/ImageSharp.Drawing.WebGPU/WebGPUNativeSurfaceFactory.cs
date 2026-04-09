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
    /// <returns>A configured <see cref="NativeSurface"/> instance.</returns>
    /// <remarks>
    /// The target texture must have been created with the <c>TEXTURE_BINDING</c> usage flag.
    /// The backend reads the target texture for Porter-Duff backdrop sampling.
    /// </remarks>
    public static NativeSurface Create<TPixel>(
        nint deviceHandle,
        nint queueHandle,
        nint targetTextureHandle,
        nint targetTextureViewHandle,
        WebGPUTextureFormatId targetFormat,
        int width,
        int height)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ValidateCommon(
            deviceHandle,
            queueHandle,
            targetTextureHandle,
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
            height));
        return nativeSurface;
    }

    /// <summary>
    /// Validates the shared handle and size requirements for every native-surface factory entry point.
    /// </summary>
    private static void ValidateCommon(
        nint deviceHandle,
        nint queueHandle,
        nint targetTextureHandle,
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

        if (targetTextureHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetTextureHandle), "Texture handle must be non-zero.");
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
