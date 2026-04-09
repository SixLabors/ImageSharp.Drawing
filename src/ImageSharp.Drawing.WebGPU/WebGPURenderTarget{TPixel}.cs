// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// An offscreen WebGPU render target.
/// Use this type when you want to render to a GPU-backed target and optionally read the result back with
/// <see cref="TryReadback"/> or <see cref="TryReadbackInto(Image{TPixel}, out string?)"/>.
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
            using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
            if (!WebGPURenderTargetAllocation.TryCreateRenderTarget<TPixel>(
                    lease.Api,
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
    /// Creates a hybrid frame over this render target and a caller-provided CPU region.
    /// </summary>
    /// <param name="cpuRegion">The CPU region to expose alongside the native surface.</param>
    /// <returns>A hybrid frame.</returns>
    public HybridCanvasFrame<TPixel> CreateHybridFrame(Buffer2DRegion<TPixel> cpuRegion)
    {
        this.ThrowIfDisposed();
        this.Graphics.ThrowIfDisposed();
        return new HybridCanvasFrame<TPixel>(this.Bounds, cpuRegion, this.Surface);
    }

    /// <summary>
    /// Creates a hybrid frame over this render target and the root frame of a CPU image.
    /// </summary>
    /// <param name="image">The CPU image that backs the frame's CPU region.</param>
    /// <returns>A hybrid frame.</returns>
    public HybridCanvasFrame<TPixel> CreateHybridFrame(Image<TPixel> image)
    {
        Guard.NotNull(image, nameof(image));
        return this.CreateHybridFrame(new Buffer2DRegion<TPixel>(image.Frames.RootFrame.PixelBuffer, image.Bounds));
    }

    /// <summary>
    /// Creates a hybrid frame over this render target and a CPU image frame.
    /// </summary>
    /// <param name="imageFrame">The CPU image frame that backs the frame's CPU region.</param>
    /// <returns>A hybrid frame.</returns>
    public HybridCanvasFrame<TPixel> CreateHybridFrame(ImageFrame<TPixel> imageFrame)
    {
        Guard.NotNull(imageFrame, nameof(imageFrame));
        return this.CreateHybridFrame(new Buffer2DRegion<TPixel>(imageFrame.PixelBuffer, imageFrame.Bounds));
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
    /// Creates a hybrid drawing canvas over this render target and a caller-provided CPU region.
    /// </summary>
    /// <param name="cpuRegion">The CPU region to expose alongside the native surface.</param>
    /// <returns>A drawing canvas targeting this render target and CPU region.</returns>
    public DrawingCanvas<TPixel> CreateHybridCanvas(Buffer2DRegion<TPixel> cpuRegion)
        => this.CreateHybridCanvas(cpuRegion, new DrawingOptions());

    /// <summary>
    /// Creates a hybrid drawing canvas over this render target and a caller-provided CPU region.
    /// </summary>
    /// <param name="cpuRegion">The CPU region to expose alongside the native surface.</param>
    /// <param name="options">The initial drawing options.</param>
    /// <returns>A drawing canvas targeting this render target and CPU region.</returns>
    public DrawingCanvas<TPixel> CreateHybridCanvas(Buffer2DRegion<TPixel> cpuRegion, DrawingOptions options)
    {
        this.ThrowIfDisposed();
        this.Graphics.ThrowIfDisposed();
        return new DrawingCanvas<TPixel>(this.Graphics.Configuration, this.Graphics.Backend, this.CreateHybridFrame(cpuRegion), options);
    }

    /// <summary>
    /// Creates a hybrid drawing canvas over this render target and the root frame of a CPU image.
    /// </summary>
    /// <param name="image">The CPU image that backs the frame's CPU region.</param>
    /// <returns>A drawing canvas targeting this render target and CPU image.</returns>
    public DrawingCanvas<TPixel> CreateHybridCanvas(Image<TPixel> image)
        => this.CreateHybridCanvas(image, new DrawingOptions());

    /// <summary>
    /// Creates a hybrid drawing canvas over this render target and the root frame of a CPU image.
    /// </summary>
    /// <param name="image">The CPU image that backs the frame's CPU region.</param>
    /// <param name="options">The initial drawing options.</param>
    /// <returns>A drawing canvas targeting this render target and CPU image.</returns>
    public DrawingCanvas<TPixel> CreateHybridCanvas(Image<TPixel> image, DrawingOptions options)
    {
        Guard.NotNull(image, nameof(image));
        return this.CreateHybridCanvas(new Buffer2DRegion<TPixel>(image.Frames.RootFrame.PixelBuffer, image.Bounds), options);
    }

    /// <summary>
    /// Creates a hybrid drawing canvas over this render target and a CPU image frame.
    /// </summary>
    /// <param name="imageFrame">The CPU image frame that backs the frame's CPU region.</param>
    /// <returns>A drawing canvas targeting this render target and CPU image frame.</returns>
    public DrawingCanvas<TPixel> CreateHybridCanvas(ImageFrame<TPixel> imageFrame)
        => this.CreateHybridCanvas(imageFrame, new DrawingOptions());

    /// <summary>
    /// Creates a hybrid drawing canvas over this render target and a CPU image frame.
    /// </summary>
    /// <param name="imageFrame">The CPU image frame that backs the frame's CPU region.</param>
    /// <param name="options">The initial drawing options.</param>
    /// <returns>A drawing canvas targeting this render target and CPU image frame.</returns>
    public DrawingCanvas<TPixel> CreateHybridCanvas(ImageFrame<TPixel> imageFrame, DrawingOptions options)
    {
        Guard.NotNull(imageFrame, nameof(imageFrame));
        return this.CreateHybridCanvas(new Buffer2DRegion<TPixel>(imageFrame.PixelBuffer, imageFrame.Bounds), options);
    }

    /// <summary>
    /// Attempts to read the current GPU texture contents back into a new CPU image.
    /// </summary>
    /// <param name="image">Receives the readback image on success.</param>
    /// <param name="error">Receives the failure reason when readback cannot complete.</param>
    /// <returns><see langword="true"/> when readback succeeds; otherwise <see langword="false"/>.</returns>
    public bool TryReadback([NotNullWhen(true)] out Image<TPixel>? image, [NotNullWhen(false)] out string? error)
    {
        if (this.isDisposed)
        {
            image = null;
            error = "Render target is disposed.";
            return false;
        }

        try
        {
            this.Graphics.ThrowIfDisposed();
        }
        catch (ObjectDisposedException ex)
        {
            image = null;
            error = ex.Message;
            return false;
        }

        image = new Image<TPixel>(this.Width, this.Height);
        if (!this.TryReadbackInto(image, out error))
        {
            image.Dispose();
            image = null;
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Attempts to read the current GPU texture contents back into an existing CPU image.
    /// </summary>
    /// <param name="destination">The destination image that receives the readback pixels.</param>
    /// <param name="error">Receives the failure reason when readback cannot complete.</param>
    /// <returns><see langword="true"/> when readback succeeds; otherwise <see langword="false"/>.</returns>
    public bool TryReadbackInto(Image<TPixel> destination, [NotNullWhen(false)] out string? error)
    {
        Guard.NotNull(destination, nameof(destination));

        if (destination.Width != this.Width || destination.Height != this.Height)
        {
            throw new ArgumentException(
                $"Destination image dimensions ({destination.Width}x{destination.Height}) must match render target dimensions ({this.Width}x{this.Height}).",
                nameof(destination));
        }

        if (this.isDisposed)
        {
            error = "Render target is disposed.";
            return false;
        }

        try
        {
            this.Graphics.ThrowIfDisposed();
        }
        catch (ObjectDisposedException ex)
        {
            error = ex.Message;
            return false;
        }

        Buffer2DRegion<TPixel> region = new(destination.Frames.RootFrame.PixelBuffer, destination.Bounds);
        if (!this.Graphics.Backend.TryReadRegion(
                this.Graphics.Configuration,
                this.NativeFrame,
                new Rectangle(0, 0, this.Width, this.Height),
                region,
                out error))
        {
            return false;
        }

        error = null;
        return true;
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

        using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        if (!WebGPURenderTargetAllocation.TryCreateRenderTarget<TPixel>(
                lease.Api,
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
