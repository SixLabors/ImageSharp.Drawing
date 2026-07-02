// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Identifies the clip primitive represented by a drawing clip descriptor.
/// </summary>
public enum DrawingClipKind : byte
{
    /// <summary>
    /// The clip is an axis-aligned rectangle.
    /// </summary>
    Rectangle,

    /// <summary>
    /// The clip is a set of integer rectangles.
    /// </summary>
    IntegerRegion,

    /// <summary>
    /// The clip is a set of floating-point rectangles.
    /// </summary>
    Region,

    /// <summary>
    /// The clip is path geometry.
    /// </summary>
    Path
}
