// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that allow the filling of polygon outlines.
/// </summary>
public static class FillPathExtensions
{
    /// <summary>
    /// Flood fills the image in the shape of the provided polygon with the specified brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="color">The color.</param>
    /// <param name="path">The logic path.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        Color color,
        IPath path) =>
        source.Fill(new SolidBrush(color), path);

    /// <summary>
    /// Flood fills the image in the shape of the provided polygon with the specified brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The drawing options.</param>
    /// <param name="color">The color.</param>
    /// <param name="path">The logic path.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        DrawingOptions options,
        Color color,
        IPath path) =>
        source.Fill(options, new SolidBrush(color), path);

    /// <summary>
    /// Flood fills the image in the shape of the provided polygon with the specified brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="path">The logic path.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        Brush brush,
        IPath path) =>
        source.Fill(source.GetDrawingOptions(), brush, path);

    /// <summary>
    /// Flood fills the image in the shape of the provided polygon with the specified brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The drawing options.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="path">The shape.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        DrawingOptions options,
        Brush brush,
        IPath path) =>
        source.ApplyProcessor(new FillPathProcessor(options, brush, path));
}
