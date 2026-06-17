// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.PolygonClipper;
using PCPolygon = SixLabors.PolygonClipper.Polygon;
using PolygonClipperAction = SixLabors.PolygonClipper.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.PolygonGeometry;

/// <summary>
/// Builders for <see cref="PCPolygon"/> from ImageSharp paths.
/// Converts ImageSharp paths to the format required by PolygonClipper.
/// </summary>
/// <remarks>
/// PolygonClipper computes parent-child relationships, depth, and orientation during its
/// sweep line algorithm, so we only need to provide contours with vertices.
/// </remarks>
internal static class PolygonClipperFactory
{
    /// <summary>
    /// Creates a polygon from multiple paths.
    /// </summary>
    /// <param name="paths">The paths to convert.</param>
    /// <returns>A <see cref="PCPolygon"/> containing all flattened paths as contours.</returns>
    public static PCPolygon FromClosedPaths(IEnumerable<IPath> paths)
        => FromClosedPaths(paths, IntersectionRule.NonZero);

    /// <summary>
    /// Creates a polygon area from multiple paths using the specified fill rule.
    /// </summary>
    /// <param name="paths">The paths to convert.</param>
    /// <param name="intersectionRule">The fill rule used to interpret the input paths.</param>
    /// <returns>A <see cref="PCPolygon"/> containing the converted contours.</returns>
    public static PCPolygon FromClosedPaths(IEnumerable<IPath> paths, IntersectionRule intersectionRule)
    {
        PCPolygon polygon = [];

        foreach (IPath path in paths)
        {
            polygon = FromSimpleClosedPaths(path.Flatten(), polygon);
        }

        return ApplyIntersectionRule(polygon, intersectionRule);
    }

    /// <summary>
    /// Creates a polygon area from a path using the specified fill rule.
    /// </summary>
    /// <param name="path">The path to convert.</param>
    /// <param name="intersectionRule">The fill rule used to interpret the path.</param>
    /// <returns>A <see cref="PCPolygon"/> containing the converted contours.</returns>
    public static PCPolygon FromClosedPath(IPath path, IntersectionRule intersectionRule)
    {
        PCPolygon polygon = FromSimpleClosedPaths(path.AsClosedPath().Flatten());

        return ApplyIntersectionRule(polygon, intersectionRule);
    }

    /// <summary>
    /// Converts closed simple paths to PolygonClipper contours.
    /// </summary>
    /// <param name="paths">Closed simple paths.</param>
    /// <param name="polygon">Optional existing polygon to populate.</param>
    /// <returns>The constructed <see cref="PCPolygon"/>.</returns>
    /// <remarks>
    /// This method simply converts ImageSharp paths to PolygonClipper contours by copying vertices.
    /// PolygonClipper's sweep line algorithm will determine parent-child relationships, depth,
    /// and proper orientation during clipping operations. We only need to ensure paths are
    /// closed and have sufficient vertices.
    /// </remarks>
    public static PCPolygon FromSimpleClosedPaths(IEnumerable<ISimplePath> paths, PCPolygon? polygon = null)
    {
        polygon ??= [];

        foreach (ISimplePath p in paths)
        {
            if (!p.IsClosed)
            {
                continue;
            }

            ReadOnlySpan<PointF> points = p.Points.Span;
            if (points.Length < 3)
            {
                continue;
            }

            Contour contour = [];

            // Copy all vertices
            for (int i = 0; i < points.Length; i++)
            {
                contour.Add(new Vertex(points[i].X, points[i].Y));
            }

            polygon.Add(contour);
        }

        return polygon;
    }

    /// <summary>
    /// Applies the input orientation required before polygon clipping.
    /// </summary>
    /// <param name="polygon">The polygon to prepare.</param>
    /// <param name="intersectionRule">The fill rule used to interpret the polygon.</param>
    /// <returns>A <see cref="PCPolygon"/> containing the prepared contours.</returns>
    private static PCPolygon ApplyIntersectionRule(PCPolygon polygon, IntersectionRule intersectionRule)
        => intersectionRule == IntersectionRule.NonZero
            ? PolygonClipperAction.Normalize(polygon)
            : polygon;
}
