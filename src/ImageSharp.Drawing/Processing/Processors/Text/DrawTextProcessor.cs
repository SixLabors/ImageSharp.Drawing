// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.Fonts;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Text
{
    /// <summary>
    /// Defines a processor to draw text on an <see cref="Image"/>.
    /// </summary>
    public class DrawTextProcessor : IImageProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DrawTextProcessor"/> class.
        /// </summary>
        /// <param name="drawingOptions">The drawing options.</param>
        /// <param name="textOptions">The text rendering options.</param>
        /// <param name="text">The text we want to render</param>
        /// <param name="brush">The brush to source pixel colors from.</param>
        /// <param name="pen">The pen to outline text with.</param>
        public DrawTextProcessor(DrawingOptions drawingOptions, TextDrawingOptions textOptions, string text, IBrush brush, IPen pen)
        {
            Guard.NotNull(text, nameof(text));
            if (brush is null && pen is null)
            {
                throw new ArgumentNullException($"Expected a {nameof(brush)} or {nameof(pen)}. Both were null");
            }

            this.DrawingOptions = drawingOptions;
            this.TextOptions = textOptions;
            this.Text = text;
            this.Brush = brush;
            this.Pen = pen;
        }

        /// <summary>
        /// Gets the brush used to fill the glyphs.
        /// </summary>
        public IBrush Brush { get; }

        /// <summary>
        /// Gets the <see cref="Processing.DrawingOptions"/> defining blending modes and shape drawing settings.
        /// </summary>
        public DrawingOptions DrawingOptions { get; }

        /// <summary>
        /// Gets the <see cref="Fonts.TextDrawingOptions"/> defining text-specific drawing settings.
        /// </summary>
        public TextDrawingOptions TextOptions { get; }

        /// <summary>
        /// Gets the text to draw.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets the pen used for outlining the text, if Null then we will not outline
        /// </summary>
        public IPen Pen { get; }

        /// <summary>
        /// Gets the location to draw the text at.
        /// </summary>
        public PointF Location { get; }

        /// <inheritdoc />
        public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle)
            where TPixel : unmanaged, IPixel<TPixel>
            => new DrawTextProcessor<TPixel>(configuration, this, source, sourceRectangle);
    }
}
