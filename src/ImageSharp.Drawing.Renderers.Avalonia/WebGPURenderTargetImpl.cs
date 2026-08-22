// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
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
        if (TryCreateSurfaceHost(nativeSurface, out WebGPUSurfaceHost host))
        {
            renderTarget = new WebGPURenderTargetImpl(session, host);
            return true;
        }

        renderTarget = null;
        return false;
    }

    private WebGPURenderTargetImpl(WebGPUSurfaceSession session, WebGPUSurfaceHost host)
    {
        this.session = session;
        this.host = host;
    }

    private static bool TryCreateSurfaceHost(INativePlatformHandleSurface nativeSurface, out WebGPUSurfaceHost host)
    {
        switch (nativeSurface.HandleDescriptor)
        {
            case "HWND":
                nint hinstance = Marshal.GetHINSTANCE(typeof(WebGPURenderTargetImpl).Module);
                host = WebGPUSurfaceHost.Win32(nativeSurface.Handle, hinstance);
                return true;

            case "SurfaceView":
                host = WebGPUSurfaceHost.Android(nativeSurface.Handle);
                return true;

            case "NSWindow":
                host = WebGPUSurfaceHost.Cocoa(nativeSurface.Handle);
                return true;

            default:
                // Avalonia currently exposes several GPU-capable platform surfaces through GL/Metal/D3D
                // abstractions rather than full native window descriptors. X11, Wayland, and NSView require
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
        this.isDisposed = true;
    }
}
