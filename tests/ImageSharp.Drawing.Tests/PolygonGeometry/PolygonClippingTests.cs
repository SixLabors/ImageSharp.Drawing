// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.PolygonGeometry;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;

namespace SixLabors.ImageSharp.Drawing.Tests.PolygonGeometry;

public class PolygonClippingTests
{
    private readonly RectanglePolygon bigSquare = new(10, 10, 40, 40);
    private readonly RectanglePolygon hole = new(20, 20, 10, 10);
    private readonly RectanglePolygon topLeft = new(0, 0, 20, 20);
    private readonly RectanglePolygon topRight = new(30, 0, 20, 20);
    private readonly RectanglePolygon topMiddle = new(20, 0, 10, 20);

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

    /// <summary>
    /// Checks that either operand can supply an open line.
    /// </summary>
    /// <param name="lineIsSubject">Whether the line is the subject.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Intersection_OpenLineAndRectangle_ReturnsOpenLine(bool lineIsSubject)
    {
        IPath line = new Path([new PointF(-5, 5), new PointF(15, 5)]);
        IPath rectangle = new RectanglePolygon(0, 0, 10, 10);

        IPath result = lineIsSubject ? line.Clip(rectangle) : rectangle.Clip(line);

        ISimplePath figure = Assert.Single(result.Flatten());
        Assert.False(figure.IsClosed);
        Assert.Equal(new[] { new PointF(0, 5), new PointF(10, 5) }, figure.Points.ToArray());
    }

    /// <summary>
    /// Checks that crossing lines produce a point that can be transformed and clipped again.
    /// </summary>
    [Fact]
    public void Intersection_CrossingLines_ReturnsReusablePoint()
    {
        IPath first = new Path([new PointF(0, 0), new PointF(10, 10)]);
        IPath second = new Path([new PointF(0, 10), new PointF(10, 0)]);

        IPath point = first.Clip(second);
        ISimplePath figure = Assert.Single(point.Flatten());
        Assert.False(figure.IsClosed);
        Assert.Equal(new PointF(5, 5), Assert.Single(figure.Points.ToArray()));

        IPath translated = point.Transform(Matrix4x4.CreateTranslation(1, 2, 0));
        IPath clipped = translated.Clip(new RectanglePolygon(0, 0, 10, 10));
        ISimplePath result = Assert.Single(clipped.Flatten());
        Assert.False(result.IsClosed);
        Assert.Equal(new PointF(6, 7), Assert.Single(result.Points.ToArray()));
    }

    /// <summary>
    /// Checks that internal rectangle boundaries do not split a clipped line.
    /// </summary>
    [Fact]
    public void Intersection_OpenLineAcrossRegionBands_ReturnsOneOpenLine()
    {
        Region region = new(new Rectangle(0, 0, 10, 10));
        region.Add(new Rectangle(0, 10, 5, 10));
        IPath line = new Path([new PointF(2, -1), new PointF(2, 21)]);

        ISimplePath result = Assert.Single(line.Clip(region.ToPath()).Flatten());
        Assert.False(result.IsClosed);
        Assert.Equal(new PointF(2, 0), result.Points.Span[0]);
        Assert.Equal(new PointF(2, 20), result.Points.Span[^1]);
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
            Assert.Contains(p, path, new ApproximateFloatComparer(RectanglePolygonValueComparer.DefaultTolerance));
        }
    }

    [Fact]
    public void NonOverlapping()
    {
        IEnumerable<RectanglePolygon> shapes = Clip(this.topLeft, this.topRight).Paths
            .OfType<Polygon>().Select(x => (RectanglePolygon)x);

        Assert.Single(shapes);
        Assert.Contains(
            shapes, x => RectanglePolygonValueComparer.Equals(this.topLeft, x));

        Assert.DoesNotContain(this.topRight, shapes);
    }

    [Fact]
    public void OverLappingReturns1NewShape()
    {
        ComplexPolygon shapes = Clip(this.bigSquare, this.topLeft);

        Assert.Single(shapes.Paths);
        Assert.DoesNotContain(shapes.Paths, x => RectanglePolygonValueComparer.Equals(this.bigSquare, x));
        Assert.DoesNotContain(shapes.Paths, x => RectanglePolygonValueComparer.Equals(this.topLeft, x));
    }

    [Fact]
    public void OverlappingButNotCrossingReturnsOrigionalShapes()
    {
        IEnumerable<RectanglePolygon> shapes = Clip(this.bigSquare, this.hole).Paths
            .OfType<Polygon>().Select(x => (RectanglePolygon)x);

        Assert.Equal(2, shapes.Count());

        Assert.Contains(shapes, x => RectanglePolygonValueComparer.Equals(this.bigSquare, x));
        Assert.Contains(shapes, x => RectanglePolygonValueComparer.Equals(this.hole, x));
    }

    [Fact]
    public void TouchingButNotOverlapping()
    {
        ComplexPolygon shapes = Clip(this.topMiddle, this.topLeft);
        Assert.Single(shapes.Paths);
        Assert.DoesNotContain(shapes.Paths, x => RectanglePolygonValueComparer.Equals(this.topMiddle, x));
        Assert.DoesNotContain(shapes.Paths, x => RectanglePolygonValueComparer.Equals(this.topLeft, x));
    }

    [Fact]
    public void ClippingRectanglesCreateCorrectNumberOfPoints()
    {
        IEnumerable<ISimplePath> paths = Clip(new RectanglePolygon(10, 10, 40, 40), new RectanglePolygon(20, 0, 20, 20))
            .Flatten();

        Assert.Single(paths);
        PointF[] points = paths.First().Points.ToArray();

        Assert.Equal(8, points.Length);
    }
}
