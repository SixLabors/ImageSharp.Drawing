// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Options for creating a <see cref="WebGPUWindow{TPixel}"/>. Use this type to choose the initial window title, size, presentation mode, and related startup settings.
/// </summary>
public sealed class WebGPUWindowOptions
{
    /// <summary>
    /// Gets or sets the initial window title.
    /// </summary>
    public string Title { get; set; } = "ImageSharp.Drawing WebGPU";

    /// <summary>
    /// Gets or sets the initial client size in pixels.
    /// </summary>
    public Size Size { get; set; } = new(1280, 720);

    /// <summary>
    /// Gets or sets the initial window position.
    /// </summary>
    public Point Position { get; set; } = new(50, 50);

    /// <summary>
    /// Gets or sets a value indicating whether the window starts visible.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of render callbacks per second.
    /// </summary>
    public double FramesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of update callbacks per second.
    /// </summary>
    public double UpdatesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window uses event-driven scheduling.
    /// </summary>
    public bool IsEventDriven { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window is kept above other windows.
    /// </summary>
    public bool TopMost { get; set; }

    /// <summary>
    /// Gets or sets the initial window state.
    /// </summary>
    public WebGPUWindowState WindowState { get; set; } = WebGPUWindowState.Normal;

    /// <summary>
    /// Gets or sets the initial window border mode.
    /// </summary>
    public WebGPUWindowBorder WindowBorder { get; set; } = WebGPUWindowBorder.Resizable;

    /// <summary>
    /// Gets or sets the present mode used when presenting the window.
    /// </summary>
    public WebGPUPresentMode PresentMode { get; set; } = WebGPUPresentMode.Fifo;

    /// <summary>
    /// Gets or sets the optional window texture format override.
    /// </summary>
    public WebGPUTextureFormatId? Format { get; set; }
}
