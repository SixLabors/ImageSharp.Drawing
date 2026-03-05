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
    /// Strokes a collection of dashed polyline spans and returns a merged outline.
    /// </summary>
    /// <param name="spans">
    /// The input spans. Each <see cref="PointF"/> array is treated as an open polyline
    /// and is stroked using the current stroker settings.
    /// Spans that are null or contain fewer than 2 points are ignored.
    /// </param>
    /// <param name="width">The stroke width in the caller's coordinate space.</param>
    /// <param name="options">The stroke geometry options.</param>
    /// <returns>
    /// A <see cref="ComplexPolygon"/> representing the stroked outline after boolean merge.
    /// </returns>
    public static ComplexPolygon GenerateStrokedShapes(List<PointF[]> spans, float width, StrokeOptions options)
    {
        // 1) Stroke each dashed span as open.
        PCPolygon rings = new(spans.Count);
        foreach (PointF[] span in spans)
        {
            if (span == null || span.Length < 2)
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

        PCPolygon result = PolygonStroker.Stroke(rings, width, CreateStrokeOptions(options));

        IPath[] shapes = new IPath[result.Count];
        int index = 0;
        for (int i = 0; i < result.Count; i++)
        {
            Contour contour = result[i];
            PointF[] points = new PointF[contour.Count];

            for (int j = 0; j < contour.Count; j++)
            {
                Vertex vertex = contour[j];
                points[j] = new PointF((float)vertex.X, (float)vertex.Y);
            }

            shapes[index++] = new Polygon(points);
        }

        return new(shapes);
    }

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
        // 1) Stroke the input path as open or closed.
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
            Contour contour = result[i];
            PointF[] points = new PointF[contour.Count];

            for (int j = 0; j < contour.Count; j++)
            {
                Vertex vertex = contour[j];
                points[j] = new PointF((float)vertex.X, (float)vertex.Y);
            }

            shapes[index++] = new Polygon(points);
        }

        return new(shapes);
    }

    private static PolygonClipper.StrokeOptions CreateStrokeOptions(StrokeOptions options)
    {
        PolygonClipper.StrokeOptions o = new()
        {
            ArcDetailScale = options.ArcDetailScale,
            MiterLimit = options.MiterLimit,
            InnerMiterLimit = options.InnerMiterLimit,
            NormalizeOutput = options.NormalizeOutput,
            LineJoin = options.LineJoin switch
            {
                LineJoin.MiterRound => PolygonClipper.LineJoin.MiterRound,
                LineJoin.Bevel => PolygonClipper.LineJoin.Bevel,
                LineJoin.Round => PolygonClipper.LineJoin.Round,
                LineJoin.MiterRevert => PolygonClipper.LineJoin.MiterRevert,
                _ => PolygonClipper.LineJoin.Miter,
            },

            InnerJoin = options.InnerJoin switch
            {
                InnerJoin.Round => PolygonClipper.InnerJoin.Round,
                InnerJoin.Miter => PolygonClipper.InnerJoin.Miter,
                InnerJoin.Jag => PolygonClipper.InnerJoin.Jag,
                _ => PolygonClipper.InnerJoin.Bevel,
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
