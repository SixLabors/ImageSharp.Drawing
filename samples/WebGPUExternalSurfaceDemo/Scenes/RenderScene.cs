// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using WebGPUExternalSurfaceDemo.Controls;

namespace WebGPUExternalSurfaceDemo.Scenes;

/// <summary>
/// Base class for a demo scene rendered into a <see cref="WebGPURenderControl"/>.
/// Each scene owns its view, frame cadence, input, and any controls that explain or manipulate it.
/// </summary>
internal abstract class RenderScene
{
    /// <summary>
    /// Gets the display name shown in the demo launcher.
    /// </summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// Gets the frame scheduling mode required by this scene.
    /// </summary>
    protected virtual WebGPURenderMode RenderMode => WebGPURenderMode.OnDemand;

    /// <summary>
    /// Creates the complete WinForms view for this scene.
    /// </summary>
    /// <param name="surfaceSession">The device session shared by all sample scenes.</param>
    /// <returns>The control inserted into the scene's tab.</returns>
    public Control CreateView(WebGPUSurfaceSession surfaceSession)
    {
        WebGPURenderControl renderControl = new(surfaceSession)
        {
            Dock = DockStyle.Fill,
            RenderMode = this.RenderMode,
        };

        // Register scene painting before scene-specific overlays. An overlay that listens
        // to PaintFrame therefore observes state produced by the frame it describes.
        renderControl.PaintFrame += this.Paint;

        Control content = this.CreateContent(renderControl);
        this.ConfigureControl(renderControl);

        // Every scene uses the same pointer plumbing. Keeping it here lets individual
        // scenes concentrate on their own interaction and presentation.
        renderControl.MouseDown += (_, e) =>
        {
            this.OnMouseDown(e);
            renderControl.Invalidate();
        };

        renderControl.MouseMove += (_, e) =>
        {
            this.OnMouseMove(e);
            renderControl.Invalidate();
        };

        renderControl.MouseUp += (_, e) =>
        {
            this.OnMouseUp(e);
            renderControl.Invalidate();
        };

        renderControl.MouseWheel += (_, e) =>
        {
            this.OnMouseWheel(e);
            renderControl.Invalidate();
        };

        return content;
    }

    /// <summary>
    /// Initializes resources that require the shared WebGPU device after the host is loaded.
    /// </summary>
    /// <param name="deviceContext">The initialized shared device context.</param>
    public virtual void OnHostLoaded(WebGPUDeviceContext deviceContext)
    {
    }

    /// <summary>
    /// Creates any scene-specific chrome around the render control.
    /// </summary>
    /// <param name="renderControl">The control that presents the scene.</param>
    /// <returns>The complete scene content.</returns>
    protected virtual Control CreateContent(WebGPURenderControl renderControl) => renderControl;

    /// <summary>
    /// Configures scene-specific control behavior such as keyboard input.
    /// </summary>
    /// <param name="renderControl">The control that presents the scene.</param>
    protected virtual void ConfigureControl(WebGPURenderControl renderControl)
    {
    }

    /// <summary>
    /// Draws the scene into <paramref name="canvas"/> for the current frame.
    /// </summary>
    /// <param name="canvas">The per-frame drawing canvas bound to the external surface's swap-chain texture.</param>
    /// <param name="deltaTime">Elapsed time since the previous frame. Scenes that render from absolute state can ignore it.</param>
    public abstract void Paint(DrawingCanvas canvas, TimeSpan deltaTime);

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
