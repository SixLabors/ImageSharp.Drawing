// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Represents the per-frame painting callback executed by <see cref="PaintExtensions.Paint(IImageProcessingContext, CanvasAction)"/>.
/// </summary>
/// <param name="canvas">The drawing canvas for the current image frame.</param>
public delegate void CanvasAction(IDrawingCanvas canvas);

/// <summary>
/// Adds image-processing extensions that paint each frame through <see cref="IDrawingCanvas"/>.
/// </summary>
public static class PaintExtensions
{
    /// <summary>
    /// Paints each image frame using drawing options from the current context.
    /// </summary>
    /// <param name="source">The image processing context to paint.</param>
    /// <param name="action">The per-frame painting callback.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> so additional processing operations can be chained.</returns>
    public static IImageProcessingContext Paint(
        this IImageProcessingContext source,
        CanvasAction action)
        => source.Paint(source.GetDrawingOptions(), action);

    /// <summary>
    /// Paints each image frame using the supplied drawing options.
    /// </summary>
    /// <param name="source">The image processing context to paint.</param>
    /// <param name="options">The drawing options applied when creating each frame canvas.</param>
    /// <param name="action">The per-frame painting callback.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> so additional processing operations can be chained.</returns>
    public static IImageProcessingContext Paint(
        this IImageProcessingContext source,
        DrawingOptions options,
        CanvasAction action)
    {
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(action, nameof(action));

        return source.ApplyProcessor(new PaintProcessor(options, action));
    }
}
