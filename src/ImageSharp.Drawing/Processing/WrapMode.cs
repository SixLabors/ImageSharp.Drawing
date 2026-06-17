// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Defines how an <see cref="ImageBrush"/> samples beyond its source region along a single axis.
/// </summary>
public enum WrapMode
{
    /// <summary>
    /// The source region is not repeated; pixels outside it are left transparent.
    /// </summary>
    None = 0,

    /// <summary>
    /// The source region is tiled, repeating edge-to-edge.
    /// </summary>
    Repeat = 1,

    /// <summary>
    /// The source region is tiled, mirroring every other repetition.
    /// </summary>
    Mirror = 2,

    /// <summary>
    /// The source region is not repeated; the nearest edge pixel is sampled for coordinates outside it.
    /// </summary>
    Clamp = 3,
}
