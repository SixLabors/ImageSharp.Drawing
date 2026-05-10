// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Tests;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class RoundedRectangleTests
{
    [Fact]
    public void BoundsMatchRectangle()
    {
        RoundedRectanglePolygon shape = new(10, 20, 100, 40, 12);

        Assert.Equal(new RectangleF(10, 20, 100, 40), shape.Bounds);
    }

    [Fact]
    public void ZeroRadiusMatchesRectangularPolygon()
    {
        RoundedRectanglePolygon shape = new(10, 20, 100, 40, 0);
        RectangularPolygon rectangle = new(10, 20, 100, 40);
        PointF[] actualPoints = shape.Flatten().Single().Points.ToArray();
        PointF[] expectedPoints = rectangle.Flatten().Single().Points.ToArray();

        Assert.Equal(expectedPoints, actualPoints);
    }

    [Fact]
    public void RoundedRectangleUsesCurvedCorners()
    {
        RoundedRectanglePolygon shape = new(10, 20, 100, 40, 12);
        PointF[] points = shape.Flatten().Single().Points.ToArray();

        Assert.True(points.Length > 4);
    }

    [Fact]
    public void OversizedRadiusScalesToFitBounds()
    {
        RoundedRectanglePolygon shape = new(10, 20, 100, 40, 100);
        PointF[] points = shape.Flatten().Single().Points.ToArray();

        Assert.Equal(new RectangleF(10, 20, 100, 40), shape.Bounds);
        Assert.True(points.Length > 4);
    }

    [Fact]
    public void ShapePaths()
    {
        RoundedRectanglePolygon shape = new(10, 20, 100, 40, 12);

        Assert.Equal(shape, shape.AsClosedPath());
    }

    [Fact]
    public void TransformIdentityReturnsShapeObject()
    {
        RoundedRectanglePolygon shape = new(10, 20, 100, 40, 12);
        IPath transformedShape = shape.Transform(Matrix4x4.Identity);

        Assert.Same(shape, transformedShape);
    }

    [Fact]
    public void Transform()
    {
        RoundedRectanglePolygon shape = new(0, 0, 100, 40, 12);

        IPath newShape = shape.Transform(new Matrix4x4(new Matrix3x2(0, 1, 1, 0, 20, 2)));

        ApproximateFloatComparer comparer = new(1e-4F);
        Assert.Equal(new PointF(20, 2), newShape.Bounds.Location, comparer);
        Assert.Equal(new SizeF(40, 100), newShape.Bounds.Size, comparer);
    }
}
