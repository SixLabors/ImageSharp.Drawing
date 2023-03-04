// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the drawing of lines.
    /// </summary>
    public static class DrawLineExtensions
    {
        /// <summary>
        /// Draws the provided points as an open linear path at the provided thickness with the supplied brush.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="options">The options.</param>
        /// <param name="brush">The brush.</param>
        /// <param name="thickness">The line thickness.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawLines(
            this IImageProcessingContext source,
            DrawingOptions options,
            Brush brush,
            float thickness,
            params PointF[] points) =>
            source.Draw(options, new Pen(brush, thickness), new Path(new LinearLineSegment(points)));

        /// <summary>
        /// Draws the provided points as an open linear path at the provided thickness with the supplied brush.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="brush">The brush.</param>
        /// <param name="thickness">The line thickness.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawLines(
            this IImageProcessingContext source,
            Brush brush,
            float thickness,
            params PointF[] points) =>
            source.Draw(new Pen(brush, thickness), new Path(new LinearLineSegment(points)));

        /// <summary>
        /// Draws the provided points as an open linear path at the provided thickness with the supplied brush.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="color">The color.</param>
        /// <param name="thickness">The line thickness.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawLines(
            this IImageProcessingContext source,
            Color color,
            float thickness,
            params PointF[] points) =>
            source.DrawLines(new SolidBrush(color), thickness, points);

        /// <summary>
        /// Draws the provided points as an open linear path at the provided thickness with the supplied brush.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="options">The options.</param>
        /// <param name="color">The color.</param>
        /// <param name="thickness">The line thickness.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>>
        public static IImageProcessingContext DrawLines(
            this IImageProcessingContext source,
            DrawingOptions options,
            Color color,
            float thickness,
            params PointF[] points) =>
            source.DrawLines(options, new SolidBrush(color), thickness, points);

        /// <summary>
        /// Draws the provided points as an open linear path with the supplied pen.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="options">The options.</param>
        /// <param name="pen">The pen.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawLines(
            this IImageProcessingContext source,
            DrawingOptions options,
            IPen pen,
            params PointF[] points) =>
            source.Draw(options, pen, new Path(new LinearLineSegment(points)));

        /// <summary>
        /// Draws the provided points as an open linear path with the supplied pen.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="pen">The pen.</param>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawLines(
            this IImageProcessingContext source,
            IPen pen,
            params PointF[] points) =>
            source.Draw(pen, new Path(new LinearLineSegment(points)));
    }
}
