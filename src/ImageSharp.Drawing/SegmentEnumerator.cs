// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Enumerates the derived linear segments in a <see cref="LinearGeometry"/>.
/// </summary>
/// <remarks>
/// The enumerator derives segments from <see cref="LinearGeometry.Points"/> and <see cref="LinearGeometry.Contours"/>.
/// Segments are yielded in contour order. Within each contour, adjacent stored points form segments in point order,
/// and a closed contour contributes one additional closing segment from its last stored point back to its first.
/// </remarks>
public ref struct SegmentEnumerator
{
    private readonly LinearGeometry geometry;
    private int contourIndex;
    private int segmentIndexInContour;
    private LinearSegment current;

    internal SegmentEnumerator(LinearGeometry geometry)
    {
        this.geometry = geometry;
        this.contourIndex = 0;
        this.segmentIndexInContour = 0;
        this.current = default;
    }

    /// <summary>
    /// Gets the current derived linear segment.
    /// </summary>
    public readonly LinearSegment Current => this.current;

    /// <summary>
    /// Advances to the next derived segment.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if a segment was produced; otherwise <see langword="false"/>.
    /// </returns>
    public bool MoveNext()
    {
        while (this.contourIndex < this.geometry.Contours.Count)
        {
            LinearContour contour = this.geometry.Contours[this.contourIndex];
            if (this.segmentIndexInContour < contour.SegmentCount)
            {
                int pointStart = contour.PointStart;
                int pointIndex = pointStart + this.segmentIndexInContour;

                PointF start = this.geometry.Points[pointIndex];
                PointF end = this.segmentIndexInContour == contour.PointCount - 1
                    ? this.geometry.Points[pointStart]
                    : this.geometry.Points[pointIndex + 1];

                this.current = CreateSegment(start, end);
                this.segmentIndexInContour++;
                return true;
            }

            this.contourIndex++;
            this.segmentIndexInContour = 0;
        }

        return false;
    }

    private static LinearSegment CreateSegment(PointF start, PointF end)
        => new()
        {
            Start = start,
            End = end,
            MinY = MathF.Min(start.Y, end.Y),
            MaxY = MathF.Max(start.Y, end.Y),
            IsHorizontal = start.Y == end.Y
        };
}
