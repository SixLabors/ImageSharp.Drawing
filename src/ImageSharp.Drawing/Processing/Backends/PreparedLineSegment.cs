// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One prepared line segment in world-space coordinates.
/// </summary>
public readonly struct PreparedLineSegment
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PreparedLineSegment"/> struct.
    /// </summary>
    /// <param name="p0">The segment start point.</param>
    /// <param name="p1">The segment end point.</param>
    public PreparedLineSegment(PointF p0, PointF p1)
    {
        this.P0 = p0;
        this.P1 = p1;
        this.MinY = Math.Min(p0.Y, p1.Y);
        this.MaxY = Math.Max(p0.Y, p1.Y);
    }

    /// <summary>
    /// Gets the segment start point.
    /// </summary>
    public PointF P0 { get; }

    /// <summary>
    /// Gets the segment end point.
    /// </summary>
    public PointF P1 { get; }

    /// <summary>
    /// Gets the smaller Y value of the two endpoints.
    /// </summary>
    public float MinY { get; }

    /// <summary>
    /// Gets the larger Y value of the two endpoints.
    /// </summary>
    public float MaxY { get; }
}
