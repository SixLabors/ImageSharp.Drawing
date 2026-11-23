// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.PolygonClipper;
using PCPolygon = SixLabors.PolygonClipper.Polygon;
using PolygonClipperAction = SixLabors.PolygonClipper.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.PolygonGeometry;

/// <summary>
/// Generates clipped shapes from one or more input paths using polygon boolean operations.
/// </summary>
/// <remarks>
/// This class provides a high-level wrapper around the low-level <see cref="PolygonClipperAction"/>.
/// It converts subject and clip paths to clipper polygons, applies the specified
/// <see cref="BooleanOperation"/>, and converts the resulting polygon contours back into
/// <see cref="ComplexPolygon"/> instances suitable for rendering or further processing.
/// Rectangle intersection with an integer region uses the region's rectangle set directly.
/// </remarks>
internal static class ClippedShapeGenerator
{
    /// <summary>
    /// Generates the final clipped shapes from the specified subject and clip paths.
    /// </summary>
    /// <param name="operation">
    /// The boolean operation to perform, such as <see cref="BooleanOperation.Union"/>,
    /// <see cref="BooleanOperation.Intersection"/>, or <see cref="BooleanOperation.Difference"/>.
    /// </param>
    /// <param name="subject">The subject path.</param>
    /// <param name="clip">The clipping paths.</param>
    /// <returns>
    /// The <see cref="ComplexPolygon"/> representing the result of the boolean operation.
    /// </returns>
    public static ComplexPolygon GenerateClippedShapes(
        BooleanOperation operation,
        IPath subject,
        IEnumerable<IPath> clip)
        => GenerateClippedShapes(operation, IntersectionRule.NonZero, subject, clip);

    /// <summary>
    /// Generates the final clipped shapes from the specified subject and clip paths.
    /// </summary>
    /// <param name="operation">The boolean operation to perform.</param>
    /// <param name="intersectionRule">The fill rule used to interpret both the subject and clipping paths.</param>
    /// <param name="subject">The subject path.</param>
    /// <param name="clip">The clipping paths.</param>
    /// <returns>
    /// The <see cref="ComplexPolygon"/> representing the result of the boolean operation.
    /// </returns>
    public static ComplexPolygon GenerateClippedShapes(
        BooleanOperation operation,
        IntersectionRule intersectionRule,
        IPath subject,
        IEnumerable<IPath> clip)
        => GenerateClippedShapes(operation, intersectionRule, subject, clip, intersectionRule);

    /// <summary>
    /// Generates the final clipped shapes from the specified subject and clip paths.
    /// </summary>
    /// <param name="operation">The boolean operation to perform.</param>
    /// <param name="intersectionRule">The fill rule used to interpret the subject path.</param>
    /// <param name="subject">The subject path.</param>
    /// <param name="clip">The clipping paths.</param>
    /// <param name="clipIntersectionRule">The fill rule used to interpret the clipping paths.</param>
    /// <returns>
    /// The <see cref="ComplexPolygon"/> representing the result of the boolean operation.
    /// </returns>
    public static ComplexPolygon GenerateClippedShapes(
        BooleanOperation operation,
        IntersectionRule intersectionRule,
        IPath subject,
        IEnumerable<IPath> clip,
        IntersectionRule clipIntersectionRule)
    {
        Guard.NotNull(subject);
        Guard.NotNull(clip);

        IRegionPath? singleRegionPath = clip is IReadOnlyList<IPath> { Count: 1 } clipList &&
            clipList[0] is IRegionPath candidate
                ? candidate
                : null;

        if (singleRegionPath is not null &&
            TryClipRectangleByRegion(operation, subject, singleRegionPath, out ComplexPolygon clippedRectangle))
        {
            return clippedRectangle;
        }

        PCPolygon s = PolygonClipperFactory.FromPath(subject);
        PCPolygon c = PolygonClipperFactory.FromPaths(clip);
        FillRule subjectRule = ToFillRule(intersectionRule);

        return ToComplexPolygon(Clip(operation, s, c, subjectRule, ToFillRule(clipIntersectionRule)));
    }

    /// <summary>
    /// Generates the final clipped shape from the specified subject and clip path.
    /// </summary>
    /// <param name="operation">The boolean operation to perform.</param>
    /// <param name="intersectionRule">The fill rule used to interpret the subject path.</param>
    /// <param name="subject">The subject path.</param>
    /// <param name="clip">The clipping path.</param>
    /// <param name="clipIntersectionRule">The fill rule used to interpret the clipping path.</param>
    /// <returns>
    /// The <see cref="ComplexPolygon"/> representing the result of the boolean operation.
    /// </returns>
    public static ComplexPolygon GenerateClippedShapes(
        BooleanOperation operation,
        IntersectionRule intersectionRule,
        IPath subject,
        IPath clip,
        IntersectionRule clipIntersectionRule)
    {
        Guard.NotNull(subject);
        Guard.NotNull(clip);

        if (clip is IRegionPath regionPath &&
            TryClipRectangleByRegion(operation, subject, regionPath, out ComplexPolygon clippedRectangle))
        {
            return clippedRectangle;
        }

        PCPolygon s = PolygonClipperFactory.FromPath(subject);
        PCPolygon c = PolygonClipperFactory.FromPath(clip);
        FillRule subjectRule = ToFillRule(intersectionRule);

        return ToComplexPolygon(Clip(operation, s, c, subjectRule, ToFillRule(clipIntersectionRule)));
    }

    /// <summary>
    /// Maps the path fill rule to the equivalent clipping fill rule.
    /// </summary>
    /// <param name="rule">The rule used to fill the source path.</param>
    /// <returns>The rule used to fill the source polygon.</returns>
    private static FillRule ToFillRule(IntersectionRule rule)
        => rule == IntersectionRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd;

    /// <summary>
    /// Attempts the rectangle-by-region fast path: intersecting an axis-aligned rectangle
    /// subject with a rect-set region needs no polygon clipper at all.
    /// </summary>
    /// <param name="operation">The boolean operation. Only <see cref="BooleanOperation.Intersection"/> qualifies.</param>
    /// <param name="subject">The subject path. Only <see cref="RectanglePolygon"/> qualifies.</param>
    /// <param name="regionPath">The rect-set clip operand.</param>
    /// <param name="clipped">When this method returns <see langword="true"/>, contains the clipped result.</param>
    /// <returns><see langword="true"/> when the fast path applied; otherwise, <see langword="false"/>.</returns>
    private static bool TryClipRectangleByRegion(
        BooleanOperation operation,
        IPath subject,
        IRegionPath regionPath,
        out ComplexPolygon clipped)
    {
        clipped = null!;
        if (operation != BooleanOperation.Intersection ||
            subject is not RectanglePolygon rectangle)
        {
            return false;
        }

        RectangleF subjectBounds = rectangle.Bounds;
        IReadOnlyList<Rectangle> rectangles = regionPath.Rectangles;
        List<IPath> paths = [];

        for (int i = 0; i < rectangles.Count; i++)
        {
            RectangleF intersection = RectangleF.Intersect(subjectBounds, ToRectangleF(rectangles[i]));
            if (intersection.Width > 0 && intersection.Height > 0)
            {
                paths.Add(new RectanglePolygon(intersection));
            }
        }

        clipped = new ComplexPolygon(paths);
        return true;
    }

    /// <summary>
    /// Converts an integer rectangle to its floating-point equivalent.
    /// </summary>
    /// <param name="rectangle">The rectangle to convert.</param>
    /// <returns>The converted rectangle.</returns>
    private static RectangleF ToRectangleF(Rectangle rectangle)
        => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    /// <summary>
    /// Applies a boolean operation between two converted polygons.
    /// </summary>
    /// <param name="operation">The boolean operation to perform.</param>
    /// <param name="subject">The converted subject polygon.</param>
    /// <param name="clip">The converted clip polygon.</param>
    /// <param name="subjectRule">The rule used to fill the subject.</param>
    /// <param name="clipRule">The rule used to fill the clip polygon.</param>
    /// <returns>The selected boundaries, open lines, and isolated points.</returns>
    private static PCPolygon Clip(
        BooleanOperation operation,
        PCPolygon subject,
        PCPolygon clip,
        FillRule subjectRule,
        FillRule clipRule)
        => operation switch
        {
            BooleanOperation.Xor => PolygonClipperAction.Xor(subject, clip, subjectRule, clipRule),
            BooleanOperation.Difference => PolygonClipperAction.Difference(subject, clip, subjectRule, clipRule),
            BooleanOperation.Union => PolygonClipperAction.Union(subject, clip, subjectRule, clipRule),
            _ => PolygonClipperAction.Intersection(subject, clip, subjectRule, clipRule),
        };

    /// <summary>
    /// Converts a PolygonClipper result into a path with the same figure closure.
    /// </summary>
    /// <param name="result">The polygon produced by the clipper.</param>
    /// <returns>The converted <see cref="ComplexPolygon"/>.</returns>
    internal static ComplexPolygon ToComplexPolygon(PCPolygon result)
    {
        IPath[] shapes = new IPath[result.Count];

        for (int i = 0; i < result.Count; i++)
        {
            Contour contour = result[i];
            if (contour.Count == 1)
            {
                Vertex vertex = contour[0];
                PointF point = new((float)vertex.X, (float)vertex.Y);
                shapes[i] = new Path(new LinearLineSegment(point));
                continue;
            }

            PointF[] points = new PointF[contour.Count];
            for (int j = 0; j < contour.Count; j++)
            {
                Vertex vertex = contour[j];
                points[j] = new PointF((float)vertex.X, (float)vertex.Y);
            }

            shapes[i] = contour.IsClosed ? new Polygon(points) : new Path(points);
        }

        return new(shapes);
    }
}
