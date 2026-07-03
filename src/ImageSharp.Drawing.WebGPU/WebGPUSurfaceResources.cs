// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.Core.Contexts;
using Silk.NET.WebGPU;
using SilkPresentMode = Silk.NET.WebGPU.PresentMode;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Owning container for the per-surface WebGPU stack: instance, surface, adapter, device, queue, device context,
/// and the negotiated swapchain texture format.
/// </summary>
/// <remarks>
/// <para>
/// Provides a single static <see cref="Create"/> factory that bootstraps every handle in order and leaves the surface
/// initially configured against the requested present mode and framebuffer size.
/// Callers hold the returned instance for the lifetime of the rendering surface and dispose it when the surface tears down.
/// </para>
/// <para>
/// Shared by the owned-window and external-surface entry points. Both provide a native surface source while this class owns the WebGPU
/// handles, surface creation, per-frame texture acquisition, and swapchain reconfiguration.
/// </para>
/// <para>
/// All handle fields are non-null after successful construction. <see cref="Dispose"/> releases them in reverse
/// acquisition order.
/// </para>
/// </remarks>
internal sealed unsafe class WebGPUSurfaceResources : IDisposable
{
    /// <summary>
    /// Upper bound for the asynchronous adapter and device request callbacks. Exceeding this throws.
    /// </summary>
    private const int CallbackTimeoutMilliseconds = 10_000;

    private bool isDisposed;

    // True while an acquired WebGPUSurfaceFrame has not yet been disposed. Guards TryAcquireFrame
    // against overlapping acquires; cleared by the frame's onDisposed callback.
    private bool frameInFlight;

    // Work-done signal registered when a frame is presented. Waiting on it at the next acquire
    // caps the CPU at one frame in flight: without a governor the CPU submits ahead until the
    // driver throttles it inside queue submit at unpredictable mid-frame points, which paces
    // worse than one deliberate wait at the frame boundary.
    private FramePacingSignal? previousFrameWorkDone;
    private readonly FeatureName requiredFeature;
    private readonly Configuration configuration;

    // Mutable because the device-owned portion of the stack is replaced on device-loss recovery.
    private WebGPUDeviceContext deviceContext;

    // Rooted so the native callbacks are not collected while wgpu holds them.
    private static readonly PfnDeviceLostCallback DeviceLostCallbackRoot = PfnDeviceLostCallback.From(HandleDeviceLost);
    private static readonly PfnErrorCallback UncapturedErrorCallbackRoot = PfnErrorCallback.From(HandleUncapturedError);

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSurfaceResources"/> class with already-acquired handles.
    /// Only invoked by <see cref="Create"/> after every handle has been successfully bootstrapped.
    /// </summary>
    /// <param name="api">The shared WebGPU API loader.</param>
    /// <param name="configuration">The ImageSharp configuration the device context uses for its rendering backend.</param>
    /// <param name="instanceHandle">The owned WebGPU instance handle.</param>
    /// <param name="surfaceHandle">The owned WebGPU surface handle attached to the native host.</param>
    /// <param name="adapterHandle">The owned adapter handle selected for <paramref name="surfaceHandle"/>.</param>
    /// <param name="deviceHandle">The owned device handle requested from <paramref name="adapterHandle"/>.</param>
    /// <param name="queueHandle">The owned default queue handle paired with <paramref name="deviceHandle"/>.</param>
    /// <param name="deviceContext">The device context bound to <paramref name="deviceHandle"/> and <paramref name="queueHandle"/>.</param>
    /// <param name="format">The negotiated swapchain texture format.</param>
    /// <param name="requiredFeature">The optional WebGPU feature required by the selected texture format.</param>
    private WebGPUSurfaceResources(
        WebGPU api,
        Configuration configuration,
        WebGPUInstanceHandle instanceHandle,
        WebGPUSurfaceHandle surfaceHandle,
        WebGPUAdapterHandle adapterHandle,
        WebGPUDeviceHandle deviceHandle,
        WebGPUQueueHandle queueHandle,
        WebGPUDeviceContext deviceContext,
        WebGPUTextureFormat format,
        FeatureName requiredFeature)
    {
        this.Api = api;
        this.configuration = configuration;
        this.InstanceHandle = instanceHandle;
        this.SurfaceHandle = surfaceHandle;
        this.AdapterHandle = adapterHandle;
        this.DeviceHandle = deviceHandle;
        this.QueueHandle = queueHandle;
        this.deviceContext = deviceContext;
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
    /// Gets the swapchain texture format chosen at construction time.
    /// Stable for the lifetime of this instance.
    /// </summary>
    public WebGPUTextureFormat Format { get; }

    /// <summary>
    /// Bootstraps the full per-surface WebGPU stack bound to <paramref name="nativeSource"/> and leaves the surface
    /// configured against <paramref name="initialPresentMode"/> and <paramref name="initialFramebufferSize"/>.
    /// </summary>
    /// <param name="configuration">The ImageSharp configuration the device context will use for its rendering backend.</param>
    /// <param name="nativeSource">The native surface source that provides the platform handles for surface creation.</param>
    /// <param name="format">The swapchain texture format.</param>
    /// <param name="initialPresentMode">The present mode to apply to the first surface configuration.</param>
    /// <param name="initialFramebufferSize">The framebuffer size to apply to the first surface configuration. Zero-area
    /// sizes are permitted and leave the surface unconfigured until the caller invokes <see cref="ConfigureSurface"/>
    /// with a positive size.</param>
    /// <returns>The fully initialized resource container. The caller owns it and must call <see cref="Dispose"/>.</returns>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="format"/> is not supported by the WebGPU backend.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when any of the underlying WebGPU bootstrap steps fail. All partially-acquired handles are released
    /// before the exception propagates.
    /// </exception>
    public static WebGPUSurfaceResources Create(
        Configuration configuration,
        INativeWindowSource nativeSource,
        WebGPUTextureFormat format,
        WebGPUPresentMode initialPresentMode,
        Size initialFramebufferSize)
    {
        WebGPUDrawingBackend.GetCompositeTextureFormatInfo(format, out _, out FeatureName requiredFeature);

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

            WebGPUSurfaceResources resources = new(
                api,
                configuration,
                instanceHandle,
                surfaceHandle,
                deviceResources.AdapterHandle,
                deviceResources.DeviceHandle,
                deviceResources.QueueHandle,
                deviceResources.DeviceContext,
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

            // Once a raw pointer has been wrapped in an owning handle the disposal above releases it.
            // The direct releases below only cover the window where creation succeeded but wrapping did not.
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
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding,
            Format = WebGPUTextureFormatMapper.ToNative(this.Format),
            PresentMode = ToNative(presentMode),
            Width = (uint)framebufferSize.Width,
            Height = (uint)framebufferSize.Height,
        };

        using WebGPUHandle.HandleReference surfaceReference = this.SurfaceHandle.AcquireReference();
        using WebGPUHandle.HandleReference deviceReference = deviceHandle.AcquireReference();
        surfaceConfiguration.Device = (Device*)deviceReference.Handle;
        this.Api.SurfaceConfigure((Surface*)surfaceReference.Handle, ref surfaceConfiguration);
    }

    /// <summary>
    /// Maps the public present-mode enumeration to the native Silk.NET value.
    /// </summary>
    /// <param name="presentMode">The present mode to map.</param>
    /// <returns>The native present mode.</returns>
    private static SilkPresentMode ToNative(WebGPUPresentMode presentMode)
        => presentMode switch
        {
            WebGPUPresentMode.Fifo => SilkPresentMode.Fifo,
            WebGPUPresentMode.Immediate => SilkPresentMode.Immediate,
            WebGPUPresentMode.Mailbox => SilkPresentMode.Mailbox,
            _ => throw new InvalidOperationException("The WebGPU present mode mapping is incomplete.")
        };

    /// <summary>
    /// Acquires the next presentable frame from <see cref="SurfaceHandle"/> and wraps it as a
    /// <see cref="WebGPUSurfaceFrame"/> with a ready-to-use <see cref="DrawingCanvas"/>.
    /// </summary>
    /// <param name="presentMode">The present mode applied when the surface needs to be reconfigured in response to a
    /// <c>Timeout</c>/<c>Outdated</c>/<c>Lost</c> acquire status.</param>
    /// <param name="framebufferSize">The current framebuffer size in pixels. A zero-area value causes the method to
    /// return <see langword="false"/> without touching the surface. Otherwise this size is used for the returned
    /// frame canvas and for any in-place surface reconfiguration triggered by a non-success acquire status.</param>
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
        [NotNullWhen(true)] out WebGPUSurfaceFrame? frame)
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

        // Frame pacing: block here until the previously presented frame's GPU work completes.
        // This is the one deliberate CPU-GPU synchronization point per frame; every flush-side
        // readback is deferred, so this wait alone bounds how far the CPU runs ahead.
        this.WaitForPreviousFrameWorkDone();

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
            DrawingCanvas canvas = this.deviceContext.CreateCanvas(
                options,
                textureHandle,
                textureViewHandle,
                this.Format,
                framebufferSize.Width,
                framebufferSize.Height);

            frame = new WebGPUSurfaceFrame(
                this.Api,
                this.deviceContext,
                this.Format,
                this.SurfaceHandle,
                textureHandle,
                textureViewHandle,
                canvas,
                onDisposed: () =>
                {
                    // The frame has just been submitted and presented; register the pacing
                    // signal its successor will wait on, then allow the next acquire.
                    this.RegisterFramePacingSignal();
                    this.frameInFlight = false;
                });

            // Mark in-flight only once the frame exists. Setting the guard before construction
            // would leave it stuck on a constructor throw, wedging every subsequent acquire.
            this.frameInFlight = true;

            return true;
        }
        catch
        {
            textureViewHandle?.Dispose();
            textureHandle?.Dispose();

            // Raw pointers are released directly only when wrapping into an owning handle never happened.
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
    /// Waits (bounded) for the previously presented frame's GPU work to complete, then retires
    /// the signal. No-op when no frame has been presented since the last wait.
    /// </summary>
    private void WaitForPreviousFrameWorkDone()
    {
        FramePacingSignal? signal = this.previousFrameWorkDone;
        if (signal is null)
        {
            return;
        }

        this.previousFrameWorkDone = null;
        using (WebGPUHandle.HandleReference deviceReference = this.DeviceHandle.AcquireReference())
        {
            _ = signal.Wait(WebGPURuntime.GetWgpuExtension(), (Device*)deviceReference.Handle);
        }

        signal.Dispose();
    }

    /// <summary>
    /// Registers a queue work-done signal covering everything submitted for the frame that was
    /// just presented. The next acquire waits on it. Failure to register (for example during
    /// device loss) simply skips pacing for one frame.
    /// </summary>
    private void RegisterFramePacingSignal()
    {
        if (this.isDisposed || this.QueueHandle.IsInvalid)
        {
            return;
        }

        // A previous signal should already have been consumed at acquire; retire a leftover
        // (an acquire that never happened) before installing the new one.
        this.WaitForPreviousFrameWorkDone();

        try
        {
            using WebGPUHandle.HandleReference queueReference = this.QueueHandle.AcquireReference();
            this.previousFrameWorkDone = new FramePacingSignal(this.Api, (Queue*)queueReference.Handle);
        }
        catch (ObjectDisposedException)
        {
            // The queue was torn down between the check and the acquire; skip pacing.
        }
    }

    /// <summary>
    /// Releases every owned handle in reverse acquisition order (device context, queue, device, adapter, surface, instance).
    /// Idempotent; subsequent calls are no-ops.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        // Retire the pacing signal before the device goes away so its pinned callback thunk
        // is never freed while the native side could still invoke it.
        this.WaitForPreviousFrameWorkDone();

        this.deviceContext.Dispose();
        this.QueueHandle.Dispose();
        this.DeviceHandle.Dispose();
        this.AdapterHandle.Dispose();
        this.SurfaceHandle.Dispose();
        this.InstanceHandle.Dispose();
        this.isDisposed = true;
    }

    /// <summary>
    /// Requests the adapter, device, queue, and device context for an existing surface.
    /// </summary>
    /// <param name="api">The shared WebGPU API loader.</param>
    /// <param name="configuration">The ImageSharp configuration the device context will use.</param>
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
        WebGPUDeviceContext? deviceContext = null;

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
            deviceContext = new WebGPUDeviceContext(configuration, deviceHandle, queueHandle);

            DeviceResources resources = new(adapterHandle, deviceHandle, queueHandle, deviceContext);
            adapterHandle = null;
            deviceHandle = null;
            queueHandle = null;
            deviceContext = null;
            return resources;
        }
        catch
        {
            deviceContext?.Dispose();
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
    /// The existing instance and surface remain valid; only adapter, device, queue, and device context are replaced.
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

            // Publish the new stack before disposing the old one so the fields never point at
            // disposed handles, even if releasing the old resources throws.
            WebGPUDeviceContext oldDeviceContext = this.deviceContext;
            WebGPUQueueHandle oldQueueHandle = this.QueueHandle;
            WebGPUDeviceHandle oldDeviceHandle = this.DeviceHandle;
            WebGPUAdapterHandle oldAdapterHandle = this.AdapterHandle;

            this.deviceContext = deviceResources.DeviceContext;
            this.QueueHandle = deviceResources.QueueHandle;
            this.DeviceHandle = deviceResources.DeviceHandle;
            this.AdapterHandle = deviceResources.AdapterHandle;
            deviceResources = null;

            oldDeviceContext.Dispose();
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
    /// Reports a native device-lost callback through the managed WebGPU error callback.
    /// </summary>
    /// <param name="reason">The native device-lost reason.</param>
    /// <param name="message">The optional UTF-8 message supplied by the runtime; may be null.</param>
    /// <param name="userData">Unused native user data.</param>
    private static void HandleDeviceLost(DeviceLostReason reason, byte* message, void* userData)
    {
        string text = message is null
            ? string.Empty
            : System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)message) ?? string.Empty;
        WebGPUEnvironment.ReportUncapturedError(WebGPUErrorType.DeviceLost, $"Device lost ({reason}): {text}");
    }

    /// <summary>
    /// Reports a native uncaptured-error callback through the managed WebGPU error callback.
    /// </summary>
    /// <param name="type">The native error type.</param>
    /// <param name="message">The optional UTF-8 message supplied by the runtime; may be null.</param>
    /// <param name="userData">Unused native user data.</param>
    private static void HandleUncapturedError(ErrorType type, byte* message, void* userData)
    {
        string text = message is null
            ? string.Empty
            : System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)message) ?? string.Empty;
        WebGPUEnvironment.ReportUncapturedError(WebGPUErrorType.Validation, $"Surface device error ({type}): {text}");
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
        FeatureName requestedFeature = requiredFeature;
        DeviceDescriptor descriptor = default;
        if (requiredFeature != FeatureName.Undefined)
        {
            descriptor.RequiredFeatureCount = 1;
            descriptor.RequiredFeatures = &requestedFeature;
        }

        // A device loss (TDR/hang/removal) is reported through this callback, NOT the uncaptured
        // error or log callbacks, so without it the loss is invisible and only surfaces later as a
        // panic on the next submit. Route the reason and message through the environment callback.
        descriptor.DeviceLostCallback = DeviceLostCallbackRoot;
        api.AdapterRequestDevice(adapter, in descriptor, callbackPtr, null);
        if (!callbackReady.Wait(CallbackTimeoutMilliseconds))
        {
            throw new InvalidOperationException("Timed out while waiting for the WebGPU device request callback.");
        }

        if (callbackStatus != RequestDeviceStatus.Success || callbackDevice is null)
        {
            throw new InvalidOperationException($"The WebGPU runtime failed to acquire a device. Status: '{callbackStatus}'.");
        }

        // The surface device renders the application, but unlike the shared runtime device it had no
        // uncaptured-error callback, so validation errors on the render device were silently dropped.
        // Wire it here so the root error that loses the device is logged before the submit panic.
        api.DeviceSetUncapturedErrorCallback(callbackDevice, UncapturedErrorCallbackRoot, null);

        return callbackDevice;
    }

    /// <summary>
    /// A pinned queue work-done callback and its completion event for one presented frame.
    /// The pinned thunk stays alive until the callback fires (or the bounded wait gives up),
    /// mirroring the deferred readback's callback lifetime rules.
    /// </summary>
    private sealed class FramePacingSignal : IDisposable
    {
        /// <summary>
        /// Set by the work-done callback once the frame's submissions have completed.
        /// </summary>
        private readonly ManualResetEventSlim workDone = new(false);

        /// <summary>
        /// The pinned work-done callback thunk; must stay alive until the callback fires.
        /// </summary>
        private readonly PfnQueueWorkDoneCallback callback;

        /// <summary>
        /// Initializes a new instance of the <see cref="FramePacingSignal"/> class and registers
        /// the work-done callback for everything submitted to the queue so far.
        /// </summary>
        /// <param name="api">The WebGPU API used to register the callback.</param>
        /// <param name="queue">The queue whose submitted work the signal covers.</param>
        public FramePacingSignal(WebGPU api, Queue* queue)
        {
            this.callback = PfnQueueWorkDoneCallback.From(this.OnWorkDone);
            api.QueueOnSubmittedWorkDone(queue, this.callback, null);
        }

        /// <summary>
        /// Waits (bounded) for the work-done callback, pumping the device when the native wgpu
        /// extension requires explicit polling for callback delivery.
        /// </summary>
        /// <param name="extension">The optional native WGPU extension used to pump callbacks.</param>
        /// <param name="device">The device that owns the queue.</param>
        /// <returns><see langword="true"/> when the callback completed before the timeout; otherwise, <see langword="false"/>.</returns>
        public bool Wait(Silk.NET.WebGPU.Extensions.WGPU.Wgpu? extension, Device* device)
        {
            if (extension is null)
            {
                return this.workDone.Wait(CallbackTimeoutMilliseconds);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!this.workDone.IsSet && stopwatch.ElapsedMilliseconds < CallbackTimeoutMilliseconds)
            {
                _ = extension.DevicePoll(device, true, (Silk.NET.WebGPU.Extensions.WGPU.WrappedSubmissionIndex*)null);
            }

            return this.workDone.IsSet;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.callback.Dispose();
            this.workDone.Dispose();
        }

        /// <summary>
        /// The queue work-done callback; any status counts as completion for pacing purposes.
        /// </summary>
        /// <param name="status">The completion status reported by the implementation.</param>
        /// <param name="userData">Unused user data pointer.</param>
        private void OnWorkDone(QueueWorkDoneStatus status, void* userData)
        {
            _ = status;
            _ = userData;
            this.workDone.Set();
        }
    }

    /// <summary>
    /// Owns one acquired adapter/device/queue/context set while it is being transferred into the surface resources.
    /// </summary>
    private sealed class DeviceResources : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceResources"/> class taking ownership of the supplied handles.
        /// </summary>
        /// <param name="adapterHandle">The owned adapter handle.</param>
        /// <param name="deviceHandle">The owned device handle.</param>
        /// <param name="queueHandle">The owned queue handle.</param>
        /// <param name="deviceContext">The owned device context.</param>
        public DeviceResources(
            WebGPUAdapterHandle adapterHandle,
            WebGPUDeviceHandle deviceHandle,
            WebGPUQueueHandle queueHandle,
            WebGPUDeviceContext deviceContext)
        {
            this.AdapterHandle = adapterHandle;
            this.DeviceHandle = deviceHandle;
            this.QueueHandle = queueHandle;
            this.DeviceContext = deviceContext;
        }

        /// <summary>
        /// Gets the owned adapter handle.
        /// </summary>
        public WebGPUAdapterHandle AdapterHandle { get; }

        /// <summary>
        /// Gets the owned device handle.
        /// </summary>
        public WebGPUDeviceHandle DeviceHandle { get; }

        /// <summary>
        /// Gets the owned queue handle.
        /// </summary>
        public WebGPUQueueHandle QueueHandle { get; }

        /// <summary>
        /// Gets the owned device context bound to <see cref="DeviceHandle"/> and <see cref="QueueHandle"/>.
        /// </summary>
        internal WebGPUDeviceContext DeviceContext { get; }

        /// <summary>
        /// Releases the owned resources in reverse acquisition order. Only invoked when the transfer
        /// into the surface resources did not complete.
        /// </summary>
        public void Dispose()
        {
            this.DeviceContext.Dispose();
            this.QueueHandle.Dispose();
            this.DeviceHandle.Dispose();
            this.AdapterHandle.Dispose();
        }
    }
}
