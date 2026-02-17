// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.PolygonClipper;
using PCPolygon = SixLabors.PolygonClipper.Polygon;
using PolygonClipperAction = SixLabors.PolygonClipper.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonGeometry;

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
    {
        Guard.NotNull(subject);
        Guard.NotNull(clip);

        PCPolygon s = PolygonClipperFactory.FromSimpleClosedPaths(subject.Flatten());
        PCPolygon c = PolygonClipperFactory.FromClosedPaths(clip);

        PCPolygon result = operation switch
        {
            BooleanOperation.Xor => PolygonClipperAction.Xor(s, c),
            BooleanOperation.Difference => PolygonClipperAction.Difference(s, c),
            BooleanOperation.Union => PolygonClipperAction.Union(s, c),
            _ => PolygonClipperAction.Intersection(s, c),
        };

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
}
