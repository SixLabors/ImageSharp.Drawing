// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that allow the drawing of lines.
/// </summary>
public static class DrawLineExtensions
{
    /// <summary>
    /// Draws the provided points as an open linear path at the provided thickness with the supplied brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="thickness">The line thickness.</param>
    /// <param name="points">The points.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext DrawLine(
        this IImageProcessingContext source,
        DrawingOptions options,
        Brush brush,
        float thickness,
        params PointF[] points) =>
        source.Draw(options, new SolidPen(brush, thickness), new Path(points));

    /// <summary>
    /// Draws the provided points as an open linear path at the provided thickness with the supplied brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="thickness">The line thickness.</param>
    /// <param name="points">The points.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext DrawLine(
        this IImageProcessingContext source,
        Brush brush,
        float thickness,
        params PointF[] points) =>
        source.Draw(new SolidPen(brush, thickness), new Path(points));

    /// <summary>
    /// Draws the provided points as an open linear path at the provided thickness with the supplied brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="color">The color.</param>
    /// <param name="thickness">The line thickness.</param>
    /// <param name="points">The points.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext DrawLine(
        this IImageProcessingContext source,
        Color color,
        float thickness,
        params PointF[] points) =>
        source.DrawLine(new SolidBrush(color), thickness, points);

    /// <summary>
    /// Draws the provided points as an open linear path at the provided thickness with the supplied brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="color">The color.</param>
    /// <param name="thickness">The line thickness.</param>
    /// <param name="points">The points.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>>
    public static IImageProcessingContext DrawLine(
        this IImageProcessingContext source,
        DrawingOptions options,
        Color color,
        float thickness,
        params PointF[] points) =>
        source.DrawLine(options, new SolidBrush(color), thickness, points);

    /// <summary>
    /// Draws the provided points as an open linear path with the supplied pen.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="pen">The pen.</param>
    /// <param name="points">The points.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext DrawLine(
        this IImageProcessingContext source,
        DrawingOptions options,
        Pen pen,
        params PointF[] points) =>
        source.Draw(options, pen, new Path(points));

    /// <summary>
    /// Draws the provided points as an open linear path with the supplied pen.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="pen">The pen.</param>
    /// <param name="points">The points.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext DrawLine(
        this IImageProcessingContext source,
        Pen pen,
        params PointF[] points) =>
        source.Draw(pen, new Path(points));
}
