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
        private Rectangle BigSquare = new Rectangle(10,10, 40,40);
        private Rectangle Hole = new Rectangle(20,20, 10,10);
        private Rectangle TopLeft = new Rectangle(0,0, 20,20);
        private Rectangle TopRight = new Rectangle(30,0, 20,20);
        private Rectangle TopMiddle = new Rectangle(20,0, 10,20);

        private ImmutableArray<IShape> Clip(IPath shape, params IPath[] hole)
        {
            var clipper = new Clipper();

            clipper.AddPath(shape, ClippingType.Subject);
            if (hole != null)
            {
                foreach (var s in hole)
                {
                    clipper.AddPath(s, ClippingType.Clip);
                }
            }
            return clipper.GenerateClippedShapes();
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
