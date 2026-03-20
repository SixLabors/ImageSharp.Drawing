// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides an implementation of an image brush for painting images within areas.
/// </summary>
/// <typeparam name="TPixel">The pixel format of the source image.</typeparam>
public sealed class ImageBrush<TPixel> : ImageBrush
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush{TPixel}"/> class.
    /// </summary>
    /// <param name="image">The source image to draw.</param>
    public ImageBrush(Image<TPixel> image)
        : base(image)
        => this.SourceImage = image;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush{TPixel}"/> class.
    /// </summary>
    /// <param name="image">The source image to draw.</param>
    /// <param name="offset">An offset to apply to the image while drawing the texture.</param>
    public ImageBrush(Image<TPixel> image, Point offset)
        : base(image, offset)
        => this.SourceImage = image;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush{TPixel}"/> class.
    /// </summary>
    /// <param name="image">The source image to draw.</param>
    /// <param name="region">The region of interest within the source image.</param>
    public ImageBrush(Image<TPixel> image, RectangleF region)
        : base(image, region)
        => this.SourceImage = image;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush{TPixel}"/> class.
    /// </summary>
    /// <param name="image">The source image to draw.</param>
    /// <param name="region">The region of interest within the source image.</param>
    /// <param name="offset">An offset to apply to the image while drawing the texture.</param>
    public ImageBrush(Image<TPixel> image, RectangleF region, Point offset)
        : base(image, region, offset)
        => this.SourceImage = image;

    /// <summary>
    /// Gets the typed source image used by this brush.
    /// </summary>
    public Image<TPixel> SourceImage { get; }
}

/// <summary>
/// The untyped base class for image brushes, used to support non-generic brush references in drawing contexts.
/// </summary>
public abstract class ImageBrush : Brush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush"/> class.
    /// </summary>
    /// <param name="image">The source image to draw.</param>
    protected ImageBrush(Image image)
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
    protected ImageBrush(Image image, Point offset)
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
    protected ImageBrush(Image image, RectangleF region)
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
    protected ImageBrush(Image image, RectangleF region, Point offset)
    {
        this.UntypedImage = image;
        this.SourceRegion = RectangleF.Intersect(image.Bounds, region);
        this.Offset = offset;
    }

    /// <summary>
    /// Gets the source image used by this brush.
    /// </summary>
    public Image UntypedImage { get; }

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
            return ib.UntypedImage == this.UntypedImage && ib.SourceRegion == this.SourceRegion;
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.UntypedImage, this.SourceRegion);

    /// <inheritdoc />
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
    {
        if (this.UntypedImage is Image<TPixel> image)
        {
            return new ImageBrushRenderer<TPixel>(configuration, options, canvasWidth, image, region, this.SourceRegion, this.Offset);
        }

        // This will never be hit as the brush is always normalized by the drawing canvas
        // but we do it to satisfy the type system.
        ThrowIfInvalidImagePixelFormat();
        return null;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowIfInvalidImagePixelFormat()
        => throw new UnreachableException("The pixel format of the image is not supported by this brush renderer");

    /// <summary>
    /// The image brush applicator.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private class ImageBrushRenderer<TPixel> : BrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly ImageFrame<TPixel> sourceFrame;

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
        public ImageBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            Image<TPixel> image,
            RectangleF targetRegion,
            RectangleF sourceRegion,
            Point offset)
            : base(configuration, options, canvasWidth)
        {
            this.sourceFrame = image.Frames.RootFrame;
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
