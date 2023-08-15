// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that allow the flood filling of rectangle outlines without blending.
/// </summary>
public static class ClearRectangleExtensions
{
    /// <summary>
    /// Flood fills the image in the rectangle of the provided rectangle with the specified color without any blending.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="color">The color.</param>
    /// <param name="rectangle">The rectangle defining the region to fill.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Clear(this IImageProcessingContext source, Color color, RectangleF rectangle)
        => source.Clear(new SolidBrush(color), rectangle);

    /// <summary>
    /// Flood fills the image in the rectangle of the provided rectangle with the specified color without any blending.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The drawing options.</param>
    /// <param name="color">The color.</param>
    /// <param name="rectangle">The rectangle defining the region to fill.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Clear(
        this IImageProcessingContext source,
        DrawingOptions options,
        Color color,
        RectangleF rectangle)
        => source.Clear(options, new SolidBrush(color), rectangle);

    /// <summary>
    /// Flood fills the image in the rectangle of the provided rectangle with the specified brush without any blending.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="rectangle">The rectangle defining the region to fill.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Clear(
        this IImageProcessingContext source,
        Brush brush,
        RectangleF rectangle)
        => source.Clear(brush, new RectangularPolygon(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));

    /// <summary>
    /// Flood fills the image at the given rectangle bounds with the specified brush without any blending.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The drawing options.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="rectangle">The rectangle defining the region to fill.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Clear(
        this IImageProcessingContext source,
        DrawingOptions options,
        Brush brush,
        RectangleF rectangle)
        => source.Clear(options, brush, new RectangularPolygon(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
}
