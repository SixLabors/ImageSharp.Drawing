// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Utilities;
using SixLabors.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonGeometry;

/// <summary>
/// Generates stroked and merged shapes using polygon stroking and boolean clipping.
/// </summary>
internal sealed class StrokedShapeGenerator
{
    private readonly PolygonStroker polygonStroker;

    /// <summary>
    /// Initializes a new instance of the <see cref="StrokedShapeGenerator"/> class.
    /// </summary>
    /// <param name="meterLimit">meter limit</param>
    /// <param name="arcTolerance">arc tolerance</param>
    public StrokedShapeGenerator(float meterLimit = 2F, float arcTolerance = .25F)
    {
        // TODO: We need to consume the joint type properties here.
        // to do so we need to replace the existing ones with our new enums and update
        // the overloads and pens.
        this.polygonStroker = new PolygonStroker();
    }

    /// <summary>
    /// Strokes a collection of dashed polyline spans and returns a merged outline.
    /// </summary>
    /// <param name="spans">
    /// The input spans. Each <see cref="PointF"/> array is treated as an open polyline
    /// and is stroked using the current stroker settings.
    /// Spans that are null or contain fewer than 2 points are ignored.
    /// </param>
    /// <param name="width">The stroke width in the caller’s coordinate space.</param>
    /// <returns>
    /// An array of closed paths representing the stroked outline after boolean merge.
    /// Returns an empty array when no valid spans are provided. Returns a single path
    /// when only one valid stroked ring is produced.
    /// </returns>
    /// <remarks>
    /// This method streams each dashed span through the internal stroker as an open polyline,
    /// producing closed stroke rings. To clean self overlaps, the rings are split between
    /// subject and clip sets and a <see cref="BooleanOperation.Union"/> is performed.
    /// The split ensures at least two operands so the union resolves overlaps.
    /// The union uses <see cref="IntersectionRule.NonZero"/> to preserve winding density.
    /// </remarks>
    public IPath[] GenerateStrokedShapes(List<PointF[]> spans, float width)
    {
        // PolygonClipper is not designed to clean up self-intersecting geometry within a single polygon.
        // It operates strictly on two polygon operands (subject and clip) and only resolves overlaps
        // between them. To force cleanup of dashed stroke overlaps, we alternate assigning each
        // stroked segment to subject or clip, ensuring at least two operands exist so the union
        // operation performs a true merge rather than a no-op on a single polygon.

        // 1) Stroke each dashed span as open.
        this.polygonStroker.Width = width;

        List<PointF[]> ringPoints = new(spans.Count);
        List<IPath> rings = new(spans.Count);
        foreach (PointF[] span in spans)
        {
            if (span == null || span.Length < 2)
            {
                continue;
            }

            PointF[] stroked = this.polygonStroker.ProcessPath(span, isClosed: false);
            if (stroked.Length < 3)
            {
                continue;
            }

            ringPoints.Add(stroked);
            rings.Add(new Polygon(new LinearLineSegment(stroked)));
        }

        int count = rings.Count;
        if (count == 0)
        {
            return [];
        }

        if (!HasIntersections(ringPoints))
        {
            return count == 1 ? [rings[0]] : [.. rings];
        }

        // 2) Partition so the first and last are on different polygons
        List<IPath> subjectRings = new(count);
        List<IPath> clipRings = new(count);

        // First => subject
        subjectRings.Add(rings[0]);

        // Middle by alternation using a single bool flag
        bool assignToSubject = false; // start with clip for i=1
        for (int i = 1; i < count - 1; i++)
        {
            if (assignToSubject)
            {
                subjectRings.Add(rings[i]);
            }
            else
            {
                clipRings.Add(rings[i]);
            }

            assignToSubject = !assignToSubject;
        }

        // Last => opposite of first (i.e., clip)
        clipRings.Add(rings[count - 1]);

        // 3) Union subject vs clip
        ClippedShapeGenerator clipper = new(IntersectionRule.NonZero);
        clipper.AddPaths(subjectRings, ClippingType.Subject);
        clipper.AddPaths(clipRings, ClippingType.Clip);
        return clipper.GenerateClippedShapes(BooleanOperation.Union);
    }

    /// <summary>
    /// Strokes a path and returns a merged outline from its flattened segments.
    /// </summary>
    /// <param name="path">The source path. It is flattened using the current flattening settings.</param>
    /// <param name="width">The stroke width in the caller’s coordinate space.</param>
    /// <returns>
    /// An array of closed paths representing the stroked outline after boolean merge.
    /// Returns an empty array when no valid rings are produced. Returns a single path
    /// when only one valid stroked ring exists.
    /// </returns>
    /// <remarks>
    /// Each flattened simple path is streamed through the internal stroker as open or closed
    /// according to <see cref="ISimplePath.IsClosed"/>. The resulting stroke rings are split
    /// between subject and clip sets and combined using <see cref="BooleanOperation.Union"/>.
    /// This split is required because the Martinez based clipper resolves overlaps only between
    /// two operands. Using <see cref="IntersectionRule.NonZero"/> preserves fill across overlaps
    /// and prevents unintended holes in the merged outline.
    /// </remarks>
    public IPath[] GenerateStrokedShapes(IPath path, float width)
    {
        // 1) Stroke the input path into closed rings
        List<PointF[]> ringPoints = [];
        List<IPath> rings = [];
        this.polygonStroker.Width = width;

        foreach (ISimplePath p in path.Flatten())
        {
            PointF[] stroked = this.polygonStroker.ProcessPath(p.Points.Span, p.IsClosed);
            if (stroked.Length < 3)
            {
                continue; // skip degenerate outputs
            }

            ringPoints.Add(stroked);
            rings.Add(new Polygon(new LinearLineSegment(stroked)));
        }

        int count = rings.Count;
        if (count == 0)
        {
            return [];
        }

        if (!HasIntersections(ringPoints))
        {
            return count == 1 ? [rings[0]] : [.. rings];
        }

        // 2) Partition so the first and last are on different polygons
        // PolygonClipper is not designed to clean up self-intersecting geometry within a single polygon.
        // It operates strictly on two polygon operands (subject and clip) and only resolves overlaps
        // between them. To force cleanup of overlaps, we alternate assigning each stroked ring to
        // subject or clip, ensuring at least two operands exist so the union performs a true merge.
        List<IPath> subjectRings = new(count);
        List<IPath> clipRings = new(count);

        // First => subject
        subjectRings.Add(rings[0]);

        // Middle by alternation using a single bool flag
        bool assignToSubject = false; // start with clip for i=1
        for (int i = 1; i < count - 1; i++)
        {
            if (assignToSubject)
            {
                subjectRings.Add(rings[i]);
            }
            else
            {
                clipRings.Add(rings[i]);
            }

            assignToSubject = !assignToSubject;
        }

        // Last => opposite of first (i.e., clip)
        clipRings.Add(rings[count - 1]);

        // 3) Union subject vs clip
        ClippedShapeGenerator clipper = new(IntersectionRule.NonZero);
        clipper.AddPaths(subjectRings, ClippingType.Subject);
        clipper.AddPaths(clipRings, ClippingType.Clip);

        // 4) Return the cleaned, merged outline
        return clipper.GenerateClippedShapes(BooleanOperation.Union);
    }

    /// <summary>
    /// Determines whether any of the provided rings contain self-intersections or intersect with other rings.
    /// </summary>
    /// <remarks>
    /// This method performs a conservative scan to detect intersections among the provided rings. It
    /// checks for both self-intersections within each ring and intersections between different rings. Rings are treated
    /// as polylines; if a ring is closed (its first and last points are equal), the closing segment is included in the
    /// intersection checks. This method is intended for fast intersection detection and may be used to determine
    /// whether further geometric processing, such as clipping, is necessary.
    /// </remarks>
    /// <param name="rings">
    /// A list of rings, where each ring is represented as an array of points defining its vertices. Each ring is
    /// expected to be a sequence of points forming a polyline or polygon.
    /// </param>
    /// <returns><see langword="true"/> if any ring self-intersects or any two rings intersect; otherwise, <see langword="false"/>.</returns>
    private static bool HasIntersections(List<PointF[]> rings)
    {
        // Detect whether any stroked ring self-intersects or intersects another ring.
        // This is a fast, conservative scan used to decide whether we can skip clipping.
        Vector2 intersection = default;

        for (int r = 0; r < rings.Count; r++)
        {
            PointF[] ring = rings[r];
            int segmentCount = ring.Length - 1;
            if (segmentCount < 2)
            {
                continue;
            }

            // 1) Self-intersection scan for the current ring.
            // Adjacent segments share a vertex and are skipped to avoid trivial hits.
            bool isClosed = ring[0] == ring[^1];
            for (int i = 0; i < segmentCount; i++)
            {
                Vector2 a0 = new(ring[i].X, ring[i].Y);
                Vector2 a1 = new(ring[i + 1].X, ring[i + 1].Y);

                for (int j = i + 1; j < segmentCount; j++)
                {
                    // Skip neighbors and the closing edge pair in a closed ring.
                    if (j == i + 1 || (isClosed && i == 0 && j == segmentCount - 1))
                    {
                        continue;
                    }

                    Vector2 b0 = new(ring[j].X, ring[j].Y);
                    Vector2 b1 = new(ring[j + 1].X, ring[j + 1].Y);
                    if (Intersect.LineSegmentToLineSegmentIgnoreCollinear(a0, a1, b0, b1, ref intersection))
                    {
                        return true;
                    }
                }
            }

            // 2) Cross-ring intersection scan against later rings only.
            // This avoids double work while checking all ring pairs.
            for (int s = r + 1; s < rings.Count; s++)
            {
                PointF[] other = rings[s];
                int otherSegmentCount = other.Length - 1;
                if (otherSegmentCount < 1)
                {
                    continue;
                }

                for (int i = 0; i < segmentCount; i++)
                {
                    Vector2 a0 = new(ring[i].X, ring[i].Y);
                    Vector2 a1 = new(ring[i + 1].X, ring[i + 1].Y);

                    for (int j = 0; j < otherSegmentCount; j++)
                    {
                        Vector2 b0 = new(other[j].X, other[j].Y);
                        Vector2 b1 = new(other[j + 1].X, other[j + 1].Y);
                        if (Intersect.LineSegmentToLineSegmentIgnoreCollinear(a0, a1, b0, b1, ref intersection))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        // No intersections detected.
        return false;
    }
}
