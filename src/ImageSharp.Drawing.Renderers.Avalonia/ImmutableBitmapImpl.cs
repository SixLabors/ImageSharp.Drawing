// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// ImageSharp-backed immutable bitmap implementation for the sample Avalonia renderer.
/// </summary>
internal sealed unsafe class ImmutableBitmapImpl : IDrawableBitmapImpl, IReadableBitmapImpl
{
    private readonly object sync = new();
    private readonly PixelFormat format;
    private readonly AlphaFormat alphaFormat;

    /// <summary>
    /// Initializes a new immutable bitmap from an encoded image stream.
    /// </summary>
    /// <param name="stream">The encoded image stream.</param>
    public ImmutableBitmapImpl(Stream stream)
        : this(SixLabors.ImageSharp.Image.Load<Bgra32P>(WriteableBitmapImpl.ImageDecoderOptions, stream))
    {
    }

    /// <summary>
    /// Initializes a new immutable bitmap by resizing an existing immutable bitmap.
    /// </summary>
    /// <param name="source">The source bitmap.</param>
    /// <param name="destinationSize">The destination size in pixels.</param>
    /// <param name="interpolationMode">The interpolation mode used when scaling.</param>
    public ImmutableBitmapImpl(ImmutableBitmapImpl source, PixelSize destinationSize, BitmapInterpolationMode interpolationMode)
    {
        Image<Bgra32P> image = new(WriteableBitmapImpl.ImageConfiguration, destinationSize.Width, destinationSize.Height);
        this.Image = image;
        this.Dpi = source.Dpi;
        this.PixelSize = destinationSize;
        this.format = AvaloniaPixelFormats.Bgra8888;
        this.alphaFormat = global::Avalonia.Platform.AlphaFormat.Premul;

        bool isUpscaling = destinationSize.Width > source.PixelSize.Width || destinationSize.Height > source.PixelSize.Height;
        IResampler sampler = interpolationMode.ToResampler(isUpscaling);
        Rectangle sourceRect = new(0, 0, source.PixelSize.Width, source.PixelSize.Height);
        RectangleF destinationRect = new(0, 0, destinationSize.Width, destinationSize.Height);

        using DrawingContextImpl context = new(image, this.Dpi);
        source.Draw(context, sourceRect, destinationRect, sampler, 1, PixelAlphaCompositionMode.Src);
    }

    /// <summary>
    /// Initializes a new immutable bitmap decoded to fit the specified width or height.
    /// </summary>
    /// <param name="stream">The encoded image stream.</param>
    /// <param name="decodeSize">The target width or height.</param>
    /// <param name="horizontal">Whether <paramref name="decodeSize"/> is a width.</param>
    /// <param name="interpolationMode">The interpolation mode used if the decoder resizes.</param>
    public ImmutableBitmapImpl(Stream stream, int decodeSize, bool horizontal, BitmapInterpolationMode interpolationMode)
        : this(
            SixLabors.ImageSharp.Image.Load<Bgra32P>(
                new()
                {
                    Configuration = WriteableBitmapImpl.ImageConfiguration,
                    TargetSize = horizontal
                        ? new SixLabors.ImageSharp.Size(decodeSize, 0)
                        : new SixLabors.ImageSharp.Size(0, decodeSize),
                    Sampler = interpolationMode.ToResampler(isUpscaling: false)
                },
                stream))
    {
    }

    /// <summary>
    /// Initializes a new immutable bitmap from a copy of external pixel data.
    /// </summary>
    /// <param name="size">The bitmap size in pixels.</param>
    /// <param name="dpi">The bitmap DPI.</param>
    /// <param name="stride">The byte stride between rows.</param>
    /// <param name="format">The source pixel format.</param>
    /// <param name="alphaFormat">The source alpha format.</param>
    /// <param name="data">The source pixel data.</param>
    public ImmutableBitmapImpl(PixelSize size, Vector dpi, int stride, PixelFormat format, AlphaFormat alphaFormat, IntPtr data)
        : this(CloneExternalPixels(size, stride, format, alphaFormat, data), dpi, format, alphaFormat)
    {
    }

    /// <summary>
    /// Initializes a new immutable bitmap wrapping an existing ImageSharp image.
    /// </summary>
    /// <param name="image">The ImageSharp image backing the bitmap.</param>
    public ImmutableBitmapImpl(Image<Bgra32P> image)
        : this(image, image.Metadata.ToDpi())
    {
    }

    /// <summary>
    /// Initializes a new immutable bitmap wrapping an existing ImageSharp image.
    /// </summary>
    /// <param name="image">The ImageSharp image backing the bitmap.</param>
    /// <param name="dpi">The bitmap DPI.</param>
    public ImmutableBitmapImpl(Image<Bgra32P> image, Vector dpi)
        : this(image, dpi, AvaloniaPixelFormats.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul)
    {
    }

    /// <summary>
    /// Initializes a new immutable bitmap with its stored pixel and alpha formats.
    /// </summary>
    /// <param name="image">The ImageSharp image backing the bitmap.</param>
    /// <param name="dpi">The bitmap DPI.</param>
    /// <param name="format">The stored pixel format.</param>
    /// <param name="alphaFormat">The stored alpha format.</param>
    private ImmutableBitmapImpl(Image image, Vector dpi, PixelFormat format, AlphaFormat alphaFormat)
    {
        this.Image = image;
        this.Dpi = dpi;
        this.PixelSize = new PixelSize(image.Width, image.Height);
        this.format = format;
        this.alphaFormat = alphaFormat;
    }

    /// <summary>
    /// Gets the ImageSharp pixels drawn for this bitmap.
    /// </summary>
    public Image Image { get; }

    /// <inheritdoc />
    public Vector Dpi { get; }

    /// <inheritdoc />
    public PixelSize PixelSize { get; }

    /// <inheritdoc />
    public int Version { get; } = 1;

    /// <inheritdoc />
    public PixelFormat? Format => this.format;

    /// <inheritdoc />
    public AlphaFormat? AlphaFormat => this.alphaFormat;

    /// <inheritdoc />
    public void Draw(
        DrawingContextImpl context,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler sampler,
        float opacity,
        PixelAlphaCompositionMode alphaCompositionMode)
        => context.DrawImage(this.Image, sourceRect, destinationRect, sampler, opacity, alphaCompositionMode);

    /// <inheritdoc />
    public ILockedFramebuffer Lock() => new BitmapFramebuffer(this);

    /// <inheritdoc />
    public void Save(Stream stream, BitmapEncoderOptions options) => this.Image.Save(stream, options);

    /// <inheritdoc />
    public void Dispose() => this.Image.Dispose();

    /// <summary>
    /// Copies external pixel memory into an ImageSharp image.
    /// </summary>
    private static Image CloneExternalPixels(PixelSize size, int stride, PixelFormat format, AlphaFormat alphaFormat, IntPtr data)
    {
        if (format == AvaloniaPixelFormats.Bgra8888)
        {
            return alphaFormat == global::Avalonia.Platform.AlphaFormat.Premul
                ? CopyExternalPixels<Bgra32P>(size, stride, data)
                : CopyExternalPixels<Bgra32>(size, stride, data);
        }

        if (format == AvaloniaPixelFormats.Rgba8888)
        {
            return alphaFormat == global::Avalonia.Platform.AlphaFormat.Premul
                ? CopyExternalPixels<Rgba32P>(size, stride, data)
                : CopyExternalPixels<Rgba32>(size, stride, data);
        }

        // Avalonia's Rgb565 and ImageSharp's Bgr565 share the same packed 5-6-5 byte layout.
        return CopyExternalPixels<Bgr565>(size, stride, data);
    }

    /// <summary>
    /// Copies external rows into owned ImageSharp storage without changing their pixel representation.
    /// </summary>
    /// <typeparam name="TPixel">The ImageSharp pixel type matching the supplied Avalonia format.</typeparam>
    /// <param name="size">The bitmap size in pixels.</param>
    /// <param name="stride">The signed byte stride between source rows.</param>
    /// <param name="data">The address of the first logical source row.</param>
    /// <returns>The owned image containing the copied bytes.</returns>
    private static Image<TPixel> CopyExternalPixels<TPixel>(PixelSize size, int stride, IntPtr data)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Image<TPixel> image = new(WriteableBitmapImpl.ImageConfiguration, size.Width, size.Height);

        for (int y = 0; y < size.Height; y++)
        {
            Span<byte> destinationRow = MemoryMarshal.AsBytes(image.DangerousGetPixelRowMemory(y).Span);

            // Avalonia addresses the first logical row even for bottom-up buffers. Advancing by the
            // signed stride therefore preserves logical row order without reinterpreting any pixels.
            ReadOnlySpan<byte> sourceRow = new((byte*)data + (y * stride), destinationRow.Length);
            sourceRow.CopyTo(destinationRow);
        }

        return image;
    }

    /// <summary>
    /// Framebuffer view over the bitmap's own contiguous ImageSharp pixel buffer.
    /// </summary>
    private sealed class BitmapFramebuffer : ILockedFramebuffer, IImageVisitor
    {
        private readonly ImmutableBitmapImpl parent;
        private MemoryHandle handle;

        /// <summary>
        /// Initializes a new framebuffer lock.
        /// </summary>
        /// <param name="parent">The bitmap owning the pixel buffer.</param>
        public BitmapFramebuffer(ImmutableBitmapImpl parent)
        {
            this.parent = parent;
            Monitor.Enter(parent.sync);
            parent.Image.AcceptVisitor(this);
        }

        /// <inheritdoc />
        public void Visit<TPixel>(Image<TPixel> image)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            if (!image.DangerousTryGetSinglePixelMemory(out Memory<TPixel> memory))
            {
                Monitor.Exit(this.parent.sync);
                throw new InvalidOperationException("ImageSharp bitmap buffers must be contiguous to lock them as Avalonia framebuffers.");
            }

            this.handle = memory.Pin();
        }

        /// <inheritdoc />
        public IntPtr Address => (IntPtr)this.handle.Pointer;

        /// <inheritdoc />
        public PixelSize Size => this.parent.PixelSize;

        /// <inheritdoc />
        public int RowBytes => ((this.parent.format.BitsPerPixel * this.parent.PixelSize.Width) + 7) / 8;

        /// <inheritdoc />
        public Vector Dpi => this.parent.Dpi;

        /// <inheritdoc />
        public PixelFormat Format => this.parent.format;

        /// <inheritdoc />
        public AlphaFormat AlphaFormat => this.parent.alphaFormat;

        /// <inheritdoc />
        public void Dispose()
        {
            this.handle.Dispose();
            Monitor.Exit(this.parent.sync);
        }
    }
}
