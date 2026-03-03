// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Represents a drawing callback executed against a <see cref="DrawingCanvas{TPixel}"/>.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
/// <param name="canvas">The drawing canvas for the current frame.</param>
public delegate void CanvasAction<TPixel>(DrawingCanvas<TPixel> canvas)
    where TPixel : unmanaged, IPixel<TPixel>;

/// <summary>
/// Adds extensions that execute drawing callbacks against all frames through <see cref="DrawingCanvas{TPixel}"/>.
/// </summary>
public static class ProcessWithCanvasExtensions
{
    /// <summary>
    /// Executes <paramref name="action"/> for each image frame using drawing options from the current context.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format expected by the callback.</typeparam>
    /// <param name="source">The source image processing context.</param>
    /// <param name="action">The drawing callback to execute for each frame.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext ProcessWithCanvas<TPixel>(
        this IImageProcessingContext source,
        CanvasAction<TPixel> action)
        where TPixel : unmanaged, IPixel<TPixel>
        => source.ProcessWithCanvas(source.GetDrawingOptions(), action);

    /// <summary>
    /// Executes <paramref name="action"/> for each image frame using the supplied drawing options.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format expected by the callback.</typeparam>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The drawing options.</param>
    /// <param name="action">The drawing callback to execute for each frame.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext ProcessWithCanvas<TPixel>(
        this IImageProcessingContext source,
        DrawingOptions options,
        CanvasAction<TPixel> action)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(action, nameof(action));

        return source.ApplyProcessor(new ProcessWithCanvasProcessor(options, typeof(TPixel), action));
    }
}
