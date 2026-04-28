// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// A single acquired drawable frame returned by a WebGPU surface.
/// Use the <see cref="Canvas"/> to draw the frame contents, then dispose the frame to show it on screen.
/// </summary>
/// <typeparam name="TPixel">The canvas pixel format.</typeparam>
public sealed unsafe class WebGPUSurfaceFrame<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly WebGPU api;
    private WebGPUHandle.HandleReference surfaceReference;
    private readonly WebGPUTextureHandle textureHandle;
    private readonly WebGPUTextureViewHandle textureViewHandle;
    private readonly Action? onDisposed;
    private bool isDisposed;

    internal WebGPUSurfaceFrame(
        WebGPU api,
        WebGPUSurfaceHandle surfaceHandle,
        WebGPUTextureHandle textureHandle,
        WebGPUTextureViewHandle textureViewHandle,
        DrawingCanvas<TPixel> canvas,
        Action? onDisposed = null)
    {
        this.api = api;
        this.surfaceReference = surfaceHandle.AcquireReference();
        this.textureHandle = textureHandle;
        this.textureViewHandle = textureViewHandle;
        this.Canvas = canvas;
        this.onDisposed = onDisposed;
    }

    /// <summary>
    /// Gets the drawing canvas for the acquired frame.
    /// </summary>
    public DrawingCanvas<TPixel> Canvas { get; }

    /// <summary>
    /// Disposes the frame, flushing and presenting it, then releasing the per-frame WebGPU resources.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        try
        {
            this.Canvas.Flush();
            this.api.SurfacePresent((Surface*)this.surfaceReference.Handle);
        }
        finally
        {
            this.Canvas.Dispose();
            this.textureViewHandle.Dispose();
            this.textureHandle.Dispose();
            this.surfaceReference.Dispose();
            this.isDisposed = true;
            this.onDisposed?.Invoke();
        }
    }
}
