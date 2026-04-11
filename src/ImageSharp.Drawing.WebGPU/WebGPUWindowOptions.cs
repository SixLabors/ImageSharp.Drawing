// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Options for creating a <see cref="WebGPUWindow{TPixel}"/>.
/// </summary>
/// <remarks>
/// These values describe the initial window and scheduling configuration used during construction. Most callers only
/// need to set <see cref="Title"/>, <see cref="Size"/>, and <see cref="PresentMode"/>. The created
/// <see cref="WebGPUWindow{TPixel}"/> can still change many of these values later.
/// </remarks>
public sealed class WebGPUWindowOptions
{
    /// <summary>
    /// Gets or sets the initial window title shown in the title bar.
    /// </summary>
    public string Title { get; set; } = "ImageSharp.Drawing WebGPU";

    /// <summary>
    /// Gets or sets the initial client-area size in pixels.
    /// </summary>
    public Size Size { get; set; } = new(1280, 720);

    /// <summary>
    /// Gets or sets the initial requested window position in screen coordinates.
    /// </summary>
    public Point Position { get; set; } = new(50, 50);

    /// <summary>
    /// Gets or sets a value indicating whether the window should be visible immediately after creation.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Gets or sets the requested upper bound for render callbacks per second.
    /// </summary>
    /// <remarks>
    /// This is a scheduling hint for the underlying window loop rather than a guarantee of presented frame rate.
    /// The chosen <see cref="PresentMode"/> and the platform's display timing still influence what the user sees.
    /// </remarks>
    public double FramesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the requested upper bound for update callbacks per second.
    /// </summary>
    /// <remarks>
    /// Use this when you want simulation or input updates to run at a different cadence from rendering.
    /// </remarks>
    public double UpdatesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window should use event-driven scheduling.
    /// </summary>
    /// <remarks>
    /// Event-driven mode is useful for UI-style apps that should sleep while idle instead of continuously driving a
    /// tight loop. Continuous rendering scenarios often leave this disabled.
    /// </remarks>
    public bool IsEventDriven { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window should stay above normal windows.
    /// </summary>
    public bool TopMost { get; set; }

    /// <summary>
    /// Gets or sets the initial window state such as normal, maximized, or fullscreen.
    /// </summary>
    public WebGPUWindowState WindowState { get; set; } = WebGPUWindowState.Normal;

    /// <summary>
    /// Gets or sets the initial window chrome and resize behavior.
    /// </summary>
    public WebGPUWindowBorder WindowBorder { get; set; } = WebGPUWindowBorder.Resizable;

    /// <summary>
    /// Gets or sets how completed frames are queued for presentation to the display.
    /// </summary>
    /// <remarks>
    /// Choose <see cref="WebGPUPresentMode.Fifo"/> for the usual v-synced behavior,
    /// <see cref="WebGPUPresentMode.Immediate"/> for the lowest latency with possible tearing, or
    /// <see cref="WebGPUPresentMode.Mailbox"/> when you want newer-frame-wins behavior and the backend supports it.
    /// </remarks>
    public WebGPUPresentMode PresentMode { get; set; } = WebGPUPresentMode.Fifo;
}
