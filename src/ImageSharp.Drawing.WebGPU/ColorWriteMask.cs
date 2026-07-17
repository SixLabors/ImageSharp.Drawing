// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Identifies the color channels written by a render target.
/// </summary>
[Flags]
internal enum ColorWriteMask : ulong
{
    /// <summary>
    /// No color channel is written.
    /// </summary>
    None = 0,

    /// <summary>
    /// The red channel is written.
    /// </summary>
    Red = 1,

    /// <summary>
    /// The green channel is written.
    /// </summary>
    Green = 2,

    /// <summary>
    /// The blue channel is written.
    /// </summary>
    Blue = 4,

    /// <summary>
    /// The alpha channel is written.
    /// </summary>
    Alpha = 8,

    /// <summary>
    /// Every color channel is written.
    /// </summary>
    All = Red | Green | Blue | Alpha
}
