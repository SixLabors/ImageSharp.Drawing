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

    public class ClipperTests
    {
        private Rectangle BigSquare = new Rectangle(10, 10, 40, 40);
        private Rectangle Hole = new Rectangle(20, 20, 10, 10);
        private Rectangle TopLeft = new Rectangle(0, 0, 20, 20);
        private Rectangle TopRight = new Rectangle(30, 0, 20, 20);
        private Rectangle TopMiddle = new Rectangle(20, 0, 10, 20);

        private Polygon BigTriangle = new Polygon(new LinearLineSegment(
                         new Vector2(10, 10),
                         new Vector2(200, 150),
                         new Vector2(50, 300)));

        private Polygon LittleTriangle = new Polygon(new LinearLineSegment(
                        new Vector2(37, 85),
                        new Vector2(130, 40),
                        new Vector2(65, 137)));

        private ImmutableArray<IPath> Clip(IPath shape, params IPath[] hole)
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
        public void OverlappingTriangles()
        {
            ImmutableArray<IPath> shapes = this.Clip(this.BigTriangle, this.LittleTriangle);
            Assert.Equal(1, shapes.Length);
            ImmutableArray<Vector2> path = shapes.Single().Flatten()[0].Points;
            Assert.Equal(7, path.Length);
            foreach (Vector2 p in this.BigTriangle.Flatten()[0].Points)
            {
                Assert.Contains(p, path);
            }
        }

        [Fact]
        public void NonOverlapping()
        {
            ImmutableArray<IPath> shapes = this.Clip(this.TopLeft, this.TopRight);
            Assert.Equal(1, shapes.Length);
            Assert.Contains(this.TopLeft, shapes);
            Assert.DoesNotContain(this.TopRight, shapes);
        }

        [Fact]
        public void OverLappingReturns1NewShape()
        {
            ImmutableArray<IPath> shapes = this.Clip(this.BigSquare, this.TopLeft);
            Assert.Equal(1, shapes.Length);
            Assert.DoesNotContain(this.BigSquare, shapes);
            Assert.DoesNotContain(this.TopLeft, shapes);
        }

        [Fact]
        public void OverlappingButNotCrossingRetuensOrigionalShapes()
        {
            ImmutableArray<IPath> shapes = this.Clip(this.BigSquare, this.Hole);
            Assert.Equal(2, shapes.Length);
            Assert.Contains(this.BigSquare, shapes);
            Assert.Contains(this.Hole, shapes);
        }

        [Fact]
        public void TouchingButNotOverlapping()
        {
            ImmutableArray<IPath> shapes = this.Clip(this.TopMiddle, this.TopLeft);
            Assert.Equal(1, shapes.Length);
            Assert.DoesNotContain(this.TopMiddle, shapes);
            Assert.DoesNotContain(this.TopLeft, shapes);
        }

        [Fact]
        public void ClippingRectanglesCreateCorrectNumberOfPoints()
        {
            ImmutableArray<ISimplePath> paths = new Rectangle(10, 10, 40, 40).Clip(new Rectangle(20, 0, 20, 20)).Flatten();
            Assert.Equal(1, paths.Length);
            ImmutableArray<Vector2> points = paths[0].Points;

            Assert.Equal(8, points.Length);
        }
    }
}
