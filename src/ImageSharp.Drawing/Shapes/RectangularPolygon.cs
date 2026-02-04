// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// A polygon tha allows the optimized drawing of rectangles.
/// </summary>
/// <seealso cref="IPath" />
public sealed class RectangularPolygon : IPath, ISimplePath, IPathInternals
{
    private readonly Vector2 topLeft;
    private readonly Vector2 bottomRight;
    private readonly PointF[] points;
    private readonly float halfLength;
    private readonly float length;

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
    public IPath Transform(Matrix3x2 matrix)
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
    public IPath AsClosedPath() => this;
}
