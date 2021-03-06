// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing
{
    /// <summary>
    /// Defines a processor to fill <see cref="Image"/> pixels withing a given <see cref="IPath"/>
    /// with the given <see cref="IBrush"/> and blending defined by the given <see cref="DrawingOptions"/>.
    /// </summary>
    public class FillPathProcessor : IImageProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FillPathProcessor" /> class.
        /// </summary>
        /// <param name="options">The graphics options.</param>
        /// <param name="brush">The details how to fill the region of interest.</param>
        /// <param name="shape">The shape to be filled.</param>
        public FillPathProcessor(DrawingOptions options, IBrush brush, IPath shape)
        {
            this.Shape = shape;
            this.Brush = brush;
            this.Options = options;
        }

        /// <summary>
        /// Gets the <see cref="IBrush"/> used for filling the destination image.
        /// </summary>
        public IBrush Brush { get; }

        /// <summary>
        /// Gets the region that this processor applies to.
        /// </summary>
        public IPath Shape { get; }

        /// <summary>
        /// Gets the <see cref="GraphicsOptions"/> defining how to blend the brush pixels over the image pixels.
        /// </summary>
        public DrawingOptions Options { get; }

        /// <inheritdoc />
        public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            IPath shape = this.Shape.Transform(this.Options.Transform);

            if (shape is RectangularPolygon rectPoly)
            {
                var rectF = new RectangleF(rectPoly.Location, rectPoly.Size);
                var rect = (Rectangle)rectF;
                if (this.Options.GraphicsOptions.Antialias == false || rectF == rect)
                {
                    var interest = Rectangle.Intersect(sourceRectangle, rect);

                    // cast as in and back are the same or we are using anti-aliasing
                    return new FillProcessor(this.Options.GraphicsOptions, this.Brush).CreatePixelSpecificProcessor(configuration, source, interest);
                }
            }

            return new FillRegionProcessor(this.Options, this.Brush, new ShapeRegion(shape)).CreatePixelSpecificProcessor(configuration, source, sourceRectangle);
        }
    }
}
