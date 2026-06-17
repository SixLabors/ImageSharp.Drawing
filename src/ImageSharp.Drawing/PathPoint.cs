// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Returns metadata about the point along a path.
/// </summary>
public readonly struct PathPoint
{
    /// <summary>
    /// Gets the point on the path.
    /// </summary>
    public PointF Point { get; init; }

    /// <summary>
    /// Gets the normalized forward tangent of the segment.
    /// </summary>
    public PointF Tangent { get; init; }

    /// <summary>
    /// Gets the forward angle of the segment, in degrees.
    /// </summary>
    public float Angle { get; init; }
}
