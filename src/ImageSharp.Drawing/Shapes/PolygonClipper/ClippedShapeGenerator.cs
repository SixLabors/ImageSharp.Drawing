// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.PolygonClipper;
using ClipperPolygon = SixLabors.PolygonClipper.Polygon;
using PolygonClipperAction = SixLabors.PolygonClipper.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

/// <summary>
/// Generates clipped shapes from one or more input paths using polygon boolean operations.
/// </summary>
/// <remarks>
/// This class provides a high-level wrapper around the low-level <see cref="PolygonClipperAction"/>.
/// It accumulates subject and clip polygons, applies the specified <see cref="BooleanOperation"/>,
/// and converts the resulting polygon contours back into <see cref="IPath"/> instances suitable
/// for rendering or further processing.
/// </remarks>
internal sealed class ClippedShapeGenerator
{
    private ClipperPolygon? subject;
    private ClipperPolygon? clip;
    private readonly IntersectionRule rule;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClippedShapeGenerator"/> class.
    /// </summary>
    /// <param name="rule">The intersection rule.</param>
    public ClippedShapeGenerator(IntersectionRule rule) => this.rule = rule;

    /// <summary>
    /// Generates the final clipped shapes from the previously provided subject and clip paths.
    /// </summary>
    /// <param name="operation">
    /// The boolean operation to perform, such as <see cref="BooleanOperation.Union"/>,
    /// <see cref="BooleanOperation.Intersection"/>, or <see cref="BooleanOperation.Difference"/>.
    /// </param>
    /// <returns>
    /// An array of <see cref="IPath"/> instances representing the result of the boolean operation.
    /// </returns>
    public IPath[] GenerateClippedShapes(BooleanOperation operation)
    {
        ArgumentNullException.ThrowIfNull(this.subject);
        ArgumentNullException.ThrowIfNull(this.clip);

        PolygonClipperAction polygonClipper = new(this.subject, this.clip, operation);

        ClipperPolygon result = polygonClipper.Run();

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

        return shapes;
    }

    /// <summary>
    /// Adds a collection of paths to the current clipping operation.
    /// </summary>
    /// <param name="paths">
    /// The paths to add. Each path may represent a simple or complex polygon.
    /// </param>
    /// <param name="clippingType">
    /// Determines whether the paths are assigned to the subject or clip polygon.
    /// </param>
    public void AddPaths(IEnumerable<IPath> paths, ClippingType clippingType)
    {
        Guard.NotNull(paths, nameof(paths));

        // Accumulate all paths of the complex shape into a single polygon.
        ClipperPolygon polygon = PolygonClipperFactory.FromPaths(paths, this.rule);

        if (clippingType == ClippingType.Clip)
        {
            this.clip = polygon;
        }
        else
        {
            this.subject = polygon;
        }
    }

    /// <summary>
    /// Adds a single path to the current clipping operation.
    /// </summary>
    /// <param name="path">The path to add.</param>
    /// <param name="clippingType">
    /// Determines whether the path is assigned to the subject or clip polygon.
    /// </param>
    public void AddPath(IPath path, ClippingType clippingType)
    {
        Guard.NotNull(path, nameof(path));

        ClipperPolygon polygon = PolygonClipperFactory.FromSimplePaths(path.Flatten(), this.rule);
        if (clippingType == ClippingType.Clip)
        {
            this.clip = polygon;
        }
        else
        {
            this.subject = polygon;
        }
    }
}
