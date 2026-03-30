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
    /// Strokes a path and returns retained linear geometry for the merged outline.
    /// </summary>
    /// <param name="path">The source path. It is flattened using the current flattening settings.</param>
    /// <param name="width">The stroke width in the caller's coordinate space.</param>
    /// <param name="options">The stroke geometry options.</param>
    /// <returns>The stroked outline as retained linear geometry.</returns>
    public static LinearGeometry GenerateStrokedGeometry(IPath path, float width, StrokeOptions options)
    {
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

        if (rings.Count == 0)
        {
            return new LinearGeometry(
                new LinearGeometryInfo
                {
                    Bounds = RectangleF.Empty,
                    ContourCount = 0,
                    PointCount = 0,
                    SegmentCount = 0,
                    NonHorizontalSegmentCountPixelBoundary = 0,
                    NonHorizontalSegmentCountPixelCenter = 0
                },
                [],
                []);
        }

        PCPolygon result = PolygonStroker.Stroke(rings, width, CreateStrokeOptions(options));
        if (result.Count == 0)
        {
            return new LinearGeometry(
                new LinearGeometryInfo
                {
                    Bounds = RectangleF.Empty,
                    ContourCount = 0,
                    PointCount = 0,
                    SegmentCount = 0,
                    NonHorizontalSegmentCountPixelBoundary = 0,
                    NonHorizontalSegmentCountPixelCenter = 0
                },
                [],
                []);
        }

        int pointCount = 0;
        int segmentCount = 0;
        int nonHorizontalBoundary = 0;
        int nonHorizontalCenter = 0;
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        for (int i = 0; i < result.Count; i++)
        {
            Contour contour = result[i];
            int contourPointCount = contour.Count;
            if (contourPointCount == 0)
            {
                continue;
            }

            pointCount += contourPointCount;
            segmentCount += contourPointCount;

            for (int j = 0; j < contourPointCount; j++)
            {
                Vertex vertex = contour[j];
                float x = (float)vertex.X;
                float y = (float)vertex.Y;
                minX = MathF.Min(minX, x);
                minY = MathF.Min(minY, y);
                maxX = MathF.Max(maxX, x);
                maxY = MathF.Max(maxY, y);

                Vertex nextVertex = contour[(j + 1) % contourPointCount];
                float nextY = (float)nextVertex.Y;
                if ((int)MathF.Floor(y) != (int)MathF.Floor(nextY))
                {
                    nonHorizontalBoundary++;
                }

                if ((int)MathF.Floor(y + 0.5F) != (int)MathF.Floor(nextY + 0.5F))
                {
                    nonHorizontalCenter++;
                }
            }
        }

        PointF[] points = new PointF[pointCount];
        LinearContour[] contours = new LinearContour[result.Count];
        int pointStart = 0;
        int segmentStart = 0;
        int outputContourIndex = 0;

        for (int i = 0; i < result.Count; i++)
        {
            Contour contour = result[i];
            int contourPointCount = contour.Count;
            if (contourPointCount == 0)
            {
                continue;
            }

            for (int j = 0; j < contourPointCount; j++)
            {
                Vertex vertex = contour[j];
                points[pointStart + j] = new PointF((float)vertex.X, (float)vertex.Y);
            }

            contours[outputContourIndex++] = new LinearContour
            {
                PointStart = pointStart,
                PointCount = contourPointCount,
                SegmentStart = segmentStart,
                SegmentCount = contourPointCount,
                IsClosed = true
            };

            pointStart += contourPointCount;
            segmentStart += contourPointCount;
        }

        if (outputContourIndex != contours.Length)
        {
            Array.Resize(ref contours, outputContourIndex);
        }

        return new LinearGeometry(
            new LinearGeometryInfo
            {
                Bounds = RectangleF.FromLTRB(minX, minY, maxX, maxY),
                ContourCount = contours.Length,
                PointCount = points.Length,
                SegmentCount = segmentCount,
                NonHorizontalSegmentCountPixelBoundary = nonHorizontalBoundary,
                NonHorizontalSegmentCountPixelCenter = nonHorizontalCenter
            },
            contours,
            points);
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
            },
            NormalizeOutput = options.NormalizeOutput
        };

        return o;
    }
}
