// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the drawing of closed linear polygons to the <see cref="Image{TPixel}"/> type.
    /// </summary>
    public static class DrawPolygonExtensions
    {
        /// <summary>
        /// Draws the provided Points as a closed Linear Polygon with the provided Pen.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="pen">The pen.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawPolygon(
            this IImageProcessingContext source,
            IPen pen,
            params PointF[] points) =>
            source.Draw(source.GetDrawingOptions(), pen, new Polygon(new LinearLineSegment(points)));

        /// <summary>
        /// Draws the provided Points as a closed Linear Polygon with the provided Pen.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="options">The options.</param>
        /// <param name="pen">The pen.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawPolygon(
            this IImageProcessingContext source,
            DrawingOptions options,
            IPen pen,
            params PointF[] points) =>
            source.Draw(options, pen, new Polygon(new LinearLineSegment(points)));

        /// <summary>
        /// Draws the provided Points as a closed Linear Polygon with the provided brush at the provided thickness.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="options">The options.</param>
        /// <param name="brush">The brush.</param>
        /// <param name="thickness">The thickness.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawPolygon(
            this IImageProcessingContext source,
            DrawingOptions options,
            IBrush brush,
            float thickness,
            params PointF[] points) =>
            source.DrawPolygon(options, new Pen(brush, thickness), points);

        /// <summary>
        /// Draws the provided Points as a closed Linear Polygon with the provided brush at the provided thickness.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="brush">The brush.</param>
        /// <param name="thickness">The thickness.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawPolygon(
            this IImageProcessingContext source,
            IBrush brush,
            float thickness,
            params PointF[] points) =>
            source.DrawPolygon(new Pen(brush, thickness), points);

        /// <summary>
        /// Draws the provided Points as a closed Linear Polygon with the provided brush at the provided thickness.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="color">The color.</param>
        /// <param name="thickness">The thickness.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawPolygon(
            this IImageProcessingContext source,
            Color color,
            float thickness,
            params PointF[] points) =>
            source.DrawPolygon(new SolidBrush(color), thickness, points);

        /// <summary>
        /// Draws the provided Points as a closed Linear Polygon with the provided brush at the provided thickness.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="options">The options.</param>
        /// <param name="color">The color.</param>
        /// <param name="thickness">The thickness.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawPolygon(
            this IImageProcessingContext source,
            DrawingOptions options,
            Color color,
            float thickness,
            params PointF[] points) =>
            source.DrawPolygon(options, new SolidBrush(color), thickness, points);
    }
}
