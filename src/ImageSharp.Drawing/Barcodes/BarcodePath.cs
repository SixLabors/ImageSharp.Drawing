// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The bars or the dark module runs of one barcode symbol as a single closed path. Each axis aligned
/// rectangle is one contour of four points. The canvas retains the path until it flushes, so the path
/// owns its rectangle array.
/// </summary>
internal sealed class BarcodePath : IPath
{
    /// <summary>
    /// The rectangles, in pixels. No two rectangles overlap.
    /// </summary>
    private readonly RectangleF[] rectangles;

    /// <summary>
    /// The geometry baked at the last scale the pipeline asked for.
    /// </summary>
    private LinearGeometryCache geometryCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodePath"/> class.
    /// </summary>
    /// <param name="rectangles">The rectangles, in pixels. The path takes ownership of the array.</param>
    /// <param name="bounds">The union of the rectangles.</param>
    public BarcodePath(RectangleF[] rectangles, RectangleF bounds)
    {
        this.rectangles = rectangles;
        this.Bounds = bounds;
    }

    /// <inheritdoc/>
    public PathTypes PathType => PathTypes.Closed;

    /// <inheritdoc/>
    public RectangleF Bounds { get; }

    /// <inheritdoc/>
    public IEnumerable<ISimplePath> Flatten()
    {
        foreach (RectangleF rectangle in this.rectangles)
        {
            yield return new RectanglePolygon(rectangle);
        }
    }

    /// <inheritdoc/>
    public LinearGeometry ToLinearGeometry(Vector2 scale)
        => this.geometryCache.TryGet(scale, out LinearGeometry? hit)
            ? hit
            : this.geometryCache.Store(scale, this.BuildLinearGeometry(scale));

    /// <inheritdoc/>
    public float ComputeLength(Vector2 scale)
        => this.ToLinearGeometry(scale).ComputeLength();

    /// <inheritdoc/>
    public float ComputeArea(Vector2 scale)
        => this.ToLinearGeometry(scale).ComputeArea();

    /// <inheritdoc/>
    public bool Contains(PointF point, IntersectionRule intersectionRule, Vector2 scale)
    {
        PointF scaledPoint = new(point.X * scale.X, point.Y * scale.Y);

        return this.ToLinearGeometry(scale).Contains(scaledPoint, intersectionRule);
    }

    /// <inheritdoc/>
    public bool TryGetPathPointAtDistance(float distance, Vector2 scale, out PathPoint pathPoint)
        => this.ToLinearGeometry(scale).TryGetPathPointAtDistance(distance, out pathPoint);

    /// <inheritdoc/>
    public bool TryGetPathPointAtDistanceUnbounded(float distance, Vector2 scale, out PathPoint pathPoint)
        => this.ToLinearGeometry(scale).TryGetPathPointAtDistanceUnbounded(distance, out pathPoint);

    /// <inheritdoc/>
    public bool TryGetSegment(float startDistance, float stopDistance, bool startOnBeginFigure, Vector2 scale, out IPath path)
        => this.ToLinearGeometry(scale).TryGetSegment(startDistance, stopDistance, startOnBeginFigure, out path);

    /// <inheritdoc/>
    public IPath Transform(Matrix4x4 matrix)
    {
        if (matrix.IsIdentity)
        {
            return this;
        }

        RectangleF[] rectangles = this.rectangles;
        if (MatrixUtilities.PreservesAxisAlignedRectangles(matrix))
        {
            // A translation, a scale or an axis swap keeps every rectangle axis aligned, so the path keeps
            // its form. Each rectangle transforms by two opposite corners; a negative scale or a swap can
            // exchange the edges, so the edges are sorted again.
            RectangleF[] transformed = new RectangleF[rectangles.Length];
            Vector2 boundsMin = Vector2.Transform(new Vector2(this.Bounds.Left, this.Bounds.Top), matrix);
            Vector2 boundsMax = Vector2.Transform(new Vector2(this.Bounds.Right, this.Bounds.Bottom), matrix);
            for (int i = 0; i < rectangles.Length; i++)
            {
                RectangleF rectangle = rectangles[i];
                Vector2 a = Vector2.Transform(new Vector2(rectangle.Left, rectangle.Top), matrix);
                Vector2 b = Vector2.Transform(new Vector2(rectangle.Right, rectangle.Bottom), matrix);
                transformed[i] = RectangleF.FromLTRB(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
            }

            return new BarcodePath(
                transformed,
                RectangleF.FromLTRB(
                    MathF.Min(boundsMin.X, boundsMax.X),
                    MathF.Min(boundsMin.Y, boundsMax.Y),
                    MathF.Max(boundsMin.X, boundsMax.X),
                    MathF.Max(boundsMin.Y, boundsMax.Y)));
        }

        // A rotation or a skew turns the rectangles into general quadrilaterals.
        IPath[] paths = new IPath[rectangles.Length];
        for (int i = 0; i < rectangles.Length; i++)
        {
            paths[i] = new RectanglePolygon(rectangles[i]).Transform(matrix);
        }

        return new ComplexPolygon(paths);
    }

    /// <inheritdoc/>
    public IPath AsClosedPath() => this;

    /// <summary>
    /// Builds the geometry: one closed contour of four points for each rectangle, clockwise from the top
    /// left corner, each corner scaled by <paramref name="scale"/>.
    /// </summary>
    /// <param name="scale">The X/Y scale applied to the corner points.</param>
    /// <returns>The retained linear geometry.</returns>
    private LinearGeometry BuildLinearGeometry(Vector2 scale)
    {
        RectangleF[] rectangles = this.rectangles;
        PointF[] points = new PointF[rectangles.Length * 4];
        LinearContour[] contours = new LinearContour[rectangles.Length];
        for (int i = 0; i < rectangles.Length; i++)
        {
            RectangleF rectangle = rectangles[i];
            float left = rectangle.Left * scale.X;
            float top = rectangle.Top * scale.Y;
            float right = rectangle.Right * scale.X;
            float bottom = rectangle.Bottom * scale.Y;

            // A negative scale mirrors the rectangle, so the contour bounds sort the edges.
            RectangleF bounds = RectangleF.FromLTRB(MathF.Min(left, right), MathF.Min(top, bottom), MathF.Max(left, right), MathF.Max(top, bottom));
            int start = i * 4;
            points[start] = new PointF(left, top);
            points[start + 1] = new PointF(right, top);
            points[start + 2] = new PointF(right, bottom);
            points[start + 3] = new PointF(left, bottom);
            contours[i] = new LinearContour
            {
                PointStart = start,
                PointCount = 4,
                Bounds = bounds,
                SegmentStart = start,
                SegmentCount = 4,
                IsClosed = true
            };
        }

        float boundsLeft = this.Bounds.Left * scale.X;
        float boundsTop = this.Bounds.Top * scale.Y;
        float boundsRight = this.Bounds.Right * scale.X;
        float boundsBottom = this.Bounds.Bottom * scale.Y;
        return new LinearGeometry(
            new LinearGeometryInfo
            {
                Bounds = RectangleF.FromLTRB(
                    MathF.Min(boundsLeft, boundsRight),
                    MathF.Min(boundsTop, boundsBottom),
                    MathF.Max(boundsLeft, boundsRight),
                    MathF.Max(boundsTop, boundsBottom)),
                ContourCount = rectangles.Length,
                PointCount = points.Length,
                SegmentCount = points.Length
            },
            contours,
            points);
    }
}
