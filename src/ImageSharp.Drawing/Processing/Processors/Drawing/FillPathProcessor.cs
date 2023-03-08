// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing
{
    /// <summary>
    /// Defines a processor to fill <see cref="Image"/> pixels withing a given <see cref="IPath"/>
    /// with the given <see cref="Processing.Brush"/> and blending defined by the given <see cref="DrawingOptions"/>.
    /// </summary>
    public class FillPathProcessor : IImageProcessor
    {
        /// <summary>
        /// Minimum subpixel count for rasterization, being applied even if antialiasing is off.
        /// </summary>
        internal const int MinimumSubpixelCount = 8;

        /// <summary>
        /// Initializes a new instance of the <see cref="FillPathProcessor" /> class.
        /// </summary>
        /// <param name="options">The graphics options.</param>
        /// <param name="brush">The details how to fill the region of interest.</param>
        /// <param name="path">The logic path to be filled.</param>
        public FillPathProcessor(DrawingOptions options, Brush brush, IPath path)
        {
            this.Region = path;
            this.Brush = brush;
            this.Options = options;
        }

        /// <summary>
        /// Gets the <see cref="Processing.Brush"/> used for filling the destination image.
        /// </summary>
        public Brush Brush { get; }

        /// <summary>
        /// Gets the logic path that this processor applies to.
        /// </summary>
        public IPath Region { get; }

        /// <summary>
        /// Gets the <see cref="GraphicsOptions"/> defining how to blend the brush pixels over the image pixels.
        /// </summary>
        public DrawingOptions Options { get; }

        /// <inheritdoc />
        public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            IPath shape = this.Region.Transform(this.Options.Transform);

            if (shape is RectangularPolygon rectPoly)
            {
                var rectF = new RectangleF(rectPoly.Location, rectPoly.Size);
                var rect = (Rectangle)rectF;
                if (!this.Options.GraphicsOptions.Antialias || rectF == rect)
                {
                    var interest = Rectangle.Intersect(sourceRectangle, rect);

                    // Cast as in and back are the same or we are using anti-aliasing
                    return new FillProcessor(this.Options, this.Brush)
                        .CreatePixelSpecificProcessor(configuration, source, interest);
                }
            }

            // Clone the definition so we can pass the transformed path.
            var definition = new FillPathProcessor(this.Options, this.Brush, shape);
            return new FillPathProcessor<TPixel>(configuration, definition, source, sourceRectangle);
        }
    }
}
