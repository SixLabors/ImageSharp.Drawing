// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
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
    /// Initializes a new instance of the <see cref="ImageBrush{TPixel}"/> class.
    /// </summary>
    /// <param name="image">The source image to draw.</param>
    /// <param name="wrapX">The wrap mode used when sampling horizontally beyond the source region.</param>
    /// <param name="wrapY">The wrap mode used when sampling vertically beyond the source region.</param>
    public ImageBrush(Image<TPixel> image, WrapMode wrapX, WrapMode wrapY)
        : base(image, wrapX, wrapY)
        => this.SourceImage = image;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush{TPixel}"/> class.
    /// </summary>
    /// <param name="image">The source image to draw.</param>
    /// <param name="offset">An offset to apply to the image while drawing the texture.</param>
    /// <param name="wrapX">The wrap mode used when sampling horizontally beyond the source region.</param>
    /// <param name="wrapY">The wrap mode used when sampling vertically beyond the source region.</param>
    public ImageBrush(Image<TPixel> image, Point offset, WrapMode wrapX, WrapMode wrapY)
        : base(image, offset, wrapX, wrapY)
        => this.SourceImage = image;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush{TPixel}"/> class.
    /// </summary>
    /// <param name="image">The source image to draw.</param>
    /// <param name="region">The region of interest within the source image.</param>
    /// <param name="wrapX">The wrap mode used when sampling horizontally beyond the source region.</param>
    /// <param name="wrapY">The wrap mode used when sampling vertically beyond the source region.</param>
    public ImageBrush(Image<TPixel> image, RectangleF region, WrapMode wrapX, WrapMode wrapY)
        : base(image, region, wrapX, wrapY)
        => this.SourceImage = image;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush{TPixel}"/> class.
    /// </summary>
    /// <param name="image">The source image to draw.</param>
    /// <param name="region">The region of interest within the source image.</param>
    /// <param name="offset">An offset to apply to the image while drawing the texture.</param>
    /// <param name="wrapX">The wrap mode used when sampling horizontally beyond the source region.</param>
    /// <param name="wrapY">The wrap mode used when sampling vertically beyond the source region.</param>
    public ImageBrush(Image<TPixel> image, RectangleF region, Point offset, WrapMode wrapX, WrapMode wrapY)
        : base(image, region, offset, wrapX, wrapY)
        => this.SourceImage = image;

    /// <summary>
    /// Gets the typed source image used by this brush.
    /// </summary>
    public Image<TPixel> SourceImage { get; }

    /// <inheritdoc />
    public override Brush Transform(Matrix4x4 matrix, Rectangle sourceInterest, Rectangle preparedInterest)
    {
        // The texture pixels are not transformed by the matrix; the brush anchors to the
        // interest region origin, so when preparation moves that origin the offset must
        // shift by the same delta to stop the texture visually sliding.
        int offsetX = sourceInterest.X - preparedInterest.X;
        int offsetY = sourceInterest.Y - preparedInterest.Y;
        if (offsetX == 0 && offsetY == 0)
        {
            return this;
        }

        return new ImageBrush<TPixel>(
            this.SourceImage,
            this.SourceRegion,
            new Point(this.Offset.X + offsetX, this.Offset.Y + offsetY),
            this.WrapX,
            this.WrapY);
    }
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
    /// An offset to apply to the image while drawing the texture.
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
    /// An offset to apply to the image while drawing the texture.
    /// </param>
    protected ImageBrush(Image image, RectangleF region, Point offset)
        : this(image, region, offset, WrapMode.Repeat, WrapMode.Repeat)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush"/> class.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="wrapX">The wrap mode used when sampling horizontally beyond the source region.</param>
    /// <param name="wrapY">The wrap mode used when sampling vertically beyond the source region.</param>
    protected ImageBrush(Image image, WrapMode wrapX, WrapMode wrapY)
        : this(image, image.Bounds, Point.Empty, wrapX, wrapY)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageBrush"/> class.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="offset">An offset to apply to the image while drawing the texture.</param>
    /// <param name="wrapX">The wrap mode used when sampling horizontally beyond the source region.</param>
    /// <param name="wrapY">The wrap mode used when sampling vertically beyond the source region.</param>
    protected ImageBrush(Image image, Point offset, WrapMode wrapX, WrapMode wrapY)
        : this(image, image.Bounds, offset, wrapX, wrapY)
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
    /// <param name="wrapX">The wrap mode used when sampling horizontally beyond the source region.</param>
    /// <param name="wrapY">The wrap mode used when sampling vertically beyond the source region.</param>
    protected ImageBrush(Image image, RectangleF region, WrapMode wrapX, WrapMode wrapY)
        : this(image, region, Point.Empty, wrapX, wrapY)
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
    /// An offset to apply to the image while drawing the texture.
    /// </param>
    /// <param name="wrapX">The wrap mode used when sampling horizontally beyond the source region.</param>
    /// <param name="wrapY">The wrap mode used when sampling vertically beyond the source region.</param>
    protected ImageBrush(Image image, RectangleF region, Point offset, WrapMode wrapX, WrapMode wrapY)
    {
        this.UntypedImage = image;
        this.SourceRegion = RectangleF.Intersect(image.Bounds, region);
        this.Offset = offset;
        this.WrapX = wrapX;
        this.WrapY = wrapY;
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

    /// <summary>
    /// Gets the wrap mode used when sampling horizontally beyond the <see cref="SourceRegion"/>.
    /// </summary>
    public WrapMode WrapX { get; }

    /// <summary>
    /// Gets the wrap mode used when sampling vertically beyond the <see cref="SourceRegion"/>.
    /// </summary>
    public WrapMode WrapY { get; }

    /// <inheritdoc />
    public override bool Equals(Brush? other)
    {
        if (other is ImageBrush ib)
        {
            return ib.UntypedImage == this.UntypedImage
                && ib.SourceRegion == this.SourceRegion
                && ib.WrapX == this.WrapX
                && ib.WrapY == this.WrapY;
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.UntypedImage, this.SourceRegion, this.WrapX, this.WrapY);

    /// <inheritdoc />
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
    {
        if (this.UntypedImage is Image<TPixel> image)
        {
            return new ImageBrushRenderer<TPixel>(configuration, options, canvasWidth, image, region, this.SourceRegion, this.Offset, this.WrapX, this.WrapY);
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
    private sealed class ImageBrushRenderer<TPixel> : BrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly ImageFrame<TPixel> sourceFrame;

        /// <summary>
        /// The region of the source image we will be using to draw from.
        /// </summary>
        private readonly Rectangle sourceRegion;

        /// <summary>
        /// The wrap mode applied horizontally when sampling beyond the source region.
        /// </summary>
        private readonly WrapMode wrapX;

        /// <summary>
        /// The wrap mode applied vertically when sampling beyond the source region.
        /// </summary>
        private readonly WrapMode wrapY;

        /// <summary>
        /// The Y offset.
        /// </summary>
        private readonly int offsetY;

        /// <summary>
        /// The X offset.
        /// </summary>
        private readonly int offsetX;

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
        /// <param name="wrapX">The horizontal wrap mode.</param>
        /// <param name="wrapY">The vertical wrap mode.</param>
        public ImageBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            Image<TPixel> image,
            RectangleF targetRegion,
            RectangleF sourceRegion,
            Point offset,
            WrapMode wrapX,
            WrapMode wrapY)
            : base(configuration, options, canvasWidth)
        {
            this.sourceFrame = image.Frames.RootFrame;
            this.sourceRegion = Rectangle.Intersect(image.Bounds, (Rectangle)sourceRegion);
            this.wrapX = wrapX;
            this.wrapY = wrapY;

            this.offsetY = (int)MathF.Floor(targetRegion.Top) + offset.Y;
            this.offsetX = (int)MathF.Floor(targetRegion.Left) + offset.X;
        }

        /// <summary>
        /// Gets the texture pixel for the given device coordinate after applying the
        /// brush origin offset and the per-axis wrap modes.
        /// </summary>
        /// <param name="x">The x-coordinate of the pixel in device space.</param>
        /// <param name="y">The y-coordinate of the pixel in device space.</param>
        /// <returns>The sampled pixel, or transparent when outside an unwrapped region.</returns>
        internal TPixel this[int x, int y]
        {
            get
            {
                if (TryWrap(x - this.offsetX, this.sourceRegion.Width, this.sourceRegion.X, this.wrapX, out int srcX)
                    && TryWrap(y - this.offsetY, this.sourceRegion.Height, this.sourceRegion.Y, this.wrapY, out int srcY))
                {
                    return this.sourceFrame[srcX, srcY];
                }

                // None wrap mode outside the source region samples as transparent.
                return default;
            }
        }

        /// <inheritdoc />
        public override void Apply(
            Span<TPixel> destinationRow,
            ReadOnlySpan<float> scanline,
            int x,
            int y,
            BrushWorkspace<TPixel> workspace)
        {
            Span<float> coverageSpan = workspace.GetAmounts(scanline.Length);
            Span<TPixel> overlaySpan = workspace.GetOverlays(scanline.Length);

            int baseX = x - this.offsetX;
            bool rowInRange = TryWrap(y - this.offsetY, this.sourceRegion.Height, this.sourceRegion.Y, this.wrapY, out int sourceY);
            Span<TPixel> sourceRow = rowInRange
                ? this.sourceFrame.PixelBuffer.DangerousGetRowSpan(sourceY)
                : default;

            for (int i = 0; i < scanline.Length; i++)
            {
                if (rowInRange && TryWrap(baseX + i, this.sourceRegion.Width, this.sourceRegion.X, this.wrapX, out int sourceX))
                {
                    coverageSpan[i] = scanline[i];
                    overlaySpan[i] = sourceRow[sourceX];
                }
                else
                {
                    // None wrap mode outside the source region: contribute nothing.
                    coverageSpan[i] = 0;
                    overlaySpan[i] = default;
                }
            }

            this.Blender.BlendWithCoverage<TPixel>(
                this.Configuration,
                destinationRow,
                destinationRow,
                overlaySpan,
                this.Options.BlendPercentage,
                coverageSpan,
                workspace.GetBlendScratch(scanline.Length, 3));
        }

        /// <summary>
        /// Maps a target coordinate (relative to the brush origin) to a source coordinate within the
        /// region, applying the per-axis wrap mode. Returns <see langword="false"/> for
        /// <see cref="WrapMode.None"/> when the coordinate falls outside the region.
        /// </summary>
        /// <param name="coordinate">The brush-origin-relative coordinate to map.</param>
        /// <param name="size">The size of the source region along this axis.</param>
        /// <param name="regionStart">The start of the source region along this axis, in source image space.</param>
        /// <param name="mode">The wrap mode to apply along this axis.</param>
        /// <param name="source">Receives the mapped coordinate in source image space.</param>
        /// <returns><see langword="true"/> if the coordinate maps to a source pixel; otherwise <see langword="false"/>.</returns>
        private static bool TryWrap(int coordinate, int size, int regionStart, WrapMode mode, out int source)
        {
            if (size <= 0)
            {
                source = regionStart;
                return false;
            }

            switch (mode)
            {
                case WrapMode.None:
                    if (coordinate < 0 || coordinate >= size)
                    {
                        source = regionStart;
                        return false;
                    }

                    source = coordinate + regionStart;
                    return true;

                case WrapMode.Mirror:
                    // Wrap into a double period using a true (non-negative) modulo, then
                    // reflect the second half so adjacent tiles are mirror images.
                    int period = size * 2;
                    int mirrored = ((coordinate % period) + period) % period;
                    if (mirrored >= size)
                    {
                        mirrored = period - 1 - mirrored;
                    }

                    source = mirrored + regionStart;
                    return true;

                case WrapMode.Clamp:
                    source = Math.Clamp(coordinate, 0, size - 1) + regionStart;
                    return true;

                default: // Repeat
                    // True modulo so negative coordinates tile correctly.
                    source = (((coordinate % size) + size) % size) + regionStart;
                    return true;
            }
        }
    }
}
