// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.PolygonGeometry;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;

namespace SixLabors.ImageSharp.Drawing.Tests.PolygonGeometry;

public class PolygonClippingTests
{
    private readonly RectangularPolygon bigSquare = new(10, 10, 40, 40);
    private readonly RectangularPolygon hole = new(20, 20, 10, 10);
    private readonly RectangularPolygon topLeft = new(0, 0, 20, 20);
    private readonly RectangularPolygon topRight = new(30, 0, 20, 20);
    private readonly RectangularPolygon topMiddle = new(20, 0, 10, 20);

    private readonly Polygon bigTriangle = new(new LinearLineSegment(
                     new Vector2(10, 10),
                     new Vector2(200, 150),
                     new Vector2(50, 300)));

    private readonly Polygon littleTriangle = new(new LinearLineSegment(
                    new Vector2(37, 85),
                    new Vector2(130, 40),
                    new Vector2(65, 137)));

    private static ComplexPolygon Clip(IPath shape, params IPath[] hole)
        => ClippedShapeGenerator.GenerateClippedShapes(BooleanOperation.Difference, shape, hole);

    [Fact]
    public void OverlappingTriangleCutRightSide()
    {
        Polygon triangle = new(new LinearLineSegment(
            new Vector2(0, 50),
            new Vector2(70, 0),
            new Vector2(50, 100)));

        Polygon cutout = new(new LinearLineSegment(
            new Vector2(20, 0),
            new Vector2(70, 0),
            new Vector2(70, 100),
            new Vector2(20, 100)));

        ComplexPolygon shapes = Clip(triangle, cutout);
        Assert.Single(shapes.Paths);
        Assert.DoesNotContain(triangle, shapes.Paths);
    }

    [Fact]
    public void OverlappingTriangles()
    {
        ComplexPolygon shapes = Clip(this.bigTriangle, this.littleTriangle);
        Assert.Single(shapes.Paths);
        PointF[] path = shapes.Paths.Single().Flatten().First().Points.ToArray();

        Assert.Equal(7, path.Length);
        foreach (Vector2 p in this.bigTriangle.Flatten().First().Points.ToArray())
        {
            Assert.Contains(p, path, new ApproximateFloatComparer(RectangularPolygonValueComparer.DefaultTolerance));
        }
    }

    [Fact]
    public void NonOverlapping()
    {
        IEnumerable<RectangularPolygon> shapes = Clip(this.topLeft, this.topRight).Paths
            .OfType<Polygon>().Select(x => (RectangularPolygon)x);

        Assert.Single(shapes);
        Assert.Contains(
            shapes, x => RectangularPolygonValueComparer.Equals(this.topLeft, x));

        Assert.DoesNotContain(this.topRight, shapes);
    }

    [Fact]
    public void OverLappingReturns1NewShape()
    {
        ComplexPolygon shapes = Clip(this.bigSquare, this.topLeft);

        Assert.Single(shapes.Paths);
        Assert.DoesNotContain(shapes.Paths, x => RectangularPolygonValueComparer.Equals(this.bigSquare, x));
        Assert.DoesNotContain(shapes.Paths, x => RectangularPolygonValueComparer.Equals(this.topLeft, x));
    }

    [Fact]
    public void OverlappingButNotCrossingReturnsOrigionalShapes()
    {
        IEnumerable<RectangularPolygon> shapes = Clip(this.bigSquare, this.hole).Paths
            .OfType<Polygon>().Select(x => (RectangularPolygon)x);

        Assert.Equal(2, shapes.Count());

        Assert.Contains(shapes, x => RectangularPolygonValueComparer.Equals(this.bigSquare, x));
        Assert.Contains(shapes, x => RectangularPolygonValueComparer.Equals(this.hole, x));
    }

    [Fact]
    public void TouchingButNotOverlapping()
    {
        ComplexPolygon shapes = Clip(this.topMiddle, this.topLeft);
        Assert.Single(shapes.Paths);
        Assert.DoesNotContain(shapes.Paths, x => RectangularPolygonValueComparer.Equals(this.topMiddle, x));
        Assert.DoesNotContain(shapes.Paths, x => RectangularPolygonValueComparer.Equals(this.topLeft, x));
    }

    [Fact]
    public void ClippingRectanglesCreateCorrectNumberOfPoints()
    {
        IEnumerable<ISimplePath> paths = new RectangularPolygon(10, 10, 40, 40)
            .Clip(new RectangularPolygon(20, 0, 20, 20))
            .Flatten();

        Assert.Single(paths);
        PointF[] points = paths.First().Points.ToArray();

        Assert.Equal(8, points.Length);
    }
}
