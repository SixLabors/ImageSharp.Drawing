// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes the startup or current state of a <see cref="WebGPUWindow{TPixel}"/>.
/// </summary>
public enum WebGPUWindowState
{
    /// <summary>
    /// Opens as a normal restored window using its configured size.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Opens minimized to the taskbar or dock.
    /// </summary>
    Minimized,

    /// <summary>
    /// Opens maximized to fill the normal desktop work area.
    /// </summary>
    Maximized,

    /// <summary>
    /// Opens in fullscreen mode and occupies the full display.
    /// </summary>
    Fullscreen,
}
