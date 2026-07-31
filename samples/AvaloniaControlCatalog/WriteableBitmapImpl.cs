// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace AvaloniaControlCatalog;

/// <summary>
/// ImageSharp-backed writeable bitmap implementation for the sample Avalonia renderer.
/// </summary>
internal class WriteableBitmapImpl : IWriteableBitmapImpl, IDrawableBitmapImpl
{
    internal static readonly Configuration ImageConfiguration = CreateImageConfiguration();
    internal static readonly DecoderOptions ImageDecoderOptions = new()
    {
        Configuration = ImageConfiguration
    };

    private readonly object sync = new();
    private int version = 1;

    /// <summary>
    /// Initializes a new empty bitmap.
    /// </summary>
    /// <param name="size">The bitmap size in pixels.</param>
    /// <param name="dpi">The bitmap DPI.</param>
    public WriteableBitmapImpl(PixelSize size, Vector dpi)
        : this(new Image<Bgra32P>(ImageConfiguration, size.Width, size.Height), dpi)
    {
    }

    /// <summary>
    /// Initializes a new empty bitmap.
    /// </summary>
    /// <param name="size">The bitmap size in pixels.</param>
    /// <param name="dpi">The bitmap DPI.</param>
    /// <param name="format">The bitmap pixel format.</param>
    /// <param name="alphaFormat">The bitmap alpha format.</param>
    public WriteableBitmapImpl(PixelSize size, Vector dpi, PixelFormat format, AlphaFormat alphaFormat)
        : this(size, dpi)
    {
    }

    /// <summary>
    /// Initializes a new bitmap from an encoded image stream.
    /// </summary>
    /// <param name="stream">The encoded image stream.</param>
    public WriteableBitmapImpl(Stream stream)
        : this(SixLabors.ImageSharp.Image.Load<Bgra32P>(ImageDecoderOptions, stream))
    {
    }

    /// <summary>
    /// Initializes a new bitmap decoded to fit the specified width or height.
    /// </summary>
    /// <param name="stream">The encoded image stream.</param>
    /// <param name="decodeSize">The target width or height.</param>
    /// <param name="horizontal">Whether <paramref name="decodeSize"/> is a width.</param>
    /// <param name="interpolationMode">The interpolation mode used if the decoder resizes.</param>
    public WriteableBitmapImpl(Stream stream, int decodeSize, bool horizontal, BitmapInterpolationMode interpolationMode)
        : this(
            SixLabors.ImageSharp.Image.Load<Bgra32P>(
                new DecoderOptions
                {
                    Configuration = ImageConfiguration,
                    TargetSize = horizontal
                        ? new SixLabors.ImageSharp.Size(decodeSize, 0)
                        : new SixLabors.ImageSharp.Size(0, decodeSize),
                    Sampler = interpolationMode.ToResampler(isUpscaling: false)
                },
                stream))
    {
    }

    /// <summary>
    /// Initializes a new bitmap wrapping an existing ImageSharp image.
    /// </summary>
    /// <param name="image">The ImageSharp image backing the bitmap.</param>
    /// <param name="dpi">The bitmap DPI.</param>
    public WriteableBitmapImpl(Image<Bgra32P> image, Vector dpi)
    {
        this.Image = image;
        this.Dpi = dpi;
        this.PixelSize = new PixelSize(image.Width, image.Height);
    }

    /// <summary>
    /// Initializes a new bitmap wrapping an existing ImageSharp image and using its metadata DPI.
    /// </summary>
    /// <param name="image">The ImageSharp image backing the bitmap.</param>
    private WriteableBitmapImpl(Image<Bgra32P> image)
        : this(image, image.Metadata.ToDpi())
    {
    }

    /// <summary>
    /// Gets the ImageSharp pixels drawn for this bitmap.
    /// </summary>
    public Image<Bgra32P> Image { get; }

    /// <inheritdoc />
    public Vector Dpi { get; }

    /// <inheritdoc />
    public PixelSize PixelSize { get; }

    /// <inheritdoc />
    public int Version => this.version;

    /// <inheritdoc />
    public PixelFormat? Format => PixelFormats.Bgra8888;

    /// <inheritdoc />
    public AlphaFormat? AlphaFormat => Avalonia.Platform.AlphaFormat.Premul;

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
    public virtual void Dispose() => this.Image.Dispose();

    private static Configuration CreateImageConfiguration()
    {
        Configuration configuration = Configuration.Default.Clone();
        configuration.PreferContiguousImageBuffers = true;

        return configuration;
    }

    /// <summary>
    /// Framebuffer view over the bitmap's own contiguous ImageSharp pixel buffer.
    /// </summary>
    private sealed unsafe class BitmapFramebuffer : ILockedFramebuffer
    {
        private readonly WriteableBitmapImpl parent;
        private MemoryHandle handle;

        /// <summary>
        /// Initializes a new framebuffer lock.
        /// </summary>
        /// <param name="parent">The bitmap owning the pixel buffer.</param>
        public BitmapFramebuffer(WriteableBitmapImpl parent)
        {
            this.parent = parent;
            Monitor.Enter(parent.sync);
            if (!parent.Image.DangerousTryGetSinglePixelMemory(out Memory<Bgra32P> memory))
            {
                Monitor.Exit(parent.sync);
                throw new InvalidOperationException("ImageSharp bitmap buffers must be contiguous to lock them as Avalonia framebuffers.");
            }

            this.handle = memory.Pin();
        }

        /// <inheritdoc />
        public IntPtr Address => (IntPtr)this.handle.Pointer;

        /// <inheritdoc />
        public PixelSize Size => this.parent.PixelSize;

        /// <inheritdoc />
        public int RowBytes => this.parent.PixelSize.Width * 4;

        /// <inheritdoc />
        public Vector Dpi => this.parent.Dpi;

        /// <inheritdoc />
        public PixelFormat Format => PixelFormats.Bgra8888;

        /// <inheritdoc />
        public AlphaFormat AlphaFormat => Avalonia.Platform.AlphaFormat.Premul;

        /// <inheritdoc />
        public void Dispose()
        {
            this.handle.Dispose();
            this.parent.version++;
            Monitor.Exit(this.parent.sync);
        }
    }
}
