// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Represents a drawing callback executed against a <see cref="IDrawingCanvas"/>.
/// </summary>
/// <param name="canvas">The drawing canvas for the current frame.</param>
public delegate void CanvasAction(IDrawingCanvas canvas);

/// <summary>
/// Adds extensions that execute drawing callbacks against all frames through <see cref="IDrawingCanvas"/>.
/// </summary>
public static class ProcessWithCanvasExtensions
{
    /// <summary>
    /// Executes <paramref name="action"/> for each image frame using drawing options from the current context.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="action">The drawing callback to execute for each frame.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext ProcessWithCanvas(
        this IImageProcessingContext source,
        CanvasAction action)
        => source.ProcessWithCanvas(source.GetDrawingOptions(), action);

    /// <summary>
    /// Executes <paramref name="action"/> for each image frame using the supplied drawing options.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The drawing options.</param>
    /// <param name="action">The drawing callback to execute for each frame.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext ProcessWithCanvas(
        this IImageProcessingContext source,
        DrawingOptions options,
        CanvasAction action)
    {
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(action, nameof(action));

        return source.ApplyProcessor(new ProcessWithCanvasProcessor(options, action));
    }
}
