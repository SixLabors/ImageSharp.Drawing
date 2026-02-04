// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

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
        Matrix3x2 matrix = Matrix3x2.CreateTranslation(new Vector2(5, 5));
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
        Matrix3x2 matrix = Matrix3x2.CreateTranslation(new Vector2(100, 100));
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
        PathBuilder builder = new(Matrix3x2.CreateScale(10));

        builder.AddLines(point1, point2, point3);

        builder.SetOrigin(origin); // new origin is scaled by default transform
        builder.StartFigure();
        builder.AddLines(point1, point2, point3);
        IPath[] shape = Assert.IsType<ComplexPolygon>(builder.Build()).Paths.ToArray();
        Assert.Equal(100, shape[0].Bounds.Left);
        Assert.Equal(-400, shape[1].Bounds.Left);
    }
}
