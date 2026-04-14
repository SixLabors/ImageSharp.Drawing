// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// A single frame returned by <see cref="WebGPUWindow{TPixel}.TryAcquireFrame(TimeSpan, out WebGPUWindowFrame{TPixel}?)"/>.
/// Use the <see cref="Canvas"/> to draw the frame contents, then either call <see cref="Present"/> or dispose the frame to show it on screen.
/// </summary>
/// <typeparam name="TPixel">The canvas pixel format.</typeparam>
public sealed unsafe class WebGPUWindowFrame<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly WebGPU api;
    private readonly WebGPUHandle.HandleReference surfaceReference;
    private readonly WebGPUTextureHandle textureHandle;
    private readonly WebGPUTextureViewHandle textureViewHandle;
    private bool isDisposed;
    private bool presented;

    internal WebGPUWindowFrame(
        WebGPU api,
        WebGPUSurfaceHandle surfaceHandle,
        WebGPUTextureHandle textureHandle,
        WebGPUTextureViewHandle textureViewHandle,
        DrawingCanvas<TPixel> canvas,
        Rectangle bounds,
        Size clientSize,
        Size framebufferSize,
        TimeSpan deltaTime,
        long frameIndex)
    {
        this.api = api;
        this.surfaceReference = surfaceHandle.AcquireReference();
        this.textureHandle = textureHandle;
        this.textureViewHandle = textureViewHandle;
        this.Canvas = canvas;
        this.Bounds = bounds;
        this.ClientSize = clientSize;
        this.FramebufferSize = framebufferSize;
        this.DeltaTime = deltaTime;
        this.FrameIndex = frameIndex;
    }

    /// <summary>
    /// Gets the drawing canvas for the acquired frame.
    /// </summary>
    public DrawingCanvas<TPixel> Canvas { get; }

    /// <summary>
    /// Gets the frame bounds in pixels.
    /// </summary>
    public Rectangle Bounds { get; }

    /// <summary>
    /// Gets the client size at frame acquisition time.
    /// </summary>
    public Size ClientSize { get; }

    /// <summary>
    /// Gets the framebuffer size at frame acquisition time.
    /// </summary>
    public Size FramebufferSize { get; }

    /// <summary>
    /// Gets the elapsed time since the previous frame.
    /// </summary>
    public TimeSpan DeltaTime { get; }

    /// <summary>
    /// Gets the zero-based frame index.
    /// </summary>
    public long FrameIndex { get; }

    /// <summary>
    /// Flushes pending canvas work and presents the frame on screen.
    /// </summary>
    /// <remarks>
    /// This method finalizes the current frame. If you do not call it explicitly, <see cref="Dispose"/> will flush and present the frame automatically.
    /// </remarks>
    public void Present()
    {
        ObjectDisposedException.ThrowIf(this.isDisposed, this);
        if (this.presented)
        {
            return;
        }

        this.Canvas.Flush();
        this.api.SurfacePresent((Surface*)this.surfaceReference.Handle);
        this.presented = true;
    }

    /// <summary>
    /// Disposes the frame, flushing and presenting it if needed, then releasing the per-frame WebGPU resources.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        try
        {
            if (!this.presented)
            {
                this.Canvas.Flush();
                this.api.SurfacePresent((Surface*)this.surfaceReference.Handle);
                this.presented = true;
            }
        }
        finally
        {
            this.Canvas.Dispose();
            this.textureViewHandle.Dispose();
            this.textureHandle.Dispose();
            this.surfaceReference.Dispose();
            this.isDisposed = true;
        }
    }
}
