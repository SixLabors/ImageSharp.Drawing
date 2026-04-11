// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// A WebGPU drawing context for a specific <typeparamref name="TPixel"/> that owns the drawing backend and device handles used to create frames, canvases, and render targets.
/// Use this type when you already own the device and queue, or when you want direct control over how ImageSharp.Drawing wraps external WebGPU textures.
/// </summary>
/// <typeparam name="TPixel">The canvas pixel format.</typeparam>
public sealed class WebGPUDeviceContext<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly nint deviceHandle;
    private readonly nint queueHandle;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceContext{TPixel}"/> class over the shared process-level WebGPU device.
    /// </summary>
    public WebGPUDeviceContext()
        : this(Configuration.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceContext{TPixel}"/> class over the shared process-level WebGPU device.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    public unsafe WebGPUDeviceContext(Configuration configuration)
    {
        Guard.NotNull(configuration, nameof(configuration));
        EnsurePixelTypeSupported();

        this.Backend = new WebGPUDrawingBackend();

        try
        {
            if (!WebGPURuntime.TryGetOrCreateDevice(out Device* device, out Queue* queue, out string? error))
            {
                throw new InvalidOperationException(error);
            }

            this.deviceHandle = (nint)device;
            this.queueHandle = (nint)queue;
            this.Configuration = configuration;
        }
        catch
        {
            this.Backend.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceContext{TPixel}"/> class over externally-owned device and queue handles.
    /// </summary>
    /// <param name="deviceHandle">The opaque <c>WGPUDevice*</c> handle.</param>
    /// <param name="queueHandle">The opaque <c>WGPUQueue*</c> handle.</param>
    public WebGPUDeviceContext(nint deviceHandle, nint queueHandle)
        : this(Configuration.Default, deviceHandle, queueHandle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceContext{TPixel}"/> class over externally-owned device and queue handles.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    /// <param name="deviceHandle">The opaque <c>WGPUDevice*</c> handle.</param>
    /// <param name="queueHandle">The opaque <c>WGPUQueue*</c> handle.</param>
    public WebGPUDeviceContext(Configuration configuration, nint deviceHandle, nint queueHandle)
    {
        Guard.NotNull(configuration, nameof(configuration));
        EnsurePixelTypeSupported();

        if (deviceHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceHandle), "Device handle must be non-zero.");
        }

        if (queueHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queueHandle), "Queue handle must be non-zero.");
        }

        this.deviceHandle = deviceHandle;
        this.queueHandle = queueHandle;
        this.Backend = new WebGPUDrawingBackend();
        this.Configuration = configuration;
    }

    /// <summary>
    /// Gets the configuration provided when the context was created.
    /// </summary>
    public Configuration Configuration { get; }

    /// <summary>
    /// Gets the WebGPU drawing backend owned by this context.
    /// </summary>
    public WebGPUDrawingBackend Backend { get; }

    /// <summary>
    /// Gets the opaque <c>WGPUDevice*</c> handle used by this context.
    /// </summary>
    public nint DeviceHandle
    {
        get
        {
            this.ThrowIfDisposed();
            return this.deviceHandle;
        }
    }

    /// <summary>
    /// Gets the opaque <c>WGPUQueue*</c> handle used by this context.
    /// </summary>
    public nint QueueHandle
    {
        get
        {
            this.ThrowIfDisposed();
            return this.queueHandle;
        }
    }

    /// <summary>
    /// Creates an owned offscreen WebGPU render target for this context.
    /// </summary>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    /// <returns>An owned offscreen WebGPU render target.</returns>
    public WebGPURenderTarget<TPixel> CreateRenderTarget(int width, int height)
    {
        this.ThrowIfDisposed();

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");
        }

        return WebGPURenderTarget<TPixel>.CreateFromContext(this, width, height);
    }

    /// <summary>
    /// Creates a native-only frame over an externally-owned WebGPU texture and view.
    /// </summary>
    /// <param name="textureHandle">The opaque <c>WGPUTexture*</c> handle.</param>
    /// <param name="textureViewHandle">The opaque <c>WGPUTextureView*</c> handle.</param>
    /// <param name="format">The texture format identifier.</param>
    /// <param name="width">The frame width in pixels.</param>
    /// <param name="height">The frame height in pixels.</param>
    /// <returns>A native-only canvas frame.</returns>
    public NativeCanvasFrame<TPixel> CreateFrame(
        nint textureHandle,
        nint textureViewHandle,
        WebGPUTextureFormatId format,
        int width,
        int height)
        => new(CreateBounds(width, height), this.CreateSurface(textureHandle, textureViewHandle, format, width, height));

    /// <summary>
    /// Creates a drawing canvas over an externally-owned WebGPU texture.
    /// </summary>
    /// <param name="textureHandle">The opaque <c>WGPUTexture*</c> handle.</param>
    /// <param name="textureViewHandle">The opaque <c>WGPUTextureView*</c> handle.</param>
    /// <param name="format">The texture format identifier.</param>
    /// <param name="width">The frame width in pixels.</param>
    /// <param name="height">The frame height in pixels.</param>
    /// <returns>A drawing canvas targeting the external texture.</returns>
    public DrawingCanvas<TPixel> CreateCanvas(
        nint textureHandle,
        nint textureViewHandle,
        WebGPUTextureFormatId format,
        int width,
        int height)
        => new(this.Configuration, this.Backend, this.CreateFrame(textureHandle, textureViewHandle, format, width, height), new DrawingOptions());

    /// <summary>
    /// Creates a drawing canvas over an externally-owned WebGPU texture.
    /// </summary>
    /// <param name="textureHandle">The opaque <c>WGPUTexture*</c> handle.</param>
    /// <param name="textureViewHandle">The opaque <c>WGPUTextureView*</c> handle.</param>
    /// <param name="format">The texture format identifier.</param>
    /// <param name="width">The frame width in pixels.</param>
    /// <param name="height">The frame height in pixels.</param>
    /// <param name="options">The initial drawing options.</param>
    /// <returns>A drawing canvas targeting the external texture.</returns>
    public DrawingCanvas<TPixel> CreateCanvas(
        nint textureHandle,
        nint textureViewHandle,
        WebGPUTextureFormatId format,
        int width,
        int height,
        DrawingOptions options)
        => new(this.Configuration, this.Backend, this.CreateFrame(textureHandle, textureViewHandle, format, width, height), options);

    /// <summary>
    /// Disposes the drawing backend owned by this context.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.Backend.Dispose();
        this.isDisposed = true;
    }

    /// <summary>
    /// Throws when the context is disposed.
    /// </summary>
    internal void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

    private static void EnsurePixelTypeSupported()
    {
        if (!WebGPUDrawingBackend.TryGetCompositeTextureFormat<TPixel>(out _))
        {
            throw new NotSupportedException($"Pixel type '{typeof(TPixel).Name}' is not supported by the WebGPU backend.");
        }
    }

    private NativeSurface CreateSurface(
        nint textureHandle,
        nint textureViewHandle,
        WebGPUTextureFormatId format,
        int width,
        int height)
    {
        this.ThrowIfDisposed();

        if (textureHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureHandle), "Texture handle must be non-zero.");
        }

        if (textureViewHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureViewHandle), "Texture view handle must be non-zero.");
        }

        return WebGPUNativeSurfaceFactory.Create<TPixel>(
            this.deviceHandle,
            this.queueHandle,
            textureHandle,
            textureViewHandle,
            format,
            width,
            height);
    }

    private static Rectangle CreateBounds(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");
        }

        return new Rectangle(0, 0, width, height);
    }
}
