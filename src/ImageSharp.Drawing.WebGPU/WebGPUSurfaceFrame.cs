// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// A single acquired drawable frame returned by a WebGPU surface.
/// Use the <see cref="Canvas"/> to draw the frame contents, then dispose the frame to show it on screen.
/// </summary>
/// <remarks>
/// Disposal submits the recorded canvas work, presents the surface, releases the per-frame texture and view,
/// and signals the owning surface so the next <c>TryAcquireFrame</c> call can succeed. Only one frame can be
/// outstanding per surface at a time.
/// </remarks>
public sealed unsafe class WebGPUSurfaceFrame : IDisposable
{
    private readonly WebGPU api;
    private readonly WebGPUDeviceContext deviceContext;
    private readonly WebGPUTextureFormat format;

    // Mutable struct: Dispose nulls its internal owner guard, so the field must not be readonly
    // or disposal would run on a defensive copy and leave the field's guard intact.
    private WebGPUHandle.HandleReference surfaceReference;
    private readonly WebGPUTextureHandle textureHandle;
    private readonly WebGPUTextureViewHandle textureViewHandle;
    private readonly Action? onDisposed;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSurfaceFrame"/> class taking ownership of the per-frame
    /// texture and view handles and holding an acquired reference on the surface handle until disposal.
    /// </summary>
    /// <param name="api">The shared WebGPU API loader.</param>
    /// <param name="deviceContext">The device context the frame renders through.</param>
    /// <param name="format">The swapchain texture format; used when creating matching render targets.</param>
    /// <param name="surfaceHandle">The surface handle presented on disposal. The frame holds a reference, not ownership.</param>
    /// <param name="textureHandle">The owned per-frame texture handle, released on disposal.</param>
    /// <param name="textureViewHandle">The owned per-frame texture-view handle, released on disposal.</param>
    /// <param name="canvas">The drawing canvas targeting the acquired texture.</param>
    /// <param name="onDisposed">Invoked after disposal completes; the owning surface uses this to allow the next acquire.</param>
    internal WebGPUSurfaceFrame(
        WebGPU api,
        WebGPUDeviceContext deviceContext,
        WebGPUTextureFormat format,
        WebGPUSurfaceHandle surfaceHandle,
        WebGPUTextureHandle textureHandle,
        WebGPUTextureViewHandle textureViewHandle,
        DrawingCanvas canvas,
        Action? onDisposed = null)
    {
        this.api = api;
        this.deviceContext = deviceContext;
        this.surfaceReference = surfaceHandle.AcquireReference();
        this.textureHandle = textureHandle;
        this.textureViewHandle = textureViewHandle;
        this.format = format;
        this.Canvas = canvas;
        this.onDisposed = onDisposed;
    }

    /// <summary>
    /// Gets the drawing canvas for the acquired frame.
    /// </summary>
    public DrawingCanvas Canvas { get; }

    /// <summary>
    /// Creates an empty render target with the same texture format as this frame.
    /// </summary>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    /// <returns>The created render target.</returns>
    /// <remarks>
    /// The created target does not contain a copy of this frame's pixels.
    /// </remarks>
    public WebGPURenderTarget CreateRenderTarget(int width, int height)
    {
        this.ThrowIfDisposed();

        return this.deviceContext.CreateRenderTarget(this.format, width, height);
    }

    /// <summary>
    /// Disposes the frame, rendering and presenting it, then releasing the per-frame WebGPU resources.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        try
        {
            // Dispose submits the canvas work. Present only after rendering has targeted this acquired surface texture.
            this.Canvas.Dispose();
            this.api.SurfacePresent((Surface*)this.surfaceReference.Handle);
        }
        finally
        {
            // Release the per-frame resources even when submit or present throws, and signal the
            // owner last so a new frame can only be acquired after this one is fully torn down.
            this.textureViewHandle.Dispose();
            this.textureHandle.Dispose();
            this.surfaceReference.Dispose();
            this.isDisposed = true;
            this.onDisposed?.Invoke();
        }
    }

    /// <summary>
    /// Throws when this frame has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);
}
