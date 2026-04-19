// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using SixLabors.ImageSharp.PixelFormats;
using SilkPresentMode = Silk.NET.WebGPU.PresentMode;
using SilkWindowBorder = Silk.NET.Windowing.WindowBorder;
using SilkWindowState = Silk.NET.Windowing.WindowState;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// A self-contained WebGPU-backed window that owns the platform window, the WebGPU device and queue, the surface
/// and swap chain, and the per-frame texture acquire/present cycle, exposing a <see cref="DrawingCanvas{TPixel}"/>
/// for each frame. Use <see cref="Run(Action{DrawingCanvas{TPixel}})"/> to let the window drive rendering, or
/// <see cref="TryAcquireFrame(TimeSpan, out WebGPUWindowFrame{TPixel}?)"/> to drive the frame loop yourself.
/// </summary>
/// <typeparam name="TPixel">The canvas pixel format.</typeparam>
/// <remarks>
/// Use this type when ImageSharp.Drawing owns the application's window. To render into a window owned by a host
/// application or UI framework, wrap the host's device and queue with <see cref="WebGPUDeviceContext{TPixel}"/>
/// and pass the host's per-frame swap-chain texture to
/// <see cref="WebGPUDeviceContext{TPixel}.CreateCanvas(nint, nint, WebGPUTextureFormatId, int, int, DrawingOptions)"/> instead.
/// </remarks>
public sealed unsafe class WebGPUWindow<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private const int CallbackTimeoutMilliseconds = 10_000;

    private readonly IWindow window;
    private readonly WindowResources resources;
    private bool isDisposed;
    private long frameIndex;
    private WebGPUPresentMode presentMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUWindow{TPixel}"/> class.
    /// </summary>
    public WebGPUWindow()
        : this(Configuration.Default, new WebGPUWindowOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUWindow{TPixel}"/> class.
    /// </summary>
    /// <param name="options">The window creation options.</param>
    public WebGPUWindow(WebGPUWindowOptions options)
        : this(Configuration.Default, options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUWindow{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    /// <param name="options">The window creation options.</param>
    public WebGPUWindow(Configuration configuration, WebGPUWindowOptions options)
    {
        if (!WebGPUDrawingBackend.TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId expectedFormat))
        {
            throw new NotSupportedException($"Pixel type '{typeof(TPixel).Name}' is not supported by the WebGPU backend.");
        }

        this.window = Window.Create(CreateSilkOptions(options));
        this.Configuration = configuration;
        this.Format = expectedFormat;
        this.presentMode = options.PresentMode;

        try
        {
            this.window.Initialize();
            this.resources = this.CreateResources();
        }
        catch
        {
            this.window.Dispose();
            throw;
        }

        this.window.Update += deltaTime => this.Update?.Invoke(TimeSpan.FromSeconds(deltaTime));
        this.window.Resize += size => this.Resized?.Invoke(ToSize(size));
        this.window.FramebufferResize += this.OnFramebufferResize;
        this.window.Closing += () => this.Closing?.Invoke();
        this.window.FocusChanged += isFocused => this.FocusChanged?.Invoke(isFocused);
        this.window.Move += position => this.Moved?.Invoke(ToPoint(position));
        this.window.StateChanged += state => this.StateChanged?.Invoke(FromSilk(state));
        this.window.FileDrop += files => this.FilesDropped?.Invoke(files);
    }

    /// <summary>
    /// Raised when the window update loop runs.
    /// </summary>
    public event Action<TimeSpan>? Update;

    /// <summary>
    /// Raised when the client size changes.
    /// </summary>
    public event Action<Size>? Resized;

    /// <summary>
    /// Raised when the framebuffer size changes.
    /// </summary>
    public event Action<Size>? FramebufferResized;

    /// <summary>
    /// Raised when the window focus changes.
    /// </summary>
    public event Action<bool>? FocusChanged;

    /// <summary>
    /// Raised when the window moves.
    /// </summary>
    public event Action<Point>? Moved;

    /// <summary>
    /// Raised when the window state changes.
    /// </summary>
    public event Action<WebGPUWindowState>? StateChanged;

    /// <summary>
    /// Raised when files are dropped onto the window.
    /// </summary>
    public event Action<string[]>? FilesDropped;

    /// <summary>
    /// Raised when the window is closing.
    /// </summary>
    public event Action? Closing;

    /// <summary>
    /// Gets the configuration provided when the window was created.
    /// </summary>
    public Configuration Configuration { get; }

    /// <summary>
    /// Gets or sets the window title.
    /// </summary>
    public string Title
    {
        get => this.window.Title;
        set => this.window.Title = value;
    }

    /// <summary>
    /// Gets or sets the client size in pixels.
    /// </summary>
    public Size ClientSize
    {
        get => ToSize(this.window.Size);
        set => this.window.Size = ToVector(value);
    }

    /// <summary>
    /// Gets the framebuffer size in pixels.
    /// </summary>
    public Size FramebufferSize => ToSize(this.window.FramebufferSize);

    /// <summary>
    /// Gets the ratio between framebuffer pixels and client pixels.
    /// </summary>
    public float RenderScale
    {
        get
        {
            Size clientSize = this.ClientSize;
            Size framebufferSize = this.FramebufferSize;
            if (clientSize.Width <= 0 || clientSize.Height <= 0)
            {
                return 1F;
            }

            return MathF.Max(
                (float)framebufferSize.Width / clientSize.Width,
                (float)framebufferSize.Height / clientSize.Height);
        }
    }

    /// <summary>
    /// Gets or sets the window position.
    /// </summary>
    public Point Position
    {
        get => ToPoint(this.window.Position);
        set => this.window.Position = ToVector(value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the window is visible.
    /// </summary>
    public bool IsVisible
    {
        get => this.window.IsVisible;
        set => this.window.IsVisible = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the window is event-driven.
    /// </summary>
    public bool IsEventDriven
    {
        get => this.window.IsEventDriven;
        set => this.window.IsEventDriven = value;
    }

    /// <summary>
    /// Gets or sets the maximum number of render callbacks per second.
    /// </summary>
    public double FramesPerSecond
    {
        get => this.window.FramesPerSecond;
        set => this.window.FramesPerSecond = value;
    }

    /// <summary>
    /// Gets or sets the maximum number of update callbacks per second.
    /// </summary>
    public double UpdatesPerSecond
    {
        get => this.window.UpdatesPerSecond;
        set => this.window.UpdatesPerSecond = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the window is top-most.
    /// </summary>
    public bool TopMost
    {
        get => this.window.TopMost;
        set => this.window.TopMost = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the window has been requested to close.
    /// </summary>
    public bool IsClosing
    {
        get => this.window.IsClosing;
        set => this.window.IsClosing = value;
    }

    /// <summary>
    /// Gets the elapsed time in seconds since the window was created.
    /// </summary>
    public double Time => this.window.Time;

    /// <summary>
    /// Gets the native window handle.
    /// </summary>
    public nint Handle => this.window.Handle;

    /// <summary>
    /// Gets or sets the window state.
    /// </summary>
    public WebGPUWindowState WindowState
    {
        get => FromSilk(this.window.WindowState);
        set => this.window.WindowState = ToSilk(value);
    }

    /// <summary>
    /// Gets or sets the window border mode.
    /// </summary>
    public WebGPUWindowBorder WindowBorder
    {
        get => FromSilk(this.window.WindowBorder);
        set => this.window.WindowBorder = ToSilk(value);
    }

    /// <summary>
    /// Gets or sets the swapchain present mode.
    /// </summary>
    public WebGPUPresentMode PresentMode
    {
        get => this.presentMode;
        set
        {
            this.presentMode = value;
            this.ConfigureSurface(this.resources);
        }
    }

    /// <summary>
    /// Gets the swapchain texture format.
    /// </summary>
    public WebGPUTextureFormatId Format { get; }

    /// <summary>
    /// Tries to acquire the next drawable window frame.
    /// </summary>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/> when the frame should be retried later.</returns>
    /// <remarks>
    /// Use this overload when you are driving the render loop yourself and want frame acquisition failures to be handled as normal retry behavior instead of exceptions. Dispose the returned frame when you are done with it to present it and release its per-frame resources.
    /// </remarks>
    public bool TryAcquireFrame([NotNullWhen(true)] out WebGPUWindowFrame<TPixel>? frame)
        => this.TryAcquireFrameCore(TimeSpan.Zero, out frame, new DrawingOptions());

    /// <summary>
    /// Tries to acquire the next drawable window frame.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since the previous frame.</param>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/> when the frame should be retried later.</returns>
    /// <remarks>
    /// Use this overload when you are driving the render loop yourself and want to provide timing information for the acquired frame. Dispose the returned frame when you are done with it to present it and release its per-frame resources.
    /// </remarks>
    public bool TryAcquireFrame(TimeSpan deltaTime, [NotNullWhen(true)] out WebGPUWindowFrame<TPixel>? frame)
        => this.TryAcquireFrameCore(deltaTime, out frame, new DrawingOptions());

    /// <summary>
    /// Tries to acquire the next drawable window frame.
    /// </summary>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <param name="options">The drawing options for the acquired frame.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/> when the frame should be retried later.</returns>
    /// <remarks>
    /// This method is intended for manual frame loops. A <see langword="false"/> result means no drawable frame is available right now, for example because the surface was lost, outdated, timed out, or has a zero-sized framebuffer. Dispose the returned frame when you are done with it to present it and release its per-frame resources.
    /// </remarks>
    public bool TryAcquireFrame([NotNullWhen(true)] out WebGPUWindowFrame<TPixel>? frame, DrawingOptions options)
        => this.TryAcquireFrameCore(TimeSpan.Zero, out frame, options);

    /// <summary>
    /// Tries to acquire the next drawable window frame.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since the previous frame.</param>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <param name="options">The drawing options for the acquired frame.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/> when the frame should be retried later.</returns>
    /// <remarks>
    /// This method is intended for manual frame loops. A <see langword="false"/> result means no drawable frame is available right now, for example because the surface was lost, outdated, timed out, or has a zero-sized framebuffer. Dispose the returned frame when you are done with it to present it and release its per-frame resources.
    /// </remarks>
    public bool TryAcquireFrame(TimeSpan deltaTime, [NotNullWhen(true)] out WebGPUWindowFrame<TPixel>? frame, DrawingOptions options)
        => this.TryAcquireFrameCore(deltaTime, out frame, options);

    /// <summary>
    /// Runs the window's event loop and renders one WebGPU frame per render callback.
    /// </summary>
    /// <param name="render">The per-frame render callback.</param>
    public void Run(Action<WebGPUWindowFrame<TPixel>> render)
        => this.Run(render, new DrawingOptions());

    /// <summary>
    /// Runs the window's event loop and renders one WebGPU frame per render callback.
    /// </summary>
    /// <param name="render">The per-frame render callback.</param>
    /// <param name="options">The drawing options applied to each acquired frame.</param>
    public void Run(Action<WebGPUWindowFrame<TPixel>> render, DrawingOptions options)
    {
        Guard.NotNull(render, nameof(render));
        this.ThrowIfDisposed();

        void OnRender(double deltaTime)
        {
            if (!this.TryAcquireFrameCore(TimeSpan.FromSeconds(deltaTime), out WebGPUWindowFrame<TPixel>? frame, options))
            {
                return;
            }

            using (frame)
            {
                render(frame);
            }
        }

        this.window.Render += OnRender;
        try
        {
            this.window.Run(() =>
            {
                this.window.DoEvents();
                if (!this.window.IsClosing)
                {
                    this.window.DoUpdate();
                }

                if (!this.window.IsClosing)
                {
                    this.window.DoRender();
                }
            });

            this.window.DoEvents();
            this.window.Reset();
        }
        finally
        {
            this.window.Render -= OnRender;
        }
    }

    /// <summary>
    /// Runs the window's event loop and renders one WebGPU canvas per render callback.
    /// </summary>
    /// <param name="render">The per-frame render callback.</param>
    public void Run(Action<DrawingCanvas<TPixel>> render)
        => this.Run(render, new DrawingOptions());

    /// <summary>
    /// Runs the window's event loop and renders one WebGPU canvas per render callback.
    /// </summary>
    /// <param name="render">The per-frame render callback.</param>
    /// <param name="options">The drawing options applied to each acquired frame.</param>
    public void Run(Action<DrawingCanvas<TPixel>> render, DrawingOptions options)
    {
        Guard.NotNull(render, nameof(render));
        this.Run(frame => render(frame.Canvas), options);
    }

    /// <summary>
    /// Polls the underlying window for events.
    /// </summary>
    public void DoEvents()
    {
        this.ThrowIfDisposed();
        this.window.DoEvents();
    }

    /// <summary>
    /// Continues the event loop when running in event-driven mode.
    /// </summary>
    public void ContinueEvents()
    {
        this.ThrowIfDisposed();
        this.window.ContinueEvents();
    }

    /// <summary>
    /// Requests that the window close.
    /// </summary>
    public void RequestClose() => this.window.IsClosing = true;

    /// <summary>
    /// Closes the window immediately.
    /// </summary>
    public void Close()
    {
        this.ThrowIfDisposed();
        this.window.Close();
    }

    /// <summary>
    /// Sets keyboard focus to the window.
    /// </summary>
    public void Focus()
    {
        this.ThrowIfDisposed();
        this.window.Focus();
    }

    /// <summary>
    /// Shows the window.
    /// </summary>
    public void Show() => this.window.IsVisible = true;

    /// <summary>
    /// Hides the window.
    /// </summary>
    public void Hide() => this.window.IsVisible = false;

    /// <summary>
    /// Converts a screen-space point to client coordinates.
    /// </summary>
    /// <param name="point">The point to convert.</param>
    /// <returns>The converted point.</returns>
    public Point PointToClient(Point point)
        => ToPoint(this.window.PointToClient(ToVector(point)));

    /// <summary>
    /// Converts a client-space point to screen coordinates.
    /// </summary>
    /// <param name="point">The point to convert.</param>
    /// <returns>The converted point.</returns>
    public Point PointToScreen(Point point)
        => ToPoint(this.window.PointToScreen(ToVector(point)));

    /// <summary>
    /// Converts a client-space point to framebuffer coordinates.
    /// </summary>
    /// <param name="point">The point to convert.</param>
    /// <returns>The converted point.</returns>
    public Point PointToFramebuffer(Point point)
        => ToPoint(this.window.PointToFramebuffer(ToVector(point)));

    /// <summary>
    /// Disposes the window and its WebGPU resources.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.resources.Dispose();
        this.window.Dispose();
        this.isDisposed = true;
    }

    private WindowResources CreateResources()
    {
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
            surface = this.window.CreateWebGPUSurface(api, instance);
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
            if (!WebGPUDrawingBackend.TryGetCompositeTextureFormat<TPixel>(out _, out FeatureName requiredFeature))
            {
                throw new NotSupportedException($"Pixel type '{typeof(TPixel).Name}' is not supported by the WebGPU backend.");
            }

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
            graphics = new WebGPUDeviceContext<TPixel>(this.Configuration, deviceHandle, queueHandle);
            WindowResources resources = new(api, instanceHandle, surfaceHandle, adapterHandle, deviceHandle, queueHandle, graphics);
            this.ConfigureSurface(resources);
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

    private bool TryAcquireFrameCore(
        TimeSpan deltaTime,
        [NotNullWhen(true)] out WebGPUWindowFrame<TPixel>? frame,
        DrawingOptions options)
    {
        this.ThrowIfDisposed();
        WindowResources resources = this.resources;
        frame = null;

        Size framebufferSize = this.FramebufferSize;
        if (framebufferSize.Width <= 0 || framebufferSize.Height <= 0)
        {
            return false;
        }

        SurfaceTexture surfaceTexture = default;
        using (WebGPUHandle.HandleReference surfaceReference = resources.SurfaceHandle.AcquireReference())
        {
            resources.Api.SurfaceGetCurrentTexture((Surface*)surfaceReference.Handle, &surfaceTexture);
        }

        switch (surfaceTexture.Status)
        {
            case SurfaceGetCurrentTextureStatus.Timeout:
            case SurfaceGetCurrentTextureStatus.Outdated:
            case SurfaceGetCurrentTextureStatus.Lost:
                if (surfaceTexture.Texture is not null)
                {
                    resources.Api.TextureRelease(surfaceTexture.Texture);
                }

                this.ConfigureSurface(resources);
                return false;

            case SurfaceGetCurrentTextureStatus.OutOfMemory:
            case SurfaceGetCurrentTextureStatus.DeviceLost:
                if (surfaceTexture.Texture is not null)
                {
                    resources.Api.TextureRelease(surfaceTexture.Texture);
                }

                throw new InvalidOperationException($"Surface texture error: {surfaceTexture.Status}");
        }

        TextureView* textureView = resources.Api.TextureCreateView(surfaceTexture.Texture, null);
        if (textureView is null)
        {
            resources.Api.TextureRelease(surfaceTexture.Texture);
            throw new InvalidOperationException("WebGPU texture view creation failed.");
        }

        WebGPUTextureHandle? textureHandle = null;
        WebGPUTextureViewHandle? textureViewHandle = null;
        try
        {
            textureHandle = new WebGPUTextureHandle(resources.Api, (nint)surfaceTexture.Texture, ownsHandle: true);
            textureViewHandle = new WebGPUTextureViewHandle(resources.Api, (nint)textureView, ownsHandle: true);
            DrawingCanvas<TPixel> canvas = resources.Graphics.CreateCanvas(
                textureHandle,
                textureViewHandle,
                this.Format,
                framebufferSize.Width,
                framebufferSize.Height,
                options);

            frame = new WebGPUWindowFrame<TPixel>(
                resources.Api,
                resources.SurfaceHandle,
                textureHandle,
                textureViewHandle,
                canvas,
                new Rectangle(0, 0, framebufferSize.Width, framebufferSize.Height),
                this.ClientSize,
                framebufferSize,
                deltaTime,
                this.frameIndex++);

            return true;
        }
        catch
        {
            textureViewHandle?.Dispose();
            textureHandle?.Dispose();

            if (textureViewHandle is null)
            {
                resources.Api.TextureViewRelease(textureView);
            }

            if (textureHandle is null)
            {
                resources.Api.TextureRelease(surfaceTexture.Texture);
            }

            throw;
        }
    }

    private void ConfigureSurface(WindowResources resources)
    {
        Size framebufferSize = this.FramebufferSize;
        if (framebufferSize.Width <= 0 || framebufferSize.Height <= 0)
        {
            return;
        }

        SurfaceConfiguration surfaceConfiguration = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopyDst | TextureUsage.TextureBinding,
            Format = WebGPUTextureFormatMapper.ToSilk(this.Format),
            PresentMode = ToSilk(this.presentMode),
            Width = (uint)framebufferSize.Width,
            Height = (uint)framebufferSize.Height,
        };

        using WebGPUHandle.HandleReference surfaceReference = resources.SurfaceHandle.AcquireReference();
        using WebGPUHandle.HandleReference deviceReference = resources.DeviceHandle.AcquireReference();
        surfaceConfiguration.Device = (Device*)deviceReference.Handle;
        resources.Api.SurfaceConfigure((Surface*)surfaceReference.Handle, ref surfaceConfiguration);
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        if (size.X > 0 && size.Y > 0)
        {
            this.ConfigureSurface(this.resources);
        }

        this.FramebufferResized?.Invoke(ToSize(size));
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

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

    private static WindowOptions CreateSilkOptions(WebGPUWindowOptions options)
    {
        WindowOptions silkOptions = WindowOptions.Default;
        silkOptions.API = GraphicsAPI.None;
        silkOptions.ShouldSwapAutomatically = false;
        silkOptions.IsContextControlDisabled = true;
        silkOptions.VSync = false;
        silkOptions.Title = options.Title;
        silkOptions.Size = ToVector(options.Size);
        silkOptions.Position = ToVector(options.Position);
        silkOptions.IsVisible = options.IsVisible;
        silkOptions.FramesPerSecond = options.FramesPerSecond;
        silkOptions.UpdatesPerSecond = options.UpdatesPerSecond;
        silkOptions.IsEventDriven = options.IsEventDriven;
        silkOptions.TopMost = options.TopMost;
        silkOptions.WindowState = ToSilk(options.WindowState);
        silkOptions.WindowBorder = ToSilk(options.WindowBorder);
        return silkOptions;
    }

    private static Vector2D<int> ToVector(Size size) => new(size.Width, size.Height);

    private static Vector2D<int> ToVector(Point point) => new(point.X, point.Y);

    private static Size ToSize(Vector2D<int> value) => new(value.X, value.Y);

    private static Point ToPoint(Vector2D<int> value) => new(value.X, value.Y);

    private static SilkWindowState ToSilk(WebGPUWindowState state) => (SilkWindowState)(int)state;

    private static WebGPUWindowState FromSilk(SilkWindowState state) => (WebGPUWindowState)(int)state;

    private static SilkWindowBorder ToSilk(WebGPUWindowBorder border) => (SilkWindowBorder)(int)border;

    private static WebGPUWindowBorder FromSilk(SilkWindowBorder border) => (WebGPUWindowBorder)(int)border;

    private static SilkPresentMode ToSilk(WebGPUPresentMode mode) => (SilkPresentMode)(int)mode;

    private sealed class WindowResources : IDisposable
    {
        public WindowResources(
            WebGPU api,
            WebGPUInstanceHandle instanceHandle,
            WebGPUSurfaceHandle surfaceHandle,
            WebGPUAdapterHandle adapterHandle,
            WebGPUDeviceHandle deviceHandle,
            WebGPUQueueHandle queueHandle,
            WebGPUDeviceContext<TPixel> graphics)
        {
            this.Api = api;
            this.InstanceHandle = instanceHandle;
            this.SurfaceHandle = surfaceHandle;
            this.AdapterHandle = adapterHandle;
            this.DeviceHandle = deviceHandle;
            this.QueueHandle = queueHandle;
            this.Graphics = graphics;
        }

        public WebGPU Api { get; }

        public WebGPUInstanceHandle InstanceHandle { get; }

        public WebGPUSurfaceHandle SurfaceHandle { get; }

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
            this.SurfaceHandle.Dispose();
            this.InstanceHandle.Dispose();
        }
    }
}
