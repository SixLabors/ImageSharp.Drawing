// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// A WebGPU rendering surface bound to an externally-owned native host. Unlike <see cref="WebGPUWindow"/>,
/// this type does not own a platform window; the host application owns the UI object, its lifecycle, and the drawable
/// size notifications forwarded through <see cref="Resize(Size)"/>.
/// </summary>
public sealed class WebGPUExternalSurface : IDisposable
{
    private readonly WebGPUSurfaceResources resources;
    private readonly WebGPUSurfaceSession? ownedSession;

    // Last size forwarded by the host via Resize. A zero-area value pauses acquisition while
    // the native surface retains its last valid configuration for the next nonzero resize.
    private Size framebufferSize;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUExternalSurface"/> class.
    /// </summary>
    /// <param name="host">The native surface host that the WebGPU surface will attach to.</param>
    /// <param name="framebufferSize">The initial framebuffer size in pixels.</param>
    public WebGPUExternalSurface(WebGPUSurfaceHost host, Size framebufferSize)
        : this(Configuration.Default, host, framebufferSize, new WebGPUExternalSurfaceOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUExternalSurface"/> class.
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
    /// Initializes a new instance of the <see cref="WebGPUExternalSurface"/> class.
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

        this.framebufferSize = framebufferSize;
        WebGPUSurfaceSession session = new(configuration);

        try
        {
            this.resources = WebGPUSurfaceResources.Create(
                session,
                host,
                options.Format,
                options.AlphaMode,
                options.PresentMode,
                this.framebufferSize);

            this.ownedSession = session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUExternalSurface"/> class that borrows the device resources owned by <paramref name="session"/>.
    /// </summary>
    /// <param name="session">The session shared with related presentation surfaces.</param>
    /// <param name="host">The native surface host that the WebGPU surface will attach to.</param>
    /// <param name="framebufferSize">The initial framebuffer size in pixels.</param>
    /// <param name="options">The external surface options.</param>
    internal WebGPUExternalSurface(
        WebGPUSurfaceSession session,
        WebGPUSurfaceHost host,
        Size framebufferSize,
        WebGPUExternalSurfaceOptions options)
    {
        this.framebufferSize = framebufferSize;
        this.resources = WebGPUSurfaceResources.Create(
            session,
            host,
            options.Format,
            options.AlphaMode,
            options.PresentMode,
            this.framebufferSize);
    }

    /// <summary>
    /// Notifies the external surface that the drawable framebuffer has resized and reconfigures the swapchain when the
    /// size changes. Zero-area sizes (minimize, mid-layout) pause frame acquisition while the last valid native
    /// configuration is retained.
    /// </summary>
    /// <param name="framebufferSize">The new framebuffer size in pixels.</param>
    public void Resize(Size framebufferSize)
    {
        this.ThrowIfDisposed();
        if (framebufferSize == this.framebufferSize)
        {
            return;
        }

        this.framebufferSize = framebufferSize;

        if (framebufferSize.Width <= 0 || framebufferSize.Height <= 0)
        {
            return;
        }

        this.resources.ConfigureSurface(this.resources.PresentMode, this.framebufferSize);
    }

    /// <summary>
    /// Tries to acquire the next drawable frame using default drawing options.
    /// </summary>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// A <see langword="false"/> result means no drawable frame is available right now, for example because the
    /// surface was lost, outdated, timed out, or has a zero-sized framebuffer.
    /// Dispose the returned frame when you are done with it to present it and release its per-frame resources.
    /// </remarks>
    public bool TryAcquireFrame([NotNullWhen(true)] out WebGPUSurfaceFrame? frame)
        => this.TryAcquireFrameCore(new DrawingOptions(), out frame);

    /// <summary>
    /// Tries to acquire the next drawable frame.
    /// </summary>
    /// <param name="options">The drawing options for the acquired frame.</param>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// A <see langword="false"/> result means no drawable frame is available right now, for example because the
    /// surface was lost, outdated, timed out, or has a zero-sized framebuffer.
    /// Dispose the returned frame when you are done with it to present it and release its per-frame resources.
    /// </remarks>
    public bool TryAcquireFrame(DrawingOptions options, [NotNullWhen(true)] out WebGPUSurfaceFrame? frame)
        => this.TryAcquireFrameCore(options, out frame);

    /// <summary>
    /// Releases the WebGPU surface stack. The native host handles supplied through
    /// <see cref="WebGPUSurfaceHost"/> remain owned by the caller and are not released.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.resources.Dispose();
        this.ownedSession?.Dispose();
        this.isDisposed = true;
    }

    /// <summary>
    /// Acquires the next drawable frame from the shared surface resources using the last size reported via
    /// <see cref="Resize(Size)"/>.
    /// </summary>
    /// <param name="options">The drawing options for the acquired frame.</param>
    /// <param name="frame">Receives the acquired frame on success.</param>
    /// <returns><see langword="true"/> when a frame is available; otherwise <see langword="false"/>.</returns>
    private bool TryAcquireFrameCore(
        DrawingOptions options,
        [NotNullWhen(true)] out WebGPUSurfaceFrame? frame)
    {
        this.ThrowIfDisposed();
        return this.resources.TryAcquireFrame(
            this.resources.PresentMode,
            this.framebufferSize,
            options,
            out frame);
    }

    /// <summary>
    /// Throws when this surface has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);
}
