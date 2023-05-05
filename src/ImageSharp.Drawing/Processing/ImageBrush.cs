// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Provides an implementation of an image brush for painting images within areas.
    /// </summary>
    public class ImageBrush : Brush
    {
        /// <summary>
        /// The image to paint.
        /// </summary>
        private readonly Image image;

        /// <summary>
        /// The region of the source image we will be using to paint.
        /// </summary>
        private readonly RectangleF region;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageBrush"/> class.
        /// </summary>
        /// <param name="image">The image.</param>
        public ImageBrush(Image image)
            : this(image, image.Bounds())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageBrush"/> class.
        /// </summary>
        /// <param name="image">The image.</param>
        /// <param name="region">
        /// The region of interest.
        /// This overrides any region used to intitialize the brush applicator.
        /// </param>
        internal ImageBrush(Image image, RectangleF region)
        {
            this.image = image;
            this.region = region;
        }

        /// <inheritdoc />
        public override bool Equals(Brush other)
        {
            if (other is ImageBrush sb)
            {
                return sb.image == this.image && sb.region == this.region;
            }

            return false;
        }

        /// <inheritdoc />
        public override BrushApplicator<TPixel> CreateApplicator<TPixel>(
            Configuration configuration,
            GraphicsOptions options,
            ImageFrame<TPixel> source,
            RectangleF region)
        {
            if (this.image is Image<TPixel> specificImage)
            {
                return new ImageBrushApplicator<TPixel>(configuration, options, source, specificImage, region, this.region, false);
            }

            specificImage = this.image.CloneAs<TPixel>();
            return new ImageBrushApplicator<TPixel>(configuration, options, source, specificImage, region, this.region, true);
        }

        /// <summary>
        /// The image brush applicator.
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        private class ImageBrushApplicator<TPixel> : BrushApplicator<TPixel>
            where TPixel : unmanaged, IPixel<TPixel>
        {
            private ImageFrame<TPixel> sourceFrame;

            private Image<TPixel> sourceImage;

            private readonly bool shouldDisposeImage;

            /// <summary>
            /// The region of the source image we will be using to draw from.
            /// </summary>
            private readonly Rectangle sourceRegion;

            /// <summary>
            /// The Y offset.
            /// </summary>
            private readonly int offsetY;

            /// <summary>
            /// The X offset.
            /// </summary>
            private readonly int offsetX;
            private bool isDisposed;

            /// <summary>
            /// Initializes a new instance of the <see cref="ImageBrushApplicator{TPixel}"/> class.
            /// </summary>
            /// <param name="configuration">The configuration instance to use when performing operations.</param>
            /// <param name="options">The graphics options.</param>
            /// <param name="target">The target image.</param>
            /// <param name="image">The image.</param>
            /// <param name="targetRegion">The region of the target image we will be drawing to.</param>
            /// <param name="sourceRegion">The region of the source image we will be using to source pixels to draw from.</param>
            /// <param name="shouldDisposeImage">Whether to dispose the image on disposal of the applicator.</param>
            public ImageBrushApplicator(
                Configuration configuration,
                GraphicsOptions options,
                ImageFrame<TPixel> target,
                Image<TPixel> image,
                RectangleF targetRegion,
                RectangleF sourceRegion,
                bool shouldDisposeImage)
                : base(configuration, options, target)
            {
                this.sourceImage = image;
                this.sourceFrame = image.Frames.RootFrame;
                this.shouldDisposeImage = shouldDisposeImage;

                this.sourceRegion = Rectangle.Intersect(image.Bounds(), (Rectangle)sourceRegion);

                this.offsetY = (int)MathF.Max(MathF.Floor(targetRegion.Top), 0);
                this.offsetX = (int)MathF.Max(MathF.Floor(targetRegion.Left), 0);
            }

            internal TPixel this[int x, int y]
            {
                get
                {
                    int srcX = ((x - this.offsetX) % this.sourceRegion.Width) + this.sourceRegion.X;
                    int srcY = ((y - this.offsetY) % this.sourceRegion.Height) + this.sourceRegion.Y;
                    return this.sourceFrame[srcX, srcY];
                }
            }

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (this.isDisposed)
                {
                    return;
                }

                if (disposing && this.shouldDisposeImage)
                {
                    this.sourceImage?.Dispose();
                }

                this.sourceImage = null;
                this.sourceFrame = null;
                this.isDisposed = true;
            }

            /// <inheritdoc />
            public override void Apply(Span<float> scanline, int x, int y)
            {
                // Create a span for colors
                MemoryAllocator allocator = this.Configuration.MemoryAllocator;
                using IMemoryOwner<float> amountBuffer = allocator.Allocate<float>(scanline.Length);
                using IMemoryOwner<TPixel> overlay = allocator.Allocate<TPixel>(scanline.Length);
                Span<float> amountSpan = amountBuffer.Memory.Span;
                Span<TPixel> overlaySpan = overlay.Memory.Span;

                int offsetX = x - this.offsetX;
                int sourceY = ((y - this.offsetY) % this.sourceRegion.Height) + this.sourceRegion.Y;
                Span<TPixel> sourceRow = this.sourceFrame.PixelBuffer.DangerousGetRowSpan(sourceY);

                for (int i = 0; i < scanline.Length; i++)
                {
                    amountSpan[i] = scanline[i] * this.Options.BlendPercentage;

                    int sourceX = ((i + offsetX) % this.sourceRegion.Width) + this.sourceRegion.X;

                    overlaySpan[i] = sourceRow[sourceX];
                }

                Span<TPixel> destinationRow = this.Target.PixelBuffer.DangerousGetRowSpan(y).Slice(x, scanline.Length);
                this.Blender.Blend(
                    this.Configuration,
                    destinationRow,
                    destinationRow,
                    overlaySpan,
                    amountSpan);
            }
        }
    }
}
