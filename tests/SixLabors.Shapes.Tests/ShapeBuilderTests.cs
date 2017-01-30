using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SixLabors.Shapes.Tests
{
    using System.Numerics;

    using Xunit;

    public class ShapeBuilderTests
    {
        [Fact]
        public void DrawLinesClosedFigure()
        {
            var builder = new ShapeBuilder();

            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            builder.CloseFigure();
            var shape = builder.Build();

            Assert.Equal(1, shape.Paths.Length);
            Assert.IsType<Polygon>(shape.Paths[0].AsShape());
        }


        [Fact]
        public void AddBezier()
        {
            var builder = new ShapeBuilder();

            builder.AddBezier(new Vector2(10, 10), new Vector2(20, 20), new Vector2(20, 30), new Vector2(10, 40));
            var shape = builder.Build();

            Assert.Equal(1, shape.Paths.Length);
        }

        [Fact]
        public void DrawLinesOpenFigure()
        {
            var builder = new ShapeBuilder();

            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            var shape = builder.Build();

            Assert.Equal(1, shape.Paths.Length);
            Assert.IsType<Path>(shape.Paths[0]);
        }

        [Fact]
        public void DrawLines2OpenFigures()
        {
            var builder = new ShapeBuilder();

            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            builder.StartFigure();
            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            var shape = builder.Build();

            Assert.Equal(2, shape.Paths.Length);
            Assert.IsType<Path>(shape.Paths[0]);
            Assert.IsType<Path>(shape.Paths[1]);
        }
        [Fact]
        public void DrawLinesOpenThenClosedFigures()
        {
            var builder = new ShapeBuilder();

            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            builder.StartFigure();
            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            builder.CloseFigure();
            var shape = builder.Build();

            Assert.Equal(2, shape.Paths.Length);
            Assert.IsType<Path>(shape.Paths[0]);
            Assert.IsType<Polygon>(shape.Paths[1].AsShape());
        }

        [Fact]
        public void DrawLinesClosedThenOpenFigures()
        {
            var builder = new ShapeBuilder();

            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            builder.CloseFigure();
            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            var shape = builder.Build();

            Assert.Equal(2, shape.Paths.Length);
            Assert.IsType<Polygon>(shape.Paths[0].AsShape());
            Assert.IsType<Path>(shape.Paths[1]);
        }

        [Fact]
        public void DrawLinesCloseAllFigures()
        {
            var builder = new ShapeBuilder();

            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            builder.StartFigure();
            builder.AddLine(10, 10, 10, 90);
            builder.AddLine(10, 90, 50, 50);
            var shape = builder.Build();

            Assert.Equal(2, shape.Paths.Length);
            Assert.IsType<Path>(shape.Paths[0]);
            Assert.IsType<Path>(shape.Paths[1]);

            builder.CloseAllFigures();
            shape = builder.Build();

            Assert.Equal(2, shape.Paths.Length);
            Assert.IsType<Polygon>(shape.Paths[0].AsShape());
            Assert.IsType<Polygon>(shape.Paths[1].AsShape());
        }

        [Fact]
        public void EnumerableAddLines()
        {
            var point1 = new Vector2(10, 10);
            var point2 = new Vector2(10, 90);
            var point3 = new Vector2(50, 50);
            var builder = new ShapeBuilder();

            builder.AddLines(new List<Vector2> { point1, point2, point3});
            var shape = builder.Build();
            Assert.Equal(10, shape.Bounds.Left);
        }

        [Fact]
        public void MultipleStartFiguresDoesntCreateEmptyPaths()
        {
            var point1 = new Vector2(10, 10);
            var point2 = new Vector2(10, 90);
            var point3 = new Vector2(50, 50);
            var builder = new ShapeBuilder();
            builder.StartFigure();
            builder.StartFigure();
            builder.StartFigure();
            builder.StartFigure();
            builder.AddLines(new List<Vector2> { point1, point2, point3 });
            var shape = builder.Build();
            Assert.Equal(1, shape.Paths.Length);
        }

        [Fact]
        public void DefaultTransform()
        {
            var point1 = new Vector2(10, 10);
            var point2 = new Vector2(10, 90);
            var point3 = new Vector2(50, 50);
            var matrix = Matrix3x2.CreateTranslation(new Vector2(5, 5));
            var builder = new ShapeBuilder(matrix);

            builder.AddLines(point1, point2, point3);
            var shape = builder.Build();
            Assert.Equal(15, shape.Bounds.Left);
        }

        [Fact]
        public void SetTransform()
        {
            var point1 = new Vector2(10, 10);
            var point2 = new Vector2(10, 90);
            var point3 = new Vector2(50, 50);
            var matrix = Matrix3x2.CreateTranslation(new Vector2(100, 100));
            var builder = new ShapeBuilder();

            builder.AddLines(point1, point2, point3);
            builder.SetTransform(matrix);
            builder.StartFigure();
            builder.AddLines(point1, point2, point3);
            builder.StartFigure();
            builder.ResetOrigin();
            builder.AddLines(point1, point2, point3);

            var shape = builder.Build().Paths;
            Assert.Equal(10, shape[0].Bounds.Left);
            Assert.Equal(110, shape[1].Bounds.Left);
            Assert.Equal(10, shape[0].Bounds.Left);
        }

        [Fact]
        public void SetOriginLeaveMatrix()
        {
            var point1 = new Vector2(10, 10);
            var point2 = new Vector2(10, 90);
            var point3 = new Vector2(50, 50);
            var origin = new Vector2(-50, -100);
            var builder = new ShapeBuilder(Matrix3x2.CreateScale(10));

            builder.AddLines(point1, point2, point3);
            builder.SetOrigin(origin);
            builder.StartFigure();
            builder.AddLines(point1, point2, point3);
            var shape = builder.Build().Paths;
            Assert.Equal(100, shape[0].Bounds.Left);
            Assert.Equal(50, shape[1].Bounds.Left);
        }
    }
}
