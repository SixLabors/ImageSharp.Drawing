// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes the window chrome and resize behavior for a <see cref="WebGPUWindow{TPixel}"/>.
/// </summary>
public enum WebGPUWindowBorder
{
    /// <summary>
    /// Uses the normal decorated window frame and allows the user to resize the window.
    /// </summary>
    Resizable = 0,

    /// <summary>
    /// Uses the normal decorated window frame but does not allow user resizing.
    /// </summary>
    Fixed,

    /// <summary>
    /// Hides the standard window border and title bar for a borderless look.
    /// </summary>
    Hidden,
}
