// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing
{
    /// <summary>
    /// Allows the recursive application of processing operations against an image.
    /// </summary>
    public class RecursiveImageProcessor : IImageProcessor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RecursiveImageProcessor"/> class.
        /// </summary>
        /// <param name="options">The drawing options.</param>
        /// <param name="path">The logic path.</param>
        /// <param name="operation">The operation to perform on the source.</param>
        public RecursiveImageProcessor(DrawingOptions options, IPath path, Action<IImageProcessingContext> operation)
        {
            this.Options = options;
            this.Path = path;
            this.Operation = operation;
        }

        /// <summary>
        /// Gets the drawing options.
        /// </summary>
        public DrawingOptions Options { get; }

        /// <summary>
        /// Gets the logic path.
        /// </summary>
        public IPath Path { get; }

        /// <summary>
        /// Gets the operation to perform on the source.
        /// </summary>
        public Action<IImageProcessingContext> Operation { get; }

        /// <inheritdoc/>
        public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(
            Configuration configuration,
            Image<TPixel> source,
            Rectangle sourceRectangle)
            where TPixel : unmanaged, IPixel<TPixel>
            => new RecursiveImageProcessorInner<TPixel>(this, source, configuration, sourceRectangle);

        /// <summary>
        /// The main workhorse class. This has access to the pixel buffer but
        /// in an abstract/generic way.
        /// </summary>
        /// <typeparam name="TPixel">The type of pixel.</typeparam>
        private class RecursiveImageProcessorInner<TPixel> : IImageProcessor<TPixel>
            where TPixel : unmanaged, IPixel<TPixel>
        {
            private readonly RecursiveImageProcessor recursiveImageProcessor;
            private readonly Image<TPixel> source;
            private readonly Configuration configuration;
            private readonly Rectangle sourceRectangle;

            public RecursiveImageProcessorInner(RecursiveImageProcessor recursiveImageProcessor, Image<TPixel> source, Configuration configuration, Rectangle sourceRectangle)
            {
                this.recursiveImageProcessor = recursiveImageProcessor;
                this.source = source;
                this.configuration = configuration;
                this.sourceRectangle = sourceRectangle;
            }

            public void Dispose()
            {
            }

            public void Execute()
            {
                // Clone out our source image so we can apply various effects to it without mutating
                // the original yet.
                using Image<TPixel> clone = this.source.Clone(this.recursiveImageProcessor.Operation);

                // Use an image brush to apply cloned image as the source for filling the shape.
                // We pass explicit bounds to avoid the need to crop the clone;
                RectangleF bounds = this.recursiveImageProcessor.Path.Bounds;
                var brush = new ImageBrush(clone, bounds);

                // Grab hold of an image processor that can fill paths with a brush to allow it to do the hard pixel pushing for us
                var processor = new FillPathProcessor(this.recursiveImageProcessor.Options, brush, this.recursiveImageProcessor.Path);
                using IImageProcessor<TPixel> p = processor.CreatePixelSpecificProcessor(this.configuration, this.source, this.sourceRectangle);

                // Fill the shape using the image brush
                p.Execute();
            }
        }
    }
}
