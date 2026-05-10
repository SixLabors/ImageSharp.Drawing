// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Describes a single contour within a <see cref="LinearGeometry"/>.
/// </summary>
/// <remarks>
/// A contour identifies a contiguous point run in <see cref="LinearGeometry.Points"/> and the corresponding range in
/// the derived segment stream exposed by <see cref="LinearGeometry.GetSegments"/>.
/// </remarks>
public readonly struct LinearContour
{
    /// <summary>
    /// Gets the zero-based index of the first point belonging to this contour in <see cref="LinearGeometry.Points"/>.
    /// </summary>
    public required int PointStart { get; init; }

    /// <summary>
    /// Gets the number of stored points belonging to this contour.
    /// </summary>
    public required int PointCount { get; init; }

    /// <summary>
    /// Gets the zero-based index of the first derived segment belonging to this contour.
    /// </summary>
    public required int SegmentStart { get; init; }

    /// <summary>
    /// Gets the number of derived segments belonging to this contour.
    /// </summary>
    public required int SegmentCount { get; init; }

    /// <summary>
    /// Gets a value indicating whether the contour is closed.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/>, the final derived segment for the contour joins the last stored point back to
    /// the first stored point. Closed contours do not duplicate the first point at the end of their stored point run.
    /// </remarks>
    public required bool IsClosed { get; init; }
}
