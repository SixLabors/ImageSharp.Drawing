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
/// <see cref="IRegionPath"/> clip operands are handled as rect-sets on dedicated fast paths.
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

        PCPolygon s = PolygonClipperFactory.FromClosedPath(subject, intersectionRule);

        if (singleRegionPath is not null)
        {
            return GenerateRegionClippedShapes(operation, s, singleRegionPath);
        }

        PCPolygon c = PolygonClipperFactory.FromClosedPaths(clip, clipIntersectionRule);

        return GenerateClippedShapes(operation, s, c);
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

        PCPolygon s = PolygonClipperFactory.FromClosedPath(subject, intersectionRule);

        if (clip is IRegionPath regionPathAfterFastPath)
        {
            return GenerateRegionClippedShapes(operation, s, regionPathAfterFastPath);
        }

        PCPolygon c = PolygonClipperFactory.FromClosedPath(clip, clipIntersectionRule);

        return GenerateClippedShapes(operation, s, c);
    }

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
    /// Applies a boolean operation between a subject polygon and a rect-set region operand.
    /// </summary>
    /// <param name="operation">The boolean operation to perform.</param>
    /// <param name="subject">The converted subject polygon.</param>
    /// <param name="regionPath">The rect-set clip operand.</param>
    /// <returns>The <see cref="ComplexPolygon"/> representing the result of the boolean operation.</returns>
    private static ComplexPolygon GenerateRegionClippedShapes(BooleanOperation operation, PCPolygon subject, IRegionPath regionPath)
    {
        IReadOnlyList<Rectangle> rectangles = regionPath.Rectangles;
        if (operation == BooleanOperation.Intersection)
        {
            PCPolygon result = [];

            // RegionPath is a rect-set operand: S & (r0 | r1 | ...) is the union of
            // each per-rectangle intersection. Keeping that algebra explicit avoids
            // asking the generic polygon clipper to infer region union semantics from
            // adjacent/disjoint rectangle contours.
            for (int i = 0; i < rectangles.Count; i++)
            {
                PCPolygon clip = PolygonClipperFactory.FromRectangle(rectangles[i]);
                result.Join(PolygonClipperAction.Intersection(subject, clip));
            }

            return ToComplexPolygon(result);
        }

        PCPolygon current = subject;
        for (int i = 0; i < rectangles.Count; i++)
        {
            PCPolygon clip = PolygonClipperFactory.FromRectangle(rectangles[i]);
            current = operation switch
            {
                BooleanOperation.Xor => PolygonClipperAction.Xor(current, clip),
                BooleanOperation.Difference => PolygonClipperAction.Difference(current, clip),
                BooleanOperation.Union => PolygonClipperAction.Union(current, clip),
                _ => PolygonClipperAction.Intersection(current, clip),
            };
        }

        return ToComplexPolygon(current);
    }

    /// <summary>
    /// Applies a boolean operation between two converted polygons.
    /// </summary>
    /// <param name="operation">The boolean operation to perform.</param>
    /// <param name="subject">The converted subject polygon.</param>
    /// <param name="clip">The converted clip polygon.</param>
    /// <returns>The <see cref="ComplexPolygon"/> representing the result of the boolean operation.</returns>
    private static ComplexPolygon GenerateClippedShapes(BooleanOperation operation, PCPolygon subject, PCPolygon clip)
    {
        PCPolygon result = operation switch
        {
            BooleanOperation.Xor => PolygonClipperAction.Xor(subject, clip),
            BooleanOperation.Difference => PolygonClipperAction.Difference(subject, clip),
            BooleanOperation.Union => PolygonClipperAction.Union(subject, clip),
            _ => PolygonClipperAction.Intersection(subject, clip),
        };

        return ToComplexPolygon(result);
    }

    /// <summary>
    /// Converts a PolygonClipper result polygon into a <see cref="ComplexPolygon"/>.
    /// </summary>
    /// <param name="result">The polygon produced by the clipper.</param>
    /// <returns>The converted <see cref="ComplexPolygon"/>.</returns>
    private static ComplexPolygon ToComplexPolygon(PCPolygon result)
    {
        IPath[] shapes = new IPath[result.Count];

        int index = 0;
        for (int i = 0; i < result.Count; i++)
        {
            shapes[index++] = new Polygon(CreateContourPoints(result, i));
        }

        return new(shapes);
    }

    /// <summary>
    /// Converts a PolygonClipper contour to ImageSharp points and normalizes winding for parent/child rings.
    /// </summary>
    /// <param name="polygon">The polygon containing the contour hierarchy.</param>
    /// <param name="contourIndex">The contour index to convert.</param>
    /// <returns>The converted point array.</returns>
    internal static PointF[] CreateContourPoints(PCPolygon polygon, int contourIndex)
    {
        Contour contour = polygon[contourIndex];
        PointF[] points = new PointF[contour.Count];
        bool reverse = ShouldReverseForNonZeroWinding(polygon, contourIndex);

        if (!reverse)
        {
            for (int i = 0; i < contour.Count; i++)
            {
                Vertex vertex = contour[i];
                points[i] = new PointF((float)vertex.X, (float)vertex.Y);
            }

            return points;
        }

        for (int sourceIndex = contour.Count - 1, targetIndex = 0; sourceIndex >= 0; sourceIndex--, targetIndex++)
        {
            Vertex vertex = contour[sourceIndex];
            points[targetIndex] = new PointF((float)vertex.X, (float)vertex.Y);
        }

        return points;
    }

    /// <summary>
    /// Ensures child contours (holes/islands) use opposite winding to their direct parent.
    /// This keeps clipped output deterministic when consumed with the NonZero fill rule.
    /// </summary>
    /// <param name="polygon">The polygon containing contour hierarchy information.</param>
    /// <param name="contourIndex">The contour index to inspect.</param>
    /// <returns><see langword="true"/> when the contour should be reversed.</returns>
    private static bool ShouldReverseForNonZeroWinding(PCPolygon polygon, int contourIndex)
    {
        Contour contour = polygon[contourIndex];
        if (contour.ParentIndex is not int parentIndex || (uint)parentIndex >= (uint)polygon.Count)
        {
            return false;
        }

        Contour parentContour = polygon[parentIndex];
        return contour.IsCounterClockwise() == parentContour.IsCounterClockwise();
    }
}
