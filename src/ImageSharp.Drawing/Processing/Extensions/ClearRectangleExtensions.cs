// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the filling of rectangles to the <see cref="Image{TPixel}"/> type.
    /// </summary>
    public static class ClearRectangleExtensions
    {
        /// <summary>
        /// Flood fills the image in the shape of the provided rectangle with the specified brush without any blending.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="options">The options.</param>
        /// <param name="brush">The brush.</param>
        /// <param name="shape">The shape.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Clear(
            this IImageProcessingContext source,
            DrawingOptions options,
            IBrush brush,
            RectangleF shape) =>
            source.Clear(options, brush, new RectangularPolygon(shape.X, shape.Y, shape.Width, shape.Height));

        /// <summary>
        /// Flood fills the image in the shape of the provided rectangle with the specified brush without any blending.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="brush">The brush.</param>
        /// <param name="shape">The shape.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Clear(this IImageProcessingContext source, IBrush brush, RectangleF shape) =>
            source.Clear(brush, new RectangularPolygon(shape.X, shape.Y, shape.Width, shape.Height));

        /// <summary>
        /// Flood fills the image in the shape of the provided rectangle with the specified brush without any blending.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="options">The options.</param>
        /// <param name="color">The color.</param>
        /// <param name="shape">The shape.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Clear(
            this IImageProcessingContext source,
            DrawingOptions options,
            Color color,
            RectangleF shape) =>
            source.Clear(options, new SolidBrush(color), shape);

        /// <summary>
        /// Flood fills the image in the shape of the provided rectangle with the specified brush without any blending.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="color">The color.</param>
        /// <param name="shape">The shape.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Clear(this IImageProcessingContext source, Color color, RectangleF shape) =>
            source.Clear(new SolidBrush(color), shape);
    }
}
