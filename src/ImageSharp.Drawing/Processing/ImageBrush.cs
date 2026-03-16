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
    public ImageBrush(Image image, RectangleF region)
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
    /// <param name="offset">
    /// An offset to apply the to image image while drawing apply the texture.
    /// </param>
    public ImageBrush(Image image, RectangleF region, Point offset)
    {
        this.SourceImage = image;
        this.SourceRegion = RectangleF.Intersect(image.Bounds, region);
        this.Offset = offset;
    }

    /// <summary>
    /// Gets the source image used by this brush.
    /// </summary>
    public Image SourceImage { get; }

    /// <summary>
    /// Gets the source region within the image.
    /// </summary>
    public RectangleF SourceRegion { get; }

    /// <summary>
    /// Gets the offset applied to the brush origin.
    /// </summary>
    public Point Offset { get; }

    /// <inheritdoc />
    public override bool Equals(Brush? other)
    {
        if (other is ImageBrush ib)
        {
            return ib.SourceImage == this.SourceImage && ib.SourceRegion == this.SourceRegion;
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.SourceImage, this.SourceRegion);

    /// <inheritdoc />
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
    {
        if (this.SourceImage is Image<TPixel> specificImage)
        {
            return new ImageBrushRenderer<TPixel>(configuration, options, canvasWidth, specificImage, region, this.SourceRegion, this.Offset, false);
        }

        specificImage = this.SourceImage.CloneAs<TPixel>();
        return new ImageBrushRenderer<TPixel>(configuration, options, canvasWidth, specificImage, region, this.SourceRegion, this.Offset, true);
    }

    /// <summary>
    /// The image brush applicator.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private class ImageBrushRenderer<TPixel> : BrushRenderer<TPixel>
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
        /// Initializes a new instance of the <see cref="ImageBrushRenderer{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="image">The image.</param>
        /// <param name="targetRegion">The region of the target image we will be drawing to.</param>
        /// <param name="sourceRegion">The region of the source image we will be using to source pixels to draw from.</param>
        /// <param name="offset">An offset to apply to the texture while drawing.</param>
        /// <param name="shouldDisposeImage">Whether to dispose the image on disposal of the applicator.</param>
        public ImageBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            Image<TPixel> image,
            RectangleF targetRegion,
            RectangleF sourceRegion,
            Point offset,
            bool shouldDisposeImage)
            : base(configuration, options, canvasWidth)
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
        public override void Apply(
            Span<TPixel> destinationRow,
            ReadOnlySpan<float> scanline,
            int x,
            int y,
            BrushWorkspace<TPixel> workspace)
        {
            Span<float> amountSpan = workspace.GetAmounts(scanline.Length);
            Span<TPixel> overlaySpan = workspace.GetOverlays(scanline.Length);

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

            this.Blender.Blend(
                this.Configuration,
                destinationRow,
                destinationRow,
                overlaySpan,
                amountSpan);
        }
    }
}
