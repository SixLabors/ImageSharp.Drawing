// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// A rounded rectangle shape defined by rectangle bounds and corner radii.
/// </summary>
public sealed class RoundedRectanglePolygon : Polygon
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoundedRectanglePolygon"/> class.
    /// </summary>
    /// <param name="rectangle">The rectangle bounds.</param>
    /// <param name="radius">The x and y radius of each corner.</param>
    public RoundedRectanglePolygon(RectangleF rectangle, float radius)
        : this(rectangle, new SizeF(radius, radius))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoundedRectanglePolygon"/> class.
    /// </summary>
    /// <param name="rectangle">The rectangle bounds.</param>
    /// <param name="radius">The x and y radii of each corner.</param>
    public RoundedRectanglePolygon(RectangleF rectangle, SizeF radius)
        : base(CreateSegments(rectangle, radius))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoundedRectanglePolygon"/> class.
    /// </summary>
    /// <param name="x">The x-coordinate of the rectangle.</param>
    /// <param name="y">The y-coordinate of the rectangle.</param>
    /// <param name="width">The rectangle width.</param>
    /// <param name="height">The rectangle height.</param>
    /// <param name="radius">The x and y radius of each corner.</param>
    public RoundedRectanglePolygon(float x, float y, float width, float height, float radius)
        : this(new RectangleF(x, y, width, height), radius)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoundedRectanglePolygon"/> class.
    /// </summary>
    /// <param name="x">The x-coordinate of the rectangle.</param>
    /// <param name="y">The y-coordinate of the rectangle.</param>
    /// <param name="width">The rectangle width.</param>
    /// <param name="height">The rectangle height.</param>
    /// <param name="radius">The x and y radii of each corner.</param>
    public RoundedRectanglePolygon(float x, float y, float width, float height, SizeF radius)
        : this(new RectangleF(x, y, width, height), radius)
    {
    }

    private RoundedRectanglePolygon(ILineSegment[] segments)
        : base(segments, true)
    {
    }

    /// <inheritdoc />
    public override IPath Transform(Matrix4x4 matrix)
    {
        if (matrix.IsIdentity)
        {
            return this;
        }

        ILineSegment[] segments = new ILineSegment[this.LineSegments.Count];

        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = this.LineSegments[i].Transform(matrix);
        }

        return new RoundedRectanglePolygon(segments);
    }

    private static ILineSegment[] CreateSegments(RectangleF rectangle, SizeF radius)
    {
        float left = MathF.Min(rectangle.Left, rectangle.Right);
        float top = MathF.Min(rectangle.Top, rectangle.Bottom);
        float right = MathF.Max(rectangle.Left, rectangle.Right);
        float bottom = MathF.Max(rectangle.Top, rectangle.Bottom);
        float width = right - left;
        float height = bottom - top;

        if (width <= 0 || height <= 0)
        {
            return [];
        }

        float radiusX = radius.Width;
        float radiusY = radius.Height;

        if (radiusX <= 0 || radiusY <= 0)
        {
            return
            [
                new LinearLineSegment(
                    new PointF(left, top),
                    new PointF(right, top),
                    new PointF(right, bottom),
                    new PointF(left, bottom))
            ];
        }

        float radiusScale = MathF.Min(width / (radiusX + radiusX), height / (radiusY + radiusY));
        if (radiusScale < 1F)
        {
            // Preserve the supplied corner shape while shrinking it enough that opposing corners do not overlap.
            radiusX *= radiusScale;
            radiusY *= radiusScale;
        }

        SizeF cornerRadius = new(radiusX, radiusY);
        PointF topLeft = new(left + radiusX, top);
        PointF topRight = new(right - radiusX, top);
        PointF rightTop = new(right, top + radiusY);
        PointF rightBottom = new(right, bottom - radiusY);
        PointF bottomRight = new(right - radiusX, bottom);
        PointF bottomLeft = new(left + radiusX, bottom);
        PointF leftBottom = new(left, bottom - radiusY);
        PointF leftTop = new(left, top + radiusY);

        return
        [
            new LinearLineSegment(topLeft, topRight),
            new ArcLineSegment(new PointF(right - radiusX, top + radiusY), cornerRadius, 0F, -90F, 90F),
            new LinearLineSegment(rightTop, rightBottom),
            new ArcLineSegment(new PointF(right - radiusX, bottom - radiusY), cornerRadius, 0F, 0F, 90F),
            new LinearLineSegment(bottomRight, bottomLeft),
            new ArcLineSegment(new PointF(left + radiusX, bottom - radiusY), cornerRadius, 0F, 90F, 90F),
            new LinearLineSegment(leftBottom, leftTop),
            new ArcLineSegment(new PointF(left + radiusX, top + radiusY), cornerRadius, 0F, 180F, 90F)
        ];
    }
}
