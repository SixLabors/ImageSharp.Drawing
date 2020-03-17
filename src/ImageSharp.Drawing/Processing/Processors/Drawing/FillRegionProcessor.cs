// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Processing.Processors.Drawing
{
    /// <summary>
    /// Defines a processor to fill <see cref="Image"/> pixels withing a given <see cref="Region"/>
    /// with the given <see cref="IBrush"/> and blending defined by the given <see cref="GraphicsOptions"/>.
    /// </summary>
    public class FillRegionProcessor : IImageProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FillRegionProcessor" /> class.
        /// </summary>
        /// <param name="options">The graphics options.</param>
        /// <param name="brush">The details how to fill the region of interest.</param>
        /// <param name="region">The region of interest to be filled.</param>
        public FillRegionProcessor(ShapeGraphicsOptions options, IBrush brush, Region region)
        {
            this.Region = region;
            this.Brush = brush;
            this.ShapeOptions = options;
            this.Options = (GraphicsOptions)options;
        }

        /// <summary>
        /// Gets the <see cref="IBrush"/> used for filling the destination image.
        /// </summary>
        public IBrush Brush { get; }

        /// <summary>
        /// Gets the region that this processor applies to.
        /// </summary>
        public Region Region { get; }

        /// <summary>
        /// Gets the <see cref="GraphicsOptions"/> defining how to blend the brush pixels over the image pixels.
        /// </summary>
        public ShapeGraphicsOptions ShapeOptions { get; }

        /// <summary>
        /// Gets the <see cref="GraphicsOptions"/> defining how to blend the brush pixels over the image pixels.
        /// </summary>
        public GraphicsOptions Options { get; }

        /// <inheritdoc />
        public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle)
            where TPixel : unmanaged, IPixel<TPixel>
            => new FillRegionProcessor<TPixel>(configuration, this, source, sourceRectangle);
    }
}
