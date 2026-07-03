// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.PolygonClipper;

using PCPolygon = SixLabors.PolygonClipper.Polygon;
using StrokeOptions = SixLabors.ImageSharp.Drawing.Processing.StrokeOptions;

namespace SixLabors.ImageSharp.Drawing.PolygonGeometry;

/// <summary>
/// Generates stroked and merged shapes using polygon stroking and boolean clipping.
/// </summary>
internal static class StrokedShapeGenerator
{
    /// <summary>
    /// Strokes a path and returns a merged outline from its flattened segments.
    /// </summary>
    /// <param name="path">The source path. It is flattened using the current flattening settings.</param>
    /// <param name="width">The stroke width in the caller's coordinate space.</param>
    /// <param name="options">The stroke geometry options.</param>
    /// <returns>
    /// A <see cref="ComplexPolygon"/> representing the stroked outline after boolean merge.
    /// </returns>
    public static ComplexPolygon GenerateStrokedShapes(IPath path, float width, StrokeOptions options)
    {
        // Convert the flattened contours to clipper rings first; the stroker handles
        // open and closed contours differently, so closedness must be preserved.
        PCPolygon rings = [];

        foreach (ISimplePath sp in path.Flatten())
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

        PCPolygon result = PolygonStroker.Stroke(rings, width, CreateStrokeOptions(options));

        IPath[] shapes = new IPath[result.Count];
        int index = 0;
        for (int i = 0; i < result.Count; i++)
        {
            shapes[index++] = new Polygon(ClippedShapeGenerator.CreateContourPoints(result, i));
        }

        return new(shapes);
    }

    /// <summary>
    /// Maps the ImageSharp <see cref="StrokeOptions"/> to the equivalent PolygonClipper options.
    /// </summary>
    /// <param name="options">The ImageSharp stroke geometry options.</param>
    /// <returns>The equivalent <see cref="PolygonClipper.StrokeOptions"/>.</returns>
    private static PolygonClipper.StrokeOptions CreateStrokeOptions(StrokeOptions options)
    {
        PolygonClipper.StrokeOptions o = new()
        {
            ArcDetailScale = options.ArcDetailScale,
            MiterLimit = options.MiterLimit,
            LineJoin = options.LineJoin switch
            {
                LineJoin.MiterRound => PolygonClipper.LineJoin.MiterRound,
                LineJoin.Bevel => PolygonClipper.LineJoin.Bevel,
                LineJoin.Round => PolygonClipper.LineJoin.Round,
                LineJoin.MiterRevert => PolygonClipper.LineJoin.MiterRevert,
                _ => PolygonClipper.LineJoin.Miter,
            },

            LineCap = options.LineCap switch
            {
                LineCap.Round => PolygonClipper.LineCap.Round,
                LineCap.Square => PolygonClipper.LineCap.Square,
                _ => PolygonClipper.LineCap.Butt,
            }
        };

        return o;
    }
}
