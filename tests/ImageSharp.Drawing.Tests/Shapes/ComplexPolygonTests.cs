// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class ComplexPolygonTests
{
    private static Polygon CreateSquare(float x, float y, float size)
        => new(new PointF[] { new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size) });

    [Fact]
    public void ToLinearGeometry_WithTransform_AppliesTransformToPoints()
    {
        Polygon outer = CreateSquare(0, 0, 10);
        Polygon hole = CreateSquare(2, 2, 6);
        ComplexPolygon complex = new(outer, hole);

        Matrix4x4 transform = Matrix4x4.CreateScale(2F);

        LinearGeometry identity = complex.ToLinearGeometry(Matrix4x4.Identity);
        LinearGeometry transformed = complex.ToLinearGeometry(transform);

        // Same structure.
        Assert.Equal(identity.Info.ContourCount, transformed.Info.ContourCount);
        Assert.Equal(identity.Info.PointCount, transformed.Info.PointCount);

        // Points are scaled.
        for (int i = 0; i < identity.Points.Count; i++)
        {
            PointF expected = PointF.Transform(identity.Points[i], transform);
            Assert.Equal(expected.X, transformed.Points[i].X, 0.001F);
            Assert.Equal(expected.Y, transformed.Points[i].Y, 0.001F);
        }

        // Bounds are scaled.
        Assert.Equal(identity.Info.Bounds.Left * 2, transformed.Info.Bounds.Left, 0.001F);
        Assert.Equal(identity.Info.Bounds.Top * 2, transformed.Info.Bounds.Top, 0.001F);
        Assert.Equal(identity.Info.Bounds.Right * 2, transformed.Info.Bounds.Right, 0.001F);
        Assert.Equal(identity.Info.Bounds.Bottom * 2, transformed.Info.Bounds.Bottom, 0.001F);
    }

    [Fact]
    public void ToLinearGeometry_WithIdentityTransform_ReturnsCachedGeometry()
    {
        ComplexPolygon complex = new(CreateSquare(0, 0, 10));

        LinearGeometry first = complex.ToLinearGeometry(Matrix4x4.Identity);
        LinearGeometry second = complex.ToLinearGeometry(Matrix4x4.Identity);

        Assert.Same(first, second);
    }

    [Fact]
    public void ToLinearGeometry_WithTransform_PreservesContourMetadata()
    {
        Polygon outer = CreateSquare(0, 0, 10);
        Polygon hole = CreateSquare(2, 2, 6);
        ComplexPolygon complex = new(outer, hole);

        Matrix4x4 transform = Matrix4x4.CreateTranslation(50, 100, 0);

        LinearGeometry transformed = complex.ToLinearGeometry(transform);

        Assert.Equal(2, transformed.Info.ContourCount);
        Assert.Equal(2, transformed.Contours.Count);

        for (int i = 0; i < transformed.Contours.Count; i++)
        {
            Assert.True(transformed.Contours[i].IsClosed);
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
