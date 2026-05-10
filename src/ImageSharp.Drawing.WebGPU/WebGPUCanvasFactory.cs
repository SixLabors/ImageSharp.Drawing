// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Creates typed canvas objects for the fixed set of WebGPU texture formats.
/// </summary>
internal static class WebGPUCanvasFactory
{
    /// <summary>
    /// Creates a typed canvas over a WebGPU native surface.
    /// </summary>
    internal static DrawingCanvas CreateCanvas(
        Configuration configuration,
        DrawingOptions options,
        IDrawingBackend backend,
        Rectangle bounds,
        NativeSurface surface,
        WebGPUTextureFormat format)
#pragma warning disable CS8524
        => format switch
        {
            WebGPUTextureFormat.Rgba8Unorm => CreateCanvas<Rgba32>(
                configuration,
                options,
                backend,
                bounds,
                surface),

            WebGPUTextureFormat.Bgra8Unorm => CreateCanvas<Bgra32>(
                configuration,
                options,
                backend,
                bounds,
                surface),

            WebGPUTextureFormat.Rgba8Snorm => CreateCanvas<NormalizedByte4>(
                configuration,
                options,
                backend,
                bounds,
                surface),

            WebGPUTextureFormat.Rgba16Float => CreateCanvas<HalfVector4>(
                configuration,
                options,
                backend,
                bounds,
                surface)
        };
#pragma warning restore CS8524

    /// <summary>
    /// Creates a typed frame over a WebGPU native surface.
    /// </summary>
    internal static NativeCanvasFrame<TPixel> CreateFrame<TPixel>(
        Rectangle bounds,
        NativeSurface surface)
        where TPixel : unmanaged, IPixel<TPixel>
        => new(bounds, surface);

    /// <summary>
    /// Creates a typed drawing canvas over an already selected WebGPU frame format.
    /// </summary>
    private static DrawingCanvas<TPixel> CreateCanvas<TPixel>(
        Configuration configuration,
        DrawingOptions options,
        IDrawingBackend backend,
        Rectangle bounds,
        NativeSurface surface)
        where TPixel : unmanaged, IPixel<TPixel>
        => new(configuration, options, backend, CreateFrame<TPixel>(bounds, surface));
}
