// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// An offscreen WebGPU render target.
/// </summary>
/// <remarks>
/// The constructors on this type allocate a target on the shared process WebGPU device.
/// </remarks>
public sealed class WebGPURenderTarget : IDisposable
{
    private readonly WebGPUDeviceContext deviceContext;

    // False when the context is shared, e.g. targets created via CreateRenderTarget or from a
    // surface frame; those must not tear down the context their siblings still use.
    private readonly bool ownsDeviceContext;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURenderTarget"/> class using the shared process-level device and default RGBA8 format.
    /// </summary>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    public WebGPURenderTarget(int width, int height)
        : this(Configuration.Default, WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Unassociated, width, height)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURenderTarget"/> class using the shared process-level device.
    /// </summary>
    /// <param name="format">The target texture format.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    public WebGPURenderTarget(
        WebGPUTextureFormat format,
        int width,
        int height)
        : this(Configuration.Default, format, PixelAlphaRepresentation.Unassociated, width, height)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURenderTarget"/> class using the shared process-level device.
    /// </summary>
    /// <param name="format">The target texture format.</param>
    /// <param name="alphaRepresentation">The alpha representation stored by the target.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    public WebGPURenderTarget(WebGPUTextureFormat format, PixelAlphaRepresentation alphaRepresentation, int width, int height)
        : this(Configuration.Default, format, alphaRepresentation, width, height)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURenderTarget"/> class using the shared process-level device and default RGBA8 format.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    public WebGPURenderTarget(Configuration configuration, int width, int height)
        : this(configuration, WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Unassociated, width, height)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURenderTarget"/> class using the shared process-level device.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    /// <param name="format">The target texture format.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    public WebGPURenderTarget(
        Configuration configuration,
        WebGPUTextureFormat format,
        int width,
        int height)
        : this(configuration, format, PixelAlphaRepresentation.Unassociated, width, height)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURenderTarget"/> class using the shared process-level device.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    /// <param name="format">The target texture format.</param>
    /// <param name="alphaRepresentation">The alpha representation stored by the target.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    public WebGPURenderTarget(Configuration configuration, WebGPUTextureFormat format, PixelAlphaRepresentation alphaRepresentation, int width, int height)
        : this(new WebGPUDeviceContext(configuration), true, WebGPUDrawingBackend.CreateOffscreenTargetDescriptor(format, alphaRepresentation), width, height, isPresentationSurface: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURenderTarget"/> class over an existing device context,
    /// allocating the backing texture and view on that context's device.
    /// </summary>
    /// <param name="deviceContext">The device context that owns the device and queue used by this target.</param>
    /// <param name="ownsDeviceContext">Whether this target disposes <paramref name="deviceContext"/> when it is disposed.</param>
    /// <param name="targetDescriptor">The target texture format and alpha representation.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    /// <param name="isPresentationSurface">Whether this target supplies pixels to a presentation surface.</param>
    internal WebGPURenderTarget(
        WebGPUDeviceContext deviceContext,
        bool ownsDeviceContext,
        WebGPUTargetDescriptor targetDescriptor,
        int width,
        int height,
        bool isPresentationSurface)
    {
        this.deviceContext = deviceContext;
        this.ownsDeviceContext = ownsDeviceContext;

        try
        {
            deviceContext.ThrowIfDisposed();

            WebGPU api = WebGPURuntime.GetApi();
            WebGPUNativeSurface surface = WebGPUNativeSurface.Create(
                api,
                deviceContext.DeviceHandle,
                deviceContext.QueueHandle,
                targetDescriptor,
                width,
                height,
                out WebGPUTextureHandle textureHandle,
                out WebGPUTextureViewHandle textureViewHandle,
                textureCoordinateOffset: default,
                isPresentationSurface);

            this.TextureHandle = textureHandle;
            this.TextureViewHandle = textureViewHandle;
            this.Surface = surface;
            this.Format = targetDescriptor.Format;
            this.AlphaRepresentation = targetDescriptor.AlphaRepresentation;
            this.Bounds = new Rectangle(0, 0, width, height);
        }
        catch
        {
            if (ownsDeviceContext)
            {
                deviceContext.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Gets the WebGPU drawing backend used by this target.
    /// </summary>
    internal WebGPUDrawingBackend Backend => this.deviceContext.Backend;

    /// <summary>
    /// Gets the native surface backing this render target.
    /// Most callers should use <see cref="CreateCanvas()"/> or <see cref="ReadbackImage()"/> instead.
    /// </summary>
    internal WebGPUNativeSurface Surface { get; }

    /// <summary>
    /// Gets the target width in pixels.
    /// </summary>
    public int Width => this.Bounds.Width;

    /// <summary>
    /// Gets the target height in pixels.
    /// </summary>
    public int Height => this.Bounds.Height;

    /// <summary>
    /// Gets the target bounds in pixels.
    /// </summary>
    public Rectangle Bounds { get; }

    /// <summary>
    /// Gets the allocated texture format.
    /// </summary>
    public WebGPUTextureFormat Format { get; }

    /// <summary>
    /// Gets the alpha representation stored by the target.
    /// </summary>
    public PixelAlphaRepresentation AlphaRepresentation { get; }

    /// <summary>
    /// Gets the owned wrapped texture handle behind this render target.
    /// </summary>
    internal WebGPUTextureHandle TextureHandle { get; }

    /// <summary>
    /// Gets the owned wrapped texture-view handle bound when this render target is used as a native surface.
    /// </summary>
    internal WebGPUTextureViewHandle TextureViewHandle { get; }

    /// <summary>
    /// Creates an empty render target with the same texture format as this target.
    /// </summary>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    /// <returns>The created render target.</returns>
    /// <remarks>
    /// The created target does not contain a copy of this target's pixels.
    /// This target must remain undisposed while the created target is in use.
    /// </remarks>
    public WebGPURenderTarget CreateRenderTarget(int width, int height)
    {
        this.ThrowIfDisposed();
        this.deviceContext.ThrowIfDisposed();
        Guard.MustBeGreaterThan(width, 0, nameof(width));
        Guard.MustBeGreaterThan(height, 0, nameof(height));

        return new WebGPURenderTarget(this.deviceContext, false, this.Surface.TargetDescriptor, width, height, isPresentationSurface: false);
    }

    /// <summary>
    /// Creates a drawing canvas over this render target.
    /// </summary>
    /// <returns>A drawing canvas targeting this render target.</returns>
    public DrawingCanvas CreateCanvas()
        => this.CreateCanvas(new DrawingOptions());

    /// <summary>
    /// Creates a drawing canvas over this render target.
    /// </summary>
    /// <param name="options">The initial drawing options.</param>
    /// <returns>A drawing canvas targeting this render target.</returns>
    public DrawingCanvas CreateCanvas(DrawingOptions options)
    {
        this.ThrowIfDisposed();
        this.deviceContext.ThrowIfDisposed();

        return WebGPUCanvasFactory.CreateCanvas(
            this.deviceContext.Configuration,
            options,
            this.deviceContext.Backend,
            this.Bounds,
            this.Surface,
            this.Surface.TargetDescriptor);
    }

    /// <summary>
    /// Creates a drawing canvas over this render target.
    /// </summary>
    /// <param name="options">The initial drawing options.</param>
    /// <param name="textCache">The text drawing cache used by this canvas instance.</param>
    /// <returns>A drawing canvas targeting this render target.</returns>
    public DrawingCanvas CreateCanvas(DrawingOptions options, DrawingTextCache textCache)
    {
        this.ThrowIfDisposed();
        this.deviceContext.ThrowIfDisposed();
        Guard.NotNull(textCache, nameof(textCache));

        return WebGPUCanvasFactory.CreateCanvas(
            this.deviceContext.Configuration,
            options,
            textCache,
            this.deviceContext.Backend,
            this.Bounds,
            this.Surface,
            this.Surface.TargetDescriptor);
    }

    /// <summary>
    /// Reads the current GPU texture contents back into a new CPU image whose pixel type matches <see cref="Format"/> and <see cref="AlphaRepresentation"/>.
    /// </summary>
    /// <returns>
    /// The readback image whose pixel type has the target's channel layout, numeric encoding, and alpha representation.
    /// </returns>
    public Image ReadbackImage()
#pragma warning disable CS8509, CS8524 // Exhaustive in practice: construction normalizes alpha and validates the format.
        => (this.Format, this.AlphaRepresentation) switch
        {
            (WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Unassociated) => this.ReadbackImage<Rgba32>(),
            (WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Associated) => this.ReadbackImage<Rgba32P>(),
            (WebGPUTextureFormat.Bgra8Unorm, PixelAlphaRepresentation.Unassociated) => this.ReadbackImage<Bgra32>(),
            (WebGPUTextureFormat.Bgra8Unorm, PixelAlphaRepresentation.Associated) => this.ReadbackImage<Bgra32P>(),
            (WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Unassociated) => this.ReadbackImage<NormalizedByte4>(),
            (WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Associated) => this.ReadbackImage<NormalizedByte4P>(),
            (WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Unassociated) => this.ReadbackImage<RgbaHalf>(),
            (WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Associated) => this.ReadbackImage<RgbaHalfP>()
        };
#pragma warning restore CS8509, CS8524

    /// <summary>
    /// Reads the current GPU texture contents back into a new CPU image.
    /// </summary>
    /// <typeparam name="TPixel">
    /// The destination image pixel format. Must match <see cref="Format"/> and <see cref="AlphaRepresentation"/>; the backend read throws
    /// <see cref="NotSupportedException"/> on a mismatch. Use <see cref="ReadbackImage()"/> to dispatch by format.
    /// </typeparam>
    /// <returns>The readback image.</returns>
    public Image<TPixel> ReadbackImage<TPixel>()
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        this.deviceContext.ThrowIfDisposed();

        Image<TPixel> image = new(this.Width, this.Height);
        try
        {
            this.ReadbackInto(image.Frames.RootFrame.PixelBuffer.GetRegion());

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the current GPU texture contents back into an existing CPU buffer region.
    /// </summary>
    /// <typeparam name="TPixel">
    /// The destination image pixel format. Must match <see cref="Format"/> and <see cref="AlphaRepresentation"/>; the backend read throws
    /// <see cref="NotSupportedException"/> on a mismatch.
    /// </typeparam>
    /// <param name="destination">The destination buffer region that receives the readback pixels.</param>
    public void ReadbackInto<TPixel>(Buffer2DRegion<TPixel> destination)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        this.deviceContext.ThrowIfDisposed();

        NativeCanvasFrame<TPixel> frame = WebGPUCanvasFactory.CreateFrame<TPixel>(this.Bounds, this.Surface);

        // A smaller destination region intentionally reads the matching top-left
        // portion of the render target instead of forcing an intermediate full-size image.
        int readbackWidth = Math.Min(this.Width, destination.Width);
        int readbackHeight = Math.Min(this.Height, destination.Height);
        Rectangle sourceRectangle = new(0, 0, readbackWidth, readbackHeight);

        // ReadRegion owns the pixel-format check because it is the point where
        // typed CPU pixels are copied from the native WebGPU texture.
        this.deviceContext.Backend.ReadRegion(
            this.deviceContext.Configuration,
            frame,
            sourceRectangle,
            destination);
    }

    /// <summary>
    /// Releases the owned texture view and texture, and the device context when this target created it.
    /// Targets created from a shared context leave that context untouched.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.TextureViewHandle.Dispose();
        this.TextureHandle.Dispose();

        if (this.ownsDeviceContext)
        {
            this.deviceContext.Dispose();
        }

        this.isDisposed = true;
    }

    /// <summary>
    /// Throws when this render target has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);
}
