// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Tests;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class PiePolygonTests
{
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(0.00001, false)]
    [InlineData(1, false)]
    public void RadiusXMustBeGreaterThan0(float radiusX, bool throws)
    {
        if (throws)
        {
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new PiePolygon(0, 0, radiusX, 99, 0, 90));
            Assert.Equal("radiusX", ex.ParamName);
        }
        else
        {
            PiePolygon p = new(0, 0, radiusX, 99, 0, 90);
            Assert.NotNull(p);
        }
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(0.00001, false)]
    [InlineData(1, false)]
    public void RadiusYMustBeGreaterThan0(float radiusY, bool throws)
    {
        if (throws)
        {
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new PiePolygon(0, 0, 99, radiusY, 0, 90));
            Assert.Equal("radiusY", ex.ParamName);
        }
        else
        {
            PiePolygon p = new(0, 0, 99, radiusY, 0, 90);
            Assert.NotNull(p);
        }
    }

    [Fact]
    public void NoRotationOverloadMatchesZeroRotation()
    {
        PiePolygon withRotation = new(new PointF(10, 20), new SizeF(30, 40), 0, 15, 120);
        PiePolygon withoutRotation = new(new PointF(10, 20), new SizeF(30, 40), 15, 120);

        PointF[] withRotationPoints = withRotation.Flatten().Single().Points.ToArray();
        PointF[] withoutRotationPoints = withoutRotation.Flatten().Single().Points.ToArray();

        Assert.Equal(withRotationPoints.Length, withoutRotationPoints.Length);

        ApproximateFloatComparer comparer = new(1e-4F);
        for (int i = 0; i < withRotationPoints.Length; i++)
        {
            Assert.Equal(withRotationPoints[i], withoutRotationPoints[i], comparer);
        }
    }

    [Fact]
    public void BoundsIncludeCenterAndArcExtents()
    {
        PiePolygon pie = new(new PointF(10, 20), new SizeF(30, 40), 0, 0, 90);

        Assert.Equal(10, pie.Bounds.Left, 4F);
        Assert.Equal(40, pie.Bounds.Right, 4F);
        Assert.Equal(20, pie.Bounds.Top, 4F);
        Assert.Equal(60, pie.Bounds.Bottom, 4F);
    }

    [Fact]
    public void ShapePaths()
    {
        PiePolygon shape = new(new PointF(10, 20), new SizeF(30, 40), 0, 90);

        Assert.Equal(shape, shape.AsClosedPath());
    }

    [Fact]
    public void TransformIdentityReturnsShapeObject()
    {
        PiePolygon shape = new(new PointF(10, 20), new SizeF(30, 40), 20, 140);
        IPath transformedShape = shape.Transform(Matrix4x4.Identity);

        Assert.Same(shape, transformedShape);
    }

    [Fact]
    public void Transform()
    {
        PiePolygon shape = new(new PointF(10, 20), new SizeF(30, 40), 0, 0, 90);

        IPath newShape = shape.Transform(new Matrix4x4(new Matrix3x2(0, 1, 1, 0, 20, 2)));

        ApproximateFloatComparer comparer = new(1e-4F);
        Assert.Equal(new PointF(40, 12), newShape.Bounds.Location, comparer);
        Assert.Equal(new SizeF(40, 30), newShape.Bounds.Size, comparer);
    }
}
