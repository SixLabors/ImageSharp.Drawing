// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.PolygonClipper;
using PCPolygon = SixLabors.PolygonClipper.Polygon;

namespace SixLabors.ImageSharp.Drawing.PolygonGeometry;

/// <summary>
/// Converts ImageSharp paths to PolygonClipper contours.
/// </summary>
internal static class PolygonClipperFactory
{
    /// <summary>
    /// Creates a polygon from the open and closed figures of multiple paths.
    /// </summary>
    /// <param name="paths">The paths to convert.</param>
    /// <returns>The converted contours.</returns>
    public static PCPolygon FromPaths(IEnumerable<IPath> paths)
    {
        PCPolygon polygon = [];

        foreach (IPath path in paths)
        {
            AddPath(path, polygon);
        }

        return polygon;
    }

    /// <summary>
    /// Creates a polygon from the open and closed figures of a path.
    /// </summary>
    /// <param name="path">The path to convert.</param>
    /// <returns>The converted contours.</returns>
    public static PCPolygon FromPath(IPath path)
    {
        PCPolygon polygon = [];
        AddPath(path, polygon);
        return polygon;
    }

    /// <summary>
    /// Adds a path's contours to the polygon.
    /// </summary>
    /// <param name="path">The source path.</param>
    /// <param name="polygon">The polygon receiving the contours.</param>
    private static void AddPath(IPath path, PCPolygon polygon)
    {
        if (path is IRegionPath regionPath)
        {
            // The rectangles have disjoint interiors. Shared edges cancel
            // when the sweep processes the whole region as one operand.
            IReadOnlyList<Rectangle> rectangles = regionPath.Rectangles;
            for (int i = 0; i < rectangles.Count; i++)
            {
                Rectangle rectangle = rectangles[i];
                Contour contour =
                [
                    new Vertex(rectangle.Left, rectangle.Top),
                    new Vertex(rectangle.Right, rectangle.Top),
                    new Vertex(rectangle.Right, rectangle.Bottom),
                    new Vertex(rectangle.Left, rectangle.Bottom)
                ];

                polygon.Add(contour);
            }

            return;
        }

        foreach (ISimplePath figure in path.Flatten())
        {
            ReadOnlySpan<PointF> points = figure.Points.Span;
            if (points.IsEmpty)
            {
                continue;
            }

            Contour contour = new(points.Length)
            {
                IsClosed = figure.IsClosed
            };

            for (int i = 0; i < points.Length; i++)
            {
                contour.Add(new Vertex(points[i].X, points[i].Y));
            }

            polygon.Add(contour);
        }
    }
}
