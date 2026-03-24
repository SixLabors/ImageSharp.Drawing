// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents one derived linear segment within a <see cref="LinearGeometry"/>.
/// </summary>
/// <remarks>
/// Instances are produced by <see cref="SegmentEnumerator"/> and contain the per-segment values required by current
/// backend scene-building code without forcing each backend to recompute them from the endpoints on every iteration.
/// </remarks>
public readonly struct LinearSegment
{
    /// <summary>
    /// Gets the segment start point.
    /// </summary>
    public required PointF Start { get; init; }

    /// <summary>
    /// Gets the segment end point.
    /// </summary>
    public required PointF End { get; init; }

    /// <summary>
    /// Gets the smaller of <see cref="Start"/>.<see cref="PointF.Y"/> and <see cref="End"/>.<see cref="PointF.Y"/>.
    /// </summary>
    public required float MinY { get; init; }

    /// <summary>
    /// Gets the larger of <see cref="Start"/>.<see cref="PointF.Y"/> and <see cref="End"/>.<see cref="PointF.Y"/>.
    /// </summary>
    public required float MaxY { get; init; }

    /// <summary>
    /// Gets a value indicating whether the segment is horizontal.
    /// </summary>
    /// <remarks>
    /// A segment is horizontal when <see cref="Start"/>.<see cref="PointF.Y"/> equals
    /// <see cref="End"/>.<see cref="PointF.Y"/>.
    /// </remarks>
    public required bool IsHorizontal { get; init; }
}
