// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Represents the current state of a <see cref="WebGPUWindow{TPixel}"/>.
/// </summary>
public enum WebGPUWindowState
{
    /// <summary>
    /// The window is in its normal state.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// The window is minimized.
    /// </summary>
    Minimized,

    /// <summary>
    /// The window is maximized.
    /// </summary>
    Maximized,

    /// <summary>
    /// The window is fullscreen.
    /// </summary>
    Fullscreen,
}
