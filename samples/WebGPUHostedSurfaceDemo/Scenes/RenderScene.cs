// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Size = SixLabors.ImageSharp.Size;

namespace WebGPUHostedSurfaceDemo.Scenes;

/// <summary>
/// Base class for a demo scene rendered into a shared <see cref="WebGPURenderControl"/>.
/// Derived scenes override <see cref="Paint"/> and optionally the mouse handlers.
/// </summary>
internal abstract class RenderScene
{
    /// <summary>
    /// Gets the display name shown in the demo launcher.
    /// </summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// Draws the scene into <paramref name="canvas"/> for the current frame.
    /// </summary>
    /// <param name="canvas">The per-frame drawing canvas bound to the hosted surface's swap-chain texture.</param>
    /// <param name="viewportSize">The framebuffer size in pixels.</param>
    /// <param name="deltaTime">Elapsed time since the previous frame.</param>
    public abstract void Paint(DrawingCanvas<Bgra32> canvas, Size viewportSize, TimeSpan deltaTime);

    /// <summary>
    /// Handles a mouse-button press. Default implementation is a no-op.
    /// </summary>
    public virtual void OnMouseDown(MouseEventArgs e)
    {
    }

    /// <summary>
    /// Handles mouse movement. Default implementation is a no-op.
    /// </summary>
    public virtual void OnMouseMove(MouseEventArgs e)
    {
    }

    /// <summary>
    /// Handles a mouse-button release. Default implementation is a no-op.
    /// </summary>
    public virtual void OnMouseUp(MouseEventArgs e)
    {
    }

    /// <summary>
    /// Handles mouse-wheel events. Default implementation is a no-op.
    /// </summary>
    public virtual void OnMouseWheel(MouseEventArgs e)
    {
    }
}
