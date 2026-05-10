// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class BezierLineSegmentTests
{
    [Fact]
    public void SingleSegmentConstructor()
    {
        CubicBezierLineSegment segment = new(new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 0), new Vector2(20, 0));
        PointF[] points = new PointF[segment.LinearVertexCount(Vector2.One)];
        segment.CopyTo(points, skipFirstPoint: false, Vector2.One);
        Assert.Contains(new Vector2(0, 0), points);
        Assert.Contains(new Vector2(10, 0), points);
        Assert.Contains(new Vector2(20, 0), points);
    }

    [Fact]
    public void MustHaveAtLeast4Points()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezierLineSegment(
            [new PointF(0, 0)]));
    }
}
