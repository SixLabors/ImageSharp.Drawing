// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonGeometry;

/// <summary>
/// Generates clipped shapes from one or more input paths using polygon boolean operations.
/// </summary>
/// <remarks>
/// This class provides a high-level wrapper around the low-level <see cref="PolygonClipper"/>.
/// It accumulates subject and clip polygons, applies the specified <see cref="BooleanOperation"/>,
/// and converts the resulting polygon contours back into <see cref="IPath"/> instances suitable
/// for rendering or further processing.
/// </remarks>
internal sealed class ClippedShapeGenerator
{
    private readonly PolygonClipper polygonClipper;
    private readonly IntersectionRule rule;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClippedShapeGenerator"/> class.
    /// </summary>
    /// <param name="rule">The intersection rule.</param>
    public ClippedShapeGenerator(IntersectionRule rule)
    {
        this.rule = rule;
        this.polygonClipper = new PolygonClipper() { PreserveCollinear = true };
    }

    /// <summary>
    /// Generates the final clipped shapes from the previously provided subject and clip paths.
    /// </summary>
    /// <param name="operation">
    /// The boolean operation to perform, such as <see cref="BooleanOperation.Union"/>,
    /// <see cref="BooleanOperation.Intersection"/>, or <see cref="BooleanOperation.Difference"/>.
    /// </param>
    /// <param name="positive">TEMP. Remove when we update IntersectionRule to add missing entries.</param>
    /// <returns>
    /// An array of <see cref="IPath"/> instances representing the result of the boolean operation.
    /// </returns>
    public IPath[] GenerateClippedShapes(BooleanOperation operation, bool? positive = null)
    {
        PathsF closedPaths = [];
        PathsF openPaths = [];

        ClipperFillRule fillRule = this.rule == IntersectionRule.EvenOdd ? ClipperFillRule.EvenOdd : ClipperFillRule.NonZero;

        if (positive.HasValue)
        {
            fillRule = positive.Value ? ClipperFillRule.Positive : ClipperFillRule.Negative;
        }

        this.polygonClipper.Execute(operation, fillRule, closedPaths, openPaths);

        IPath[] shapes = new IPath[closedPaths.Count + openPaths.Count];

        int index = 0;
        for (int i = 0; i < closedPaths.Count; i++)
        {
            PathF path = closedPaths[i];
            PointF[] points = new PointF[path.Count];

            for (int j = 0; j < path.Count; j++)
            {
                points[j] = path[j];
            }

            shapes[index++] = new Polygon(points);
        }

        for (int i = 0; i < openPaths.Count; i++)
        {
            PathF path = openPaths[i];
            PointF[] points = new PointF[path.Count];

            for (int j = 0; j < path.Count; j++)
            {
                points[j] = path[j];
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

        foreach (IPath p in paths)
        {
            this.AddPath(p, clippingType);
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

        foreach (ISimplePath p in path.Flatten())
        {
            this.AddPath(p, clippingType);
        }
    }

    private void AddPath(ISimplePath path, ClippingType clippingType)
    {
        ReadOnlySpan<PointF> vectors = path.Points.Span;
        PathF points = new(vectors.Length);
        for (int i = 0; i < vectors.Length; i++)
        {
            points.Add(vectors[i]);
        }

        this.polygonClipper.AddPath(points, clippingType, !path.IsClosed);
    }
}
