// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// A closed rectangular path defined by four straight edges.
/// </summary>
public sealed class RectangularPolygon : IPath, ISimplePath, IPathInternals
{
    private readonly Vector2 topLeft;
    private readonly Vector2 bottomRight;
    private readonly PointF[] points;
    private readonly float halfLength;
    private readonly float length;
    private LinearGeometryCache geometryCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangularPolygon" /> class.
    /// </summary>
    /// <param name="x">The horizontal position of the rectangle.</param>
    /// <param name="y">The vertical position of the rectangle.</param>
    /// <param name="width">The width of the rectangle.</param>
    /// <param name="height">The height of the rectangle.</param>
    public RectangularPolygon(float x, float y, float width, float height)
        : this(new PointF(x, y), new SizeF(width, height))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangularPolygon" /> class.
    /// </summary>
    /// <param name="topLeft">
    /// The <see cref="PointF"/> which specifies the rectangles top/left point in a two-dimensional plane.
    /// </param>
    /// <param name="bottomRight">
    /// The <see cref="PointF"/> which specifies the rectangles bottom/right point in a two-dimensional plane.
    /// </param>
    public RectangularPolygon(PointF topLeft, PointF bottomRight)
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

        this.halfLength = this.Size.Width + this.Size.Height;
        this.length = this.halfLength * 2;
        this.Bounds = new RectangleF(this.Location, this.Size);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangularPolygon"/> class.
    /// </summary>
    /// <param name="point">
    /// The <see cref="PointF"/> which specifies the rectangles point in a two-dimensional plane.
    /// </param>
    /// <param name="size">
    /// The <see cref="SizeF"/> which specifies the rectangles height and width.
    /// </param>
    public RectangularPolygon(PointF point, SizeF size)
        : this(point, point + size)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangularPolygon"/> class.
    /// </summary>
    /// <param name="rectangle">The rectangle.</param>
    public RectangularPolygon(RectangleF rectangle)
        : this(rectangle.Location, rectangle.Location + rectangle.Size)
    {
    }

    /// <summary>
    /// Gets the location.
    /// </summary>
    public PointF Location { get; }

    /// <summary>
    /// Gets the x-coordinate of the left edge.
    /// </summary>
    public float Left => this.X;

    /// <summary>
    /// Gets the x-coordinate.
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
    /// Gets the y-coordinate.
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
    /// Converts the polygon to a rectangular polygon from its bounds.
    /// </summary>
    /// <param name="polygon">The polygon to convert.</param>
    public static explicit operator RectangularPolygon(Polygon polygon)
        => new(polygon.Bounds.X, polygon.Bounds.Y, polygon.Bounds.Width, polygon.Bounds.Height);

    /// <inheritdoc/>
    public IPath Transform(Matrix4x4 matrix)
    {
        if (matrix.IsIdentity)
        {
            return this;
        }

        // Rectangles may be rotated and skewed which means they will then need representing by a polygon
        return new Polygon(new LinearLineSegment(this.points).Transform(matrix));
    }

    /// <inheritdoc />
    SegmentInfo IPathInternals.PointAlongPath(float distance)
    {
        distance %= this.length;

        if (distance < this.Width)
        {
            // we are on the top stretch
            return new SegmentInfo
            {
                Point = new Vector2(this.Left + distance, this.Top),
                Angle = MathF.PI
            };
        }

        distance -= this.Width;
        if (distance < this.Height)
        {
            // down on right
            return new SegmentInfo
            {
                Point = new Vector2(this.Right, this.Top + distance),
                Angle = -MathF.PI / 2
            };
        }

        distance -= this.Height;
        if (distance < this.Width)
        {
            // bottom right to left
            return new SegmentInfo
            {
                Point = new Vector2(this.Right - distance, this.Bottom),
                Angle = 0
            };
        }

        distance -= this.Width;
        return new SegmentInfo
        {
            Point = new Vector2(this.Left, this.Bottom - distance),
            Angle = (float)(Math.PI / 2)
        };
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

        // Any rotation or shear in the transform can turn the axis-aligned edges into slanted ones,
        // so count each edge individually rather than assuming the axis-aligned case.
        int nonHorizontalSegmentCountPixelBoundary = 0;
        int nonHorizontalSegmentCountPixelCenter = 0;
        for (int i = 0; i < 4; i++)
        {
            PointF a = points[i];
            PointF b = points[(i + 1) % 4];
            if (MathF.Floor(a.Y) != MathF.Floor(b.Y))
            {
                nonHorizontalSegmentCountPixelBoundary++;
            }

            if (MathF.Floor(a.Y + 0.5F) != MathF.Floor(b.Y + 0.5F))
            {
                nonHorizontalSegmentCountPixelCenter++;
            }
        }

        return new LinearGeometry(
            new LinearGeometryInfo
            {
                Bounds = RectangleF.FromLTRB(minX, minY, maxX, maxY),
                ContourCount = 1,
                PointCount = 4,
                SegmentCount = 4,
                NonHorizontalSegmentCountPixelBoundary = nonHorizontalSegmentCountPixelBoundary,
                NonHorizontalSegmentCountPixelCenter = nonHorizontalSegmentCountPixelCenter
            },
            [new LinearContour { PointStart = 0, PointCount = 4, SegmentStart = 0, SegmentCount = 4, IsClosed = true }],
            points);
    }

    /// <inheritdoc/>
    public IPath AsClosedPath() => this;
}
