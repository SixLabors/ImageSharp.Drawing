// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// A closed rectangular path with rounded corners.
/// </summary>
public sealed class RoundedRectanglePolygon : Polygon
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoundedRectanglePolygon"/> class.
    /// </summary>
    /// <param name="rectangle">The rectangle bounds.</param>
    /// <param name="radius">The x and y radius of each corner.</param>
    public RoundedRectanglePolygon(RectangleF rectangle, float radius)
        : this(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, radius)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoundedRectanglePolygon"/> class.
    /// </summary>
    /// <param name="rectangle">The rectangle bounds.</param>
    /// <param name="radius">The x and y radii of each corner.</param>
    public RoundedRectanglePolygon(RectangleF rectangle, SizeF radius)
        : this(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, radius)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoundedRectanglePolygon"/> class.
    /// </summary>
    /// <param name="rectangle">The rectangle bounds.</param>
    /// <param name="topLeftRadius">The x and y radii of the top-left corner.</param>
    /// <param name="topRightRadius">The x and y radii of the top-right corner.</param>
    /// <param name="bottomRightRadius">The x and y radii of the bottom-right corner.</param>
    /// <param name="bottomLeftRadius">The x and y radii of the bottom-left corner.</param>
    public RoundedRectanglePolygon(
        RectangleF rectangle,
        SizeF topLeftRadius,
        SizeF topRightRadius,
        SizeF bottomRightRadius,
        SizeF bottomLeftRadius)
        : this(
            rectangle.X,
            rectangle.Y,
            rectangle.Width,
            rectangle.Height,
            topLeftRadius,
            topRightRadius,
            bottomRightRadius,
            bottomLeftRadius)
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
        : this(x, y, width, height, new SizeF(radius, radius))
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
        : this(x, y, width, height, radius, radius, radius, radius)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoundedRectanglePolygon"/> class.
    /// </summary>
    /// <param name="x">The x-coordinate of the rectangle.</param>
    /// <param name="y">The y-coordinate of the rectangle.</param>
    /// <param name="width">The rectangle width.</param>
    /// <param name="height">The rectangle height.</param>
    /// <param name="topLeftRadius">The x and y radii of the top-left corner.</param>
    /// <param name="topRightRadius">The x and y radii of the top-right corner.</param>
    /// <param name="bottomRightRadius">The x and y radii of the bottom-right corner.</param>
    /// <param name="bottomLeftRadius">The x and y radii of the bottom-left corner.</param>
    public RoundedRectanglePolygon(
        float x,
        float y,
        float width,
        float height,
        SizeF topLeftRadius,
        SizeF topRightRadius,
        SizeF bottomRightRadius,
        SizeF bottomLeftRadius)
        : base(CreateSegments(x, y, width, height, topLeftRadius, topRightRadius, bottomRightRadius, bottomLeftRadius))
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

    private static ILineSegment[] CreateSegments(
        float x,
        float y,
        float width,
        float height,
        SizeF topLeftRadius,
        SizeF topRightRadius,
        SizeF bottomRightRadius,
        SizeF bottomLeftRadius)
    {
        RectangleF rectangle = new(x, y, width, height);
        float left = MathF.Min(rectangle.Left, rectangle.Right);
        float top = MathF.Min(rectangle.Top, rectangle.Bottom);
        float right = MathF.Max(rectangle.Left, rectangle.Right);
        float bottom = MathF.Max(rectangle.Top, rectangle.Bottom);
        float rectangleWidth = right - left;
        float rectangleHeight = bottom - top;

        if (rectangleWidth <= 0 || rectangleHeight <= 0)
        {
            return [];
        }

        SizeF topLeft = topLeftRadius.Width > 0F && topLeftRadius.Height > 0F ? topLeftRadius : default;
        SizeF topRight = topRightRadius.Width > 0F && topRightRadius.Height > 0F ? topRightRadius : default;
        SizeF bottomRight = bottomRightRadius.Width > 0F && bottomRightRadius.Height > 0F ? bottomRightRadius : default;
        SizeF bottomLeft = bottomLeftRadius.Width > 0F && bottomLeftRadius.Height > 0F ? bottomLeftRadius : default;
        bool hasTopLeftRadius = topLeft.Width > 0F;
        bool hasTopRightRadius = topRight.Width > 0F;
        bool hasBottomRightRadius = bottomRight.Width > 0F;
        bool hasBottomLeftRadius = bottomLeft.Width > 0F;

        if (!hasTopLeftRadius && !hasTopRightRadius && !hasBottomRightRadius && !hasBottomLeftRadius)
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

        float radiiWidth = MathF.Max(topLeft.Width + topRight.Width, bottomLeft.Width + bottomRight.Width);
        float radiiHeight = MathF.Max(topLeft.Height + bottomLeft.Height, topRight.Height + bottomRight.Height);
        float radiusScale = MathF.Min(rectangleWidth / radiiWidth, rectangleHeight / radiiHeight);

        if (radiusScale < 1F)
        {
            // Preserve the supplied corner shape while shrinking it enough that opposing corners do not overlap.
            topLeft = new SizeF(topLeft.Width * radiusScale, topLeft.Height * radiusScale);
            topRight = new SizeF(topRight.Width * radiusScale, topRight.Height * radiusScale);
            bottomRight = new SizeF(bottomRight.Width * radiusScale, bottomRight.Height * radiusScale);
            bottomLeft = new SizeF(bottomLeft.Width * radiusScale, bottomLeft.Height * radiusScale);
        }

        PointF topLeftPoint = new(left + topLeft.Width, top);
        PointF topRightPoint = new(right - topRight.Width, top);
        PointF rightTopPoint = new(right, top + topRight.Height);
        PointF rightBottomPoint = new(right, bottom - bottomRight.Height);
        PointF bottomRightPoint = new(right - bottomRight.Width, bottom);
        PointF bottomLeftPoint = new(left + bottomLeft.Width, bottom);
        PointF leftBottomPoint = new(left, bottom - bottomLeft.Height);
        PointF leftTopPoint = new(left, top + topLeft.Height);
        int roundedCornerCount = 0;

        if (hasTopRightRadius)
        {
            roundedCornerCount++;
        }

        if (hasBottomRightRadius)
        {
            roundedCornerCount++;
        }

        if (hasBottomLeftRadius)
        {
            roundedCornerCount++;
        }

        if (hasTopLeftRadius)
        {
            roundedCornerCount++;
        }

        ILineSegment[] segments = new ILineSegment[4 + roundedCornerCount];
        int index = 0;

        segments[index++] = new LinearLineSegment(topLeftPoint, topRightPoint);
        if (hasTopRightRadius)
        {
            segments[index++] = new ArcLineSegment(
                new PointF(right - topRight.Width, top + topRight.Height),
                topRight,
                0F,
                -90F,
                90F);
        }

        segments[index++] = new LinearLineSegment(rightTopPoint, rightBottomPoint);
        if (hasBottomRightRadius)
        {
            segments[index++] = new ArcLineSegment(
                new PointF(right - bottomRight.Width, bottom - bottomRight.Height),
                bottomRight,
                0F,
                0F,
                90F);
        }

        segments[index++] = new LinearLineSegment(bottomRightPoint, bottomLeftPoint);
        if (hasBottomLeftRadius)
        {
            segments[index++] = new ArcLineSegment(
                new PointF(left + bottomLeft.Width, bottom - bottomLeft.Height),
                bottomLeft,
                0F,
                90F,
                90F);
        }

        segments[index++] = new LinearLineSegment(leftBottomPoint, leftTopPoint);
        if (hasTopLeftRadius)
        {
            segments[index++] = new ArcLineSegment(
                new PointF(left + topLeft.Width, top + topLeft.Height),
                topLeft,
                0F,
                180F,
                90F);
        }

        return segments;
    }
}
