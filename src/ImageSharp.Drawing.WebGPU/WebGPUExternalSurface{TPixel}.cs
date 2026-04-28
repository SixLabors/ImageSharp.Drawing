// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// A WebGPU rendering surface bound to an externally-owned native host. Unlike <see cref="WebGPUWindow{TPixel}"/>,
/// this type does not own a platform window; the host application owns the UI object, its lifecycle, and the drawable
/// size notifications forwarded through <see cref="Resize(Size)"/>.
/// </summary>
/// <typeparam name="TPixel">The canvas pixel format.</typeparam>
public sealed class WebGPUExternalSurface<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly WebGPUSurfaceResources<TPixel> resources;
    private WebGPUPresentMode presentMode;
    private Size framebufferSize;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUExternalSurface{TPixel}"/> class.
    /// </summary>
    /// <param name="host">The native surface host that the WebGPU surface will attach to.</param>
    /// <param name="framebufferSize">The initial framebuffer size in pixels.</param>
    public WebGPUExternalSurface(WebGPUSurfaceHost host, Size framebufferSize)
        : this(Configuration.Default, host, framebufferSize, new WebGPUExternalSurfaceOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUExternalSurface{TPixel}"/> class.
    /// </summary>
    /// <param name="host">The native surface host that the WebGPU surface will attach to.</param>
    /// <param name="framebufferSize">The initial framebuffer size in pixels.</param>
    /// <param name="options">The external surface options.</param>
    public WebGPUExternalSurface(
        WebGPUSurfaceHost host,
        Size framebufferSize,
        WebGPUExternalSurfaceOptions options)
        : this(Configuration.Default, host, framebufferSize, options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUExternalSurface{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    /// <param name="host">The native surface host that the WebGPU surface will attach to.</param>
    /// <param name="framebufferSize">The initial framebuffer size in pixels.</param>
    /// <param name="options">The external surface options.</param>
    public WebGPUExternalSurface(
        Configuration configuration,
        WebGPUSurfaceHost host,
        Size framebufferSize,
        WebGPUExternalSurfaceOptions options)
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(options, nameof(options));
        Guard.MustBeGreaterThan(framebufferSize.Width, 0, nameof(framebufferSize));
        Guard.MustBeGreaterThan(framebufferSize.Height, 0, nameof(framebufferSize));

        this.Configuration = configuration;
        this.presentMode = options.PresentMode;
        this.framebufferSize = framebufferSize;
        this.resources = WebGPUSurfaceResources<TPixel>.Create(
            configuration,
            new SilkNativeSurfaceAdapter(host),
            this.presentMode,
            this.framebufferSize);
        this.Format = this.resources.Format;
    }

    /// <summary>
    /// Gets the configuration provided when the external surface was created.
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
    /// Notifies the external surface that the drawable framebuffer has resized and reconfigures the swapchain when the
    /// size changes.
    /// </summary>
    /// <param name="framebufferSize">The new framebuffer size in pixels.</param>
    public void Resize(Size framebufferSize)
    {
        this.ThrowIfDisposed();
        if (framebufferSize.Width <= 0 || framebufferSize.Height <= 0)
        {
            return;
        }

        if (framebufferSize == this.framebufferSize)
        {
            return;
        }

        this.framebufferSize = framebufferSize;
        this.resources.ConfigureSurface(this.presentMode, this.framebufferSize);
    }

    /// <summary>
    /// Tries to acquire the next drawable frame.
    /// </summary>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/>.</returns>
    public bool TryAcquireFrame([NotNullWhen(true)] out WebGPUSurfaceFrame<TPixel>? frame)
        => this.TryAcquireFrameCore(new DrawingOptions(), out frame);

    /// <summary>
    /// Tries to acquire the next drawable frame.
    /// </summary>
    /// <param name="options">The drawing options for the acquired frame.</param>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/>.</returns>
    public bool TryAcquireFrame(DrawingOptions options, [NotNullWhen(true)] out WebGPUSurfaceFrame<TPixel>? frame)
        => this.TryAcquireFrameCore(options, out frame);

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
        DrawingOptions options,
        [NotNullWhen(true)] out WebGPUSurfaceFrame<TPixel>? frame)
    {
        this.ThrowIfDisposed();
        return this.resources.TryAcquireFrame(
            this.presentMode,
            this.framebufferSize,
            options,
            out frame);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);
}
