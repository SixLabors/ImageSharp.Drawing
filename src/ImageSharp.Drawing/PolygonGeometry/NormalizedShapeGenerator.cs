// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.PolygonClipper;
using PCPolygon = SixLabors.PolygonClipper.Polygon;
using PolygonClipperAction = SixLabors.PolygonClipper.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.PolygonGeometry;

/// <summary>
/// Generates normalized polygon shapes from paths.
/// </summary>
internal static class NormalizedShapeGenerator
{
    /// <summary>
    /// Resolves self-intersections and overlaps using the non-zero fill rule.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>
    /// A <see cref="ComplexPolygon"/> containing the boundaries of the filled area.
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

            rings.Add(ring);
        }

        int count = rings.Count;
        if (count == 0)
        {
            return new([]);
        }

        PCPolygon result = PolygonClipperAction.Normalize(rings, FillRule.NonZero);
        return ClippedShapeGenerator.ToComplexPolygon(result);
    }
}
