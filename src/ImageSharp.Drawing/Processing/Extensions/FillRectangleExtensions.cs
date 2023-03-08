// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the filling of rectangles.
    /// </summary>
    public static class FillRectangleExtensions
    {
        /// <summary>
        /// Flood fills the image in the shape of the provided rectangle with the specified brush.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="options">The options.</param>
        /// <param name="brush">The brush.</param>
        /// <param name="shape">The shape.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Fill(
            this IImageProcessingContext source,
            DrawingOptions options,
            Brush brush,
            RectangleF shape) =>
            source.Fill(options, brush, new RectangularPolygon(shape.X, shape.Y, shape.Width, shape.Height));

        /// <summary>
        /// Flood fills the image in the shape of the provided rectangle with the specified brush.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="brush">The brush.</param>
        /// <param name="shape">The shape.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext
            Fill(this IImageProcessingContext source, Brush brush, RectangleF shape) =>
            source.Fill(brush, new RectangularPolygon(shape.X, shape.Y, shape.Width, shape.Height));

        /// <summary>
        /// Flood fills the image in the shape of the provided rectangle with the specified brush.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="options">The options.</param>
        /// <param name="color">The color.</param>
        /// <param name="shape">The shape.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Fill(
            this IImageProcessingContext source,
            DrawingOptions options,
            Color color,
            RectangleF shape) =>
            source.Fill(options, new SolidBrush(color), shape);

        /// <summary>
        /// Flood fills the image in the shape of the provided rectangle with the specified brush.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="color">The color.</param>
        /// <param name="shape">The shape.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext
            Fill(this IImageProcessingContext source, Color color, RectangleF shape) =>
            source.Fill(new SolidBrush(color), shape);
    }
}
