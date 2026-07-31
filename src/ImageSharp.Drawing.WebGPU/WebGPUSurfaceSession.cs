// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Provides shared WebGPU resources for a group of external presentation surfaces.
/// </summary>
/// <remarks>
/// Create one session for related windows and popups so they use the same GPU device and drawing context.
/// Dispose every surface and render target created by the session before disposing the session.
/// </remarks>
public sealed unsafe class WebGPUSurfaceSession : IDisposable
{
    private readonly Configuration configuration;
    private WebGPU? api;
    private WebGPUAdapterHandle? adapterHandle;
    private WebGPUDeviceHandle? deviceHandle;
    private WebGPUQueueHandle? queueHandle;
    private WebGPUDeviceContext? deviceContext;
    private WebGPURuntime.DeviceSharedState? deviceState;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSurfaceSession"/> class.
    /// </summary>
    public WebGPUSurfaceSession()
        : this(Configuration.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSurfaceSession"/> class.
    /// </summary>
    /// <param name="configuration">The configuration used by surfaces and render targets created by this session.</param>
    public WebGPUSurfaceSession(Configuration configuration)
    {
        Guard.NotNull(configuration, nameof(configuration));

        this.configuration = configuration;
    }

    /// <summary>
    /// Gets a value indicating whether the shared WebGPU device has been lost.
    /// </summary>
    public bool IsLost => this.deviceState?.IsLost ?? false;

    /// <summary>
    /// Gets the shared WebGPU API loader.
    /// </summary>
    internal WebGPU Api => this.api ??= WebGPURuntime.GetApi();

    /// <summary>
    /// Gets the adapter selected for the first presentation surface.
    /// </summary>
    internal WebGPUAdapterHandle AdapterHandle => this.adapterHandle!;

    /// <summary>
    /// Gets the device shared by every surface in the session.
    /// </summary>
    internal WebGPUDeviceHandle DeviceHandle => this.deviceHandle!;

    /// <summary>
    /// Gets the queue shared by every surface in the session.
    /// </summary>
    internal WebGPUQueueHandle QueueHandle => this.queueHandle!;

    /// <summary>
    /// Gets the device context shared by every surface and render target in this session.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no presentation surface has initialized the session.</exception>
    /// <remarks>The session owns the returned context; callers must not dispose it separately.</remarks>
    public WebGPUDeviceContext DeviceContext
    {
        get
        {
            this.ThrowIfDisposed();

            return this.deviceContext
                ?? throw new InvalidOperationException("Create a presentation surface before accessing its WebGPU device context.");
        }
    }

    /// <summary>
    /// Creates a presentation surface attached to an externally owned native host.
    /// </summary>
    /// <param name="host">The native surface host.</param>
    /// <param name="framebufferSize">The initial framebuffer size in pixels.</param>
    /// <returns>The created presentation surface.</returns>
    public WebGPUExternalSurface CreateSurface(WebGPUSurfaceHost host, Size framebufferSize)
        => this.CreateSurface(host, framebufferSize, new WebGPUExternalSurfaceOptions());

    /// <summary>
    /// Creates a presentation surface attached to an externally owned native host.
    /// </summary>
    /// <param name="host">The native surface host.</param>
    /// <param name="framebufferSize">The initial framebuffer size in pixels.</param>
    /// <param name="options">The presentation options.</param>
    /// <returns>The created presentation surface.</returns>
    public WebGPUExternalSurface CreateSurface(WebGPUSurfaceHost host, Size framebufferSize, WebGPUExternalSurfaceOptions options)
    {
        this.ThrowIfDisposed();
        Guard.NotNull(options, nameof(options));
        Guard.MustBeGreaterThan(framebufferSize.Width, 0, nameof(framebufferSize));
        Guard.MustBeGreaterThan(framebufferSize.Height, 0, nameof(framebufferSize));

        return new WebGPUExternalSurface(this, host, framebufferSize, options);
    }

    /// <summary>
    /// Creates an offscreen render target on the shared WebGPU device.
    /// </summary>
    /// <param name="format">The texture format.</param>
    /// <param name="alphaRepresentation">The alpha representation stored by the target.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    /// <returns>The created render target.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no presentation surface has initialized the session.</exception>
    public WebGPURenderTarget CreateRenderTarget(WebGPUTextureFormat format, PixelAlphaRepresentation alphaRepresentation, int width, int height)
    {
        this.ThrowIfDisposed();

        if (this.deviceContext is null)
        {
            throw new InvalidOperationException("Create a presentation surface before creating an offscreen target from its WebGPU session.");
        }

        Guard.MustBeGreaterThan(width, 0, nameof(width));
        Guard.MustBeGreaterThan(height, 0, nameof(height));

        WebGPUTargetDescriptor targetDescriptor = WebGPUDrawingBackend.CreateOffscreenTargetDescriptor(format, alphaRepresentation);

        return new WebGPURenderTarget(this.deviceContext, false, targetDescriptor, width, height, isPresentationSurface: false);
    }

    /// <summary>
    /// Initializes the session against its first compatible presentation surface.
    /// </summary>
    /// <param name="compatibleSurface">The surface used to select the presentation adapter.</param>
    internal void EnsureInitialized(WGPUSurfaceImpl* compatibleSurface)
    {
        this.ThrowIfDisposed();

        if (this.deviceContext is not null)
        {
            return;
        }

        WGPUAdapterImpl* adapter = null;
        WGPUDeviceImpl* device = null;
        WebGPUAdapterHandle? acquiredAdapter = null;
        WebGPUDeviceHandle? acquiredDevice = null;
        WebGPUQueueHandle? acquiredQueue = null;
        WebGPUDeviceContext? acquiredContext = null;

        try
        {
            WGPUInstanceImpl* instance = WebGPURuntime.GetOrCreateSharedInstance();

            if (!WebGPURuntime.TryRequestAdapter(this.Api, instance, compatibleSurface, out adapter, out WebGPUEnvironmentError adapterError))
            {
                throw new InvalidOperationException(WebGPURuntime.CreateEnvironmentExceptionMessage(adapterError));
            }

            acquiredAdapter = new WebGPUAdapterHandle(this.Api, (nint)adapter, ownsHandle: true);

            if (!WebGPURuntime.TryRequestDevice(this.Api, adapter, out device, out WebGPUEnvironmentError deviceError))
            {
                throw new InvalidOperationException(WebGPURuntime.CreateEnvironmentExceptionMessage(deviceError));
            }

            acquiredDevice = new WebGPUDeviceHandle(this.Api, (nint)device, ownsHandle: true);
            WGPUQueueImpl* queue = this.Api.DeviceGetQueue(device);

            if (queue is null)
            {
                throw new InvalidOperationException("The WebGPU device did not provide a default queue.");
            }

            acquiredQueue = new WebGPUQueueHandle(this.Api, (nint)queue, ownsHandle: true);
            acquiredContext = new WebGPUDeviceContext(this.configuration, acquiredDevice, acquiredQueue);
            WebGPURuntime.DeviceSharedState acquiredState = WebGPURuntime.GetOrCreateDeviceState(this.Api, acquiredDevice);

            // Device-state construction queries native limits and can fail. Publish the session
            // only after that final fallible operation so a retry cannot observe disposed handles
            // left behind by this method's exception cleanup.
            this.adapterHandle = acquiredAdapter;
            this.deviceHandle = acquiredDevice;
            this.queueHandle = acquiredQueue;
            this.deviceContext = acquiredContext;
            this.deviceState = acquiredState;
            acquiredAdapter = null;
            acquiredDevice = null;
            acquiredQueue = null;
            acquiredContext = null;
        }
        catch
        {
            acquiredContext?.Dispose();
            acquiredQueue?.Dispose();
            acquiredDevice?.Dispose();
            acquiredAdapter?.Dispose();

            // A raw callback result is released directly only when constructing its owning
            // SafeHandle failed before ownership could transfer to managed code.
            if (acquiredDevice is null && device is not null && this.deviceHandle is null)
            {
                this.Api.DeviceRelease(device);
            }

            if (acquiredAdapter is null && adapter is not null && this.adapterHandle is null)
            {
                this.Api.AdapterRelease(adapter);
            }

            throw;
        }
    }

    /// <summary>
    /// Releases the shared WebGPU resources.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.deviceContext?.Dispose();

        if (this.deviceHandle is not null)
        {
            // DeviceSharedState owns the compiled pipelines and a reference to the device.
            // Remove it while the owning device handle is still live, after the shared backend
            // has drained its pending work and before releasing the native queue/device pair.
            WebGPURuntime.RemoveDeviceState(this.deviceHandle.DangerousGetHandle());
        }

        this.queueHandle?.Dispose();
        this.deviceHandle?.Dispose();
        this.adapterHandle?.Dispose();
        this.isDisposed = true;
    }

    /// <summary>
    /// Throws when this session has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);
}
