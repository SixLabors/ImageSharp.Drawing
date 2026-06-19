// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.PolygonClipper;
using PCPolygon = SixLabors.PolygonClipper.Polygon;
using PolygonClipperAction = SixLabors.PolygonClipper.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.PolygonGeometry;

/// <summary>
/// Generates clipped shapes from one or more input paths using polygon boolean operations.
/// </summary>
/// <remarks>
/// This class provides a high-level wrapper around the low-level <see cref="PolygonClipperAction"/>.
/// It accumulates subject and clip polygons, applies the specified <see cref="BooleanOperation"/>,
/// and converts the resulting polygon contours back into <see cref="ComplexPolygon"/> instances suitable
/// for rendering or further processing.
/// </remarks>
internal static class ClippedShapeGenerator
{
    /// <summary>
    /// Generates the final clipped shapes from the previously provided subject and clip paths.
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
        => GenerateClippedShapes(new ShapeOptions { BooleanOperation = operation }, subject, clip);

    /// <summary>
    /// Generates the final clipped shapes from the previously provided subject and clip paths.
    /// </summary>
    /// <param name="options">The shape options used to interpret the input paths.</param>
    /// <param name="subject">The subject path.</param>
    /// <param name="clip">The clipping paths.</param>
    /// <returns>
    /// The <see cref="ComplexPolygon"/> representing the result of the boolean operation.
    /// </returns>
    public static ComplexPolygon GenerateClippedShapes(
        ShapeOptions options,
        IPath subject,
        IEnumerable<IPath> clip)
        => GenerateClippedShapes(options, subject, clip, options.IntersectionRule);

    /// <summary>
    /// Generates the final clipped shapes from the previously provided subject and clip paths.
    /// </summary>
    /// <param name="options">The shape options used to interpret the subject path and choose the boolean operation.</param>
    /// <param name="subject">The subject path.</param>
    /// <param name="clip">The clipping paths.</param>
    /// <param name="clipIntersectionRule">The fill rule used to interpret the clipping paths.</param>
    /// <returns>
    /// The <see cref="ComplexPolygon"/> representing the result of the boolean operation.
    /// </returns>
    public static ComplexPolygon GenerateClippedShapes(
        ShapeOptions options,
        IPath subject,
        IEnumerable<IPath> clip,
        IntersectionRule clipIntersectionRule)
    {
        Guard.NotNull(subject);
        Guard.NotNull(clip);

        PCPolygon s = PolygonClipperFactory.FromClosedPath(subject, options.IntersectionRule);

        if (clip is IReadOnlyList<IPath> { Count: 1 } clipList && clipList[0] is IRegionPath regionPath)
        {
            return GenerateRegionClippedShapes(options, s, regionPath);
        }

        PCPolygon c = PolygonClipperFactory.FromClosedPaths(clip, clipIntersectionRule);

        return GenerateClippedShapes(options, s, c);
    }

    /// <summary>
    /// Generates the final clipped shape from the previously provided subject and clip path.
    /// </summary>
    /// <param name="options">The shape options used to interpret the subject path and choose the boolean operation.</param>
    /// <param name="subject">The subject path.</param>
    /// <param name="clip">The clipping path.</param>
    /// <param name="clipIntersectionRule">The fill rule used to interpret the clipping path.</param>
    /// <returns>
    /// The <see cref="ComplexPolygon"/> representing the result of the boolean operation.
    /// </returns>
    public static ComplexPolygon GenerateClippedShapes(
        ShapeOptions options,
        IPath subject,
        IPath clip,
        IntersectionRule clipIntersectionRule)
    {
        Guard.NotNull(subject);
        Guard.NotNull(clip);

        PCPolygon s = PolygonClipperFactory.FromClosedPath(subject, options.IntersectionRule);

        if (clip is IRegionPath regionPath)
        {
            return GenerateRegionClippedShapes(options, s, regionPath);
        }

        PCPolygon c = PolygonClipperFactory.FromClosedPath(clip, clipIntersectionRule);

        return GenerateClippedShapes(options, s, c);
    }

    private static ComplexPolygon GenerateRegionClippedShapes(ShapeOptions options, PCPolygon subject, IRegionPath regionPath)
    {
        IReadOnlyList<Rectangle> rectangles = regionPath.Rectangles;
        if (options.BooleanOperation == BooleanOperation.Intersection)
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
            current = options.BooleanOperation switch
            {
                BooleanOperation.Xor => PolygonClipperAction.Xor(current, clip),
                BooleanOperation.Difference => PolygonClipperAction.Difference(current, clip),
                BooleanOperation.Union => PolygonClipperAction.Union(current, clip),
                _ => PolygonClipperAction.Intersection(current, clip),
            };
        }

        return ToComplexPolygon(current);
    }

    private static ComplexPolygon GenerateClippedShapes(ShapeOptions options, PCPolygon subject, PCPolygon clip)
    {
        PCPolygon result = options.BooleanOperation switch
        {
            BooleanOperation.Xor => PolygonClipperAction.Xor(subject, clip),
            BooleanOperation.Difference => PolygonClipperAction.Difference(subject, clip),
            BooleanOperation.Union => PolygonClipperAction.Union(subject, clip),
            _ => PolygonClipperAction.Intersection(subject, clip),
        };

        return ToComplexPolygon(result);
    }

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
