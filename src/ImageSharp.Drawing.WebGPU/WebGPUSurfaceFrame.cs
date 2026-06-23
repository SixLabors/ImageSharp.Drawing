// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// A single acquired drawable frame returned by a WebGPU surface.
/// Use the <see cref="Canvas"/> to draw the frame contents, then dispose the frame to show it on screen.
/// </summary>
public sealed unsafe class WebGPUSurfaceFrame : IDisposable
{
    private readonly WebGPU api;
    private readonly WebGPUDeviceContext deviceContext;
    private readonly WebGPUTextureFormat format;
    private WebGPUHandle.HandleReference surfaceReference;
    private readonly WebGPUTextureHandle textureHandle;
    private readonly WebGPUTextureViewHandle textureViewHandle;
    private readonly Action? onDisposed;
    private bool isDisposed;

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
            this.textureViewHandle.Dispose();
            this.textureHandle.Dispose();
            this.surfaceReference.Dispose();
            this.isDisposed = true;
            this.onDisposed?.Invoke();
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);
}
