// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Returns metadata about the point along a path.
/// </summary>
public readonly struct SegmentInfo
{
    /// <summary>
    /// Gets the point on the path
    /// </summary>
    public PointF Point { get; init; }

    /// <summary>
    /// Gets the angle of the segment. Measured in radians.
    /// </summary>
    public float Angle { get; init; }
}
