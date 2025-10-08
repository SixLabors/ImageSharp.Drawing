// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.PolygonClipper;
using ClipperPolygon = SixLabors.PolygonClipper.Polygon;
using PolygonClipperAction = SixLabors.PolygonClipper.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

/// <summary>
/// Performs polygon clipping operations.
/// </summary>
internal sealed class Clipper
{
    private ClipperPolygon? subject;
    private ClipperPolygon? clip;
    private readonly IntersectionRule rule;

    /// <summary>
    /// Initializes a new instance of the <see cref="Clipper"/> class.
    /// </summary>
    /// <param name="rule">The intersection rule.</param>
    public Clipper(IntersectionRule rule) => this.rule = rule;

    /// <summary>
    /// Generates the clipped shapes from the previously provided paths.
    /// </summary>
    /// <param name="operation">The clipping operation.</param>
    /// <returns>The <see cref="T:IPath[]"/>.</returns>
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
    /// Adds the collection of paths.
    /// </summary>
    /// <param name="paths">The paths.</param>
    /// <param name="clippingType">The clipping type.</param>
    public void AddPaths(IEnumerable<IPath> paths, ClippingType clippingType)
    {
        Guard.NotNull(paths, nameof(paths));

        // Accumulate all paths of the complex shape into a single polygon.
        ClipperPolygon polygon = [];

        foreach (IPath path in paths)
        {
            polygon = PolygonClipperFactory.FromSimplePaths(path.Flatten(), this.rule, polygon);
        }

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
    /// Adds the path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="clippingType">The clipping type.</param>
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
