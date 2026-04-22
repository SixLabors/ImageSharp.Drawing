// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// A WebGPU rendering surface bound to an externally-owned native window. Unlike <see cref="WebGPUWindow{TPixel}"/>
/// this type does not own a platform window; the host application is responsible for the window's lifecycle and must
/// tell the hosted window when the client area resizes via <see cref="Resize(int, int)"/>.
/// </summary>
/// <typeparam name="TPixel">The canvas pixel format.</typeparam>
public sealed class WebGPUHostedWindow<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly WebGPUWindowResources<TPixel> resources;
    private WebGPUPresentMode presentMode;
    private Size framebufferSize;
    private bool isDisposed;
    private long frameIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUHostedWindow{TPixel}"/> class.
    /// </summary>
    /// <param name="host">The native window host that the WebGPU surface will attach to.</param>
    /// <param name="width">The initial client-area width in pixels.</param>
    /// <param name="height">The initial client-area height in pixels.</param>
    public WebGPUHostedWindow(WebGPUWindowHost host, int width, int height)
        : this(Configuration.Default, host, width, height, new WebGPUHostedWindowOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUHostedWindow{TPixel}"/> class.
    /// </summary>
    /// <param name="host">The native window host that the WebGPU surface will attach to.</param>
    /// <param name="width">The initial client-area width in pixels.</param>
    /// <param name="height">The initial client-area height in pixels.</param>
    /// <param name="options">The hosted window options.</param>
    public WebGPUHostedWindow(WebGPUWindowHost host, int width, int height, WebGPUHostedWindowOptions options)
        : this(Configuration.Default, host, width, height, options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUHostedWindow{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    /// <param name="host">The native window host that the WebGPU surface will attach to.</param>
    /// <param name="width">The initial client-area width in pixels.</param>
    /// <param name="height">The initial client-area height in pixels.</param>
    /// <param name="options">The hosted window options.</param>
    public WebGPUHostedWindow(
        Configuration configuration,
        WebGPUWindowHost host,
        int width,
        int height,
        WebGPUHostedWindowOptions options)
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(options, nameof(options));
        Guard.MustBeGreaterThan(width, 0, nameof(width));
        Guard.MustBeGreaterThan(height, 0, nameof(height));

        this.Configuration = configuration;
        this.presentMode = options.PresentMode;
        this.framebufferSize = new Size(width, height);
        this.resources = WebGPUWindowResources<TPixel>.Create(
            configuration,
            new SilkNativeWindowAdapter(host),
            this.presentMode,
            this.framebufferSize);
        this.Format = this.resources.Format;
    }

    /// <summary>
    /// Gets the configuration provided when the hosted window was created.
    /// </summary>
    public Configuration Configuration { get; }

    /// <summary>
    /// Gets the swapchain texture format.
    /// </summary>
    public WebGPUTextureFormatId Format { get; }

    /// <summary>
    /// Gets the current framebuffer size in pixels.
    /// </summary>
    public Size FramebufferSize => this.framebufferSize;

    /// <summary>
    /// Gets or sets the swapchain present mode.
    /// </summary>
    public WebGPUPresentMode PresentMode
    {
        get => this.presentMode;
        set
        {
            this.presentMode = value;
            this.resources.ConfigureSurface(this.presentMode, this.framebufferSize);
        }
    }

    /// <summary>
    /// Notifies the hosted window that the client area has resized. Reconfigures the swapchain on the next acquire.
    /// </summary>
    /// <param name="width">The new client-area width in pixels.</param>
    /// <param name="height">The new client-area height in pixels.</param>
    public void Resize(int width, int height)
    {
        this.ThrowIfDisposed();
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Size newSize = new(width, height);
        if (newSize == this.framebufferSize)
        {
            return;
        }

        this.framebufferSize = newSize;
        this.resources.ConfigureSurface(this.presentMode, this.framebufferSize);
    }

    /// <summary>
    /// Tries to acquire the next drawable frame.
    /// </summary>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/>.</returns>
    public bool TryAcquireFrame([NotNullWhen(true)] out WebGPUWindowFrame<TPixel>? frame)
        => this.TryAcquireFrameCore(TimeSpan.Zero, new DrawingOptions(), out frame);

    /// <summary>
    /// Tries to acquire the next drawable frame.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since the previous frame.</param>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/>.</returns>
    public bool TryAcquireFrame(TimeSpan deltaTime, [NotNullWhen(true)] out WebGPUWindowFrame<TPixel>? frame)
        => this.TryAcquireFrameCore(deltaTime, new DrawingOptions(), out frame);

    /// <summary>
    /// Tries to acquire the next drawable frame.
    /// </summary>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <param name="options">The drawing options for the acquired frame.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/>.</returns>
    public bool TryAcquireFrame([NotNullWhen(true)] out WebGPUWindowFrame<TPixel>? frame, DrawingOptions options)
        => this.TryAcquireFrameCore(TimeSpan.Zero, options, out frame);

    /// <summary>
    /// Tries to acquire the next drawable frame.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since the previous frame.</param>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <param name="options">The drawing options for the acquired frame.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/>.</returns>
    public bool TryAcquireFrame(TimeSpan deltaTime, [NotNullWhen(true)] out WebGPUWindowFrame<TPixel>? frame, DrawingOptions options)
        => this.TryAcquireFrameCore(deltaTime, options, out frame);

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.resources.Dispose();
        this.isDisposed = true;
    }

    private bool TryAcquireFrameCore(
        TimeSpan deltaTime,
        DrawingOptions options,
        [NotNullWhen(true)] out WebGPUWindowFrame<TPixel>? frame)
    {
        this.ThrowIfDisposed();
        return this.resources.TryAcquireFrame(
            this.presentMode,
            this.framebufferSize,
            this.framebufferSize,
            deltaTime,
            this.frameIndex++,
            options,
            out frame);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);
}
