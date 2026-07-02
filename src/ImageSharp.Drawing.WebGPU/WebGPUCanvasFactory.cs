// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Creates typed canvas objects for the fixed set of WebGPU texture formats.
/// </summary>
/// <remarks>
/// The surface texture format selects the canvas pixel type: <see cref="WebGPUTextureFormat.Rgba8Unorm"/>
/// yields <see cref="Rgba32"/>, <see cref="WebGPUTextureFormat.Bgra8Unorm"/> yields <see cref="Bgra32"/>,
/// <see cref="WebGPUTextureFormat.Rgba8Snorm"/> yields <see cref="NormalizedByte4"/>, and
/// <see cref="WebGPUTextureFormat.Rgba16Float"/> yields <see cref="HalfVector4"/>.
/// </remarks>
internal static class WebGPUCanvasFactory
{
    /// <summary>
    /// Creates a typed canvas over a WebGPU native surface.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="options">The drawing options for the canvas.</param>
    /// <param name="backend">The drawing backend the canvas renders through.</param>
    /// <param name="bounds">The canvas bounds within the surface.</param>
    /// <param name="surface">The WebGPU native surface backing the canvas.</param>
    /// <param name="format">The surface texture format that selects the canvas pixel type.</param>
    /// <returns>The typed drawing canvas.</returns>
    internal static DrawingCanvas CreateCanvas(
        Configuration configuration,
        DrawingOptions options,
        IDrawingBackend backend,
        Rectangle bounds,
        NativeSurface surface,
        WebGPUTextureFormat format)

        // CS8524 (unnamed enum values) is suppressed rather than adding a discard arm:
        // WebGPUTextureFormat is a closed set and every named value is matched, so an
        // out-of-range value can only come from invalid casting elsewhere.
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
    /// Creates a typed canvas over a WebGPU native surface with a shared text cache.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="options">The drawing options for the canvas.</param>
    /// <param name="textCache">The reusable text drawing cache shared across canvases.</param>
    /// <param name="backend">The drawing backend the canvas renders through.</param>
    /// <param name="bounds">The canvas bounds within the surface.</param>
    /// <param name="surface">The WebGPU native surface backing the canvas.</param>
    /// <param name="format">The surface texture format that selects the canvas pixel type.</param>
    /// <returns>The typed drawing canvas.</returns>
    internal static DrawingCanvas CreateCanvas(
        Configuration configuration,
        DrawingOptions options,
        DrawingTextCache textCache,
        IDrawingBackend backend,
        Rectangle bounds,
        NativeSurface surface,
        WebGPUTextureFormat format)

        // See the comment on the overload above for why CS8524 is suppressed.
#pragma warning disable CS8524
        => format switch
        {
            WebGPUTextureFormat.Rgba8Unorm => CreateCanvas<Rgba32>(
                configuration,
                options,
                textCache,
                backend,
                bounds,
                surface),

            WebGPUTextureFormat.Bgra8Unorm => CreateCanvas<Bgra32>(
                configuration,
                options,
                textCache,
                backend,
                bounds,
                surface),

            WebGPUTextureFormat.Rgba8Snorm => CreateCanvas<NormalizedByte4>(
                configuration,
                options,
                textCache,
                backend,
                bounds,
                surface),

            WebGPUTextureFormat.Rgba16Float => CreateCanvas<HalfVector4>(
                configuration,
                options,
                textCache,
                backend,
                bounds,
                surface)
        };
#pragma warning restore CS8524

    /// <summary>
    /// Creates a typed frame over a WebGPU native surface.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format matching the surface texture format.</typeparam>
    /// <param name="bounds">The frame bounds within the surface.</param>
    /// <param name="surface">The WebGPU native surface backing the frame.</param>
    /// <returns>The typed native canvas frame.</returns>
    internal static NativeCanvasFrame<TPixel> CreateFrame<TPixel>(
        Rectangle bounds,
        NativeSurface surface)
        where TPixel : unmanaged, IPixel<TPixel>
        => new(bounds, surface);

    /// <summary>
    /// Creates a typed drawing canvas over an already selected WebGPU frame format.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format matching the surface texture format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="options">The drawing options for the canvas.</param>
    /// <param name="backend">The drawing backend the canvas renders through.</param>
    /// <param name="bounds">The canvas bounds within the surface.</param>
    /// <param name="surface">The WebGPU native surface backing the canvas.</param>
    /// <returns>The typed drawing canvas.</returns>
    private static DrawingCanvas<TPixel> CreateCanvas<TPixel>(
        Configuration configuration,
        DrawingOptions options,
        IDrawingBackend backend,
        Rectangle bounds,
        NativeSurface surface)
        where TPixel : unmanaged, IPixel<TPixel>
        => new(configuration, options, backend, CreateFrame<TPixel>(bounds, surface));

    /// <summary>
    /// Creates a typed drawing canvas over an already selected WebGPU frame format with a shared text cache.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format matching the surface texture format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="options">The drawing options for the canvas.</param>
    /// <param name="textCache">The reusable text drawing cache shared across canvases.</param>
    /// <param name="backend">The drawing backend the canvas renders through.</param>
    /// <param name="bounds">The canvas bounds within the surface.</param>
    /// <param name="surface">The WebGPU native surface backing the canvas.</param>
    /// <returns>The typed drawing canvas.</returns>
    private static DrawingCanvas<TPixel> CreateCanvas<TPixel>(
        Configuration configuration,
        DrawingOptions options,
        DrawingTextCache textCache,
        IDrawingBackend backend,
        Rectangle bounds,
        NativeSurface surface)
        where TPixel : unmanaged, IPixel<TPixel>
        => new(configuration, options, textCache, backend, CreateFrame<TPixel>(bounds, surface));
}
