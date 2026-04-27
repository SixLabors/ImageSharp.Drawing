// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

/// <summary>
/// The internal path tests.
/// </summary>
public class PathTests
{
    [Fact]
    public void Bounds()
    {
        LinearLineSegment seg1 = new(new PointF(0, 0), new PointF(2, 2));
        LinearLineSegment seg2 = new(new PointF(4, 4), new PointF(5, 5));

        Path path = new(seg1, seg2);

        Assert.Equal(0, path.Bounds.Left);
        Assert.Equal(5, path.Bounds.Right);
        Assert.Equal(0, path.Bounds.Top);
        Assert.Equal(5, path.Bounds.Bottom);
    }

    [Fact]
    public void SimplePath()
    {
        Path path = new(new LinearLineSegment(new PointF(0, 0), new PointF(10, 0), new PointF(10, 10), new PointF(0, 10)));
        PointF[] points = path.Flatten().Single().Points.ToArray();

        Assert.Equal(4, points.Length);
        Assert.Equal(new PointF(0, 0), points[0]);
        Assert.Equal(new PointF(10, 0), points[1]);
        Assert.Equal(new PointF(10, 10), points[2]);
        Assert.Equal(new PointF(0, 10), points[3]);
    }

    [Fact]
    public void EmptyPath_SingletonsExposeExpectedPathTypes()
    {
        Assert.Same(EmptyPath.OpenPath, Path.Empty);
        Assert.Equal(PathTypes.Open, EmptyPath.OpenPath.PathType);
        Assert.Equal(PathTypes.Closed, EmptyPath.ClosedPath.PathType);
    }

    [Fact]
    public void EmptyPath_OperationsPreserveSingletons()
    {
        Matrix4x4 transform = Matrix4x4.CreateTranslation(12, 34, 0);

        Assert.Same(EmptyPath.ClosedPath, EmptyPath.OpenPath.AsClosedPath());
        Assert.Same(EmptyPath.ClosedPath, EmptyPath.ClosedPath.AsClosedPath());
        Assert.Same(EmptyPath.OpenPath, EmptyPath.OpenPath.Transform(transform));
        Assert.Same(EmptyPath.ClosedPath, EmptyPath.ClosedPath.Transform(transform));
    }

    [Fact]
    public void EmptyPath_ExposesNoPathData()
    {
        Assert.Equal(RectangleF.Empty, EmptyPath.OpenPath.Bounds);
        Assert.Equal(RectangleF.Empty, EmptyPath.ClosedPath.Bounds);
        Assert.Empty(EmptyPath.OpenPath.Flatten());
        Assert.Empty(EmptyPath.ClosedPath.Flatten());
    }

    [Fact]
    public void EmptyPath_ToLinearGeometry_ReturnsEmptyGeometry()
    {
        LinearGeometry identity = EmptyPath.OpenPath.ToLinearGeometry(Vector2.One);
        LinearGeometry scaled = EmptyPath.ClosedPath.ToLinearGeometry(new Vector2(2F, 3F));

        Assert.Same(identity, scaled);
        Assert.Equal(RectangleF.Empty, identity.Info.Bounds);
        Assert.Equal(0, identity.Info.ContourCount);
        Assert.Equal(0, identity.Info.PointCount);
        Assert.Equal(0, identity.Info.SegmentCount);
        Assert.Equal(0, identity.Info.NonHorizontalSegmentCountPixelBoundary);
        Assert.Equal(0, identity.Info.NonHorizontalSegmentCountPixelCenter);
        Assert.Empty(identity.Contours);
        Assert.Empty(identity.Points);

        SegmentEnumerator segments = identity.GetSegments();

        Assert.False(segments.MoveNext());
    }
}
