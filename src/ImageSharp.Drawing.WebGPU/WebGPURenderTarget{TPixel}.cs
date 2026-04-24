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
/// <remarks>
/// The constructors on this type allocate a target on the shared process WebGPU device. To allocate offscreen
/// targets against an externally-owned device (for example, a host UI framework's WebGPU device), call
/// <see cref="WebGPUDeviceContext{TPixel}.CreateRenderTarget(int, int)"/> instead.
/// </remarks>
public sealed class WebGPURenderTarget<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly bool ownsGraphics;
    private bool isDisposed;

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
        : this(new WebGPUDeviceContext<TPixel>(configuration), ownsGraphics: true, width, height)
    {
    }

    private WebGPURenderTarget(WebGPUDeviceContext<TPixel> graphics, bool ownsGraphics, int width, int height)
    {
        this.Graphics = graphics;
        this.ownsGraphics = ownsGraphics;

        try
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
                    out WebGPUTextureHandle? textureHandle,
                    out WebGPUTextureViewHandle? textureViewHandle,
                    out WebGPUTextureFormatId format,
                    out string allocationError))
            {
                throw new InvalidOperationException(allocationError);
            }

            if (textureHandle is null || textureViewHandle is null)
            {
                throw new InvalidOperationException("WebGPU render-target allocation succeeded without returning both owned texture handles.");
            }

            this.TextureHandle = textureHandle;
            this.TextureViewHandle = textureViewHandle;
            this.Surface = surface;
            this.Format = format;
            this.Bounds = new Rectangle(0, 0, width, height);
            this.NativeFrame = new NativeCanvasFrame<TPixel>(this.Bounds, surface);
        }
        catch
        {
            if (ownsGraphics)
            {
                graphics.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Gets the graphics device context used by this target.
    /// </summary>
    public WebGPUDeviceContext<TPixel> Graphics { get; }

    /// <summary>
    /// Gets the wrapped native surface backing this render target.
    /// Exposed for advanced interop with <see cref="WebGPUDrawingBackend"/> APIs that consume a native surface;
    /// most callers do not need to touch this directly.
    /// </summary>
    public NativeSurface Surface { get; }

    /// <summary>
    /// Gets the native-only frame over this render target.
    /// Pass this to <see cref="WebGPUDrawingBackend.TryReadRegion{TPixel}(Configuration, ICanvasFrame{TPixel}, Rectangle, Buffer2DRegion{TPixel})"/>
    /// when you need to read back into a caller-owned region instead of using <see cref="Readback"/>.
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

    /// <summary>
    /// Gets the owned wrapped texture handle behind this render target.
    /// </summary>
    internal WebGPUTextureHandle TextureHandle { get; }

    /// <summary>
    /// Gets the owned wrapped texture-view handle bound when this render target is used as a native surface.
    /// </summary>
    internal WebGPUTextureViewHandle TextureViewHandle { get; }

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

        if (!this.Graphics.Backend.TryReadRegion(
                this.Graphics.Configuration,
                this.NativeFrame,
                new Rectangle(0, 0, this.Width, this.Height),
                new Buffer2DRegion<TPixel>(destination.Frames.RootFrame.PixelBuffer),
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
        => new(graphics, ownsGraphics: false, width, height);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);
}
