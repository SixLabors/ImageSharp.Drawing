// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Tests;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class PathBuilderTests
{
    [Fact]
    public void DrawLinesClosedFigure()
    {
        PathBuilder builder = new();

        builder.AddLine(10, 10, 10, 90);
        builder.AddLine(10, 90, 50, 50);
        builder.CloseFigure();

        Assert.IsType<Polygon>(builder.Build());
    }

    [Fact]
    public void AddBezier()
    {
        PathBuilder builder = new();

        builder.AddCubicBezier(new Vector2(10, 10), new Vector2(20, 20), new Vector2(20, 30), new Vector2(10, 40));

        Assert.IsType<Path>(builder.Build());
    }

    [Fact]
    public void AddEllipticArc()
    {
        PathBuilder builder = new();

        builder.AddArc(new PointF(10, 10), 10, 10, 0, 0, 360);

        Assert.IsType<Path>(builder.Build());
    }

    [Fact]
    public void AddPieBuildsClosedFigure()
    {
        PathBuilder builder = new();

        builder.AddPie(new PointF(10, 20), new SizeF(30, 40), 0, 90);

        Assert.IsType<Polygon>(builder.Build());
    }

    [Fact]
    public void AddPieRotationOverloadMatchesPieShape()
    {
        PathBuilder builder = new();
        builder.AddPie(10, 20, 30, 40, 15, 120, 210);

        Polygon actual = Assert.IsType<Polygon>(builder.Build());
        Pie expected = new(10, 20, 30, 40, 15, 120, 210);

        AssertEquivalentPaths(actual, expected);
    }

    [Fact]
    public void AddPieNoRotationOverloadMatchesPieShape()
    {
        PathBuilder builder = new();
        builder.AddPie(new PointF(10, 20), new SizeF(30, 40), 15, 120);

        Polygon actual = Assert.IsType<Polygon>(builder.Build());
        Pie expected = new(new PointF(10, 20), new SizeF(30, 40), 15, 120);

        AssertEquivalentPaths(actual, expected);
    }

    [Fact]
    public void AddRectangleMatchesRectangularPolygon()
    {
        PathBuilder builder = new();
        builder.AddRectangle(new RectangleF(10, 20, 30, 40));

        Polygon actual = Assert.IsType<Polygon>(builder.Build());
        RectangularPolygon expected = new(10, 20, 30, 40);

        AssertEquivalentPaths(actual, expected);
    }

    [Fact]
    public void AddPolygonEnumerableMatchesPolygonShape()
    {
        PathBuilder builder = new();
        PointF[] points =
        [
            new PointF(10, 20),
            new PointF(30, 40),
            new PointF(15, 45)
        ];

        builder.AddPolygon(new List<PointF>(points));

        Polygon actual = Assert.IsType<Polygon>(builder.Build());
        Polygon expected = new(points);

        AssertEquivalentPaths(actual, expected);
    }

    [Fact]
    public void AddRegularPolygonMatchesRegularPolygonShape()
    {
        PathBuilder builder = new();
        builder.AddRegularPolygon(new PointF(10, 20), 5, 30, 45);

        Polygon actual = Assert.IsType<Polygon>(builder.Build());
        RegularPolygon expected = new(new PointF(10, 20), 5, 30, 45);

        AssertEquivalentPaths(actual, expected);
    }

    [Fact]
    public void AddStarMatchesStarShape()
    {
        PathBuilder builder = new();
        builder.AddStar(new PointF(10, 20), 5, 15, 30, 45);

        Polygon actual = Assert.IsType<Polygon>(builder.Build());
        Star expected = new(new PointF(10, 20), 5, 15, 30, 45);

        AssertEquivalentPaths(actual, expected);
    }

    [Fact]
    public void DrawLinesOpenFigure()
    {
        PathBuilder builder = new();

        builder.AddLine(10, 10, 10, 90);
        builder.AddLine(10, 90, 50, 50);
        Assert.IsType<Path>(builder.Build());
    }

    [Fact]
    public void DrawLines2OpenFigures()
    {
        PathBuilder builder = new();

        builder.AddLine(10, 10, 10, 90);
        builder.AddLine(10, 90, 50, 50);
        builder.StartFigure();
        builder.AddLine(10, 10, 10, 90);
        builder.AddLine(10, 90, 50, 50);

        ComplexPolygon shape = Assert.IsType<ComplexPolygon>(builder.Build());
        IPath[] p = shape.Paths.ToArray();
        Assert.Equal(2, p.Length);
        Assert.IsType<Path>(p[0]);
        Assert.IsType<Path>(p[1]);
    }

    [Fact]
    public void DrawLinesOpenThenClosedFigures()
    {
        PathBuilder builder = new();

        builder.AddLine(10, 10, 10, 90);
        builder.AddLine(10, 90, 50, 50);
        builder.StartFigure();
        builder.AddLine(10, 10, 10, 90);
        builder.AddLine(10, 90, 50, 50);
        builder.CloseFigure();
        ComplexPolygon shape = Assert.IsType<ComplexPolygon>(builder.Build());
        IPath[] p = shape.Paths.ToArray();

        Assert.Equal(2, p.Length);
        Assert.IsType<Path>(p[0]);
        Assert.IsType<Polygon>(p[1]);
    }

    [Fact]
    public void DrawLinesClosedThenOpenFigures()
    {
        PathBuilder builder = new();

        builder.AddLine(10, 10, 10, 90);
        builder.AddLine(10, 90, 50, 50);
        builder.CloseFigure();
        builder.AddLine(10, 10, 10, 90);
        builder.AddLine(10, 90, 50, 50);
        ComplexPolygon shape = Assert.IsType<ComplexPolygon>(builder.Build());

        IPath[] p = shape.Paths.ToArray();
        Assert.Equal(2, p.Length);
        Assert.IsType<Polygon>(p[0]);
        Assert.IsType<Path>(p[1]);
    }

    [Fact]
    public void DrawLinesCloseAllFigures()
    {
        PathBuilder builder = new();

        builder.AddLine(10, 10, 10, 90);
        builder.AddLine(10, 90, 50, 50);
        builder.StartFigure();
        builder.AddLine(10, 10, 10, 90);
        builder.AddLine(10, 90, 50, 50);
        ComplexPolygon shape = Assert.IsType<ComplexPolygon>(builder.Build());

        IPath[] p = shape.Paths.ToArray();
        Assert.Equal(2, p.Length);
        Assert.IsType<Path>(p[0]);
        Assert.IsType<Path>(p[1]);

        builder.CloseAllFigures();
        shape = Assert.IsType<ComplexPolygon>(builder.Build());

        p = shape.Paths.ToArray();
        Assert.Equal(2, p.Length);
        Assert.IsType<Polygon>(p[0]);
        Assert.IsType<Polygon>(p[1]);
    }

    [Fact]
    public void EnumerableAddLines()
    {
        Vector2 point1 = new(10, 10);
        Vector2 point2 = new(10, 90);
        Vector2 point3 = new(50, 50);
        PathBuilder builder = new();

        builder.AddLines(new List<PointF> { point1, point2, point3 });
        Path shape = Assert.IsType<Path>(builder.Build());
        Assert.Equal(10, shape.Bounds.Left);
    }

    [Fact]
    public void MultipleStartFiguresDoesntCreateEmptyPaths()
    {
        Vector2 point1 = new(10, 10);
        Vector2 point2 = new(10, 90);
        Vector2 point3 = new(50, 50);
        PathBuilder builder = new();
        builder.StartFigure();
        builder.StartFigure();
        builder.StartFigure();
        builder.StartFigure();
        builder.AddLines(new List<PointF> { point1, point2, point3 });
        Assert.IsType<Path>(builder.Build());
    }

    [Fact]
    public void DefaultTransform()
    {
        Vector2 point1 = new(10, 10);
        Vector2 point2 = new(10, 90);
        Vector2 point3 = new(50, 50);
        Matrix4x4 matrix = Matrix4x4.CreateTranslation(5, 5, 0);
        PathBuilder builder = new(matrix);

        builder.AddLines(point1, point2, point3);
        IPath shape = builder.Build();
        Assert.Equal(15, shape.Bounds.Left);
    }

    [Fact]
    public void SetTransform()
    {
        Vector2 point1 = new(10, 10);
        Vector2 point2 = new(10, 90);
        Vector2 point3 = new(50, 50);
        Matrix4x4 matrix = Matrix4x4.CreateTranslation(100, 100, 0);
        PathBuilder builder = new();

        builder.AddLines(point1, point2, point3);
        builder.SetTransform(matrix);
        builder.StartFigure();
        builder.AddLines(point1, point2, point3);
        builder.StartFigure();
        builder.ResetOrigin();
        builder.AddLines(point1, point2, point3);

        IPath[] shape = Assert.IsType<ComplexPolygon>(builder.Build()).Paths.ToArray();
        Assert.Equal(10, shape[0].Bounds.Left);
        Assert.Equal(110, shape[1].Bounds.Left);
        Assert.Equal(10, shape[0].Bounds.Left);
    }

    [Fact]
    public void SetOriginLeaveMatrix()
    {
        Vector2 point1 = new(10, 10);
        Vector2 point2 = new(10, 90);
        Vector2 point3 = new(50, 50);
        Vector2 origin = new(-50, -100);
        PathBuilder builder = new(Matrix4x4.CreateScale(10));

        builder.AddLines(point1, point2, point3);

        builder.SetOrigin(origin); // new origin is scaled by default transform
        builder.StartFigure();
        builder.AddLines(point1, point2, point3);
        IPath[] shape = Assert.IsType<ComplexPolygon>(builder.Build()).Paths.ToArray();
        Assert.Equal(100, shape[0].Bounds.Left);
        Assert.Equal(-400, shape[1].Bounds.Left);
    }

    private static void AssertEquivalentPaths(IPath actual, IPath expected)
    {
        PointF[] actualPoints = actual.Flatten().Single().Points.ToArray();
        PointF[] expectedPoints = expected.Flatten().Single().Points.ToArray();

        Assert.Equal(expectedPoints.Length, actualPoints.Length);

        ApproximateFloatComparer comparer = new(1e-4F);
        for (int i = 0; i < expectedPoints.Length; i++)
        {
            Assert.Equal(expectedPoints[i], actualPoints[i], comparer);
        }
    }
}
