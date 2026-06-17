// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.PolygonClipper;
using PCPolygon = SixLabors.PolygonClipper.Polygon;
using PolygonClipperAction = SixLabors.PolygonClipper.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.PolygonGeometry;

/// <summary>
/// Generates normalized polygon shapes from paths.
/// </summary>
/// <remarks>
/// Normalization converts the incoming path contours to a positive-winding polygon representation.
/// This gives later boolean operations a stable contour set instead of preserving ambiguous
/// self-intersections, overlaps, or parity-dependent contour relationships from the source path.
/// </remarks>
internal static class NormalizedShapeGenerator
{
    /// <summary>
    /// Normalizes the specified path.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>
    /// A <see cref="ComplexPolygon"/> containing the normalized positive-winding contours.
    /// </returns>
    public static ComplexPolygon NormalizeShape(IPath path)
    {
        IPath closed = path.AsClosedPath();
        PCPolygon rings = [];

        foreach (ISimplePath sp in closed.Flatten())
        {
            ReadOnlySpan<PointF> span = sp.Points.Span;

            if (span.Length < 2)
            {
                continue;
            }

            Contour ring = new(span.Length);
            for (int i = 0; i < span.Length; i++)
            {
                PointF p = span[i];
                ring.Add(new Vertex(p.X, p.Y));
            }

            // PolygonClipper expects closed rings to repeat their start point.
            if (sp.IsClosed)
            {
                ring.Add(ring[0]);
            }

            rings.Add(ring);
        }

        int count = rings.Count;
        if (count == 0)
        {
            return new([]);
        }

        PCPolygon result = PolygonClipperAction.Normalize(rings);

        IPath[] shapes = new IPath[result.Count];
        for (int i = 0; i < result.Count; i++)
        {
            shapes[i] = new Polygon(ClippedShapeGenerator.CreateContourPoints(result, i));
        }

        return new(shapes);
    }
}
