// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class LinearLineSegmentTests
{
    [Fact]
    public void SingleSegmentConstructor()
    {
        LinearLineSegment segment = new(new Vector2(0, 0), new Vector2(10, 10));
        PointF[] flatPath = new PointF[segment.LinearVertexCount(Vector2.One)];
        segment.CopyTo(flatPath, skipFirstPoint: false, Vector2.One);
        Assert.Equal(2, flatPath.Length);
        Assert.Equal(new PointF(0, 0), flatPath[0]);
        Assert.Equal(new PointF(10, 10), flatPath[1]);
    }

    [Fact]
    public void MustHaveAtLeast2Points()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => new LinearLineSegment([new PointF(0, 0)
        ]));
    }

    [Fact]
    public void NullPointsArrayThrowsCountException() => Assert.ThrowsAny<ArgumentNullException>(() => new LinearLineSegment(null));
}
