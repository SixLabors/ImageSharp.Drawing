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

        private ImmutableArray<IShape> Clip(IShape shape, params IShape[] hole)
        {
            var clipper = new Clipper();

            clipper.AddShape(shape, ClippingType.Subject);
            if (hole != null)
            {
                foreach (var s in hole)
                {
                    clipper.AddShape(s, ClippingType.Clip);
                }
            }

            return clipper.GenerateClippedShapes();
        }

        [Fact]
        public void OverlappingTriangles()
        {
            var shapes = this.Clip(this.BigTriangle, this.LittleTriangle);
            Assert.Equal(1, shapes.Length);
            var path = shapes.Single().Paths.Single().Flatten();
            Assert.Equal(7, path.Length);
            foreach (var p in this.BigTriangle.Flatten())
            {
                Assert.Contains(p, path);
            }
        }

        [Fact]
        public void NonOverlapping()
        {
            var shapes = this.Clip(this.TopLeft, this.TopRight);
            Assert.Equal(1, shapes.Length);
            Assert.Contains(this.TopLeft, shapes);
            Assert.DoesNotContain(this.TopRight, shapes);
        }

        [Fact]
        public void OverLappingReturns1NewShape()
        {
            var shapes = this.Clip(this.BigSquare, this.TopLeft);
            Assert.Equal(1, shapes.Length);
            Assert.DoesNotContain(this.BigSquare, shapes);
            Assert.DoesNotContain(this.TopLeft, shapes);
        }

        [Fact]
        public void OverlappingButNotCrossingRetuensOrigionalShapes()
        {
            var shapes = this.Clip(this.BigSquare, this.Hole);
            Assert.Equal(2, shapes.Length);
            Assert.Contains(this.BigSquare, shapes);
            Assert.Contains(this.Hole, shapes);
        }

        [Fact]
        public void TrimsDipliactesOfStartFromEnd()
        {
            var clipper = new Clipper();
            var mockPath = new Mock<IPath>();
            mockPath.Setup(x => x.Flatten())
                .Returns(ImmutableArray.Create(new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0)));

            Assert.Throws<ClipperException>(() => { clipper.AddPath(mockPath.Object, ClippingType.Subject); });
        }

        [Fact]
        public void SelfClosingPathTrimsDuplicatesOfEndFromEnd()
        {
            var clipper = new Clipper();
            var mockPath = new Mock<IPath>();
            mockPath.Setup(x => x.Flatten())
                .Returns(ImmutableArray.Create(new Vector2(0, 0), new Vector2(1, 1), new Vector2(1, 1)));

            Assert.Throws<ClipperException>(() => { clipper.AddPath(mockPath.Object, ClippingType.Subject); });
        }

        [Fact]
        public void TouchingByNotOverlapping()
        {
            var shapes = this.Clip(this.TopMiddle, this.TopLeft);
            Assert.Equal(1, shapes.Length);
            Assert.DoesNotContain(this.TopMiddle, shapes);
            Assert.DoesNotContain(this.TopLeft, shapes);
        }
    }
}
