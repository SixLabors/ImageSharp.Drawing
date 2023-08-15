// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that allow the flood filling of polygon outlines.
/// </summary>
public static class FillPathBuilderExtensions
{
    /// <summary>
    /// Flood fills the image within the provided region defined by an <see cref="PathBuilder"/> method
    /// using the specified color.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="color">The color.</param>
    /// <param name="region">The <see cref="PathBuilder"/> method defining the region to fill.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        Color color,
        Action<PathBuilder> region)
        => source.Fill(new SolidBrush(color), region);

    /// <summary>
    /// Flood fills the image within the provided region defined by an <see cref="PathBuilder"/> method
    /// using the specified color.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The drawing options.</param>
    /// <param name="color">The color.</param>
    /// <param name="region">The <see cref="PathBuilder"/> method defining the region to fill.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        DrawingOptions options,
        Color color,
        Action<PathBuilder> region)
        => source.Fill(options, new SolidBrush(color), region);

    /// <summary>
    /// Flood fills the image within the provided region defined by an <see cref="PathBuilder"/> method
    /// using the specified brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="region">The <see cref="PathBuilder"/> method defining the region to fill.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        Brush brush,
        Action<PathBuilder> region)
        => source.Fill(source.GetDrawingOptions(), brush, region);

    /// <summary>
    /// Flood fills the image within the provided region defined by an <see cref="PathBuilder"/> method
    /// using the specified brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The graphics options.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="region">The <see cref="PathBuilder"/> method defining the region to fill.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        DrawingOptions options,
        Brush brush,
        Action<PathBuilder> region)
    {
        var pb = new PathBuilder();
        region(pb);

        return source.Fill(options, brush, pb.Build());
    }
}
