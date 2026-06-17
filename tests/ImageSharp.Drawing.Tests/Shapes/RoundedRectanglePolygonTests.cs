// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class RoundedRectanglePolygonTests
{
    [Fact]
    public void SeparateCornerRadiiIsCorrect()
    {
        RoundedRectanglePolygon polygon = new(
            new RectangleF(10, 20, 100, 80),
            new SizeF(10, 2),
            new SizeF(20, 4),
            new SizeF(30, 6),
            new SizeF(40, 8));

        Assert.Equal(8, polygon.LineSegments.Count);

        ApproximateFloatComparer comparer = new(1e-4F);
        AssertSegment(polygon.LineSegments[0], new PointF(20, 20), new PointF(90, 20), comparer);
        AssertSegment(polygon.LineSegments[1], new PointF(90, 20), new PointF(110, 24), comparer);
        AssertSegment(polygon.LineSegments[2], new PointF(110, 24), new PointF(110, 94), comparer);
        AssertSegment(polygon.LineSegments[3], new PointF(110, 94), new PointF(80, 100), comparer);
        AssertSegment(polygon.LineSegments[4], new PointF(80, 100), new PointF(50, 100), comparer);
        AssertSegment(polygon.LineSegments[5], new PointF(50, 100), new PointF(10, 92), comparer);
        AssertSegment(polygon.LineSegments[6], new PointF(10, 92), new PointF(10, 22), comparer);
        AssertSegment(polygon.LineSegments[7], new PointF(10, 22), new PointF(20, 20), comparer);
    }

    private static void AssertSegment(ILineSegment segment, PointF start, PointF end, ApproximateFloatComparer comparer)
    {
        Assert.Equal(start, segment.StartPoint, comparer);
        Assert.Equal(end, segment.EndPoint, comparer);
    }
}
