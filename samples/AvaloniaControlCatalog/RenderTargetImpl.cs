// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Avalonia.Platform;
using Avalonia.Platform.Surfaces;

namespace AvaloniaControlCatalog;

/// <summary>
/// Framebuffer render target for the ImageSharp.Drawing sample renderer.
/// </summary>
internal sealed class RenderTargetImpl : IRenderTarget
{
    private IFramebufferRenderTarget? framebufferRenderTarget;

    /// <summary>
    /// Initializes a new framebuffer render target wrapper.
    /// </summary>
    /// <param name="framebufferRenderTarget">The Avalonia framebuffer render target.</param>
    public RenderTargetImpl(IFramebufferRenderTarget framebufferRenderTarget)
        => this.framebufferRenderTarget = framebufferRenderTarget;

    /// <inheritdoc />
    public RenderTargetProperties Properties => new()
    {
        IsSuitableForDirectRendering = true,
        RetainsPreviousFrameContents = this.framebufferRenderTarget?.RetainsFrameContents == true
    };

    /// <inheritdoc />
    public PlatformRenderTargetState PlatformRenderTargetState
        => this.framebufferRenderTarget?.State ?? PlatformRenderTargetState.Disposed;

    /// <inheritdoc />
    public IDrawingContextImpl CreateDrawingContext(IRenderTarget.RenderTargetSceneInfo sceneInfo, out RenderTargetDrawingContextProperties properties)
    {
        if (this.framebufferRenderTarget is null)
        {
            throw new ObjectDisposedException(nameof(RenderTargetImpl));
        }

        ILockedFramebuffer framebuffer = this.framebufferRenderTarget.Lock(sceneInfo, out FramebufferLockProperties lockProperties);

        properties = new RenderTargetDrawingContextProperties
        {
            PreviousFrameIsRetained = lockProperties.PreviousFrameIsRetained
        };

        return new DrawingContextImpl(framebuffer);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.framebufferRenderTarget?.Dispose();
        this.framebufferRenderTarget = null;
    }
}
