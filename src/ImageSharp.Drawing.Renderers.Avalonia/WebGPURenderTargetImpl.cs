// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using ImageSharpSize = SixLabors.ImageSharp.Size;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// WebGPU render target bound directly to an Avalonia native window surface.
/// </summary>
internal sealed class WebGPURenderTargetImpl : IRenderTarget
{
    private readonly WebGPUSurfaceHost host;
    private readonly WebGPUSurfaceSession session;
    private readonly nint x11Display;
    private WebGPUExternalSurface? surface;
    private WebGPUCompositeAlphaMode alphaMode;
    private bool isDisposed;

    /// <summary>
    /// Attempts to create a WebGPU render target for an Avalonia native surface.
    /// </summary>
    /// <param name="session">The WebGPU session shared by every Avalonia top-level.</param>
    /// <param name="nativeSurface">The Avalonia-owned native surface to present into.</param>
    /// <param name="renderTarget">The created WebGPU render target.</param>
    /// <returns><see langword="true"/> when the native surface can be used by WebGPU.</returns>
    public static bool TryCreate(WebGPUSurfaceSession session, INativePlatformHandleSurface nativeSurface, [NotNullWhen(true)] out WebGPURenderTargetImpl? renderTarget)
    {
        if (TryCreateSurfaceHost(nativeSurface, out WebGPUSurfaceHost host, out nint x11Display))
        {
            renderTarget = new WebGPURenderTargetImpl(session, host, x11Display);
            return true;
        }

        renderTarget = null;
        return false;
    }

    private WebGPURenderTargetImpl(WebGPUSurfaceSession session, WebGPUSurfaceHost host, nint x11Display)
    {
        this.session = session;
        this.host = host;
        this.x11Display = x11Display;
    }

    private static bool TryCreateSurfaceHost(INativePlatformHandleSurface nativeSurface, out WebGPUSurfaceHost host, out nint x11Display)
    {
        x11Display = 0;
        switch (nativeSurface.HandleDescriptor)
        {
            case "HWND":
                // Avalonia registers its window classes against the process executable, so that is
                // the module that owns this window. The managed assembly's module would be the wrong one.
                host = WebGPUSurfaceHost.Win32(nativeSurface.Handle, Kernel32.GetModuleHandle(null));
                return true;

            case "XID":
                // Avalonia exposes the window id but keeps its Display* internal. Window ids are valid
                // on any connection to the same server, so open our own connection and keep it open for
                // the lifetime of the render target; the WebGPU surface is created against it.
                x11Display = Xlib.XOpenDisplay(null);
                if (x11Display == 0)
                {
                    host = default;
                    return false;
                }

                host = WebGPUSurfaceHost.X11(x11Display, (nuint)nativeSurface.Handle);
                return true;

            case "SurfaceView":
                host = WebGPUSurfaceHost.Android(nativeSurface.Handle);
                return true;

            case "NSWindow":
                host = WebGPUSurfaceHost.Cocoa(nativeSurface.Handle);
                return true;

            default:
                // Avalonia currently exposes several GPU-capable platform surfaces through GL/Metal/D3D
                // abstractions rather than full native window descriptors. Wayland and NSView require
                // additional upstream handle metadata before they can be mapped to Silk.NET WebGPU hosts here.
                host = default;
                return false;
        }
    }

    /// <inheritdoc />
    public RenderTargetProperties Properties { get; } = default;

    /// <inheritdoc />
    public PlatformRenderTargetState PlatformRenderTargetState
        => this.isDisposed ? PlatformRenderTargetState.Disposed : PlatformRenderTargetState.Ready;

    /// <inheritdoc />
    public IDrawingContextImpl CreateDrawingContext(IRenderTarget.RenderTargetSceneInfo sceneInfo, out RenderTargetDrawingContextProperties properties)
    {
        ObjectDisposedException.ThrowIf(this.isDisposed, this);

        ImageSharpSize framebufferSize = new(sceneInfo.Size.Width, sceneInfo.Size.Height);

        if (framebufferSize.Width <= 0 || framebufferSize.Height <= 0)
        {
            throw new RenderTargetNotReadyException();
        }

        WebGPUCompositeAlphaMode alphaMode = sceneInfo.TransparencyLevel == CompositionTransparencyLevel.None
            ? WebGPUCompositeAlphaMode.Auto
            : WebGPUCompositeAlphaMode.Premultiplied;

        if (this.surface is null || this.alphaMode != alphaMode)
        {
            // The surface configuration fixes its alpha interpretation. Avalonia may change a
            // top-level's transparency state, so recreate only at that state transition.
            this.surface?.Dispose();
            this.surface = this.session.CreateSurface(
                this.host,
                framebufferSize,
                new WebGPUExternalSurfaceOptions
                {
                    Format = WebGPUTextureFormat.Bgra8Unorm,
                    AlphaMode = alphaMode
                });

            this.alphaMode = alphaMode;
        }
        else
        {
            this.surface.Resize(framebufferSize);
        }

        if (!this.surface.TryAcquireFrame(out WebGPUSurfaceFrame? frame))
        {
            throw new RenderTargetNotReadyException();
        }

        properties = default;
        AvaloniaVector dpi = new(96D * sceneInfo.Scaling, 96D * sceneInfo.Scaling);

        return new DrawingContextImpl(frame, dpi, sceneInfo.Size);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.surface?.Dispose();

        // The surface is gone, so the X connection it was created on can close.
        if (this.x11Display != 0)
        {
            Xlib.XCloseDisplay(this.x11Display);
        }

        this.isDisposed = true;
    }
}
