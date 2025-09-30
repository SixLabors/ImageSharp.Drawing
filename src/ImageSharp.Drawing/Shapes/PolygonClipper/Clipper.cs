// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

/// <summary>
/// Library to clip polygons.
/// </summary>
internal class Clipper
{
    private SixLabors.PolygonClipper.Polygon? subject;
    private SixLabors.PolygonClipper.Polygon? clip;

    /// <summary>
    /// Generates the clipped shapes from the previously provided paths.
    /// </summary>
    /// <param name="operation">The clipping operation.</param>
    /// <param name="rule">The intersection rule.</param>
    /// <returns>The <see cref="T:IPath[]"/>.</returns>
    public IPath[] GenerateClippedShapes(BooleanOperation operation)
    {
        ArgumentNullException.ThrowIfNull(this.subject);
        ArgumentNullException.ThrowIfNull(this.clip);

        SixLabors.PolygonClipper.PolygonClipper polygonClipper = new(this.subject, this.clip, operation);

        SixLabors.PolygonClipper.Polygon result = polygonClipper.Run();


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
    /// Adds the shapes.
    /// </summary>
    /// <param name="paths">The paths.</param>
    /// <param name="clippingType">The clipping type.</param>
    public void AddPaths(IEnumerable<IPath> paths, ClippingType clippingType)
    {
        Guard.NotNull(paths, nameof(paths));

        foreach (IPath p in paths)
        {
            this.AddPath(p, clippingType);
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

        foreach (ISimplePath p in path.Flatten())
        {
            this.AddPath(p, clippingType);
        }
    }

    /// <summary>
    /// Adds the path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="clippingType">Type of the poly.</param>
    internal void AddPath(ISimplePath path, ClippingType clippingType)
    {
        ReadOnlySpan<PointF> vectors = path.Points.Span;
        SixLabors.PolygonClipper.Polygon polygon = [];
        Contour contour = new();
        polygon.Add(contour);

        foreach (PointF point in vectors)
        {
            contour.AddVertex(new Vertex(point.X, point.Y));
        }

        switch (clippingType)
        {
            case ClippingType.Clip:
                this.clip = polygon;
                break;
            case ClippingType.Subject:
                this.subject = polygon;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(clippingType), clippingType, null);
        }
    }
}
