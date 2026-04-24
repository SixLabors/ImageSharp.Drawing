// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using Silk.NET.Core.Contexts;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;
using SilkPresentMode = Silk.NET.WebGPU.PresentMode;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Owning container for the per-window WebGPU stack: instance, surface, adapter, device, queue, drawing context,
/// and the negotiated swapchain texture format.
/// </summary>
/// <remarks>
/// <para>
/// Provides a single static <see cref="Create"/> factory that bootstraps every handle in order and leaves the surface
/// initially configured against <paramref>initialPresentMode</paramref> and <paramref>initialFramebufferSize</paramref>.
/// Callers hold the returned instance for the lifetime of the window and dispose it when the window tears down.
/// </para>
/// <para>
/// Shared by the two public window types: <see cref="WebGPUWindow{TPixel}"/> (where this type binds to a library-owned
/// Silk <c>IWindow</c>) and <see cref="WebGPUHostedWindow{TPixel}"/> (where this type binds to an externally-owned
/// native window via <see cref="SilkNativeWindowAdapter"/>). Neither caller owns the Silk types directly; they pass
/// an <see cref="INativeWindowSource"/> and this class drives surface creation, per-frame texture acquisition, and
/// swapchain reconfiguration.
/// </para>
/// <para>
/// All handle fields are non-null after successful construction. <see cref="Dispose"/> releases them in reverse
/// acquisition order.
/// </para>
/// </remarks>
/// <typeparam name="TPixel">The canvas pixel format. Must map to a WebGPU texture format that
/// <see cref="WebGPUDrawingBackend.TryGetCompositeTextureFormat{TPixel}(out WebGPUTextureFormatId, out FeatureName)"/>
/// recognizes, otherwise <see cref="Create"/> throws <see cref="NotSupportedException"/>.</typeparam>
internal sealed unsafe class WebGPUWindowResources<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <summary>
    /// Upper bound for the asynchronous adapter and device request callbacks. Exceeding this throws.
    /// </summary>
    private const int CallbackTimeoutMilliseconds = 10_000;

    private bool isDisposed;
    private bool frameInFlight;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUWindowResources{TPixel}"/> class with already-acquired handles.
    /// Only invoked by <see cref="Create"/> after every handle has been successfully bootstrapped.
    /// </summary>
    /// <param name="api">The shared WebGPU API loader.</param>
    /// <param name="instanceHandle">The owned WebGPU instance handle.</param>
    /// <param name="surfaceHandle">The owned WebGPU surface handle attached to the hosting native window.</param>
    /// <param name="adapterHandle">The owned adapter handle selected for <paramref name="surfaceHandle"/>.</param>
    /// <param name="deviceHandle">The owned device handle requested from <paramref name="adapterHandle"/>.</param>
    /// <param name="queueHandle">The owned default queue handle paired with <paramref name="deviceHandle"/>.</param>
    /// <param name="graphics">The drawing context bound to <paramref name="deviceHandle"/> and <paramref name="queueHandle"/>.</param>
    /// <param name="format">The negotiated swapchain texture format for <typeparamref name="TPixel"/>.</param>
    private WebGPUWindowResources(
        WebGPU api,
        WebGPUInstanceHandle instanceHandle,
        WebGPUSurfaceHandle surfaceHandle,
        WebGPUAdapterHandle adapterHandle,
        WebGPUDeviceHandle deviceHandle,
        WebGPUQueueHandle queueHandle,
        WebGPUDeviceContext<TPixel> graphics,
        WebGPUTextureFormatId format)
    {
        this.Api = api;
        this.InstanceHandle = instanceHandle;
        this.SurfaceHandle = surfaceHandle;
        this.AdapterHandle = adapterHandle;
        this.DeviceHandle = deviceHandle;
        this.QueueHandle = queueHandle;
        this.Graphics = graphics;
        this.Format = format;
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
    /// Gets the WebGPU surface attached to the hosting native window. The surface is reconfigured whenever
    /// <see cref="ConfigureSurface"/> runs and released on <see cref="Dispose"/>.
    /// </summary>
    public WebGPUSurfaceHandle SurfaceHandle { get; }

    /// <summary>
    /// Gets the adapter selected for the current surface. Released on <see cref="Dispose"/>.
    /// </summary>
    public WebGPUAdapterHandle AdapterHandle { get; }

    /// <summary>
    /// Gets the logical device requested from <see cref="AdapterHandle"/>. Released on <see cref="Dispose"/>.
    /// </summary>
    public WebGPUDeviceHandle DeviceHandle { get; }

    /// <summary>
    /// Gets the default queue for <see cref="DeviceHandle"/>. Released on <see cref="Dispose"/>.
    /// </summary>
    public WebGPUQueueHandle QueueHandle { get; }

    /// <summary>
    /// Gets the drawing context bound to <see cref="DeviceHandle"/>/<see cref="QueueHandle"/>, used to wrap acquired
    /// per-frame textures into <see cref="DrawingCanvas{TPixel}"/> instances.
    /// </summary>
    public WebGPUDeviceContext<TPixel> Graphics { get; }

    /// <summary>
    /// Gets the swapchain texture format chosen for <typeparamref name="TPixel"/> at construction time.
    /// Stable for the lifetime of this instance.
    /// </summary>
    public WebGPUTextureFormatId Format { get; }

    /// <summary>
    /// Bootstraps the full per-window WebGPU stack bound to <paramref name="nativeSource"/> and leaves the surface
    /// configured against <paramref name="initialPresentMode"/> and <paramref name="initialFramebufferSize"/>.
    /// </summary>
    /// <param name="configuration">The ImageSharp configuration the drawing context will use for its rendering backend.</param>
    /// <param name="nativeSource">The native window source that provides the platform handles for surface creation.
    /// Supplied by the owning window type (a Silk <c>IWindow</c> for <see cref="WebGPUWindow{TPixel}"/>, a
    /// <see cref="SilkNativeWindowAdapter"/> for <see cref="WebGPUHostedWindow{TPixel}"/>).</param>
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
    public static WebGPUWindowResources<TPixel> Create(
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
        Adapter* adapter = null;
        WebGPUInstanceHandle? instanceHandle = null;
        WebGPUSurfaceHandle? surfaceHandle = null;
        WebGPUAdapterHandle? adapterHandle = null;
        WebGPUDeviceHandle? deviceHandle = null;
        WebGPUQueueHandle? queueHandle = null;
        WebGPUDeviceContext<TPixel>? graphics = null;

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
            if (!TryRequestAdapter(api, instance, surface, out adapter, out string? adapterError))
            {
                throw new InvalidOperationException(adapterError);
            }

            adapterHandle = new WebGPUAdapterHandle(api, (nint)adapter, ownsHandle: true);
            if (!TryRequestDevice(api, adapter, requiredFeature, out Device* device, out string? deviceError))
            {
                throw new InvalidOperationException(deviceError);
            }

            Queue* queue = api.DeviceGetQueue(device);
            if (queue is null)
            {
                throw new InvalidOperationException("WebGPU queue acquisition failed.");
            }

            deviceHandle = new WebGPUDeviceHandle(api, (nint)device, ownsHandle: true);
            queueHandle = new WebGPUQueueHandle(api, (nint)queue, ownsHandle: true);
            graphics = new WebGPUDeviceContext<TPixel>(configuration, deviceHandle, queueHandle);

            WebGPUWindowResources<TPixel> resources = new(
                api,
                instanceHandle,
                surfaceHandle,
                adapterHandle,
                deviceHandle,
                queueHandle,
                graphics,
                format);

            resources.ConfigureSurface(initialPresentMode, initialFramebufferSize);
            return resources;
        }
        catch
        {
            graphics?.Dispose();
            queueHandle?.Dispose();
            deviceHandle?.Dispose();
            adapterHandle?.Dispose();
            surfaceHandle?.Dispose();
            instanceHandle?.Dispose();

            if (adapterHandle is null && adapter is not null)
            {
                api.AdapterRelease(adapter);
            }

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

        SurfaceConfiguration surfaceConfiguration = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopyDst | TextureUsage.TextureBinding,
            Format = WebGPUTextureFormatMapper.ToSilk(this.Format),
            PresentMode = (SilkPresentMode)(int)presentMode,
            Width = (uint)framebufferSize.Width,
            Height = (uint)framebufferSize.Height,
        };

        using WebGPUHandle.HandleReference surfaceReference = this.SurfaceHandle.AcquireReference();
        using WebGPUHandle.HandleReference deviceReference = this.DeviceHandle.AcquireReference();
        surfaceConfiguration.Device = (Device*)deviceReference.Handle;
        this.Api.SurfaceConfigure((Surface*)surfaceReference.Handle, ref surfaceConfiguration);
    }

    /// <summary>
    /// Acquires the next presentable frame from <see cref="SurfaceHandle"/> and wraps it as a
    /// <see cref="WebGPUWindowFrame{TPixel}"/> with a ready-to-use <see cref="DrawingCanvas{TPixel}"/>.
    /// </summary>
    /// <param name="presentMode">The present mode applied when the surface needs to be reconfigured in response to a
    /// <c>Timeout</c>/<c>Outdated</c>/<c>Lost</c> acquire status.</param>
    /// <param name="clientSize">The current client-area size in pixels, reported verbatim on the returned frame.</param>
    /// <param name="framebufferSize">The current framebuffer size in pixels. A zero-area value causes the method to
    /// return <see langword="false"/> without touching the surface. Otherwise this size is used both for the returned
    /// frame bounds and for any in-place surface reconfiguration triggered by a non-success acquire status.</param>
    /// <param name="deltaTime">The elapsed time since the previous frame, reported verbatim on the returned frame.</param>
    /// <param name="frameIndex">The frame index, reported verbatim on the returned frame.</param>
    /// <param name="options">The drawing options that seed the canvas on the returned frame.</param>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns><see langword="true"/> when a frame is available; <see langword="false"/> when no drawable frame is
    /// available right now (zero-area framebuffer, or the surface was timed out / outdated / lost and has been
    /// transparently reconfigured for the next attempt).</returns>
    /// <exception cref="InvalidOperationException">Thrown on unrecoverable acquire status values
    /// (<c>OutOfMemory</c>, <c>DeviceLost</c>), or when texture-view creation fails for an otherwise valid surface texture.</exception>
    public bool TryAcquireFrame(
        WebGPUPresentMode presentMode,
        Size clientSize,
        Size framebufferSize,
        TimeSpan deltaTime,
        long frameIndex,
        DrawingOptions options,
        [NotNullWhen(true)] out WebGPUWindowFrame<TPixel>? frame)
    {
        frame = null;

        if (framebufferSize.Width <= 0 || framebufferSize.Height <= 0)
        {
            return false;
        }

        // Reject acquire while a previously-issued frame is still outstanding. wgpu-native's surface
        // state machine doesn't tolerate overlapping acquires, and some hosts (WinForms DWM pumping
        // during SurfacePresent, Silk message loops calling into user callbacks) can dispatch a paint
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

            case SurfaceGetCurrentTextureStatus.OutOfMemory:
            case SurfaceGetCurrentTextureStatus.DeviceLost:
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
                textureHandle,
                textureViewHandle,
                this.Format,
                framebufferSize.Width,
                framebufferSize.Height,
                options);

            this.frameInFlight = true;
            frame = new WebGPUWindowFrame<TPixel>(
                this.Api,
                this.SurfaceHandle,
                textureHandle,
                textureViewHandle,
                canvas,
                new Rectangle(0, 0, framebufferSize.Width, framebufferSize.Height),
                clientSize,
                framebufferSize,
                deltaTime,
                frameIndex,
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
    /// Requests a high-performance adapter compatible with <paramref name="surface"/> and waits synchronously on the
    /// asynchronous callback, up to <see cref="CallbackTimeoutMilliseconds"/>.
    /// </summary>
    /// <param name="api">The shared WebGPU API loader.</param>
    /// <param name="instance">The WebGPU instance the adapter is requested from.</param>
    /// <param name="surface">The surface the adapter must be compatible with.</param>
    /// <param name="adapter">Receives the requested adapter on success.</param>
    /// <param name="error">Receives the failure reason when the adapter request cannot complete.</param>
    /// <returns><see langword="true"/> when the adapter was returned by the native callback; otherwise <see langword="false"/>.</returns>
    private static bool TryRequestAdapter(
        WebGPU api,
        Instance* instance,
        Surface* surface,
        out Adapter* adapter,
        out string? error)
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
            adapter = null;
            error = "Timed out while waiting for the WebGPU adapter request callback.";
            return false;
        }

        adapter = callbackAdapter;
        if (callbackStatus != RequestAdapterStatus.Success || callbackAdapter is null)
        {
            error = $"WebGPU adapter request failed with status '{callbackStatus}'.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Requests a device from <paramref name="adapter"/> with <paramref name="requiredFeature"/> enabled (when not
    /// <see cref="FeatureName.Undefined"/>) and waits synchronously on the asynchronous callback, up to
    /// <see cref="CallbackTimeoutMilliseconds"/>.
    /// </summary>
    /// <param name="api">The shared WebGPU API loader.</param>
    /// <param name="adapter">The adapter the device is requested from.</param>
    /// <param name="requiredFeature">The feature to enable on the requested device, or <see cref="FeatureName.Undefined"/> for none.</param>
    /// <param name="device">Receives the requested device on success.</param>
    /// <param name="error">Receives the failure reason when the device request cannot complete.</param>
    /// <returns><see langword="true"/> when the device was returned by the native callback; otherwise <see langword="false"/>.</returns>
    private static bool TryRequestDevice(
        WebGPU api,
        Adapter* adapter,
        FeatureName requiredFeature,
        out Device* device,
        out string? error)
    {
        if (requiredFeature != FeatureName.Undefined && !api.AdapterHasFeature(adapter, requiredFeature))
        {
            device = null;
            error = $"The selected adapter does not support required feature '{requiredFeature}'.";
            return false;
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
            device = null;
            error = "Timed out while waiting for the WebGPU device request callback.";
            return false;
        }

        device = callbackDevice;
        if (callbackStatus != RequestDeviceStatus.Success || callbackDevice is null)
        {
            error = $"WebGPU device request failed with status '{callbackStatus}'.";
            return false;
        }

        error = null;
        return true;
    }
}
