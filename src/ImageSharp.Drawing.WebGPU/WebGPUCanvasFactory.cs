// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Creates typed canvas objects for the supported WebGPU target descriptors.
/// </summary>
/// <remarks>
/// The texture format selects the channel layout and component encoding. The alpha representation
/// selects the associated or unassociated CLR pixel type with that physical layout.
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
    /// <param name="targetDescriptor">The surface texture format and alpha representation that select the canvas pixel type.</param>
    /// <returns>The typed drawing canvas.</returns>
    public static DrawingCanvas CreateCanvas(
        Configuration configuration,
        DrawingOptions options,
        IDrawingBackend backend,
        Rectangle bounds,
        NativeSurface surface,
        WebGPUTargetDescriptor targetDescriptor)

        // CS8524 (unnamed enum values) is suppressed rather than adding a discard arm:
        // WebGPUTextureFormat is a closed set and every named value is matched, so an
        // out-of-range value can only come from invalid casting elsewhere.
#pragma warning disable CS8509, CS8524
        => (targetDescriptor.Format, targetDescriptor.AlphaRepresentation) switch
        {
            (WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Unassociated) => CreateCanvas<Rgba32>(
                configuration,
                options,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Associated) => CreateCanvas<Rgba32P>(
                configuration,
                options,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Bgra8Unorm, PixelAlphaRepresentation.Unassociated) => CreateCanvas<Bgra32>(
                configuration,
                options,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Bgra8Unorm, PixelAlphaRepresentation.Associated) => CreateCanvas<Bgra32P>(
                configuration,
                options,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Unassociated) => CreateCanvas<NormalizedByte4>(
                configuration,
                options,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Associated) => CreateCanvas<NormalizedByte4P>(
                configuration,
                options,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Unassociated) => CreateCanvas<RgbaHalf>(
                configuration,
                options,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Associated) => CreateCanvas<RgbaHalfP>(
                configuration,
                options,
                backend,
                bounds,
                surface)
        };
#pragma warning restore CS8509, CS8524

    /// <summary>
    /// Creates a typed canvas over a WebGPU native surface with a shared text cache.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="options">The drawing options for the canvas.</param>
    /// <param name="textCache">The reusable text drawing cache shared across canvases.</param>
    /// <param name="backend">The drawing backend the canvas renders through.</param>
    /// <param name="bounds">The canvas bounds within the surface.</param>
    /// <param name="surface">The WebGPU native surface backing the canvas.</param>
    /// <param name="targetDescriptor">The surface texture format and alpha representation that select the canvas pixel type.</param>
    /// <returns>The typed drawing canvas.</returns>
    public static DrawingCanvas CreateCanvas(
        Configuration configuration,
        DrawingOptions options,
        DrawingTextCache textCache,
        IDrawingBackend backend,
        Rectangle bounds,
        NativeSurface surface,
        WebGPUTargetDescriptor targetDescriptor)

        // See the comment on the overload above for why CS8524 is suppressed.
#pragma warning disable CS8509, CS8524
        => (targetDescriptor.Format, targetDescriptor.AlphaRepresentation) switch
        {
            (WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Unassociated) => CreateCanvas<Rgba32>(
                configuration,
                options,
                textCache,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Associated) => CreateCanvas<Rgba32P>(
                configuration,
                options,
                textCache,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Bgra8Unorm, PixelAlphaRepresentation.Unassociated) => CreateCanvas<Bgra32>(
                configuration,
                options,
                textCache,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Bgra8Unorm, PixelAlphaRepresentation.Associated) => CreateCanvas<Bgra32P>(
                configuration,
                options,
                textCache,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Unassociated) => CreateCanvas<NormalizedByte4>(
                configuration,
                options,
                textCache,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Associated) => CreateCanvas<NormalizedByte4P>(
                configuration,
                options,
                textCache,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Unassociated) => CreateCanvas<RgbaHalf>(
                configuration,
                options,
                textCache,
                backend,
                bounds,
                surface),

            (WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Associated) => CreateCanvas<RgbaHalfP>(
                configuration,
                options,
                textCache,
                backend,
                bounds,
                surface)
        };
#pragma warning restore CS8509, CS8524

    /// <summary>
    /// Creates a typed frame over a WebGPU native surface.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format matching the surface texture format.</typeparam>
    /// <param name="bounds">The frame bounds within the surface.</param>
    /// <param name="surface">The WebGPU native surface backing the frame.</param>
    /// <returns>The typed native canvas frame.</returns>
    public static NativeCanvasFrame<TPixel> CreateFrame<TPixel>(
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
