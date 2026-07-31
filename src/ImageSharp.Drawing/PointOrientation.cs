// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents the orientation of an ordered triplet of points: the turn taken at the middle point
/// when travelling from the first point to the third.
/// </summary>
internal enum PointOrientation
{
    /// <summary>
    /// The three points lie on (or within tolerance of) a single line.
    /// </summary>
    Collinear = 0,

    /// <summary>
    /// The triplet makes a clockwise turn.
    /// </summary>
    Clockwise = 1,

    /// <summary>
    /// The triplet makes a counter-clockwise turn.
    /// </summary>
    Counterclockwise = 2
}
