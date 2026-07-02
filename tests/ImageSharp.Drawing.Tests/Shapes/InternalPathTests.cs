// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

/// <summary>
/// The internal path tests.
/// </summary>
public class InternalPathTests
{
    [Fact]
    public void MultipleLineSegmentsSimplePathsAreMerged()
    {
        LinearLineSegment seg1 = new(new PointF(0, 0), new PointF(2, 2));
        LinearLineSegment seg2 = new(new PointF(4, 4), new PointF(5, 5));

        InternalPath path = new([seg1, seg2], true);

        Assert.Contains(new PointF(0, 0), path.Points().ToArray());
        Assert.DoesNotContain(new PointF(2, 2), path.Points().ToArray());
        Assert.DoesNotContain(new PointF(4, 4), path.Points().ToArray());
        Assert.Contains(new PointF(5, 5), path.Points().ToArray());
    }

    [Fact]
    public void Length_Closed()
    {
        LinearLineSegment seg1 = new(new PointF(0, 0), new PointF(0, 2));

        InternalPath path = new([seg1], true);

        Assert.Equal(4, path.Length);
    }

    [Fact]
    public void Length_Open()
    {
        LinearLineSegment seg1 = new(new PointF(0, 0), new PointF(0, 2));

        InternalPath path = new([seg1], false);

        Assert.Equal(2, path.Length);
    }

    [Fact]
    public void Bounds()
    {
        LinearLineSegment seg1 = new(new PointF(0, 0), new PointF(2, 2));
        LinearLineSegment seg2 = new(new PointF(4, 4), new PointF(5, 5));

        InternalPath path = new([seg1, seg2], true);

        Assert.Equal(0, path.Bounds.Left);
        Assert.Equal(5, path.Bounds.Right);
        Assert.Equal(0, path.Bounds.Top);
        Assert.Equal(5, path.Bounds.Bottom);
    }

    public static TheoryData<TestPoint, TestSize, TestPoint, bool> PointInPolygonTheoryData { get; }
        = new()
        {
            {
                new PointF(10, 10), // loc
                new SizeF(100, 100), // size
                new PointF(10, 10), // test
                true
            }, // corner is inside
            {
                new PointF(10, 10), // loc
                new SizeF(100, 100), // size
                new PointF(9, 9), // test
                false
            },
        };
}
