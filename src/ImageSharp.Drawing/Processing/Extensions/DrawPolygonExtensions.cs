// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the drawing of closed linear polygons.
    /// </summary>
    public static class DrawPolygonExtensions
    {
        /// <summary>
        /// Draws the provided points as a closed linear polygon with the provided pen.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="pen">The pen.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawPolygon(
            this IImageProcessingContext source,
            Pen pen,
            params PointF[] points) =>
            source.Draw(source.GetDrawingOptions(), pen, new Polygon(points));

        /// <summary>
        /// Draws the provided points as a closed linear polygon with the provided pen.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="options">The options.</param>
        /// <param name="pen">The pen.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawPolygon(
            this IImageProcessingContext source,
            DrawingOptions options,
            Pen pen,
            params PointF[] points) =>
            source.Draw(options, pen, new Polygon(points));

        /// <summary>
        /// Draws the provided points as a closed linear polygon with the provided brush at the provided thickness.
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
            Brush brush,
            float thickness,
            params PointF[] points) =>
            source.DrawPolygon(options, new SolidPen(brush, thickness), points);

        /// <summary>
        /// Draws the provided points as a closed linear polygon with the provided brush at the provided thickness.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="brush">The brush.</param>
        /// <param name="thickness">The thickness.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawPolygon(
            this IImageProcessingContext source,
            Brush brush,
            float thickness,
            params PointF[] points) =>
            source.DrawPolygon(new SolidPen(brush, thickness), points);

        /// <summary>
        /// Draws the provided points as a closed linear polygon with the provided brush at the provided thickness.
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
        /// Draws the provided points as a closed linear polygon with the provided brush at the provided thickness.
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
