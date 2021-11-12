// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the drawing of text on an image.
    /// </summary>
    public static class DrawTextExtensions
    {
        /// <summary>
        /// Draws the text onto the image filled with the given color.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="font">The font.</param>
        /// <param name="color">The color.</param>
        /// <param name="location">The location.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawText(
            this IImageProcessingContext source,
            string text,
            Font font,
            Color color,
            PointF location) =>
            source.DrawText(source.GetDrawingOptions(), text, font, color, location);

        /// <summary>
        /// Draws the text using the supplied drawing options onto the image filled with the given color.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="drawingOptions">The drawing options.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="font">The font.</param>
        /// <param name="color">The color.</param>
        /// <param name="location">The location.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawText(
            this IImageProcessingContext source,
            DrawingOptions drawingOptions,
            string text,
            Font font,
            Color color,
            PointF location) =>
            source.DrawText(drawingOptions, text, font, Brushes.Solid(color), null, location);

        /// <summary>
        /// Draws the text  using the supplied text options onto the image filled via the brush.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="textOptions">The text rendering options.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="color">The color.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawText(
            this IImageProcessingContext source,
            TextOptions textOptions,
            string text,
            Color color) =>
            source.DrawText(textOptions, text, Brushes.Solid(color), null);

        /// <summary>
        /// Draws the text onto the image filled via the brush.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="font">The font.</param>
        /// <param name="brush">The brush used to fill the text.</param>
        /// <param name="location">The location.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawText(
            this IImageProcessingContext source,
            string text,
            Font font,
            IBrush brush,
            PointF location) =>
            source.DrawText(source.GetDrawingOptions(), text, font, brush, location);

        /// <summary>
        /// Draws the text onto the image outlined via the pen.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="font">The font.</param>
        /// <param name="pen">The pen used to outline the text.</param>
        /// <param name="location">The location.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawText(
            this IImageProcessingContext source,
            string text,
            Font font,
            IPen pen,
            PointF location) =>
            source.DrawText(source.GetDrawingOptions(), text, font, pen, location);

        /// <summary>
        /// Draws the text onto the image filled via the brush then outlined via the pen.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="font">The font.</param>
        /// <param name="brush">The brush used to fill the text.</param>
        /// <param name="pen">The pen used to outline the text.</param>
        /// <param name="location">The location.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawText(
            this IImageProcessingContext source,
            string text,
            Font font,
            IBrush brush,
            IPen pen,
            PointF location)
        {
            TextOptions textOptions = new(font) { Origin = location };
            return source.DrawText(textOptions, text, brush, pen);
        }

        /// <summary>
        /// Draws the text using the given options onto the image filled via the brush.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="textOptions">The text rendering options.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="brush">The brush used to fill the text.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawText(
            this IImageProcessingContext source,
            TextOptions textOptions,
            string text,
            IBrush brush) =>
            source.DrawText(source.GetDrawingOptions(), textOptions, text, brush, null);

        /// <summary>
        /// Draws the text using the given options onto the image outlined via the pen.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="textOptions">The text rendering options.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="pen">The pen used to outline the text.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawText(
            this IImageProcessingContext source,
            TextOptions textOptions,
            string text,
            IPen pen) =>
            source.DrawText(source.GetDrawingOptions(), textOptions, text, null, pen);

        /// <summary>
        /// Draws the text using the given options onto the image filled via the brush then outlined via the pen.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="textOptions">The text rendering options.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="brush">The brush used to fill the text.</param>
        /// <param name="pen">The pen used to outline the text.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawText(
            this IImageProcessingContext source,
            TextOptions textOptions,
            string text,
            IBrush brush,
            IPen pen) =>
            source.DrawText(source.GetDrawingOptions(), textOptions, text, brush, pen);

        /// <summary>
        /// Draws the text onto the image outlined via the pen.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="drawingOptions">The drawing options.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="font">The font.</param>
        /// <param name="pen">The pen used to outline the text.</param>
        /// <param name="location">The location.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawText(
            this IImageProcessingContext source,
            DrawingOptions drawingOptions,
            string text,
            Font font,
            IPen pen,
            PointF location)
            => source.DrawText(drawingOptions, text, font, null, pen, location);

        /// <summary>
        /// Draws the text onto the image filled via the brush.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="drawingOptions">The drawing options.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="font">The font.</param>
        /// <param name="brush">The brush used to fill the text.</param>
        /// <param name="location">The location.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawText(
            this IImageProcessingContext source,
            DrawingOptions drawingOptions,
            string text,
            Font font,
            IBrush brush,
            PointF location)
            => source.DrawText(drawingOptions, text, font, brush, null, location);

        /// <summary>
        /// Draws the text using the given drawing options onto the image filled via the brush then outlined via the pen.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="drawingOptions">The drawing options.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="font">The font.</param>
        /// <param name="brush">The brush used to fill the text.</param>
        /// <param name="pen">The pen used to outline the text.</param>
        /// <param name="location">The location.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawText(
            this IImageProcessingContext source,
            DrawingOptions drawingOptions,
            string text,
            Font font,
            IBrush brush,
            IPen pen,
            PointF location)
        {
            TextOptions textOptions = new(font) { Origin = location };
            return source.ApplyProcessor(new DrawTextProcessor(drawingOptions, textOptions, text, brush, pen));
        }

        /// <summary>
        /// Draws the text using the given options onto the image filled via the brush then outlined via the pen.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="drawingOptions">The drawing options.</param>
        /// <param name="textOptions">The text rendering options.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="brush">The brush used to fill the text.</param>
        /// <param name="pen">The pen used to outline the text.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext DrawText(
            this IImageProcessingContext source,
            DrawingOptions drawingOptions,
            TextOptions textOptions,
            string text,
            IBrush brush,
            IPen pen)
            => source.ApplyProcessor(new DrawTextProcessor(drawingOptions, textOptions, text, brush, pen));
    }
}
