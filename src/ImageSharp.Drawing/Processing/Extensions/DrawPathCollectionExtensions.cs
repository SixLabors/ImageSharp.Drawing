// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that allow the drawing of collections of polygon outlines.
/// </summary>
public static class DrawPathCollectionExtensions
{
    /// <summary>
    /// Draws the outline of the polygon with the provided pen.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="pen">The pen.</param>
    /// <param name="paths">The paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        DrawingOptions options,
        Pen pen,
        IPathCollection paths)
    {
        foreach (IPath path in paths)
        {
            source.Draw(options, pen, path);
        }

        return source;
    }

    /// <summary>
    /// Draws the outline of the polygon with the provided pen.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="pen">The pen.</param>
    /// <param name="paths">The paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext
        Draw(this IImageProcessingContext source, Pen pen, IPathCollection paths)
        => source.Draw(source.GetDrawingOptions(), pen, paths);

    /// <summary>
    /// Draws the outline of the polygon with the provided brush at the provided thickness.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="thickness">The thickness.</param>
    /// <param name="paths">The shapes.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        DrawingOptions options,
        Brush brush,
        float thickness,
        IPathCollection paths) =>
        source.Draw(options, new SolidPen(brush, thickness), paths);

    /// <summary>
    /// Draws the outline of the polygon with the provided brush at the provided thickness.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="thickness">The thickness.</param>
    /// <param name="paths">The paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        Brush brush,
        float thickness,
        IPathCollection paths) =>
        source.Draw(new SolidPen(brush, thickness), paths);

    /// <summary>
    /// Draws the outline of the polygon with the provided brush at the provided thickness.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="color">The color.</param>
    /// <param name="thickness">The thickness.</param>
    /// <param name="paths">The paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        DrawingOptions options,
        Color color,
        float thickness,
        IPathCollection paths) =>
        source.Draw(options, new SolidBrush(color), thickness, paths);

    /// <summary>
    /// Draws the outline of the polygon with the provided brush at the provided thickness.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="color">The color.</param>
    /// <param name="thickness">The thickness.</param>
    /// <param name="paths">The paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Draw(
        this IImageProcessingContext source,
        Color color,
        float thickness,
        IPathCollection paths) =>
        source.Draw(new SolidBrush(color), thickness, paths);
}
