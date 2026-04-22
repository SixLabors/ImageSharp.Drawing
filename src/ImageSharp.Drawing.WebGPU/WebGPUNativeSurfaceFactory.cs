// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Low-level escape hatch for constructing a <see cref="NativeSurface"/> directly from raw WebGPU handles.
/// </summary>
/// <remarks>
/// Most callers should use <see cref="WebGPUDeviceContext{TPixel}.CreateCanvas(nint, nint, WebGPUTextureFormatId, int, int, DrawingOptions)"/>
/// or <see cref="WebGPUDeviceContext{TPixel}.CreateFrame(nint, nint, WebGPUTextureFormatId, int, int)"/> instead, which wrap this factory
/// and validate handle/format compatibility against the canvas pixel type. Use this factory only when you need a
/// <see cref="NativeSurface"/> independent of <see cref="WebGPUDeviceContext{TPixel}"/>.
/// </remarks>
public static class WebGPUNativeSurfaceFactory
{
    /// <summary>
    /// Creates a WebGPU-backed <see cref="NativeSurface"/> from external native handles.
    /// </summary>
    /// <typeparam name="TPixel">Canvas pixel format.</typeparam>
    /// <param name="deviceHandle">The external WebGPU device handle.</param>
    /// <param name="queueHandle">The external WebGPU queue handle.</param>
    /// <param name="targetTextureHandle">The external WebGPU texture handle for writable uploads.</param>
    /// <param name="targetTextureViewHandle">The external WebGPU texture-view handle for render-target binding.</param>
    /// <param name="targetFormat">Texture format identifier.</param>
    /// <param name="width">Surface width in pixels.</param>
    /// <param name="height">Surface height in pixels.</param>
    /// <returns>A configured <see cref="NativeSurface"/> instance.</returns>
    /// <remarks>
    /// These handles must originate from the same process WebGPU runtime used by ImageSharp.Drawing.WebGPU.
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
        => Create<TPixel>(
            new WebGPUDeviceHandle(deviceHandle, ownsHandle: false),
            new WebGPUQueueHandle(queueHandle, ownsHandle: false),
            new WebGPUTextureHandle(targetTextureHandle, ownsHandle: false),
            new WebGPUTextureViewHandle(targetTextureViewHandle, ownsHandle: false),
            targetFormat,
            width,
            height);

    internal static NativeSurface Create<TPixel>(
        WebGPUDeviceHandle deviceHandle,
        WebGPUQueueHandle queueHandle,
        WebGPUTextureHandle targetTextureHandle,
        WebGPUTextureViewHandle targetTextureViewHandle,
        WebGPUTextureFormatId targetFormat,
        int width,
        int height)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(deviceHandle, nameof(deviceHandle));
        Guard.NotNull(queueHandle, nameof(queueHandle));
        Guard.NotNull(targetTextureHandle, nameof(targetTextureHandle));
        Guard.NotNull(targetTextureViewHandle, nameof(targetTextureViewHandle));

        Guard.MustBeGreaterThan(width, 0, nameof(width));
        Guard.MustBeGreaterThan(height, 0, nameof(height));
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
    /// Validates that the requested pixel type maps to the supplied WebGPU texture format.
    /// </summary>
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
