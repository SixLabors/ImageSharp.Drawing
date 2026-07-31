// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// A closed rectangular path defined by four straight edges.
/// </summary>
public sealed class RectanglePolygon : IPath, ISimplePath
{
    /// <summary>
    /// The top-left corner of the rectangle.
    /// </summary>
    private readonly Vector2 topLeft;

    /// <summary>
    /// The bottom-right corner of the rectangle.
    /// </summary>
    private readonly Vector2 bottomRight;

    /// <summary>
    /// The four corner points in clockwise order starting at the top-left.
    /// </summary>
    private readonly PointF[] points;

    /// <summary>
    /// The per-scale cache of retained linear geometry.
    /// </summary>
    private LinearGeometryCache geometryCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="RectanglePolygon" /> class.
    /// </summary>
    /// <param name="x">The horizontal position of the rectangle.</param>
    /// <param name="y">The vertical position of the rectangle.</param>
    /// <param name="width">The width of the rectangle.</param>
    /// <param name="height">The height of the rectangle.</param>
    public RectanglePolygon(float x, float y, float width, float height)
        : this(new PointF(x, y), new SizeF(width, height))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectanglePolygon" /> class.
    /// </summary>
    /// <param name="topLeft">
    /// The <see cref="PointF"/> which specifies the rectangle's top-left point in a two-dimensional plane.
    /// </param>
    /// <param name="bottomRight">
    /// The <see cref="PointF"/> which specifies the rectangle's bottom-right point in a two-dimensional plane.
    /// </param>
    public RectanglePolygon(PointF topLeft, PointF bottomRight)
    {
        this.Location = topLeft;
        this.topLeft = topLeft;
        this.bottomRight = bottomRight;
        this.Size = new SizeF(bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);

        this.points =
        [
            this.topLeft,
            new Vector2(this.bottomRight.X, this.topLeft.Y),
            this.bottomRight,
            new Vector2(this.topLeft.X, this.bottomRight.Y)
        ];

        this.Bounds = new RectangleF(this.Location, this.Size);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectanglePolygon"/> class.
    /// </summary>
    /// <param name="point">
    /// The <see cref="PointF"/> which specifies the rectangle's point in a two-dimensional plane.
    /// </param>
    /// <param name="size">
    /// The <see cref="SizeF"/> which specifies the rectangle's height and width.
    /// </param>
    public RectanglePolygon(PointF point, SizeF size)
        : this(point, point + size)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectanglePolygon"/> class.
    /// </summary>
    /// <param name="rectangle">The rectangle.</param>
    public RectanglePolygon(RectangleF rectangle)
        : this(rectangle.Location, rectangle.Location + rectangle.Size)
    {
    }

    /// <summary>
    /// Gets the top-left location of the rectangle.
    /// </summary>
    public PointF Location { get; }

    /// <summary>
    /// Gets the x-coordinate of the left edge.
    /// </summary>
    public float Left => this.X;

    /// <summary>
    /// Gets the x-coordinate of the top-left corner.
    /// </summary>
    public float X => this.topLeft.X;

    /// <summary>
    /// Gets the x-coordinate of the right edge.
    /// </summary>
    public float Right => this.bottomRight.X;

    /// <summary>
    /// Gets the y-coordinate of the top edge.
    /// </summary>
    public float Top => this.Y;

    /// <summary>
    /// Gets the y-coordinate of the top-left corner.
    /// </summary>
    public float Y => this.topLeft.Y;

    /// <summary>
    /// Gets the y-coordinate of the bottom edge.
    /// </summary>
    public float Bottom => this.bottomRight.Y;

    /// <inheritdoc/>
    public RectangleF Bounds { get; private set; }

    /// <inheritdoc/>
    public bool IsClosed => true;

    /// <inheritdoc/>
    public ReadOnlyMemory<PointF> Points => this.points;

    /// <summary>
    /// Gets the size.
    /// </summary>
    public SizeF Size { get; }

    /// <summary>
    /// Gets the width.
    /// </summary>
    public float Width => this.Size.Width;

    /// <summary>
    /// Gets the height.
    /// </summary>
    public float Height => this.Size.Height;

    /// <inheritdoc/>
    public PathTypes PathType => PathTypes.Closed;

    /// <summary>
    /// Gets the center point.
    /// </summary>
    public PointF Center => (this.topLeft + this.bottomRight) / 2;

    /// <summary>
    /// Converts a polygon to a rectangle polygon from its bounds.
    /// </summary>
    /// <param name="polygon">The polygon to convert.</param>
    /// <returns>The rectangle polygon covering the source polygon's bounds.</returns>
    public static explicit operator RectanglePolygon(Polygon polygon)
        => new(polygon.Bounds.X, polygon.Bounds.Y, polygon.Bounds.Width, polygon.Bounds.Height);

    /// <inheritdoc/>
    public IPath Transform(Matrix4x4 matrix)
    {
        if (matrix.IsIdentity)
        {
            return this;
        }

        if (MatrixUtilities.PreservesAxisAlignedRectangles(matrix))
        {
            // Keep the rectangle type when possible so later clip/scissor code can still
            // recognize the shape without flattening a generic polygon.
            Vector2 topRight = new(this.bottomRight.X, this.topLeft.Y);
            Vector2 bottomLeft = new(this.topLeft.X, this.bottomRight.Y);

            // Transform all four corners rather than transforming location/size. Negative
            // scales and axis swaps can invert edges, so the bounds must be recomputed.
            Vector2 p0 = Vector2.Transform(this.topLeft, matrix);
            Vector2 p1 = Vector2.Transform(topRight, matrix);
            Vector2 p2 = Vector2.Transform(this.bottomRight, matrix);
            Vector2 p3 = Vector2.Transform(bottomLeft, matrix);

            float left = MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X));
            float top = MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y));
            float right = MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X));
            float bottom = MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y));

            return new RectanglePolygon(RectangleF.FromLTRB(left, top, right, bottom));
        }

        // Skewed or freely rotated rectangles need polygon geometry to preserve their edges.
        return new Polygon(new LinearLineSegment(this.points).Transform(matrix));
    }

    /// <inheritdoc/>
    public IEnumerable<ISimplePath> Flatten()
    {
        yield return this;
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

    /// <inheritdoc />
    public bool TryGetPathPointAtDistance(float distance, Vector2 scale, out PathPoint pathPoint)
        => this.ToLinearGeometry(scale).TryGetPathPointAtDistance(distance, out pathPoint);

    /// <inheritdoc />
    public bool TryGetPathPointAtDistanceUnbounded(float distance, Vector2 scale, out PathPoint pathPoint)
        => this.ToLinearGeometry(scale).TryGetPathPointAtDistanceUnbounded(distance, out pathPoint);

    /// <inheritdoc/>
    public bool TryGetSegment(float startDistance, float stopDistance, bool startOnBeginFigure, Vector2 scale, out IPath path)
        => this.ToLinearGeometry(scale).TryGetSegment(startDistance, stopDistance, startOnBeginFigure, out path);

    /// <summary>
    /// Builds the retained four-point closed contour, scaling each corner by <paramref name="scale"/>.
    /// </summary>
    /// <param name="scale">The X/Y scale applied to the corner points.</param>
    /// <returns>The retained linear geometry.</returns>
    private LinearGeometry BuildLinearGeometry(Vector2 scale)
    {
        PointF p0 = new(this.points[0].X * scale.X, this.points[0].Y * scale.Y);
        PointF p1 = new(this.points[1].X * scale.X, this.points[1].Y * scale.Y);
        PointF p2 = new(this.points[2].X * scale.X, this.points[2].Y * scale.Y);
        PointF p3 = new(this.points[3].X * scale.X, this.points[3].Y * scale.Y);

        PointF[] points = [p0, p1, p2, p3];

        float minX = MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X));
        float minY = MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y));
        float maxX = MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X));
        float maxY = MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y));
        RectangleF bounds = RectangleF.FromLTRB(minX, minY, maxX, maxY);

        return new LinearGeometry(
            new LinearGeometryInfo
            {
                Bounds = bounds,
                ContourCount = 1,
                PointCount = 4,
                SegmentCount = 4
            },
            [new LinearContour
            {
                PointStart = 0,
                PointCount = 4,
                Bounds = bounds,
                SegmentStart = 0,
                SegmentCount = 4,
                IsClosed = true
            }
            ],
            points);
    }

    /// <inheritdoc/>
    public IPath AsClosedPath() => this;
}
