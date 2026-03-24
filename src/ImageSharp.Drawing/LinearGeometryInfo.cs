// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Describes geometry-wide metadata for a <see cref="LinearGeometry"/> instance.
/// </summary>
/// <remarks>
/// This metadata is computed eagerly during lowering so backends do not need to enumerate the geometry again to
/// discover basic information such as total segment count or bounds.
/// </remarks>
public readonly struct LinearGeometryInfo
{
    /// <summary>
    /// Gets the bounds of all points stored in the containing <see cref="LinearGeometry"/>.
    /// </summary>
    public required RectangleF Bounds { get; init; }

    /// <summary>
    /// Gets the total number of contours in the containing <see cref="LinearGeometry"/>.
    /// </summary>
    public required int ContourCount { get; init; }

    /// <summary>
    /// Gets the total number of stored points across all contours.
    /// </summary>
    public required int PointCount { get; init; }

    /// <summary>
    /// Gets the total number of derived linear segments across all contours.
    /// </summary>
    public required int SegmentCount { get; init; }
}
