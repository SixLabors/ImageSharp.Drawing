// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing
{
    /// <summary>
    /// Defines a processor to fill <see cref="Image"/> pixels withing a given <see cref="IPath"/>
    /// with the given <see cref="IBrush"/> and blending defined by the given <see cref="ShapeGraphicsOptions"/>.
    /// </summary>
    public class FillPathProcessor : IImageProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FillPathProcessor" /> class.
        /// </summary>
        /// <param name="options">The graphics options.</param>
        /// <param name="brush">The details how to fill the region of interest.</param>
        /// <param name="shape">The shape to be filled.</param>
        public FillPathProcessor(ShapeGraphicsOptions options, IBrush brush, IPath shape)
        {
            this.Shape = shape;
            this.Brush = brush;
            this.ShapeOptions = options;
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
        public ShapeGraphicsOptions ShapeOptions { get; }

        /// <inheritdoc />
        public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle)
            where TPixel : unmanaged, IPixel<TPixel>
            => new FillRegionProcessor(this.ShapeOptions, this.Brush, new ShapeRegion(this.Shape)).CreatePixelSpecificProcessor(configuration, source, sourceRectangle);
    }
}
