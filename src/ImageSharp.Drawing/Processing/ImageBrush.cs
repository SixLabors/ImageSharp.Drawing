// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing;

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
    /// The an offet to apply to the source image while applying the imagebrush
    /// </summary>
    private readonly Point offet;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush"/> class.
    /// </summary>
    /// <param name="image">The source image to draw.</param>
    public ImageBrush(Image image)
        : this(image, image.Bounds)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush"/> class.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="offset">
    /// An offset to apply the to image image while drawing apply the texture.
    /// </param>
    public ImageBrush(Image image, Point offset)
        : this(image, image.Bounds, offset)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush"/> class.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="region">
    /// The region of interest.
    /// This overrides any region used to initialize the brush applicator.
    /// </param>
    internal ImageBrush(Image image, RectangleF region)
        : this(image, region, Point.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush"/> class.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="region">
    /// The region of interest.
    /// This overrides any region used to initialize the brush applicator.
    /// </param>
    /// <param name="offet">
    /// An offset to apply the to image image while drawing apply the texture.
    /// </param>
    internal ImageBrush(Image image, RectangleF region, Point offet)
    {
        this.image = image;
        this.region = RectangleF.Intersect(image.Bounds, region);
        this.offet = offet;
    }

    /// <inheritdoc />
    public override bool Equals(Brush? other)
    {
        if (other is ImageBrush ib)
        {
            return ib.image == this.image && ib.region == this.region;
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.image, this.region);

    /// <inheritdoc />
    public override BrushApplicator<TPixel> CreateApplicator<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        ImageFrame<TPixel> source,
        RectangleF region)
    {
        if (this.image is Image<TPixel> specificImage)
        {
            return new ImageBrushApplicator<TPixel>(configuration, options, source, specificImage, region, this.region, this.offet, false);
        }

        specificImage = this.image.CloneAs<TPixel>();
        return new ImageBrushApplicator<TPixel>(configuration, options, source, specificImage, region, this.region, this.offet, true);
    }

    /// <summary>
    /// The image brush applicator.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private class ImageBrushApplicator<TPixel> : BrushApplicator<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly ImageFrame<TPixel> sourceFrame;
        private readonly Image<TPixel> sourceImage;
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
        /// <param name="offset">An offset to apply to the texture while drawing.</param>
        /// <param name="shouldDisposeImage">Whether to dispose the image on disposal of the applicator.</param>
        public ImageBrushApplicator(
            Configuration configuration,
            GraphicsOptions options,
            ImageFrame<TPixel> target,
            Image<TPixel> image,
            RectangleF targetRegion,
            RectangleF sourceRegion,
            Point offset,
            bool shouldDisposeImage)
            : base(configuration, options, target)
        {
            this.sourceImage = image;
            this.sourceFrame = image.Frames.RootFrame;
            this.shouldDisposeImage = shouldDisposeImage;

            this.sourceRegion = Rectangle.Intersect(image.Bounds, (Rectangle)sourceRegion);

            this.offsetY = (int)MathF.Floor(targetRegion.Top) + offset.Y;
            this.offsetX = (int)MathF.Floor(targetRegion.Left) + offset.X;
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

            this.isDisposed = true;
            base.Dispose(disposing);
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
            int sourceY = ((((y - this.offsetY) % this.sourceRegion.Height) // clamp the number between -height and +height
                        + this.sourceRegion.Height) % this.sourceRegion.Height) // clamp the number between 0  and +height
                        + this.sourceRegion.Y;
            Span<TPixel> sourceRow = this.sourceFrame.PixelBuffer.DangerousGetRowSpan(sourceY);

            for (int i = 0; i < scanline.Length; i++)
            {
                amountSpan[i] = scanline[i] * this.Options.BlendPercentage;

                int sourceX = ((((i + offsetX) % this.sourceRegion.Width) // clamp the number between -width and +width
                        + this.sourceRegion.Width) % this.sourceRegion.Width) // clamp the number between 0  and +width
                        + this.sourceRegion.X;

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
