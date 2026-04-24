// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class ArcLineSegmentTest
{
    [Fact]
    public void ContainsStartAndEnd()
    {
        ArcLineSegment segment = new(new PointF(10, 10), new SizeF(10, 20), 0, 0, 90);
        PointF[] points = Flatten(segment);
        Assert.Equal(20, points[0].X, 5F);
        Assert.Equal(10, points[0].Y, 5F);
        Assert.Equal(10, segment.EndPoint.X, 5F);
        Assert.Equal(30, segment.EndPoint.Y, 5F);
    }

    [Fact]
    public void CheckZeroRadii()
    {
        PointF[] xRadiusZero = Flatten(new ArcLineSegment(new PointF(20, 10), new SizeF(0, 20), 0, 0, 360));
        PointF[] yRadiusZero = Flatten(new ArcLineSegment(new PointF(20, 10), new SizeF(30, 0), 0, 0, 360));
        PointF[] bothRadiiZero = Flatten(new ArcLineSegment(new PointF(20, 10), new SizeF(0, 0), 0, 0, 360));
        foreach (PointF point in xRadiusZero)
        {
            Assert.Equal(20, point.X);
        }

        foreach (PointF point in yRadiusZero)
        {
            Assert.Equal(10, point.Y);
        }

        foreach (PointF point in bothRadiiZero)
        {
            Assert.Equal(20, point.X);
            Assert.Equal(10, point.Y);
        }
    }

    private static PointF[] Flatten(ArcLineSegment segment)
    {
        PointF[] points = new PointF[segment.LinearVertexCount(Vector2.One)];
        segment.CopyTo(points, skipFirstPoint: false, Vector2.One);
        return points;
    }
}
