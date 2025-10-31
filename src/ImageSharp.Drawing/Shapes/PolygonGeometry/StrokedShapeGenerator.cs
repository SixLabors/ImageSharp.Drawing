// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

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

            rings.Add(new Polygon(new LinearLineSegment(stroked)));
        }

        int count = rings.Count;
        if (count == 0)
        {
            return [];
        }

        if (count == 1)
        {
            // Only one stroked ring. Return as-is; two-operand union requires both sides non-empty.
            return [rings[0]];
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
        List<IPath> rings = [];
        this.polygonStroker.Width = width;

        foreach (ISimplePath p in path.Flatten())
        {
            PointF[] stroked = this.polygonStroker.ProcessPath(p.Points.Span, p.IsClosed);
            if (stroked.Length < 3)
            {
                continue; // skip degenerate outputs
            }

            rings.Add(new Polygon(new LinearLineSegment(stroked)));
        }

        int count = rings.Count;
        if (count == 0)
        {
            return [];
        }

        if (count == 1)
        {
            // Only one stroked ring. Return as-is; two-operand union requires both sides non-empty.
            return [rings[0]];
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
}
