// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class ComplexPolygonTests
{
    private static Polygon CreateSquare(float x, float y, float size)
        => new(new PointF[] { new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size) });

    [Fact]
    public void ToLinearGeometry_WithScale_BakesPoints()
    {
        Polygon outer = CreateSquare(0, 0, 10);
        Polygon hole = CreateSquare(2, 2, 6);
        ComplexPolygon complex = new(outer, hole);

        Vector2 scale = new(2F);

        LinearGeometry identity = complex.ToLinearGeometry(Vector2.One);
        LinearGeometry scaled = complex.ToLinearGeometry(scale);

        Assert.Equal(identity.Info.ContourCount, scaled.Info.ContourCount);
        Assert.Equal(identity.Info.PointCount, scaled.Info.PointCount);

        for (int i = 0; i < identity.Points.Count; i++)
        {
            Assert.Equal(identity.Points[i].X * scale.X, scaled.Points[i].X, 0.001F);
            Assert.Equal(identity.Points[i].Y * scale.Y, scaled.Points[i].Y, 0.001F);
        }

        Assert.Equal(identity.Info.Bounds.Left * 2, scaled.Info.Bounds.Left, 0.001F);
        Assert.Equal(identity.Info.Bounds.Top * 2, scaled.Info.Bounds.Top, 0.001F);
        Assert.Equal(identity.Info.Bounds.Right * 2, scaled.Info.Bounds.Right, 0.001F);
        Assert.Equal(identity.Info.Bounds.Bottom * 2, scaled.Info.Bounds.Bottom, 0.001F);
    }

    [Fact]
    public void ToLinearGeometry_WithIdentityScale_ReturnsCachedGeometry()
    {
        ComplexPolygon complex = new(CreateSquare(0, 0, 10));

        LinearGeometry first = complex.ToLinearGeometry(Vector2.One);
        LinearGeometry second = complex.ToLinearGeometry(Vector2.One);

        Assert.Same(first, second);
    }

    [Fact]
    public void ToLinearGeometry_WithScale_PreservesContourMetadata()
    {
        Polygon outer = CreateSquare(0, 0, 10);
        Polygon hole = CreateSquare(2, 2, 6);
        ComplexPolygon complex = new(outer, hole);

        LinearGeometry geometry = complex.ToLinearGeometry(new Vector2(3F, 2F));

        Assert.Equal(2, geometry.Info.ContourCount);
        Assert.Equal(2, geometry.Contours.Count);

        for (int i = 0; i < geometry.Contours.Count; i++)
        {
            Assert.True(geometry.Contours[i].IsClosed);
        }
    }

    [Fact]
    public void PointAlongPath_AtStart_ReturnsFirstPoint()
    {
        ComplexPolygon complex = new(CreateSquare(0, 0, 10));

        IPathInternals internals = complex;
        SegmentInfo info = internals.PointAlongPath(0);

        Assert.Equal(0, info.Point.X, 1F);
        Assert.Equal(0, info.Point.Y, 1F);
    }

    [Fact]
    public void PointAlongPath_MidSegment_ReturnsInterpolatedPoint()
    {
        // First segment goes from (0,0) to (10,0), length 10.
        ComplexPolygon complex = new(CreateSquare(0, 0, 10));

        IPathInternals internals = complex;
        SegmentInfo info = internals.PointAlongPath(5);

        Assert.Equal(5, info.Point.X, 1F);
        Assert.Equal(0, info.Point.Y, 1F);
    }

    [Fact]
    public void PointAlongPath_WrapsAroundTotalLength()
    {
        // Perimeter is 40, so distance 45 should wrap to 5.
        ComplexPolygon complex = new(CreateSquare(0, 0, 10));

        IPathInternals internals = complex;
        SegmentInfo atFive = internals.PointAlongPath(5);
        SegmentInfo wrapped = internals.PointAlongPath(45);

        Assert.Equal(atFive.Point.X, wrapped.Point.X, 1F);
        Assert.Equal(atFive.Point.Y, wrapped.Point.Y, 1F);
    }

    [Fact]
    public void PointAlongPath_MultipleSubPaths_TraversesSecondPath()
    {
        // Two separate squares; first has perimeter 40, second has perimeter 20.
        Polygon first = CreateSquare(0, 0, 10);
        Polygon second = CreateSquare(50, 50, 5);
        ComplexPolygon complex = new(first, second);

        IPathInternals internals = complex;

        // Distance 42 = 40 (first path perimeter) + 2 into second path.
        // Second path first segment goes from (50,50) to (55,50), so 2 units in -> (52,50).
        SegmentInfo info = internals.PointAlongPath(42);

        Assert.Equal(52, info.Point.X, 1F);
        Assert.Equal(50, info.Point.Y, 1F);
    }
}
