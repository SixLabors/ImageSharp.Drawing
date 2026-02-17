// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.PolygonClipper;
using PCPolygon = SixLabors.PolygonClipper.Polygon;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonGeometry;

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
    {
        PCPolygon polygon = [];

        foreach (IPath path in paths)
        {
            polygon = FromSimpleClosedPaths(path.Flatten(), polygon);
        }

        return polygon;
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

            // Add the contour - PolygonClipper will determine parent/depth/orientation during sweep
            polygon.Add(contour);
        }

        return polygon;
    }
}
