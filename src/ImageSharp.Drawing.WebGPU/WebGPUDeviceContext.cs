// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Internal WebGPU device/queue binding used by render targets and surface resources.
/// </summary>
internal sealed class WebGPUDeviceContext : IDisposable
{
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceContext"/> class over the shared process-level WebGPU device.
    /// </summary>
    internal WebGPUDeviceContext()
        : this(Configuration.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceContext"/> class over the shared process-level WebGPU device.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    internal WebGPUDeviceContext(Configuration configuration)
    {
        Guard.NotNull(configuration, nameof(configuration));

        this.Backend = new WebGPUDrawingBackend();

        try
        {
            if (!WebGPURuntime.TryGetOrCreateDevice(
                    out WebGPUDeviceHandle? deviceHandle,
                    out WebGPUQueueHandle? queueHandle,
                    out WebGPUEnvironmentError errorCode)
                || deviceHandle is null
                || queueHandle is null)
            {
                throw new InvalidOperationException(WebGPURuntime.CreateEnvironmentExceptionMessage(errorCode));
            }

            this.DeviceHandle = deviceHandle;
            this.QueueHandle = queueHandle;
            this.Configuration = configuration;
        }
        catch
        {
            this.Backend.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceContext"/> class over externally-owned device and queue handles.
    /// </summary>
    /// <param name="deviceHandle">The external WebGPU device handle.</param>
    /// <param name="queueHandle">The external WebGPU queue handle.</param>
    /// <remarks>
    /// These handles must originate from the same process WebGPU runtime used by ImageSharp.Drawing.WebGPU.
    /// The context does not take ownership of them.
    /// </remarks>
    internal WebGPUDeviceContext(nint deviceHandle, nint queueHandle)
        : this(Configuration.Default, deviceHandle, queueHandle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceContext"/> class over externally-owned device and queue handles.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    /// <param name="deviceHandle">The external WebGPU device handle.</param>
    /// <param name="queueHandle">The external WebGPU queue handle.</param>
    /// <remarks>
    /// These handles must originate from the same process WebGPU runtime used by ImageSharp.Drawing.WebGPU.
    /// The context does not take ownership of them.
    /// </remarks>
    internal WebGPUDeviceContext(Configuration configuration, nint deviceHandle, nint queueHandle)
        : this(configuration, CreateExternalDeviceHandle(deviceHandle), CreateExternalQueueHandle(queueHandle))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceContext"/> class over wrapped device and queue handles using the default configuration.
    /// </summary>
    /// <param name="deviceHandle">The wrapped WebGPU device handle.</param>
    /// <param name="queueHandle">The wrapped WebGPU queue handle.</param>
    internal WebGPUDeviceContext(WebGPUDeviceHandle deviceHandle, WebGPUQueueHandle queueHandle)
        : this(Configuration.Default, deviceHandle, queueHandle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceContext"/> class over externally-owned device and queue handles.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    /// <param name="deviceHandle">The wrapped WebGPU device handle.</param>
    /// <param name="queueHandle">The wrapped WebGPU queue handle.</param>
    internal WebGPUDeviceContext(Configuration configuration, WebGPUDeviceHandle deviceHandle, WebGPUQueueHandle queueHandle)
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(deviceHandle, nameof(deviceHandle));
        Guard.NotNull(queueHandle, nameof(queueHandle));

        this.DeviceHandle = deviceHandle;
        this.QueueHandle = queueHandle;

        // Device-scoped shared state owns the uncaptured-error callback, so create it
        // before any later surface or render-target work can report native validation errors.
        _ = WebGPURuntime.GetOrCreateDeviceState(WebGPURuntime.GetApi(), deviceHandle);

        this.Backend = new WebGPUDrawingBackend();
        this.Configuration = configuration;
    }

    /// <summary>
    /// Gets the configuration provided when the context was created.
    /// </summary>
    internal Configuration Configuration { get; }

    /// <summary>
    /// Gets the WebGPU drawing backend owned by this context.
    /// Use this to inspect per-flush diagnostics for chunked rendering.
    /// </summary>
    internal WebGPUDrawingBackend Backend { get; }

    /// <summary>
    /// Gets the wrapped WebGPU device handle used by frames, canvases, and render-target allocation created from this context.
    /// </summary>
    internal WebGPUDeviceHandle DeviceHandle { get; }

    /// <summary>
    /// Gets the wrapped WebGPU queue handle paired with <see cref="DeviceHandle"/> for uploads, readback, and command submission.
    /// </summary>
    internal WebGPUQueueHandle QueueHandle { get; }

    /// <summary>
    /// Creates an owned offscreen WebGPU render target using the default RGBA8 texture format.
    /// </summary>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    /// <returns>An owned offscreen WebGPU render target.</returns>
    internal WebGPURenderTarget CreateRenderTarget(int width, int height)
        => this.CreateRenderTarget(WebGPUTextureFormat.Rgba8Unorm, width, height);

    /// <summary>
    /// Creates an owned offscreen WebGPU render target for this context.
    /// </summary>
    /// <param name="format">The target texture format.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    /// <returns>An owned offscreen WebGPU render target.</returns>
    internal WebGPURenderTarget CreateRenderTarget(
        WebGPUTextureFormat format,
        int width,
        int height)
    {
        this.ThrowIfDisposed();
        Guard.MustBeGreaterThan(width, 0, nameof(width));
        Guard.MustBeGreaterThan(height, 0, nameof(height));

        return WebGPURenderTarget.CreateFromContext(this, format, width, height);
    }

    /// <summary>
    /// Creates a drawing canvas that renders directly into an externally-owned WebGPU texture.
    /// </summary>
    /// <param name="textureHandle">The external WebGPU texture handle.</param>
    /// <param name="textureViewHandle">The external WebGPU texture-view handle.</param>
    /// <param name="format">The texture format.</param>
    /// <param name="width">The frame width in pixels.</param>
    /// <param name="height">The frame height in pixels.</param>
    /// <returns>A drawing canvas targeting the external texture.</returns>
    /// <remarks>
    /// The caller retains ownership of the texture and view; this context does not release them.
    /// The texture must have been created with <c>RenderAttachment | CopySrc | CopyDst | TextureBinding</c> usage.
    /// Dispose the returned canvas before the host calls <c>wgpuSurfacePresent</c>, then create a new canvas on the next frame.
    /// </remarks>
    internal DrawingCanvas CreateCanvas(
        nint textureHandle,
        nint textureViewHandle,
        WebGPUTextureFormat format,
        int width,
        int height)
        => this.CreateCanvas(
            new DrawingOptions(),
            CreateExternalTextureHandle(textureHandle),
            CreateExternalTextureViewHandle(textureViewHandle),
            format,
            width,
            height);

    /// <summary>
    /// Creates a drawing canvas that renders directly into an externally-owned WebGPU texture.
    /// </summary>
    /// <param name="options">The initial drawing options.</param>
    /// <param name="textureHandle">The external WebGPU texture handle.</param>
    /// <param name="textureViewHandle">The external WebGPU texture-view handle.</param>
    /// <param name="format">The texture format.</param>
    /// <param name="width">The frame width in pixels.</param>
    /// <param name="height">The frame height in pixels.</param>
    /// <returns>A drawing canvas targeting the external texture.</returns>
    /// <remarks>
    /// The caller retains ownership of the texture and view; this context does not release them.
    /// The texture must have been created with <c>RenderAttachment | CopySrc | CopyDst | TextureBinding</c> usage.
    /// Dispose the returned canvas before the host calls <c>wgpuSurfacePresent</c>, then create a new canvas on the next frame.
    /// </remarks>
    internal DrawingCanvas CreateCanvas(
        DrawingOptions options,
        nint textureHandle,
        nint textureViewHandle,
        WebGPUTextureFormat format,
        int width,
        int height)
        => this.CreateCanvas(
            options,
            CreateExternalTextureHandle(textureHandle),
            CreateExternalTextureViewHandle(textureViewHandle),
            format,
            width,
            height);

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

    /// <summary>
    /// Creates a drawing canvas over wrapped texture handles that are already in this assembly's ownership model.
    /// </summary>
    internal DrawingCanvas CreateCanvas(
        DrawingOptions options,
        WebGPUTextureHandle textureHandle,
        WebGPUTextureViewHandle textureViewHandle,
        WebGPUTextureFormat format,
        int width,
        int height)
    {
        Rectangle bounds = new(0, 0, width, height);
        NativeSurface surface = this.CreateSurface(textureHandle, textureViewHandle, format, width, height);

        return WebGPUCanvasFactory.CreateCanvas(this.Configuration, options, this.Backend, bounds, surface, format);
    }

    /// <summary>
    /// Creates the wrapped native surface over the supplied texture handles.
    /// </summary>
    private NativeSurface CreateSurface(
        WebGPUTextureHandle textureHandle,
        WebGPUTextureViewHandle textureViewHandle,
        WebGPUTextureFormat format,
        int width,
        int height)
    {
        this.ThrowIfDisposed();
        Guard.NotNull(textureHandle, nameof(textureHandle));
        Guard.NotNull(textureViewHandle, nameof(textureViewHandle));

        return WebGPUNativeSurface.Create(
            this.DeviceHandle,
            this.QueueHandle,
            textureHandle,
            textureViewHandle,
            format,
            width,
            height);
    }

    /// <summary>
    /// Wraps one externally-owned device handle without taking ownership.
    /// </summary>
    private static WebGPUDeviceHandle CreateExternalDeviceHandle(nint deviceHandle)
        => new(deviceHandle, ownsHandle: false);

    /// <summary>
    /// Wraps one externally-owned queue handle without taking ownership.
    /// </summary>
    private static WebGPUQueueHandle CreateExternalQueueHandle(nint queueHandle)
        => new(queueHandle, ownsHandle: false);

    /// <summary>
    /// Wraps one externally-owned texture handle without taking ownership.
    /// </summary>
    private static WebGPUTextureHandle CreateExternalTextureHandle(nint textureHandle)
        => new(textureHandle, ownsHandle: false);

    /// <summary>
    /// Wraps one externally-owned texture-view handle without taking ownership.
    /// </summary>
    private static WebGPUTextureViewHandle CreateExternalTextureViewHandle(nint textureViewHandle)
        => new(textureViewHandle, ownsHandle: false);
}
