// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using NativeWindowBorder = Silk.NET.Windowing.WindowBorder;
using NativeWindowState = Silk.NET.Windowing.WindowState;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// A self-contained WebGPU-backed window that owns the platform window, the WebGPU device and queue, the surface
/// and swap chain, and the per-frame texture acquire/present cycle, exposing a <see cref="DrawingCanvas"/>
/// for each frame. Use <see cref="Run(Action{DrawingCanvas})"/> to let the window drive rendering, or
/// <see cref="TryAcquireFrame(out WebGPUSurfaceFrame?)"/> to drive the frame loop yourself.
/// </summary>
/// <remarks>
/// Use this type when ImageSharp.Drawing owns the application's window. To render into a window owned by a host
/// application or UI framework, use <see cref="WebGPUExternalSurface"/>.
/// </remarks>
public sealed class WebGPUWindow : IDisposable
{
    private readonly IWindow window;
    private readonly WebGPUSurfaceResources resources;
    private bool isDisposed;
    private WebGPUPresentMode presentMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUWindow"/> class.
    /// </summary>
    public WebGPUWindow()
        : this(Configuration.Default, new WebGPUWindowOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUWindow"/> class.
    /// </summary>
    /// <param name="options">The window creation options.</param>
    public WebGPUWindow(WebGPUWindowOptions options)
        : this(Configuration.Default, options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUWindow"/> class.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    /// <param name="options">The window creation options.</param>
    public WebGPUWindow(Configuration configuration, WebGPUWindowOptions options)
    {
        this.window = Window.Create(CreateSilkOptions(options));
        this.Configuration = configuration;
        this.Format = options.Format;
        this.presentMode = options.PresentMode;

        try
        {
            this.window.Initialize();
            this.resources = WebGPUSurfaceResources.Create(
                configuration,
                this.window,
                this.Format,
                this.presentMode,
                ToSize(this.window.FramebufferSize));
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
        this.window.StateChanged += state => this.StateChanged?.Invoke(FromNative(state));
        this.window.FileDrop += files => this.FilesDropped?.Invoke(files);
    }

    /// <summary>
    /// Raised when the window update loop runs.
    /// </summary>
    public event Action<TimeSpan>? Update;

    /// <summary>
    /// Raised when the client-area size in window coordinates changes.
    /// </summary>
    public event Action<Size>? Resized;

    /// <summary>
    /// Raised when the framebuffer size changes.
    /// </summary>
    public event Action<Size>? FramebufferResized;

    /// <summary>
    /// Raised when the window is closing.
    /// </summary>
    public event Action? Closing;

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
    /// Gets or sets the client-area size in window coordinates.
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
    /// Gets the ratio between framebuffer pixels and client coordinate units.
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
    /// Gets or sets the window position in screen coordinates.
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
    /// Gets or sets a value indicating whether the window stays above normal windows.
    /// </summary>
    public bool IsTopMost
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
    /// Gets or sets the window state.
    /// </summary>
    public WebGPUWindowState WindowState
    {
        get => FromNative(this.window.WindowState);
        set => this.window.WindowState = ToNative(value);
    }

    /// <summary>
    /// Gets or sets the window border mode.
    /// </summary>
    public WebGPUWindowBorder WindowBorder
    {
        get => FromNative(this.window.WindowBorder);
        set => this.window.WindowBorder = ToNative(value);
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
            this.resources.ConfigureSurface(this.presentMode, this.FramebufferSize);
        }
    }

    /// <summary>
    /// Gets the swapchain texture format.
    /// </summary>
    public WebGPUTextureFormat Format { get; }

    /// <summary>
    /// Tries to acquire the next drawable frame using default drawing options.
    /// </summary>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns>
    /// <see langword="true"/> when a frame is available; otherwise <see langword="false"/> when the frame should be retried later.
    /// </returns>
    /// <remarks>
    /// Use this overload when the default drawing options are sufficient.
    /// </remarks>
    public bool TryAcquireFrame([NotNullWhen(true)] out WebGPUSurfaceFrame? frame)
        => this.TryAcquireFrameCore(new DrawingOptions(), out frame);

    /// <summary>
    /// Tries to acquire the next drawable frame.
    /// </summary>
    /// <param name="options">The drawing options for the acquired frame.</param>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns>
    /// <see langword="true"/> when a frame is available; otherwise <see langword="false"/> when the frame should be retried later.
    /// </returns>
    /// <remarks>
    /// Use this overload when you are driving the render loop yourself and need explicit drawing options.
    /// A <see langword="false"/> result means no drawable frame is available right now, for example because the
    /// surface was lost, outdated, timed out, has a zero-sized framebuffer, or the window recovered from device loss.
    /// Dispose the returned frame when you are done with it to present it and release its per-frame resources.
    /// </remarks>
    public bool TryAcquireFrame(DrawingOptions options, [NotNullWhen(true)] out WebGPUSurfaceFrame? frame)
        => this.TryAcquireFrameCore(options, out frame);

    /// <summary>
    /// Runs the window's event loop and renders one WebGPU frame per render callback.
    /// </summary>
    /// <param name="render">The per-frame render callback.</param>
    public void Run(Action<WebGPUSurfaceFrame> render)
        => this.Run(new DrawingOptions(), render);

    /// <summary>
    /// Runs the window's event loop and renders one WebGPU frame per render callback.
    /// </summary>
    /// <param name="options">The drawing options applied to each acquired frame.</param>
    /// <param name="render">The per-frame render callback.</param>
    public void Run(DrawingOptions options, Action<WebGPUSurfaceFrame> render)
    {
        Guard.NotNull(render, nameof(render));
        this.Run(options, (frame, _) => render(frame));
    }

    /// <summary>
    /// Runs the window's event loop and renders one WebGPU frame per render callback.
    /// </summary>
    /// <param name="render">The per-frame render callback. The second argument is elapsed time since the previous render callback.</param>
    public void Run(Action<WebGPUSurfaceFrame, TimeSpan> render)
        => this.Run(new DrawingOptions(), render);

    /// <summary>
    /// Runs the window's event loop and renders one WebGPU frame per render callback.
    /// </summary>
    /// <param name="options">The drawing options applied to each acquired frame.</param>
    /// <param name="render">The per-frame render callback. The second argument is elapsed time since the previous render callback.</param>
    public void Run(DrawingOptions options, Action<WebGPUSurfaceFrame, TimeSpan> render)
    {
        Guard.NotNull(render, nameof(render));
        this.ThrowIfDisposed();

        void OnRender(double deltaTime)
        {
            if (!this.TryAcquireFrameCore(options, out WebGPUSurfaceFrame? frame))
            {
                return;
            }

            using (frame)
            {
                render(frame, TimeSpan.FromSeconds(deltaTime));
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
    public void Run(Action<DrawingCanvas> render)
        => this.Run(new DrawingOptions(), render);

    /// <summary>
    /// Runs the window's event loop and renders one WebGPU canvas per render callback.
    /// </summary>
    /// <param name="options">The drawing options applied to each acquired frame.</param>
    /// <param name="render">The per-frame render callback.</param>
    public void Run(DrawingOptions options, Action<DrawingCanvas> render)
    {
        Guard.NotNull(render, nameof(render));
        this.Run(options, frame => render(frame.Canvas));
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

    private bool TryAcquireFrameCore(
        DrawingOptions options,
        [NotNullWhen(true)] out WebGPUSurfaceFrame? frame)
    {
        this.ThrowIfDisposed();
        return this.resources.TryAcquireFrame(
            this.presentMode,
            this.FramebufferSize,
            options,
            out frame);
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        if (size.X > 0 && size.Y > 0)
        {
            this.resources.ConfigureSurface(this.presentMode, ToSize(size));
        }

        this.FramebufferResized?.Invoke(ToSize(size));
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

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
        silkOptions.WindowState = ToNative(options.WindowState);
        silkOptions.WindowBorder = ToNative(options.WindowBorder);
        silkOptions.TopMost = options.IsTopMost;
        return silkOptions;
    }

    private static Vector2D<int> ToVector(Size size) => new(size.Width, size.Height);

    private static Vector2D<int> ToVector(Point point) => new(point.X, point.Y);

    private static Size ToSize(Vector2D<int> value) => new(value.X, value.Y);

    private static Point ToPoint(Vector2D<int> value) => new(value.X, value.Y);

    private static NativeWindowState ToNative(WebGPUWindowState state)
        => state switch
        {
            WebGPUWindowState.Normal => NativeWindowState.Normal,
            WebGPUWindowState.Minimized => NativeWindowState.Minimized,
            WebGPUWindowState.Maximized => NativeWindowState.Maximized,
            WebGPUWindowState.Fullscreen => NativeWindowState.Fullscreen,
            _ => throw new InvalidOperationException("The WebGPU window state mapping is incomplete.")
        };

    private static WebGPUWindowState FromNative(NativeWindowState state)
        => state switch
        {
            NativeWindowState.Normal => WebGPUWindowState.Normal,
            NativeWindowState.Minimized => WebGPUWindowState.Minimized,
            NativeWindowState.Maximized => WebGPUWindowState.Maximized,
            NativeWindowState.Fullscreen => WebGPUWindowState.Fullscreen,
            _ => throw new InvalidOperationException("The native window state mapping is incomplete.")
        };

    private static NativeWindowBorder ToNative(WebGPUWindowBorder border)
        => border switch
        {
            WebGPUWindowBorder.Resizable => NativeWindowBorder.Resizable,
            WebGPUWindowBorder.Fixed => NativeWindowBorder.Fixed,
            WebGPUWindowBorder.Hidden => NativeWindowBorder.Hidden,
            _ => throw new InvalidOperationException("The WebGPU window border mapping is incomplete.")
        };

    private static WebGPUWindowBorder FromNative(NativeWindowBorder border)
        => border switch
        {
            NativeWindowBorder.Resizable => WebGPUWindowBorder.Resizable,
            NativeWindowBorder.Fixed => WebGPUWindowBorder.Fixed,
            NativeWindowBorder.Hidden => WebGPUWindowBorder.Hidden,
            _ => throw new InvalidOperationException("The native window border mapping is incomplete.")
        };
}
