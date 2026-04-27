// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using Silk.NET.Core.Contexts;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;
using SilkPresentMode = Silk.NET.WebGPU.PresentMode;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Owning container for the per-surface WebGPU stack: instance, surface, adapter, device, queue, drawing context,
/// and the negotiated swapchain texture format.
/// </summary>
/// <remarks>
/// <para>
/// Provides a single static <see cref="Create"/> factory that bootstraps every handle in order and leaves the surface
/// initially configured against <paramref>initialPresentMode</paramref> and <paramref>initialFramebufferSize</paramref>.
/// Callers hold the returned instance for the lifetime of the rendering surface and dispose it when the surface tears down.
/// </para>
/// <para>
/// Shared by the owned-window and hosted-surface entry points. Both provide a native surface source while this class owns the WebGPU
/// handles, surface creation, per-frame texture acquisition, and swapchain reconfiguration.
/// </para>
/// <para>
/// All handle fields are non-null after successful construction. <see cref="Dispose"/> releases them in reverse
/// acquisition order.
/// </para>
/// </remarks>
/// <typeparam name="TPixel">The canvas pixel format. Must map to a WebGPU texture format that
/// <see cref="WebGPUDrawingBackend.TryGetCompositeTextureFormat{TPixel}(out WebGPUTextureFormatId, out FeatureName)"/>
/// recognizes, otherwise <see cref="Create"/> throws <see cref="NotSupportedException"/>.</typeparam>
internal sealed unsafe class WebGPUSurfaceResources<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <summary>
    /// Upper bound for the asynchronous adapter and device request callbacks. Exceeding this throws.
    /// </summary>
    private const int CallbackTimeoutMilliseconds = 10_000;

    private bool isDisposed;
    private bool frameInFlight;
    private readonly FeatureName requiredFeature;
    private readonly Configuration configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSurfaceResources{TPixel}"/> class with already-acquired handles.
    /// Only invoked by <see cref="Create"/> after every handle has been successfully bootstrapped.
    /// </summary>
    /// <param name="api">The shared WebGPU API loader.</param>
    /// <param name="configuration">The ImageSharp configuration the drawing context uses for its rendering backend.</param>
    /// <param name="instanceHandle">The owned WebGPU instance handle.</param>
    /// <param name="surfaceHandle">The owned WebGPU surface handle attached to the native host.</param>
    /// <param name="adapterHandle">The owned adapter handle selected for <paramref name="surfaceHandle"/>.</param>
    /// <param name="deviceHandle">The owned device handle requested from <paramref name="adapterHandle"/>.</param>
    /// <param name="queueHandle">The owned default queue handle paired with <paramref name="deviceHandle"/>.</param>
    /// <param name="graphics">The drawing context bound to <paramref name="deviceHandle"/> and <paramref name="queueHandle"/>.</param>
    /// <param name="format">The negotiated swapchain texture format for <typeparamref name="TPixel"/>.</param>
    /// <param name="requiredFeature">The optional WebGPU feature required by the selected texture format.</param>
    private WebGPUSurfaceResources(
        WebGPU api,
        Configuration configuration,
        WebGPUInstanceHandle instanceHandle,
        WebGPUSurfaceHandle surfaceHandle,
        WebGPUAdapterHandle adapterHandle,
        WebGPUDeviceHandle deviceHandle,
        WebGPUQueueHandle queueHandle,
        WebGPUDeviceContext<TPixel> graphics,
        WebGPUTextureFormatId format,
        FeatureName requiredFeature)
    {
        this.Api = api;
        this.configuration = configuration;
        this.InstanceHandle = instanceHandle;
        this.SurfaceHandle = surfaceHandle;
        this.AdapterHandle = adapterHandle;
        this.DeviceHandle = deviceHandle;
        this.QueueHandle = queueHandle;
        this.Graphics = graphics;
        this.Format = format;
        this.requiredFeature = requiredFeature;
    }

    /// <summary>
    /// Gets the shared WebGPU API loader used for every native call made through this stack.
    /// </summary>
    public WebGPU Api { get; }

    /// <summary>
    /// Gets the WebGPU instance owned by this stack. Released on <see cref="Dispose"/>.
    /// </summary>
    public WebGPUInstanceHandle InstanceHandle { get; }

    /// <summary>
    /// Gets the WebGPU surface attached to the native host. The surface is reconfigured whenever
    /// <see cref="ConfigureSurface"/> runs and released on <see cref="Dispose"/>.
    /// </summary>
    public WebGPUSurfaceHandle SurfaceHandle { get; }

    /// <summary>
    /// Gets the adapter selected for the current surface. Released on <see cref="Dispose"/>.
    /// </summary>
    public WebGPUAdapterHandle AdapterHandle { get; private set; }

    /// <summary>
    /// Gets the logical device requested from <see cref="AdapterHandle"/>. Released on <see cref="Dispose"/>.
    /// </summary>
    public WebGPUDeviceHandle DeviceHandle { get; private set; }

    /// <summary>
    /// Gets the default queue for <see cref="DeviceHandle"/>. Released on <see cref="Dispose"/>.
    /// </summary>
    public WebGPUQueueHandle QueueHandle { get; private set; }

    /// <summary>
    /// Gets the drawing context bound to <see cref="DeviceHandle"/>/<see cref="QueueHandle"/>, used to wrap acquired
    /// per-frame textures into <see cref="DrawingCanvas{TPixel}"/> instances.
    /// </summary>
    public WebGPUDeviceContext<TPixel> Graphics { get; private set; }

    /// <summary>
    /// Gets the swapchain texture format chosen for <typeparamref name="TPixel"/> at construction time.
    /// Stable for the lifetime of this instance.
    /// </summary>
    public WebGPUTextureFormatId Format { get; }

    /// <summary>
    /// Bootstraps the full per-surface WebGPU stack bound to <paramref name="nativeSource"/> and leaves the surface
    /// configured against <paramref name="initialPresentMode"/> and <paramref name="initialFramebufferSize"/>.
    /// </summary>
    /// <param name="configuration">The ImageSharp configuration the drawing context will use for its rendering backend.</param>
    /// <param name="nativeSource">The native surface source that provides the platform handles for surface creation.</param>
    /// <param name="initialPresentMode">The present mode to apply to the first surface configuration.</param>
    /// <param name="initialFramebufferSize">The framebuffer size to apply to the first surface configuration. Zero-area
    /// sizes are permitted and leave the surface unconfigured until the caller invokes <see cref="ConfigureSurface"/>
    /// with a positive size.</param>
    /// <returns>The fully initialized resource container. The caller owns it and must call <see cref="Dispose"/>.</returns>
    /// <exception cref="NotSupportedException">Thrown when <typeparamref name="TPixel"/> does not map to a supported WebGPU texture format.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when any of the underlying WebGPU bootstrap steps fail. All partially-acquired handles are released
    /// before the exception propagates.
    /// </exception>
    public static WebGPUSurfaceResources<TPixel> Create(
        Configuration configuration,
        INativeWindowSource nativeSource,
        WebGPUPresentMode initialPresentMode,
        Size initialFramebufferSize)
    {
        if (!WebGPUDrawingBackend.TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId format, out FeatureName requiredFeature))
        {
            throw new NotSupportedException($"Pixel type '{typeof(TPixel).Name}' is not supported by the WebGPU backend.");
        }

        WebGPU api = WebGPURuntime.GetApi();
        Instance* instance = null;
        Surface* surface = null;
        WebGPUInstanceHandle? instanceHandle = null;
        WebGPUSurfaceHandle? surfaceHandle = null;
        DeviceResources? deviceResources = null;

        try
        {
            InstanceDescriptor instanceDescriptor = default;
            instance = api.CreateInstance(&instanceDescriptor);
            if (instance is null)
            {
                throw new InvalidOperationException("WebGPU instance creation failed.");
            }

            instanceHandle = new WebGPUInstanceHandle(api, (nint)instance, ownsHandle: true);
            surface = nativeSource.CreateWebGPUSurface(api, instance);
            if (surface is null)
            {
                throw new InvalidOperationException("WebGPU surface creation failed.");
            }

            surfaceHandle = new WebGPUSurfaceHandle(api, (nint)surface, ownsHandle: true);
            deviceResources = CreateDeviceResources(api, configuration, instance, surface, requiredFeature);

            WebGPUSurfaceResources<TPixel> resources = new(
                api,
                configuration,
                instanceHandle,
                surfaceHandle,
                deviceResources.AdapterHandle,
                deviceResources.DeviceHandle,
                deviceResources.QueueHandle,
                deviceResources.Graphics,
                format,
                requiredFeature);

            resources.ConfigureSurface(initialPresentMode, initialFramebufferSize);
            deviceResources = null;
            return resources;
        }
        catch
        {
            deviceResources?.Dispose();
            surfaceHandle?.Dispose();
            instanceHandle?.Dispose();

            if (surfaceHandle is null && surface is not null)
            {
                api.SurfaceRelease(surface);
            }

            if (instanceHandle is null && instance is not null)
            {
                api.InstanceRelease(instance);
            }

            throw;
        }
    }

    /// <summary>
    /// Reconfigures <see cref="SurfaceHandle"/> for a new present mode or framebuffer size.
    /// A zero-area <paramref name="framebufferSize"/> is ignored so callers can safely propagate resize
    /// events during minimize or zero-sized layout transitions.
    /// </summary>
    /// <param name="presentMode">The present mode applied to the swapchain.</param>
    /// <param name="framebufferSize">The new framebuffer size in pixels.</param>
    public void ConfigureSurface(WebGPUPresentMode presentMode, Size framebufferSize)
    {
        if (framebufferSize.Width <= 0 || framebufferSize.Height <= 0)
        {
            return;
        }

        this.ConfigureSurfaceCore(presentMode, framebufferSize, this.DeviceHandle);
    }

    /// <summary>
    /// Reconfigures <see cref="SurfaceHandle"/> against the supplied device handle.
    /// </summary>
    /// <param name="presentMode">The present mode applied to the swapchain.</param>
    /// <param name="framebufferSize">The framebuffer size in pixels.</param>
    /// <param name="deviceHandle">The device handle used by the new surface configuration.</param>
    private void ConfigureSurfaceCore(WebGPUPresentMode presentMode, Size framebufferSize, WebGPUDeviceHandle deviceHandle)
    {
        if (framebufferSize.Width <= 0 || framebufferSize.Height <= 0)
        {
            return;
        }

        SurfaceConfiguration surfaceConfiguration = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopyDst | TextureUsage.TextureBinding,
            Format = WebGPUTextureFormatMapper.ToSilk(this.Format),
            PresentMode = (SilkPresentMode)(int)presentMode,
            Width = (uint)framebufferSize.Width,
            Height = (uint)framebufferSize.Height,
        };

        using WebGPUHandle.HandleReference surfaceReference = this.SurfaceHandle.AcquireReference();
        using WebGPUHandle.HandleReference deviceReference = deviceHandle.AcquireReference();
        surfaceConfiguration.Device = (Device*)deviceReference.Handle;
        this.Api.SurfaceConfigure((Surface*)surfaceReference.Handle, ref surfaceConfiguration);
    }

    /// <summary>
    /// Acquires the next presentable frame from <see cref="SurfaceHandle"/> and wraps it as a
    /// <see cref="WebGPUSurfaceFrame{TPixel}"/> with a ready-to-use <see cref="DrawingCanvas{TPixel}"/>.
    /// </summary>
    /// <param name="presentMode">The present mode applied when the surface needs to be reconfigured in response to a
    /// <c>Timeout</c>/<c>Outdated</c>/<c>Lost</c> acquire status.</param>
    /// <param name="framebufferSize">The current framebuffer size in pixels. A zero-area value causes the method to
    /// return <see langword="false"/> without touching the surface. Otherwise this size is used both for the returned
    /// frame's <see cref="WebGPUSurfaceFrame{TPixel}.FramebufferSize"/> and for any in-place surface
    /// reconfiguration triggered by a non-success acquire status.</param>
    /// <param name="options">The drawing options that seed the canvas on the returned frame.</param>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns><see langword="true"/> when a frame is available; <see langword="false"/> when no drawable frame is
    /// available right now (zero-area framebuffer, recoverable surface acquisition status, or recovered device loss).</returns>
    /// <exception cref="InvalidOperationException">Thrown on an out-of-memory acquire status, or when texture-view
    /// creation or device recovery fails.</exception>
    public bool TryAcquireFrame(
        WebGPUPresentMode presentMode,
        Size framebufferSize,
        DrawingOptions options,
        [NotNullWhen(true)] out WebGPUSurfaceFrame<TPixel>? frame)
    {
        frame = null;

        if (framebufferSize.Width <= 0 || framebufferSize.Height <= 0)
        {
            return false;
        }

        // Reject acquire while a previously-issued frame is still outstanding. The native surface
        // state machine doesn't tolerate overlapping acquires, and some hosts can dispatch a paint
        // before the current frame's Dispose returns.
        if (this.frameInFlight)
        {
            return false;
        }

        SurfaceTexture surfaceTexture = default;
        using (WebGPUHandle.HandleReference surfaceReference = this.SurfaceHandle.AcquireReference())
        {
            this.Api.SurfaceGetCurrentTexture((Surface*)surfaceReference.Handle, &surfaceTexture);
        }

        switch (surfaceTexture.Status)
        {
            case SurfaceGetCurrentTextureStatus.Timeout:
            case SurfaceGetCurrentTextureStatus.Outdated:
            case SurfaceGetCurrentTextureStatus.Lost:
                if (surfaceTexture.Texture is not null)
                {
                    this.Api.TextureRelease(surfaceTexture.Texture);
                }

                this.ConfigureSurface(presentMode, framebufferSize);
                return false;

            case SurfaceGetCurrentTextureStatus.DeviceLost:
                if (surfaceTexture.Texture is not null)
                {
                    this.Api.TextureRelease(surfaceTexture.Texture);
                }

                this.RecoverDeviceResources(presentMode, framebufferSize);
                return false;

            case SurfaceGetCurrentTextureStatus.OutOfMemory:
                if (surfaceTexture.Texture is not null)
                {
                    this.Api.TextureRelease(surfaceTexture.Texture);
                }

                throw new InvalidOperationException($"Surface texture error: {surfaceTexture.Status}");
        }

        TextureView* textureView = this.Api.TextureCreateView(surfaceTexture.Texture, null);
        if (textureView is null)
        {
            this.Api.TextureRelease(surfaceTexture.Texture);
            throw new InvalidOperationException("WebGPU texture view creation failed.");
        }

        WebGPUTextureHandle? textureHandle = null;
        WebGPUTextureViewHandle? textureViewHandle = null;
        try
        {
            textureHandle = new WebGPUTextureHandle(this.Api, (nint)surfaceTexture.Texture, ownsHandle: true);
            textureViewHandle = new WebGPUTextureViewHandle(this.Api, (nint)textureView, ownsHandle: true);
            DrawingCanvas<TPixel> canvas = this.Graphics.CreateCanvas(
                options,
                textureHandle,
                textureViewHandle,
                this.Format,
                framebufferSize.Width,
                framebufferSize.Height);

            this.frameInFlight = true;
            frame = new WebGPUSurfaceFrame<TPixel>(
                this.Api,
                this.SurfaceHandle,
                textureHandle,
                textureViewHandle,
                canvas,
                framebufferSize,
                onDisposed: () => this.frameInFlight = false);

            return true;
        }
        catch
        {
            textureViewHandle?.Dispose();
            textureHandle?.Dispose();

            if (textureViewHandle is null)
            {
                this.Api.TextureViewRelease(textureView);
            }

            if (textureHandle is null)
            {
                this.Api.TextureRelease(surfaceTexture.Texture);
            }

            throw;
        }
    }

    /// <summary>
    /// Releases every owned handle in reverse acquisition order (graphics context, queue, device, adapter, surface, instance).
    /// Idempotent; subsequent calls are no-ops.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.Graphics.Dispose();
        this.QueueHandle.Dispose();
        this.DeviceHandle.Dispose();
        this.AdapterHandle.Dispose();
        this.SurfaceHandle.Dispose();
        this.InstanceHandle.Dispose();
        this.isDisposed = true;
    }

    /// <summary>
    /// Requests the adapter, device, queue, and drawing context for an existing surface.
    /// </summary>
    /// <param name="api">The shared WebGPU API loader.</param>
    /// <param name="configuration">The ImageSharp configuration the drawing context will use.</param>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="surface">The surface the adapter must be compatible with.</param>
    /// <param name="requiredFeature">The optional WebGPU feature required by the selected texture format.</param>
    /// <returns>The acquired device resources. The caller owns the returned handles.</returns>
    private static DeviceResources CreateDeviceResources(
        WebGPU api,
        Configuration configuration,
        Instance* instance,
        Surface* surface,
        FeatureName requiredFeature)
    {
        Adapter* adapter = null;
        WebGPUAdapterHandle? adapterHandle = null;
        WebGPUDeviceHandle? deviceHandle = null;
        WebGPUQueueHandle? queueHandle = null;
        WebGPUDeviceContext<TPixel>? graphics = null;

        try
        {
            adapter = RequestAdapter(api, instance, surface);
            adapterHandle = new WebGPUAdapterHandle(api, (nint)adapter, ownsHandle: true);

            Device* device = RequestDevice(api, adapter, requiredFeature);
            deviceHandle = new WebGPUDeviceHandle(api, (nint)device, ownsHandle: true);
            Queue* queue = api.DeviceGetQueue(device);
            if (queue is null)
            {
                throw new InvalidOperationException("The WebGPU device did not provide a default queue.");
            }

            queueHandle = new WebGPUQueueHandle(api, (nint)queue, ownsHandle: true);
            graphics = new WebGPUDeviceContext<TPixel>(configuration, deviceHandle, queueHandle);

            DeviceResources resources = new(adapterHandle, deviceHandle, queueHandle, graphics);
            adapterHandle = null;
            deviceHandle = null;
            queueHandle = null;
            graphics = null;
            return resources;
        }
        catch
        {
            graphics?.Dispose();
            queueHandle?.Dispose();
            deviceHandle?.Dispose();
            adapterHandle?.Dispose();

            if (adapterHandle is null && adapter is not null)
            {
                api.AdapterRelease(adapter);
            }

            throw;
        }
    }

    /// <summary>
    /// Recovers the device-owned portion of the surface stack after device loss.
    /// The existing instance and surface remain valid; only adapter, device, queue, and drawing context are replaced.
    /// </summary>
    /// <param name="presentMode">The present mode applied to the recovered swapchain.</param>
    /// <param name="framebufferSize">The framebuffer size in pixels.</param>
    private void RecoverDeviceResources(WebGPUPresentMode presentMode, Size framebufferSize)
    {
        DeviceResources? deviceResources = null;

        try
        {
            using WebGPUHandle.HandleReference instanceReference = this.InstanceHandle.AcquireReference();
            using WebGPUHandle.HandleReference surfaceReference = this.SurfaceHandle.AcquireReference();
            deviceResources = CreateDeviceResources(
                this.Api,
                this.configuration,
                (Instance*)instanceReference.Handle,
                (Surface*)surfaceReference.Handle,
                this.requiredFeature);

            this.ConfigureSurfaceCore(presentMode, framebufferSize, deviceResources.DeviceHandle);

            WebGPUDeviceContext<TPixel> oldGraphics = this.Graphics;
            WebGPUQueueHandle oldQueueHandle = this.QueueHandle;
            WebGPUDeviceHandle oldDeviceHandle = this.DeviceHandle;
            WebGPUAdapterHandle oldAdapterHandle = this.AdapterHandle;

            this.Graphics = deviceResources.Graphics;
            this.QueueHandle = deviceResources.QueueHandle;
            this.DeviceHandle = deviceResources.DeviceHandle;
            this.AdapterHandle = deviceResources.AdapterHandle;
            deviceResources = null;

            oldGraphics.Dispose();
            oldQueueHandle.Dispose();
            oldDeviceHandle.Dispose();
            oldAdapterHandle.Dispose();
        }
        catch
        {
            deviceResources?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Requests a high-performance adapter compatible with <paramref name="surface"/>.
    /// </summary>
    /// <param name="api">The shared WebGPU API loader.</param>
    /// <param name="instance">The WebGPU instance the adapter is requested from.</param>
    /// <param name="surface">The surface the adapter must be compatible with.</param>
    /// <returns>The requested adapter.</returns>
    private static Adapter* RequestAdapter(
        WebGPU api,
        Instance* instance,
        Surface* surface)
    {
        RequestAdapterStatus callbackStatus = RequestAdapterStatus.Unknown;
        Adapter* callbackAdapter = null;
        using ManualResetEventSlim callbackReady = new(false);

        void Callback(RequestAdapterStatus status, Adapter* adapterPtr, byte* message, void* userData)
        {
            _ = message;
            _ = userData;
            callbackStatus = status;
            callbackAdapter = adapterPtr;
            callbackReady.Set();
        }

        using PfnRequestAdapterCallback callbackPtr = PfnRequestAdapterCallback.From(Callback);
        RequestAdapterOptions options = new()
        {
            CompatibleSurface = surface,
            PowerPreference = PowerPreference.HighPerformance,
        };

        api.InstanceRequestAdapter(instance, in options, callbackPtr, null);
        if (!callbackReady.Wait(CallbackTimeoutMilliseconds))
        {
            throw new InvalidOperationException("Timed out while waiting for the WebGPU adapter request callback.");
        }

        if (callbackStatus != RequestAdapterStatus.Success || callbackAdapter is null)
        {
            throw new InvalidOperationException($"The WebGPU runtime failed to acquire a surface-compatible adapter. Status: '{callbackStatus}'.");
        }

        return callbackAdapter;
    }

    /// <summary>
    /// Requests a device from <paramref name="adapter"/> with <paramref name="requiredFeature"/> enabled (when not
    /// <see cref="FeatureName.Undefined"/>) and waits synchronously on the asynchronous callback, up to
    /// <see cref="CallbackTimeoutMilliseconds"/>.
    /// </summary>
    /// <param name="api">The shared WebGPU API loader.</param>
    /// <param name="adapter">The adapter the device is requested from.</param>
    /// <param name="requiredFeature">The feature to enable on the requested device, or <see cref="FeatureName.Undefined"/> for none.</param>
    /// <returns>The requested device.</returns>
    private static Device* RequestDevice(
        WebGPU api,
        Adapter* adapter,
        FeatureName requiredFeature)
    {
        if (requiredFeature != FeatureName.Undefined && !api.AdapterHasFeature(adapter, requiredFeature))
        {
            throw new NotSupportedException($"The selected WebGPU adapter does not support required feature '{requiredFeature}'.");
        }

        RequestDeviceStatus callbackStatus = RequestDeviceStatus.Unknown;
        Device* callbackDevice = null;
        using ManualResetEventSlim callbackReady = new(false);

        void Callback(RequestDeviceStatus status, Device* devicePtr, byte* message, void* userData)
        {
            _ = message;
            _ = userData;
            callbackStatus = status;
            callbackDevice = devicePtr;
            callbackReady.Set();
        }

        using PfnRequestDeviceCallback callbackPtr = PfnRequestDeviceCallback.From(Callback);
        DeviceDescriptor descriptor = default;
        if (requiredFeature != FeatureName.Undefined)
        {
            FeatureName requestedFeature = requiredFeature;
            descriptor = new DeviceDescriptor
            {
                RequiredFeatureCount = 1,
                RequiredFeatures = &requestedFeature,
            };
        }

        api.AdapterRequestDevice(adapter, in descriptor, callbackPtr, null);
        if (!callbackReady.Wait(CallbackTimeoutMilliseconds))
        {
            throw new InvalidOperationException("Timed out while waiting for the WebGPU device request callback.");
        }

        if (callbackStatus != RequestDeviceStatus.Success || callbackDevice is null)
        {
            throw new InvalidOperationException($"The WebGPU runtime failed to acquire a device. Status: '{callbackStatus}'.");
        }

        return callbackDevice;
    }

    /// <summary>
    /// Owns one acquired adapter/device/queue/context set while it is being transferred into the surface resources.
    /// </summary>
    private sealed class DeviceResources : IDisposable
    {
        public DeviceResources(
            WebGPUAdapterHandle adapterHandle,
            WebGPUDeviceHandle deviceHandle,
            WebGPUQueueHandle queueHandle,
            WebGPUDeviceContext<TPixel> graphics)
        {
            this.AdapterHandle = adapterHandle;
            this.DeviceHandle = deviceHandle;
            this.QueueHandle = queueHandle;
            this.Graphics = graphics;
        }

        public WebGPUAdapterHandle AdapterHandle { get; }

        public WebGPUDeviceHandle DeviceHandle { get; }

        public WebGPUQueueHandle QueueHandle { get; }

        public WebGPUDeviceContext<TPixel> Graphics { get; }

        public void Dispose()
        {
            this.Graphics.Dispose();
            this.QueueHandle.Dispose();
            this.DeviceHandle.Dispose();
            this.AdapterHandle.Dispose();
        }
    }
}
