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
    /// <summary>
    /// The geometry whose derived segments are enumerated.
    /// </summary>
    private readonly LinearGeometry geometry;

    /// <summary>
    /// The zero-based index of the contour currently being enumerated.
    /// </summary>
    private int contourIndex;

    /// <summary>
    /// The zero-based index of the next segment to yield within the current contour.
    /// </summary>
    private int segmentIndexInContour;

    /// <summary>
    /// The most recently yielded segment.
    /// </summary>
    private LinearSegment current;

    /// <summary>
    /// Initializes a new instance of the <see cref="SegmentEnumerator"/> struct positioned before the first segment.
    /// </summary>
    /// <param name="geometry">The geometry whose derived segments are enumerated.</param>
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

                // Closed contours have SegmentCount == PointCount, so the final index wraps back to
                // the first stored point and forms the closing segment. Open contours have
                // SegmentCount == PointCount - 1 and never reach the wrapping branch.
                PointF end = this.segmentIndexInContour == contour.PointCount - 1
                    ? this.geometry.Points[pointStart]
                    : this.geometry.Points[pointIndex + 1];

                this.current = CreateSegment(start, end, this.contourIndex);
                this.segmentIndexInContour++;
                return true;
            }

            this.contourIndex++;
            this.segmentIndexInContour = 0;
        }

        return false;
    }

    /// <summary>
    /// Creates a segment with its precomputed per-segment metadata.
    /// </summary>
    /// <param name="start">The segment start point.</param>
    /// <param name="end">The segment end point.</param>
    /// <param name="contourIndex">The zero-based index of the owning contour.</param>
    /// <returns>The derived <see cref="LinearSegment"/>.</returns>
    private static LinearSegment CreateSegment(PointF start, PointF end, int contourIndex)
        => new()
        {
            Start = start,
            End = end,
            ContourIndex = contourIndex,
            MinY = MathF.Min(start.Y, end.Y),
            MaxY = MathF.Max(start.Y, end.Y),
            IsHorizontal = start.Y == end.Y
        };
}
