using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SixLabors.Shapes.Tests
{
    using SixLabors.Primitives;
    using System.Numerics;

    using Xunit;

    public class PathBuilderTests
    {
        [Fact]
        public void DrawLinesClosedFigure()
        {
            PathBuilder builder = new PathBuilder();

            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            builder.CloseFigure();

            Polygon shape = Assert.IsType<Polygon>(builder.Build());            
        }


        [Fact]
        public void AddBezier()
        {
            PathBuilder builder = new PathBuilder();

            builder.AddBezier(new Vector2(10, 10), new Vector2(20, 20), new Vector2(20, 30), new Vector2(10, 40));

            Path shape = Assert.IsType<Path>(builder.Build());
        }

        [Fact]
        public void DrawLinesOpenFigure()
        {
            PathBuilder builder = new PathBuilder();

            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            Path shape = Assert.IsType<Path>(builder.Build());
        }

        [Fact]
        public void DrawLines2OpenFigures()
        {
            PathBuilder builder = new PathBuilder();

            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            builder.StartFigure();
            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);

            ComplexPolygon shape = Assert.IsType<ComplexPolygon>(builder.Build());
            var p = shape.Paths.ToArray();
            Assert.Equal(2, p.Length);
            Assert.IsType<Path>(p[0]);
            Assert.IsType<Path>(p[1]);
        }
        [Fact]
        public void DrawLinesOpenThenClosedFigures()
        {
            PathBuilder builder = new PathBuilder();

            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            builder.StartFigure();
            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            builder.CloseFigure();
            ComplexPolygon shape = Assert.IsType<ComplexPolygon>(builder.Build());
            var p = shape.Paths.ToArray();

            Assert.Equal(2, p.Length);
            Assert.IsType<Path>(p[0]);
            Assert.IsType<Polygon>(p[1]);
        }

        [Fact]
        public void DrawLinesClosedThenOpenFigures()
        {
            PathBuilder builder = new PathBuilder();

            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            builder.CloseFigure();
            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            ComplexPolygon shape = Assert.IsType<ComplexPolygon>(builder.Build());

            var p = shape.Paths.ToArray();
            Assert.Equal(2, p.Length);
            Assert.IsType<Polygon>(p[0]);
            Assert.IsType<Path>(p[1]);
        }

        [Fact]
        public void DrawLinesCloseAllFigures()
        {
            PathBuilder builder = new PathBuilder();

            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            builder.StartFigure();
            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            ComplexPolygon shape = Assert.IsType<ComplexPolygon>(builder.Build());

            var p = shape.Paths.ToArray();
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
            Vector2 point1 = new Vector2(10, 10);
            Vector2 point2 = new Vector2(10, 90);
            Vector2 point3 = new Vector2(50, 50);
            PathBuilder builder = new PathBuilder();

            builder.AddLines(new List<PointF> { point1, point2, point3});
            Path shape = Assert.IsType<Path>(builder.Build());
            Assert.Equal(10, shape.Bounds.Left);
        }

        [Fact]
        public void MultipleStartFiguresDoesntCreateEmptyPaths()
        {
            Vector2 point1 = new Vector2(10, 10);
            Vector2 point2 = new Vector2(10, 90);
            Vector2 point3 = new Vector2(50, 50);
            PathBuilder builder = new PathBuilder();
            builder.StartFigure();
            builder.StartFigure();
            builder.StartFigure();
            builder.StartFigure();
            builder.AddLines(new List<PointF> { point1, point2, point3 });
            Path shape = Assert.IsType<Path>(builder.Build());
        }

        [Fact]
        public void DefaultTransform()
        {
            Vector2 point1 = new Vector2(10, 10);
            Vector2 point2 = new Vector2(10, 90);
            Vector2 point3 = new Vector2(50, 50);
            Matrix3x2 matrix = Matrix3x2.CreateTranslation(new Vector2(5, 5));
            PathBuilder builder = new PathBuilder(matrix);

            builder.AddLines(point1, point2, point3);
            IPath shape = builder.Build();
            Assert.Equal(15, shape.Bounds.Left);
        }

        [Fact]
        public void SetTransform()
        {
            Vector2 point1 = new Vector2(10, 10);
            Vector2 point2 = new Vector2(10, 90);
            Vector2 point3 = new Vector2(50, 50);
            Matrix3x2 matrix = Matrix3x2.CreateTranslation(new Vector2(100, 100));
            PathBuilder builder = new PathBuilder();

            builder.AddLines(point1, point2, point3);
            builder.SetTransform(matrix);
            builder.StartFigure();
            builder.AddLines(point1, point2, point3);
            builder.StartFigure();
            builder.ResetOrigin();
            builder.AddLines(point1, point2, point3);

            var shape = Assert.IsType<ComplexPolygon>(builder.Build()).Paths.ToArray();
            Assert.Equal(10, shape[0].Bounds.Left);
            Assert.Equal(110, shape[1].Bounds.Left);
            Assert.Equal(10, shape[0].Bounds.Left);
        }

        [Fact]
        public void SetOriginLeaveMatrix()
        {
            Vector2 point1 = new Vector2(10, 10);
            Vector2 point2 = new Vector2(10, 90);
            Vector2 point3 = new Vector2(50, 50);
            Vector2 origin = new Vector2(-50, -100);
            PathBuilder builder = new PathBuilder(Matrix3x2.CreateScale(10));

            builder.AddLines(point1, point2, point3);

            builder.SetOrigin(origin); //new origin is scaled by default transform
            builder.StartFigure();
            builder.AddLines(point1, point2, point3);
            var shape = Assert.IsType<ComplexPolygon>(builder.Build()).Paths.ToArray();
            Assert.Equal(100, shape[0].Bounds.Left);
            Assert.Equal(-400, shape[1].Bounds.Left);
        }
    }
}
