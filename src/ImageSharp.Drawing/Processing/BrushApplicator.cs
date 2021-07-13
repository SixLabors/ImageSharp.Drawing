// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Performs the application of an <see cref="IBrush"/> implementation against individual scanlines.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <seealso cref="IDisposable" />
    public abstract class BrushApplicator<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BrushApplicator{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="target">The target image frame.</param>
        protected BrushApplicator(Configuration configuration, GraphicsOptions options, ImageFrame<TPixel> target)
        {
            this.Configuration = configuration;
            this.Target = target;
            this.Options = options;
            this.Blender = PixelOperations<TPixel>.Instance.GetPixelBlender(options);
        }

        /// <summary>
        /// Gets the configuration instance to use when performing operations.
        /// </summary>
        protected Configuration Configuration { get; }

        /// <summary>
        /// Gets the pixel blender.
        /// </summary>
        internal PixelBlender<TPixel> Blender { get; }

        /// <summary>
        /// Gets the target image frame.
        /// </summary>
        protected ImageFrame<TPixel> Target { get; }

        /// <summary>
        /// Gets the graphics options
        /// </summary>
        protected GraphicsOptions Options { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the object and frees resources for the Garbage Collector.
        /// </summary>
        /// <param name="disposing">Whether to dispose managed and unmanaged objects.</param>
        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>
        /// Applies the opacity weighting for each pixel in a scanline to the target based on the
        /// pattern contained in the brush.
        /// </summary>
        /// <param name="scanline">
        /// A collection of opacity values between 0 and 1 to be merged with the brushed color value
        /// before being applied to the
        /// target.
        /// </param>
        /// <param name="x">The x-position in the target pixel space that the start of the scanline data corresponds to.</param>
        /// <param name="y">The y-position in  the target pixel space that whole scanline corresponds to.</param>
        public abstract void Apply(Span<float> scanline, int x, int y);
    }
}
