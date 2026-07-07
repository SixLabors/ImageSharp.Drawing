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
            if (path is IRegionPath regionPath)
            {
                AddRegionRectangles(regionPath.Rectangles, polygon);
                continue;
            }

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
        if (path is IRegionPath regionPath)
        {
            PCPolygon regionPolygon = [];
            AddRegionRectangles(regionPath.Rectangles, regionPolygon);

            return ApplyIntersectionRule(regionPolygon, intersectionRule);
        }

        PCPolygon polygon = FromSimpleClosedPaths(path.AsClosedPath().Flatten());

        return ApplyIntersectionRule(polygon, intersectionRule);
    }

    /// <summary>
    /// Creates a polygon from one axis-aligned integer rectangle.
    /// </summary>
    /// <param name="rectangle">The rectangle to convert.</param>
    /// <returns>A <see cref="PCPolygon"/> containing one rectangle contour.</returns>
    public static PCPolygon FromRectangle(Rectangle rectangle)
    {
        PCPolygon polygon = [];
        AddRectangle(rectangle, polygon);
        return polygon;
    }

    /// <summary>
    /// Adds an integer region as rectangle contours without first exporting its boundary path.
    /// </summary>
    /// <param name="rectangles">The normalized rectangles that cover the region.</param>
    /// <param name="polygon">The polygon receiving the rectangle contours.</param>
    private static void AddRegionRectangles(IReadOnlyList<Rectangle> rectangles, PCPolygon polygon)
    {
        // Skia clips regions as rectangle sets. Preserve that model at the polygon boundary so
        // disjoint dirty-region islands are unioned by coverage instead of being inferred from
        // boundary-link geometry.
        for (int i = 0; i < rectangles.Count; i++)
        {
            AddRectangle(rectangles[i], polygon);
        }
    }

    /// <summary>
    /// Adds one rectangle contour to a polygon.
    /// </summary>
    /// <param name="rectangle">The rectangle to add.</param>
    /// <param name="polygon">The polygon receiving the rectangle contour.</param>
    private static void AddRectangle(Rectangle rectangle, PCPolygon polygon)
    {
        Contour contour =
        [
            new Vertex(rectangle.Left, rectangle.Top),
            new Vertex(rectangle.Right, rectangle.Top),
            new Vertex(rectangle.Right, rectangle.Bottom),
            new Vertex(rectangle.Left, rectangle.Bottom)
        ];

        polygon.Add(contour);
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
    /// Prepares a polygon so the boolean clipper reproduces the requested fill rule.
    /// </summary>
    /// <remarks>
    /// The clipper classifies coverage by even-odd boundary crossings, which is orientation
    /// independent: a point is inside when an odd number of the polygon's edges lie below it,
    /// regardless of edge direction. Even-odd input already matches that model and is returned
    /// unchanged. Non-zero input differs from even-odd only where the outline covers a region more
    /// than once, that is where contours self-overlap or self-intersect (winding of magnitude two
    /// or more, common in glyph outlines with overlapping components or crossings introduced by
    /// curve flattening). Non-zero fills such a region while even-odd punches it out as a hole,
    /// which is what produced clipped glyphs with spurious holes. Reorienting the contours cannot
    /// fix this, because even-odd ignores direction, so the overlapping regions must actually be
    /// merged. <see cref="PolygonClipperAction.Normalize"/> performs that self-union and yields an
    /// equivalent simple polygon whose even-odd boundary equals the non-zero fill.
    /// </remarks>
    /// <param name="polygon">The polygon to prepare.</param>
    /// <param name="intersectionRule">The fill rule used to interpret the polygon.</param>
    /// <returns>A <see cref="PCPolygon"/> containing the prepared contours.</returns>
    private static PCPolygon ApplyIntersectionRule(PCPolygon polygon, IntersectionRule intersectionRule)
        => intersectionRule == IntersectionRule.NonZero
            ? PolygonClipperAction.Normalize(polygon)
            : polygon;
}
