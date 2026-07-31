// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Specifies how a clip shape is combined with the current clip region.
/// </summary>
public enum ClipOperation
{
    /// <summary>
    /// Keeps only the area covered by both the current clip region and the clip shape.
    /// </summary>
    Intersection = 0,

    /// <summary>
    /// Removes the clip shape from the current clip region.
    /// </summary>
    Difference = 1
}
