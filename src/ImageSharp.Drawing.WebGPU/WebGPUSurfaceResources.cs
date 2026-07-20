// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.Core.Contexts;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Owns one native presentation surface and its negotiated swapchain state.
/// </summary>
/// <remarks>
/// <para>
/// Provides static factory methods that attach a native surface to a shared
/// <see cref="WebGPUSurfaceSession"/> and leaves it initially configured.
/// Callers hold the returned instance for the lifetime of the rendering surface and dispose it when the surface tears down.
/// </para>
/// <para>
/// Shared by the owned-window and external-surface entry points. Both provide a native surface source while this class owns surface
/// creation, per-frame texture acquisition, and swapchain reconfiguration.
/// </para>
/// <para>
/// The adapter, device, queue, drawing context, and compiled pipelines belong to the session and outlive this surface.
/// </para>
/// </remarks>
internal sealed unsafe class WebGPUSurfaceResources : IDisposable
{
    // WebGPU guarantees render-attachment support for presentable textures. CopyDst is optional,
    // but enables the native-copy presentation path when the surface reports it.
    private const TextureUsage RequiredSurfaceUsage = TextureUsage.RenderAttachment;

    /// <summary>
    /// Upper bound for polling a presented frame's queue-completion callback.
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
    private readonly WebGPUSurfaceSession session;
    private readonly TextureUsage surfaceUsage;
    private WebGPURenderTarget? presentationTarget;
    private WebGPUPresentationRenderer? presentationRenderer;
    private readonly uint supportedPresentModes;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSurfaceResources"/> class with an acquired surface.
    /// </summary>
    /// <param name="api">The shared WebGPU API loader.</param>
    /// <param name="session">The session that owns the shared device resources.</param>
    /// <param name="surfaceHandle">The owned WebGPU surface handle attached to the native host.</param>
    /// <param name="format">The negotiated swapchain texture format.</param>
    /// <param name="alphaMode">How the native compositor interprets the swapchain alpha channel.</param>
    /// <param name="presentMode">The negotiated swapchain presentation mode.</param>
    /// <param name="supportedPresentModes">A bit field containing the presentation modes supported by the surface.</param>
    /// <param name="surfaceUsage">The texture usages selected from the capabilities reported by the surface.</param>
    private WebGPUSurfaceResources(
        WebGPU api,
        WebGPUSurfaceSession session,
        WebGPUSurfaceHandle surfaceHandle,
        WebGPUTextureFormat format,
        WebGPUCompositeAlphaMode alphaMode,
        WebGPUPresentMode presentMode,
        uint supportedPresentModes,
        TextureUsage surfaceUsage)
    {
        this.Api = api;
        this.session = session;
        this.SurfaceHandle = surfaceHandle;
        this.Format = format;
        this.AlphaMode = alphaMode;
        this.PresentMode = presentMode;
        this.supportedPresentModes = supportedPresentModes;
        this.surfaceUsage = surfaceUsage;
    }

    /// <summary>
    /// Gets the shared WebGPU API loader used for every native call made through this stack.
    /// </summary>
    public WebGPU Api { get; }

    /// <summary>
    /// Gets the WebGPU surface attached to the native host. The surface is reconfigured whenever
    /// <see cref="ConfigureSurface"/> runs and released on <see cref="Dispose"/>.
    /// </summary>
    public WebGPUSurfaceHandle SurfaceHandle { get; }

    /// <summary>
    /// Gets the negotiated swapchain texture format.
    /// </summary>
    public WebGPUTextureFormat Format { get; private set; }

    /// <summary>
    /// Gets the negotiated composite alpha mode.
    /// </summary>
    public WebGPUCompositeAlphaMode AlphaMode { get; }

    /// <summary>
    /// Gets the texture format and alpha representation negotiated for acquired frames.
    /// </summary>
    public WebGPUTargetDescriptor TargetDescriptor
        => WebGPUDrawingBackend.CreateNativeTargetDescriptor(
            this.Format,
            this.AlphaMode == WebGPUCompositeAlphaMode.Premultiplied ? PixelAlphaRepresentation.Associated : PixelAlphaRepresentation.Unassociated);

    /// <summary>
    /// Gets the device context owned by the surface session.
    /// </summary>
    public WebGPUDeviceContext DeviceContext => this.session.DeviceContext;

    /// <summary>
    /// Gets the negotiated swapchain presentation mode.
    /// </summary>
    public WebGPUPresentMode PresentMode { get; private set; }

    /// <summary>
    /// Creates a native presentation surface bound to <paramref name="session"/> and leaves it configured against
    /// <paramref name="initialPresentMode"/> and <paramref name="initialFramebufferSize"/>.
    /// </summary>
    /// <param name="session">The session that owns the shared adapter, device, queue, and drawing context.</param>
    /// <param name="nativeSource">The native surface source that provides the platform handles for surface creation.</param>
    /// <param name="format">The swapchain texture format.</param>
    /// <param name="alphaMode">How the native compositor interprets the swapchain alpha channel.</param>
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
        WebGPUSurfaceSession session,
        INativeWindowSource nativeSource,
        WebGPUTextureFormat format,
        WebGPUCompositeAlphaMode alphaMode,
        WebGPUPresentMode initialPresentMode,
        Size initialFramebufferSize)
    {
        WebGPU api = session.Api;
        WGPUInstanceImpl* instance = WebGPURuntime.GetOrCreateSharedInstance();
        WGPUSurfaceImpl* surface = WebGPUSurfaceFactory.Create(api, instance, nativeSource);
        return Create(session, surface, format, alphaMode, initialPresentMode, initialFramebufferSize);
    }

    /// <summary>
    /// Creates a native presentation surface for an externally-owned host.
    /// </summary>
    /// <param name="session">The session that owns the shared adapter, device, queue, and drawing context.</param>
    /// <param name="host">The native surface host.</param>
    /// <param name="format">The swapchain texture format.</param>
    /// <param name="alphaMode">How the native compositor interprets the swapchain alpha channel.</param>
    /// <param name="initialPresentMode">The present mode to apply to the first surface configuration.</param>
    /// <param name="initialFramebufferSize">The initial framebuffer size.</param>
    /// <returns>The fully initialized resource container.</returns>
    public static WebGPUSurfaceResources Create(
        WebGPUSurfaceSession session,
        WebGPUSurfaceHost host,
        WebGPUTextureFormat format,
        WebGPUCompositeAlphaMode alphaMode,
        WebGPUPresentMode initialPresentMode,
        Size initialFramebufferSize)
    {
        WebGPU api = session.Api;
        WGPUInstanceImpl* instance = WebGPURuntime.GetOrCreateSharedInstance();
        WGPUSurfaceImpl* surface = WebGPUSurfaceFactory.Create(api, instance, host);
        return Create(session, surface, format, alphaMode, initialPresentMode, initialFramebufferSize);
    }

    /// <summary>
    /// Completes surface negotiation and transfers ownership of the raw native surface to the returned resource container.
    /// </summary>
    /// <param name="session">The session that owns the shared device resources.</param>
    /// <param name="surface">The raw surface returned by wgpu-native.</param>
    /// <param name="format">The requested swapchain texture format.</param>
    /// <param name="alphaMode">The requested compositor alpha mode.</param>
    /// <param name="initialPresentMode">The requested initial presentation mode.</param>
    /// <param name="initialFramebufferSize">The initial framebuffer size.</param>
    /// <returns>The initialized surface resources.</returns>
    private static WebGPUSurfaceResources Create(
        WebGPUSurfaceSession session,
        WGPUSurfaceImpl* surface,
        WebGPUTextureFormat format,
        WebGPUCompositeAlphaMode alphaMode,
        WebGPUPresentMode initialPresentMode,
        Size initialFramebufferSize)
    {
        WebGPU api = session.Api;
        WebGPUSurfaceHandle? surfaceHandle = null;
        uint supportedPresentModes = 0;
        TextureUsage surfaceUsage = RequiredSurfaceUsage;

        try
        {
            if (surface is null)
            {
                throw new InvalidOperationException("WebGPU surface creation failed.");
            }

            surfaceHandle = new WebGPUSurfaceHandle(api, (nint)surface, ownsHandle: true);

            // The first surface selects the presentation-compatible adapter and provisions the
            // shared device. Later surfaces reuse it and only query their own capabilities.
            session.EnsureInitialized(surface);

            using (WebGPUHandle.HandleReference adapterReference = session.AdapterHandle.AcquireReference())
            {
                NegotiateSurfaceConfiguration(
                    api,
                    (WGPUAdapterImpl*)adapterReference.Handle,
                    surface,
                    ref format,
                    ref alphaMode,
                    ref initialPresentMode,
                    out supportedPresentModes,
                    out surfaceUsage);
            }

            WebGPUSurfaceResources resources = new(
                api,
                session,
                surfaceHandle,
                format,
                alphaMode,
                initialPresentMode,
                supportedPresentModes,
                surfaceUsage);

            resources.ConfigureSurface(initialPresentMode, initialFramebufferSize);
            return resources;
        }
        catch
        {
            surfaceHandle?.Dispose();

            // Once the raw pointer is wrapped, SafeHandle owns its native reference. Release
            // directly only if wrapper construction failed before that ownership transfer.
            if (surfaceHandle is null && surface is not null)
            {
                api.SurfaceRelease(surface);
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
        presentMode = presentMode switch
        {
            WebGPUPresentMode.Fifo => WebGPUPresentMode.Fifo,
            WebGPUPresentMode.Immediate => WebGPUPresentMode.Immediate,
            WebGPUPresentMode.Mailbox => WebGPUPresentMode.Mailbox,
            _ => WebGPUPresentMode.Fifo
        };

        // The public enum values occupy consecutive bit positions, allowing the capabilities
        // retained at creation to resolve later property changes without another native query.
        if ((this.supportedPresentModes & (1U << (int)presentMode)) == 0)
        {
            presentMode = WebGPUPresentMode.Fifo;
        }

        this.PresentMode = presentMode;

        if (framebufferSize.Width <= 0 || framebufferSize.Height <= 0)
        {
            return;
        }

        this.ConfigureSurfaceCore(presentMode, framebufferSize, this.session.DeviceHandle, this.Format, this.AlphaMode);
    }

    /// <summary>
    /// Reconfigures <see cref="SurfaceHandle"/> against the supplied device handle.
    /// </summary>
    /// <param name="presentMode">The present mode applied to the swapchain.</param>
    /// <param name="framebufferSize">The framebuffer size in pixels.</param>
    /// <param name="deviceHandle">The device handle used by the new surface configuration.</param>
    /// <param name="format">The texture format applied to the swapchain.</param>
    /// <param name="alphaMode">The composite alpha mode applied to the swapchain.</param>
    private void ConfigureSurfaceCore(
        WebGPUPresentMode presentMode,
        Size framebufferSize,
        WebGPUDeviceHandle deviceHandle,
        WebGPUTextureFormat format,
        WebGPUCompositeAlphaMode alphaMode)
    {
        if (framebufferSize.Width <= 0 || framebufferSize.Height <= 0)
        {
            return;
        }

        WGPUSurfaceConfiguration surfaceConfiguration = new()
        {
            usage = (ulong)this.surfaceUsage,
            format = WebGPUTextureFormatMapper.ToNative(format),
            alphaMode = ToNative(alphaMode),
            presentMode = ToNative(presentMode),
            width = (uint)framebufferSize.Width,
            height = (uint)framebufferSize.Height,
        };

        using WebGPUHandle.HandleReference surfaceReference = this.SurfaceHandle.AcquireReference();
        using WebGPUHandle.HandleReference deviceReference = deviceHandle.AcquireReference();
        surfaceConfiguration.device = (WGPUDeviceImpl*)deviceReference.Handle;
        this.Api.SurfaceConfigure((WGPUSurfaceImpl*)surfaceReference.Handle, in surfaceConfiguration);
    }

    /// <summary>
    /// Maps the public present-mode enumeration to the native WebGPU value.
    /// </summary>
    /// <param name="presentMode">The present mode to map.</param>
    /// <returns>The native present mode.</returns>
    private static WGPUPresentMode ToNative(WebGPUPresentMode presentMode)
        => presentMode switch
        {
            WebGPUPresentMode.Fifo => WGPUPresentMode.Fifo,
            WebGPUPresentMode.Immediate => WGPUPresentMode.Immediate,
            WebGPUPresentMode.Mailbox => WGPUPresentMode.Mailbox,
            _ => throw new InvalidOperationException("The WebGPU present mode mapping is incomplete.")
        };

    /// <summary>
    /// Maps the public composite-alpha enumeration to the native WebGPU value.
    /// </summary>
    /// <param name="alphaMode">The composite-alpha mode to map.</param>
    /// <returns>The native composite-alpha mode.</returns>
    private static WGPUCompositeAlphaMode ToNative(WebGPUCompositeAlphaMode alphaMode)
        => alphaMode switch
        {
            WebGPUCompositeAlphaMode.Auto => WGPUCompositeAlphaMode.Auto,
            WebGPUCompositeAlphaMode.Opaque => WGPUCompositeAlphaMode.Opaque,
            WebGPUCompositeAlphaMode.Premultiplied => WGPUCompositeAlphaMode.Premultiplied,
            _ => throw new InvalidOperationException("The WebGPU composite alpha mode mapping is incomplete.")
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
    /// available right now (zero-area framebuffer or recoverable surface acquisition status).</returns>
    /// <exception cref="InvalidOperationException">Thrown when surface texture or texture-view acquisition fails.</exception>
    public bool TryAcquireFrame(
        WebGPUPresentMode presentMode,
        Size framebufferSize,
        DrawingOptions options,
        [NotNullWhen(true)] out WebGPUSurfaceFrame? frame)
        => this.TryAcquireFrameCore(presentMode, framebufferSize, options, textCache: null, out frame);

    /// <summary>
    /// Acquires the next presentable frame using a caller-owned text cache.
    /// </summary>
    /// <param name="presentMode">The present mode used when reconfiguring the surface.</param>
    /// <param name="framebufferSize">The current framebuffer size in pixels.</param>
    /// <param name="options">The drawing options that seed the canvas on the returned frame.</param>
    /// <param name="textCache">The text cache used by the returned frame canvas.</param>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/>.</returns>
    public bool TryAcquireFrame(
        WebGPUPresentMode presentMode,
        Size framebufferSize,
        DrawingOptions options,
        DrawingTextCache textCache,
        [NotNullWhen(true)] out WebGPUSurfaceFrame? frame)
        => this.TryAcquireFrameCore(presentMode, framebufferSize, options, textCache, out frame);

    /// <summary>
    /// Acquires a presentable frame and selects the cache ownership used by its canvas.
    /// </summary>
    private bool TryAcquireFrameCore(
        WebGPUPresentMode presentMode,
        Size framebufferSize,
        DrawingOptions options,
        DrawingTextCache? textCache,
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

        WGPUSurfaceTexture surfaceTexture = default;
        using (WebGPUHandle.HandleReference surfaceReference = this.SurfaceHandle.AcquireReference())
        {
            this.Api.SurfaceGetCurrentTexture((WGPUSurfaceImpl*)surfaceReference.Handle, &surfaceTexture);
        }

        switch (surfaceTexture.status)
        {
            case WGPUSurfaceGetCurrentTextureStatus.SuccessOptimal:
            case WGPUSurfaceGetCurrentTextureStatus.SuccessSuboptimal:
                break;

            case (WGPUSurfaceGetCurrentTextureStatus)WGPUNativeSurfaceGetCurrentTextureStatus.WGPUSurfaceGetCurrentTextureStatus_Occluded:
                // wgpu-native uses this extension status when the compositor has no drawable
                // surface. The surface remains configured and can be retried on the next frame.
                return false;

            case WGPUSurfaceGetCurrentTextureStatus.Timeout:
            case WGPUSurfaceGetCurrentTextureStatus.Outdated:
            case WGPUSurfaceGetCurrentTextureStatus.Lost:
                if (surfaceTexture.texture is not null)
                {
                    this.Api.TextureRelease(surfaceTexture.texture);
                }

                this.ConfigureSurface(presentMode, framebufferSize);
                return false;

            case WGPUSurfaceGetCurrentTextureStatus.Error:
            default:
                if (surfaceTexture.texture is not null)
                {
                    this.Api.TextureRelease(surfaceTexture.texture);
                }

                throw new InvalidOperationException($"Surface texture error: {surfaceTexture.status}");
        }

        WGPUTextureViewImpl* textureView = this.Api.TextureCreateView(surfaceTexture.texture, null);
        if (textureView is null)
        {
            this.Api.TextureRelease(surfaceTexture.texture);
            throw new InvalidOperationException("WebGPU texture view creation failed.");
        }

        WebGPUTextureHandle? textureHandle = null;
        WebGPUTextureViewHandle? textureViewHandle = null;
        try
        {
            textureHandle = new WebGPUTextureHandle(this.Api, (nint)surfaceTexture.texture, ownsHandle: true);
            textureViewHandle = new WebGPUTextureViewHandle(this.Api, (nint)textureView, ownsHandle: true);

            if (this.presentationTarget is null ||
                this.presentationTarget.Width != framebufferSize.Width ||
                this.presentationTarget.Height != framebufferSize.Height)
            {
                this.presentationRenderer?.Dispose();
                this.presentationTarget?.Dispose();
                this.presentationRenderer = null;
                this.presentationTarget = null;

                // Keep one fully capable texture per surface. Reusing it avoids driver allocations
                // per frame and isolates the drawing pipeline from optional swapchain usages.
                WebGPURenderTarget createdTarget = new(
                    this.session.DeviceContext,
                    ownsDeviceContext: false,
                    this.TargetDescriptor,
                    framebufferSize.Width,
                    framebufferSize.Height,
                    isPresentationSurface: true);

                try
                {
                    this.presentationRenderer = new WebGPUPresentationRenderer(
                        this.Api,
                        this.session.DeviceContext,
                        createdTarget,
                        (this.surfaceUsage & TextureUsage.CopyDst) != 0);

                    this.presentationTarget = createdTarget;
                }
                catch
                {
                    createdTarget.Dispose();
                    throw;
                }
            }

            // A supplied cache survives frame disposal and retains glyph/run geometry. The null
            // path preserves the existing frame-local cache behavior for every current caller.
            DrawingCanvas canvas = textCache is null
                ? this.presentationTarget.CreateCanvas(options)
                : this.presentationTarget.CreateCanvas(options, textCache);

            frame = new WebGPUSurfaceFrame(
                this.Api,
                this.session.DeviceContext,
                this.TargetDescriptor,
                this.SurfaceHandle,
                textureHandle,
                textureViewHandle,
                canvas,
                this.presentationRenderer!,
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
                this.Api.TextureRelease(surfaceTexture.texture);
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
        using (WebGPUHandle.HandleReference deviceReference = this.session.DeviceHandle.AcquireReference())
        {
            _ = signal.Wait(this.Api, (WGPUDeviceImpl*)deviceReference.Handle);
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
        if (this.isDisposed || this.session.QueueHandle.IsInvalid)
        {
            return;
        }

        // A previous signal should already have been consumed at acquire; retire a leftover
        // (an acquire that never happened) before installing the new one.
        this.WaitForPreviousFrameWorkDone();

        try
        {
            using WebGPUHandle.HandleReference queueReference = this.session.QueueHandle.AcquireReference();
            this.previousFrameWorkDone = new FramePacingSignal(this.Api, (WGPUQueueImpl*)queueReference.Handle);
        }
        catch (ObjectDisposedException)
        {
            // The queue was torn down between the check and the acquire; skip pacing.
        }
    }

    /// <summary>
    /// Releases the owned native presentation surface after retiring its frame-pacing callback.
    /// Idempotent; subsequent calls are no-ops.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        // Retire the pacing signal before the device goes away. Its callback wrapper remains
        // self-rooted if native WebGPU still owes the registered completion invocation.
        this.WaitForPreviousFrameWorkDone();

        this.presentationRenderer?.Dispose();
        this.presentationTarget?.Dispose();
        this.SurfaceHandle.Dispose();
        this.isDisposed = true;
    }

    /// <summary>
    /// Negotiates the format, alpha mode, and present mode for one surface on the session adapter.
    /// </summary>
    /// <param name="api">The shared WebGPU API loader.</param>
    /// <param name="adapter">The adapter shared by the surface session.</param>
    /// <param name="surface">The surface whose capabilities are queried.</param>
    /// <param name="format">The requested texture format. Receives the negotiated format.</param>
    /// <param name="alphaMode">The requested composite alpha mode. Receives <see cref="WebGPUCompositeAlphaMode.Auto"/> when the requested mode is unavailable.</param>
    /// <param name="presentMode">The requested presentation mode. Receives <see cref="WebGPUPresentMode.Fifo"/> when the requested mode is unavailable.</param>
    /// <param name="supportedPresentModes">Receives a bit field containing the presentation modes supported by the surface.</param>
    /// <param name="surfaceUsage">Receives the guaranteed render-attachment usage and any supported optional usages selected by the backend.</param>
    private static void NegotiateSurfaceConfiguration(
        WebGPU api,
        WGPUAdapterImpl* adapter,
        WGPUSurfaceImpl* surface,
        ref WebGPUTextureFormat format,
        ref WebGPUCompositeAlphaMode alphaMode,
        ref WebGPUPresentMode presentMode,
        out uint supportedPresentModes,
        out TextureUsage surfaceUsage)
    {
        supportedPresentModes = 0;
        surfaceUsage = RequiredSurfaceUsage;

        WGPUSurfaceCapabilities capabilities = default;
        WGPUStatus capabilityStatus = api.SurfaceGetCapabilities(surface, adapter, &capabilities);

        if (capabilityStatus != WGPUStatus.Success)
        {
            throw new InvalidOperationException($"The WebGPU runtime could not query surface capabilities. Status: '{capabilityStatus}'.");
        }

        try
        {
            TextureUsage supportedUsage = (TextureUsage)capabilities.usages;
            if ((supportedUsage & RequiredSurfaceUsage) != RequiredSurfaceUsage)
            {
                throw new NotSupportedException($"The WebGPU surface does not support the texture usages required by the drawing backend. Supported usages: '{supportedUsage}'.");
            }

            // A same-format native copy avoids the presentation shader entirely. Surfaces that
            // omit CopyDst retain the guaranteed render-attachment presentation fallback.
            surfaceUsage |= supportedUsage & TextureUsage.CopyDst;

            format = format switch
            {
                WebGPUTextureFormat.Rgba8Unorm => WebGPUTextureFormat.Rgba8Unorm,
                WebGPUTextureFormat.Rgba8Snorm => WebGPUTextureFormat.Rgba8Snorm,
                WebGPUTextureFormat.Bgra8Unorm => WebGPUTextureFormat.Bgra8Unorm,
                WebGPUTextureFormat.Rgba16Float => WebGPUTextureFormat.Rgba16Float,
                _ => WebGPUTextureFormat.Rgba8Unorm
            };

            WebGPUDrawingBackend.GetCompositeTextureFormatInfo(format, out WGPUTextureFormat nativeFormat, out WGPUFeatureName requiredFeature);
            bool supportsFormat = requiredFeature == default || api.AdapterHasFeature(adapter, requiredFeature);

            if (supportsFormat)
            {
                supportsFormat = false;

                for (nuint i = 0; i < capabilities.formatCount; i++)
                {
                    if (capabilities.formats[i] == nativeFormat)
                    {
                        supportsFormat = true;
                        break;
                    }
                }
            }

            if (!supportsFormat)
            {
                // Prefer the public default, then the remaining formats in their documented
                // order. This keeps fallback deterministic rather than depending on the
                // platform-specific order of the native capability array.
                ReadOnlySpan<WebGPUTextureFormat> fallbackFormats =
                [
                    WebGPUTextureFormat.Rgba8Unorm,
                    WebGPUTextureFormat.Rgba8Snorm,
                    WebGPUTextureFormat.Bgra8Unorm,
                    WebGPUTextureFormat.Rgba16Float
                ];

                bool foundFallback = false;

                foreach (WebGPUTextureFormat fallbackFormat in fallbackFormats)
                {
                    WebGPUDrawingBackend.GetCompositeTextureFormatInfo(fallbackFormat, out WGPUTextureFormat fallbackNativeFormat, out WGPUFeatureName fallbackFeature);

                    if (fallbackFeature != default && !api.AdapterHasFeature(adapter, fallbackFeature))
                    {
                        continue;
                    }

                    for (nuint i = 0; i < capabilities.formatCount; i++)
                    {
                        if (capabilities.formats[i] == fallbackNativeFormat)
                        {
                            format = fallbackFormat;
                            foundFallback = true;
                            break;
                        }
                    }

                    if (foundFallback)
                    {
                        break;
                    }
                }

                if (!foundFallback)
                {
                    throw new NotSupportedException("The WebGPU surface does not expose a texture format supported by the drawing backend.");
                }
            }

            // Each public present-mode value owns the matching bit. Retaining this compact
            // mask lets runtime property changes degrade without another native allocation.
            for (nuint i = 0; i < capabilities.presentModeCount; i++)
            {
                supportedPresentModes |= capabilities.presentModes[i] switch
                {
                    WGPUPresentMode.Fifo => 1U << (int)WebGPUPresentMode.Fifo,
                    WGPUPresentMode.Immediate => 1U << (int)WebGPUPresentMode.Immediate,
                    WGPUPresentMode.Mailbox => 1U << (int)WebGPUPresentMode.Mailbox,
                    _ => 0
                };
            }

            presentMode = presentMode switch
            {
                WebGPUPresentMode.Fifo => WebGPUPresentMode.Fifo,
                WebGPUPresentMode.Immediate => WebGPUPresentMode.Immediate,
                WebGPUPresentMode.Mailbox => WebGPUPresentMode.Mailbox,
                _ => WebGPUPresentMode.Fifo
            };

            if ((supportedPresentModes & (1U << (int)presentMode)) == 0)
            {
                presentMode = WebGPUPresentMode.Fifo;
            }

            alphaMode = alphaMode switch
            {
                WebGPUCompositeAlphaMode.Auto => WebGPUCompositeAlphaMode.Auto,
                WebGPUCompositeAlphaMode.Opaque => WebGPUCompositeAlphaMode.Opaque,
                WebGPUCompositeAlphaMode.Premultiplied => WebGPUCompositeAlphaMode.Premultiplied,
                _ => WebGPUCompositeAlphaMode.Auto
            };

            WGPUCompositeAlphaMode nativeAlphaMode = ToNative(alphaMode);

            if (nativeAlphaMode != WGPUCompositeAlphaMode.Auto)
            {
                bool supportsAlphaMode = false;

                for (nuint i = 0; i < capabilities.alphaModeCount; i++)
                {
                    if (capabilities.alphaModes[i] == nativeAlphaMode)
                    {
                        supportsAlphaMode = true;
                        break;
                    }
                }

                // Auto is the WebGPU-defined compatibility fallback for an unavailable
                // explicit mode and resolves to opaque or inherited composition.
                if (!supportsAlphaMode)
                {
                    alphaMode = WebGPUCompositeAlphaMode.Auto;
                }
            }
        }
        finally
        {
            // The capability arrays are native allocations owned by the query result.
            api.SurfaceCapabilitiesFreeMembers(capabilities);
        }
    }

    /// <summary>
    /// A queue work-done callback and its completion event for one presented frame. The callback
    /// wrapper roots itself while native WebGPU owes an invocation and suppresses access to the
    /// event after a bounded wait retires its managed owner.
    /// </summary>
    private sealed class FramePacingSignal : IDisposable
    {
        /// <summary>
        /// Set by the work-done callback once the frame's submissions have completed.
        /// </summary>
        private readonly ManualResetEventSlim workDone = new(false);

        /// <summary>
        /// The self-rooting work-done callback wrapper registered with native WebGPU.
        /// </summary>
        private readonly WebGPUQueueWorkDoneCallback callback;

        /// <summary>
        /// Initializes a new instance of the <see cref="FramePacingSignal"/> class and registers
        /// the work-done callback for everything submitted to the queue so far.
        /// </summary>
        /// <param name="api">The WebGPU API used to register the callback.</param>
        /// <param name="queue">The queue whose submitted work the signal covers.</param>
        public FramePacingSignal(WebGPU api, WGPUQueueImpl* queue)
        {
            this.callback = WebGPUQueueWorkDoneCallback.From(this.OnWorkDone);
            api.QueueOnSubmittedWorkDone(queue, this.callback, null);
        }

        /// <summary>
        /// Waits (bounded) for the work-done callback while polling the owning device.
        /// </summary>
        /// <param name="api">The WebGPU API used to poll the device.</param>
        /// <param name="device">The device that owns the queue.</param>
        /// <returns><see langword="true"/> when the callback completed before the timeout; otherwise, <see langword="false"/>.</returns>
        public bool Wait(WebGPU api, WGPUDeviceImpl* device)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!this.workDone.IsSet && stopwatch.ElapsedMilliseconds < CallbackTimeoutMilliseconds)
            {
                // The queue callback already captures the work boundary registered by the
                // constructor. A blocking poll parks this thread in the native fence wait for the
                // duration of the GPU work instead of spinning tens of thousands of non-blocking
                // polls per frame; the callback is delivered during the poll that observes its
                // boundary. wgpu bounds the native wait internally, so the stopwatch still
                // enforces the overall timeout when the device is lost or stalled.
                _ = api.DevicePoll(device, true, null);
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
        private void OnWorkDone(WGPUQueueWorkDoneStatus status, void* userData)
        {
            _ = status;
            _ = userData;
            this.workDone.Set();
        }
    }
}
