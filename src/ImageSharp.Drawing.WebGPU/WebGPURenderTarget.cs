// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;
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
    private readonly bool ownsDeviceContext;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURenderTarget"/> class using the shared process-level device and default RGBA8 format.
    /// </summary>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    public WebGPURenderTarget(int width, int height)
        : this(Configuration.Default, WebGPUTextureFormat.Rgba8Unorm, width, height)
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
        : this(Configuration.Default, format, width, height)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURenderTarget"/> class using the shared process-level device and default RGBA8 format.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    public WebGPURenderTarget(Configuration configuration, int width, int height)
        : this(configuration, WebGPUTextureFormat.Rgba8Unorm, width, height)
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
        : this(new WebGPUDeviceContext(configuration), true, format, width, height)
    {
    }

    private WebGPURenderTarget(
        WebGPUDeviceContext deviceContext,
        bool ownsDeviceContext,
        WebGPUTextureFormat format,
        int width,
        int height)
    {
        this.deviceContext = deviceContext;
        this.ownsDeviceContext = ownsDeviceContext;

        try
        {
            deviceContext.ThrowIfDisposed();

            WebGPU api = WebGPURuntime.GetApi();
            NativeSurface surface = WebGPURenderTargetAllocation.CreateRenderTarget(
                api,
                deviceContext.DeviceHandle,
                deviceContext.QueueHandle,
                format,
                width,
                height,
                out WebGPUTextureHandle textureHandle,
                out WebGPUTextureViewHandle textureViewHandle);

            this.TextureHandle = textureHandle;
            this.TextureViewHandle = textureViewHandle;
            this.Surface = surface;
            this.Format = format;
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
    internal NativeSurface Surface { get; }

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
    /// Gets the owned wrapped texture handle behind this render target.
    /// </summary>
    internal WebGPUTextureHandle TextureHandle { get; }

    /// <summary>
    /// Gets the owned wrapped texture-view handle bound when this render target is used as a native surface.
    /// </summary>
    internal WebGPUTextureViewHandle TextureViewHandle { get; }

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
            this.Format);
    }

    /// <summary>
    /// Reads the current GPU texture contents back into a new CPU image.
    /// </summary>
    /// <returns>The readback image.</returns>
    public Image ReadbackImage()
#pragma warning disable CS8524
        => this.Format switch
        {
            WebGPUTextureFormat.Rgba8Unorm => this.ReadbackImage<Rgba32>(),
            WebGPUTextureFormat.Bgra8Unorm => this.ReadbackImage<Bgra32>(),
            WebGPUTextureFormat.Rgba8Snorm => this.ReadbackImage<NormalizedByte4>(),
            WebGPUTextureFormat.Rgba16Float => this.ReadbackImage<HalfVector4>()
        };
#pragma warning restore CS8524

    /// <summary>
    /// Reads the current GPU texture contents back into a new CPU image.
    /// </summary>
    /// <typeparam name="TPixel">The destination image pixel format.</typeparam>
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
    /// <typeparam name="TPixel">The destination image pixel format.</typeparam>
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
    /// Releases the owned texture view and texture.
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
    /// Allocates an owned render target for the specified context, format, and size.
    /// </summary>
    internal static WebGPURenderTarget CreateFromContext(
        WebGPUDeviceContext deviceContext,
        WebGPUTextureFormat format,
        int width,
        int height)
        => new(deviceContext, false, format, width, height);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);
}
