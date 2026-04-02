// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Represents the border mode of a <see cref="WebGPUWindow{TPixel}"/>.
/// </summary>
public enum WebGPUWindowBorder
{
    /// <summary>
    /// The window border is visible and resizable.
    /// </summary>
    Resizable = 0,

    /// <summary>
    /// The window border is visible but fixed-size.
    /// </summary>
    Fixed,

    /// <summary>
    /// The window border is hidden.
    /// </summary>
    Hidden,
}
