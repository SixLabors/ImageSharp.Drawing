// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// An offscreen WebGPU render target.
/// Use this type when you want to render to a GPU-backed target and optionally read the result back with
/// <see cref="Readback"/> or <see cref="ReadbackInto(Image{TPixel})"/>.
/// </summary>
/// <typeparam name="TPixel">The canvas pixel format.</typeparam>
public sealed class WebGPURenderTarget<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly bool ownsGraphics;
    private bool isDisposed;

    private WebGPURenderTarget(
        WebGPUDeviceContext<TPixel> graphics,
        bool ownsGraphics,
        nint textureHandle,
        nint textureViewHandle,
        NativeSurface surface,
        WebGPUTextureFormatId format,
        Rectangle bounds)
    {
        this.Graphics = graphics;
        this.ownsGraphics = ownsGraphics;
        this.TextureHandle = textureHandle;
        this.TextureViewHandle = textureViewHandle;
        this.Surface = surface;
        this.Format = format;
        this.Bounds = bounds;
        this.NativeFrame = new NativeCanvasFrame<TPixel>(bounds, surface);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURenderTarget{TPixel}"/> class using the shared process-level device.
    /// </summary>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    public WebGPURenderTarget(int width, int height)
        : this(Configuration.Default, width, height)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURenderTarget{TPixel}"/> class using the shared process-level device.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    public WebGPURenderTarget(Configuration configuration, int width, int height)
        : this(AllocateOwnedTarget(configuration, width, height))
    {
    }

    private WebGPURenderTarget(OwnedTarget ownedTarget)
        : this(
            ownedTarget.Graphics,
            ownsGraphics: true,
            ownedTarget.TextureHandle,
            ownedTarget.TextureViewHandle,
            ownedTarget.Surface,
            ownedTarget.Format,
            ownedTarget.Bounds)
    {
    }

    /// <summary>
    /// Gets the owned opaque <c>WGPUTexture*</c> handle.
    /// </summary>
    public nint TextureHandle { get; private set; }

    /// <summary>
    /// Gets the owned opaque <c>WGPUTextureView*</c> handle.
    /// </summary>
    public nint TextureViewHandle { get; private set; }

    /// <summary>
    /// Gets the graphics device context used by this target.
    /// </summary>
    public WebGPUDeviceContext<TPixel> Graphics { get; }

    /// <summary>
    /// Gets the wrapped native surface.
    /// </summary>
    public NativeSurface Surface { get; }

    /// <summary>
    /// Gets the native-only frame over this render target.
    /// </summary>
    public NativeCanvasFrame<TPixel> NativeFrame { get; }

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
    /// Gets the allocated texture format identifier.
    /// </summary>
    public WebGPUTextureFormatId Format { get; }

    private static OwnedTarget AllocateOwnedTarget(Configuration configuration, int width, int height)
    {
        WebGPUDeviceContext<TPixel> graphics = new(configuration);
        try
        {
            WebGPU api = WebGPURuntime.GetApi();
            if (!WebGPURenderTargetAllocation.TryCreateRenderTarget<TPixel>(
                    api,
                    graphics.DeviceHandle,
                    graphics.QueueHandle,
                    width,
                    height,
                    out NativeSurface surface,
                    out nint textureHandle,
                    out nint textureViewHandle,
                    out WebGPUTextureFormatId format,
                    out string allocationError))
            {
                graphics.Dispose();
                throw new InvalidOperationException(allocationError);
            }

            return new OwnedTarget(
                graphics,
                textureHandle,
                textureViewHandle,
                surface,
                format,
                new Rectangle(0, 0, width, height));
        }
        catch
        {
            graphics.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a native-only frame over this render target.
    /// </summary>
    /// <returns>The native-only frame.</returns>
    public NativeCanvasFrame<TPixel> CreateFrame()
    {
        this.ThrowIfDisposed();
        this.Graphics.ThrowIfDisposed();
        return this.NativeFrame;
    }

    /// <summary>
    /// Creates a drawing canvas over this native-only render target.
    /// </summary>
    /// <returns>A drawing canvas targeting this render target.</returns>
    public DrawingCanvas<TPixel> CreateCanvas()
        => this.CreateCanvas(new DrawingOptions());

    /// <summary>
    /// Creates a drawing canvas over this native-only render target.
    /// </summary>
    /// <param name="options">The initial drawing options.</param>
    /// <returns>A drawing canvas targeting this render target.</returns>
    public DrawingCanvas<TPixel> CreateCanvas(DrawingOptions options)
    {
        this.ThrowIfDisposed();
        this.Graphics.ThrowIfDisposed();
        return new DrawingCanvas<TPixel>(this.Graphics.Configuration, this.Graphics.Backend, this.NativeFrame, options);
    }

    /// <summary>
    /// Reads the current GPU texture contents back into a new CPU image.
    /// </summary>
    /// <returns>The readback image.</returns>
    public Image<TPixel> Readback()
    {
        this.ThrowIfDisposed();
        this.Graphics.ThrowIfDisposed();

        Image<TPixel> image = new(this.Width, this.Height);
        try
        {
            this.ReadbackInto(image);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the current GPU texture contents back into an existing CPU image.
    /// </summary>
    /// <param name="destination">The destination image that receives the readback pixels.</param>
    public void ReadbackInto(Image<TPixel> destination)
    {
        Guard.NotNull(destination, nameof(destination));
        this.ThrowIfDisposed();
        this.Graphics.ThrowIfDisposed();

        if (destination.Width != this.Width || destination.Height != this.Height)
        {
            throw new ArgumentException(
                $"Destination image dimensions ({destination.Width}x{destination.Height}) must match render target dimensions ({this.Width}x{this.Height}).",
                nameof(destination));
        }

        Buffer2DRegion<TPixel> region = new(destination.Frames.RootFrame.PixelBuffer, destination.Bounds);
        if (!this.Graphics.Backend.TryReadRegion(
                this.Graphics.Configuration,
                this.NativeFrame,
                new Rectangle(0, 0, this.Width, this.Height),
                region,
                out string? error))
        {
            if (error is null)
            {
                throw new InvalidOperationException("The WebGPU render target readback failed without reporting a reason.");
            }

            throw new InvalidOperationException(error);
        }
    }

    /// <summary>
    /// Releases the owned texture and texture view.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        WebGPUTextureTransfer.Release(this.TextureHandle, this.TextureViewHandle);
        this.TextureHandle = 0;
        this.TextureViewHandle = 0;
        if (this.ownsGraphics)
        {
            this.Graphics.Dispose();
        }

        this.isDisposed = true;
    }

    /// <summary>
    /// Allocates an owned render target for the specified generic context and size.
    /// </summary>
    /// <param name="graphics">The creating graphics context.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    /// <returns>The created render target.</returns>
    internal static WebGPURenderTarget<TPixel> CreateFromContext(WebGPUDeviceContext<TPixel> graphics, int width, int height)
        => CreateCore(graphics, ownsGraphics: false, width, height);

    private static WebGPURenderTarget<TPixel> CreateCore(
        WebGPUDeviceContext<TPixel> graphics,
        bool ownsGraphics,
        int width,
        int height)
    {
        graphics.ThrowIfDisposed();

        WebGPU api = WebGPURuntime.GetApi();
        if (!WebGPURenderTargetAllocation.TryCreateRenderTarget<TPixel>(
                api,
                graphics.DeviceHandle,
                graphics.QueueHandle,
                width,
                height,
                out NativeSurface surface,
                out nint textureHandle,
                out nint textureViewHandle,
                out WebGPUTextureFormatId format,
                out string error))
        {
            throw new InvalidOperationException(error);
        }

        return new WebGPURenderTarget<TPixel>(
            graphics,
            ownsGraphics,
            textureHandle,
            textureViewHandle,
            surface,
            format,
            new Rectangle(0, 0, width, height));
    }

    /// <summary>
    /// Throws when this render target is disposed.
    /// </summary>
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

    private sealed class OwnedTarget
    {
        public OwnedTarget(
            WebGPUDeviceContext<TPixel> graphics,
            nint textureHandle,
            nint textureViewHandle,
            NativeSurface surface,
            WebGPUTextureFormatId format,
            Rectangle bounds)
        {
            this.Graphics = graphics;
            this.TextureHandle = textureHandle;
            this.TextureViewHandle = textureViewHandle;
            this.Surface = surface;
            this.Format = format;
            this.Bounds = bounds;
        }

        public WebGPUDeviceContext<TPixel> Graphics { get; }

        public nint TextureHandle { get; }

        public nint TextureViewHandle { get; }

        public NativeSurface Surface { get; }

        public WebGPUTextureFormatId Format { get; }

        public Rectangle Bounds { get; }
    }
}
