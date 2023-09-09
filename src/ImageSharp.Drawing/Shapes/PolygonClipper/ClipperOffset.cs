// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

/// <summary>
/// Wrapper for clipper offset
/// </summary>
internal class ClipperOffset
{
    private readonly PolygonOffsetter polygonClipperOffset;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClipperOffset"/> class.
    /// </summary>
    /// <param name="meterLimit">meter limit</param>
    /// <param name="arcTolerance">arc tolerance</param>
    public ClipperOffset(float meterLimit = 2F, float arcTolerance = .25F)
        => this.polygonClipperOffset = new(meterLimit, arcTolerance);

    /// <summary>
    /// Calculates an offset polygon based on the given path and width.
    /// </summary>
    /// <param name="width">Width</param>
    /// <returns>path offset</returns>
    public ComplexPolygon Execute(float width)
    {
        PathsF solution = new();
        this.polygonClipperOffset.Execute(width, solution);

        Polygon[] polygons = new Polygon[solution.Count];
        for (int i = 0; i < solution.Count; i++)
        {
            PathF pt = solution[i];
            PointF[] points = new PointF[pt.Count];
            for (int j = 0; j < pt.Count; j++)
            {
                points[j] = pt[j];
            }

            polygons[i] = new Polygon(points);
        }

        return new ComplexPolygon(polygons);
    }

    /// <summary>
    /// Adds the path points
    /// </summary>
    /// <param name="pathPoints">The path points</param>
    /// <param name="jointStyle">Joint Style</param>
    /// <param name="endCapStyle">Endcap Style</param>
    public void AddPath(ReadOnlySpan<PointF> pathPoints, JointStyle jointStyle, EndCapStyle endCapStyle)
    {
        PathF points = new(pathPoints.Length);
        for (int i = 0; i < pathPoints.Length; i++)
        {
            points.Add(pathPoints[i]);
        }

        this.polygonClipperOffset.AddPath(points, jointStyle, endCapStyle);
    }

    /// <summary>
    /// Adds the path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="jointStyle">Joint Style</param>
    /// <param name="endCapStyle">Endcap Style</param>
    public void AddPath(IPath path, JointStyle jointStyle, EndCapStyle endCapStyle)
    {
        Guard.NotNull(path, nameof(path));

        foreach (ISimplePath p in path.Flatten())
        {
            this.AddPath(p, jointStyle, endCapStyle);
        }
    }

    /// <summary>
    /// Adds the path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="jointStyle">Joint Style</param>
    /// <param name="endCapStyle">Endcap Style</param>
    private void AddPath(ISimplePath path, JointStyle jointStyle, EndCapStyle endCapStyle)
    {
        ReadOnlySpan<PointF> vectors = path.Points.Span;
        this.AddPath(vectors, jointStyle, path.IsClosed ? EndCapStyle.Joined : endCapStyle);
    }
}
