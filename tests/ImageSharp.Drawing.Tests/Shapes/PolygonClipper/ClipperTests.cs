// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using SixLabors.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.Tests.PolygonClipper;

public class ClipperTests
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

    private IEnumerable<IPath> Clip(IPath shape, params IPath[] hole)
    {
        ClippedShapeGenerator clipper = new(IntersectionRule.EvenOdd);

        clipper.AddPath(shape, ClippingType.Subject);
        if (hole != null)
        {
            clipper.AddPaths(hole, ClippingType.Clip);
        }

        return clipper.GenerateClippedShapes(BooleanOperation.Difference);
    }

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

        IEnumerable<IPath> shapes = this.Clip(triangle, cutout);
        Assert.Single(shapes);
        Assert.DoesNotContain(triangle, shapes);
    }

    [Fact]
    public void OverlappingTriangles()
    {
        IEnumerable<IPath> shapes = this.Clip(this.bigTriangle, this.littleTriangle);
        Assert.Single(shapes);
        IReadOnlyList<PointF> path = shapes.Single().Flatten().First().Points.ToArray();

        Assert.Equal(7, path.Count);
        foreach (Vector2 p in this.bigTriangle.Flatten().First().Points.ToArray())
        {
            Assert.Contains(p, path, new ApproximateFloatComparer(RectangularPolygonValueComparer.DefaultTolerance));
        }
    }

    [Fact]
    public void NonOverlapping()
    {
        IEnumerable<RectangularPolygon> shapes = this.Clip(this.topLeft, this.topRight)
            .OfType<Polygon>().Select(x => (RectangularPolygon)x);

        Assert.Single(shapes);
        Assert.Contains(
            shapes, x => RectangularPolygonValueComparer.Equals(this.topLeft, x));

        Assert.DoesNotContain(this.topRight, shapes);
    }

    [Fact]
    public void OverLappingReturns1NewShape()
    {
        IEnumerable<IPath> shapes = this.Clip(this.bigSquare, this.topLeft);

        Assert.Single(shapes);
        Assert.DoesNotContain(shapes, x => RectangularPolygonValueComparer.Equals(this.bigSquare, x));
        Assert.DoesNotContain(shapes, x => RectangularPolygonValueComparer.Equals(this.topLeft, x));
    }

    [Fact]
    public void OverlappingButNotCrossingReturnsOrigionalShapes()
    {
        IEnumerable<RectangularPolygon> shapes = this.Clip(this.bigSquare, this.hole)
            .OfType<Polygon>().Select(x => (RectangularPolygon)x);

        Assert.Equal(2, shapes.Count());

        Assert.Contains(shapes, x => RectangularPolygonValueComparer.Equals(this.bigSquare, x));
        Assert.Contains(shapes, x => RectangularPolygonValueComparer.Equals(this.hole, x));
    }

    [Fact]
    public void TouchingButNotOverlapping()
    {
        IEnumerable<IPath> shapes = this.Clip(this.topMiddle, this.topLeft);
        Assert.Single(shapes);
        Assert.DoesNotContain(shapes, x => RectangularPolygonValueComparer.Equals(this.topMiddle, x));
        Assert.DoesNotContain(shapes, x => RectangularPolygonValueComparer.Equals(this.topLeft, x));
    }

    [Fact]
    public void ClippingRectanglesCreateCorrectNumberOfPoints()
    {
        IEnumerable<ISimplePath> paths = new RectangularPolygon(10, 10, 40, 40)
            .Clip(new RectangularPolygon(20, 0, 20, 20))
            .Flatten();

        Assert.Single(paths);
        IReadOnlyList<PointF> points = paths.First().Points.ToArray();

        Assert.Equal(8, points.Count);
    }
}
