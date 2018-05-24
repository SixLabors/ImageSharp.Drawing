using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;

namespace SixLabors.Shapes.Tests.PolygonClipper
{
    using System.Collections.Immutable;
    using System.Numerics;

    using SixLabors.Shapes.PolygonClipper;

    using Xunit;
    using ClipperLib;
    using Clipper = SixLabors.Shapes.PolygonClipper.Clipper;
    using SixLabors.Primitives;

    public class ClipperTests
    {
        private RectangularPolygon BigSquare = new RectangularPolygon(10, 10, 40, 40);
        private RectangularPolygon Hole = new RectangularPolygon(20, 20, 10, 10);
        private RectangularPolygon TopLeft = new RectangularPolygon(0, 0, 20, 20);
        private RectangularPolygon TopRight = new RectangularPolygon(30, 0, 20, 20);
        private RectangularPolygon TopMiddle = new RectangularPolygon(20, 0, 10, 20);

        private Polygon BigTriangle = new Polygon(new LinearLineSegment(
                         new Vector2(10, 10),
                         new Vector2(200, 150),
                         new Vector2(50, 300)));

        private Polygon LittleTriangle = new Polygon(new LinearLineSegment(
                        new Vector2(37, 85),
                        new Vector2(130, 40),
                        new Vector2(65, 137)));

        private IEnumerable<IPath> Clip(IPath shape, params IPath[] hole)
        {
            Clipper clipper = new Clipper();

            clipper.AddPath(shape, ClippingType.Subject);
            if (hole != null)
            {
                foreach (IPath s in hole)
                {
                    clipper.AddPath(s, ClippingType.Clip);
                }
            }

            return clipper.GenerateClippedShapes();
        }

        [Fact]
        public void OverlappingTriangleCutRightSide()
        {
            Polygon triangle = new Polygon(new LinearLineSegment(
                new Vector2(0, 50),
                new Vector2(70, 0),
                new Vector2(50, 100)));

            Polygon cutout = new Polygon(new LinearLineSegment(
                new Vector2(20, 0),
                new Vector2(70, 0),
                new Vector2(70, 100),
                new Vector2(20, 100)));

            IEnumerable<IPath> shapes = this.Clip(triangle, cutout);
            Assert.Equal(1, shapes.Count());
            Assert.DoesNotContain(triangle, shapes);
        }

        [Fact]
        public void OverlappingTriangles()
        {
            IEnumerable<IPath> shapes = this.Clip(this.BigTriangle, this.LittleTriangle);
            Assert.Equal(1, shapes.Count());
            IReadOnlyList<PointF> path = shapes.Single().Flatten().First().Points;
            Assert.Equal(7, path.Count);
            foreach (Vector2 p in this.BigTriangle.Flatten().First().Points)
            {
                Assert.Contains(p, path);
            }
        }

        [Fact]
        public void NonOverlapping()
        {
            IEnumerable<RectangularPolygon> shapes = this.Clip(this.TopLeft, this.TopRight)
                .OfType<Polygon>().Select(x => (RectangularPolygon)x);

            Assert.Equal(1, shapes.Count());
            Assert.Contains(this.TopLeft, shapes);

            Assert.DoesNotContain(this.TopRight, shapes);
        }

        [Fact]
        public void OverLappingReturns1NewShape()
        {
            IEnumerable<IPath> shapes = this.Clip(this.BigSquare, this.TopLeft);

            Assert.Equal(1, shapes.Count());
            Assert.DoesNotContain(this.BigSquare, shapes);
            Assert.DoesNotContain(this.TopLeft, shapes);
        }

        [Fact]
        public void OverlappingButNotCrossingRetuensOrigionalShapes()
        {
            IEnumerable<RectangularPolygon> shapes = this.Clip(this.BigSquare, this.Hole)
                .OfType<Polygon>().Select(x => (RectangularPolygon)x);

            Assert.Equal(2, shapes.Count());
            Assert.Contains(this.BigSquare, shapes);
            Assert.Contains(this.Hole, shapes);
        }

        [Fact]
        public void TouchingButNotOverlapping()
        {
            IEnumerable<IPath> shapes = this.Clip(this.TopMiddle, this.TopLeft);
            Assert.Equal(1, shapes.Count());
            Assert.DoesNotContain(this.TopMiddle, shapes);
            Assert.DoesNotContain(this.TopLeft, shapes);
        }

        [Fact]
        public void ClippingRectanglesCreateCorrectNumberOfPoints()
        {
            IEnumerable<ISimplePath> paths = new RectangularPolygon(10, 10, 40, 40).Clip(new RectangularPolygon(20, 0, 20, 20)).Flatten();
            Assert.Equal(1, paths.Count());
            IReadOnlyList<PointF> points = paths.First().Points;

            Assert.Equal(8, points.Count);
        }
    }
}
