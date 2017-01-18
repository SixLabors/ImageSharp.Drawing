using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Shaper2D.Tests.PolygonClipper
{
    using Shaper2D.PolygonClipper;

    using Xunit;

    public class ClipperTests
    {
        private Rectangle BigSquare = new Rectangle(10,10, 40,40);
        private Rectangle Hole = new Rectangle(20,20, 10,10);
        private Rectangle TopLeft = new Rectangle(0,0, 20,20);
        private Rectangle TopRight = new Rectangle(30,0, 20,20);

        private IShape[] Clip(IPath shape, params IPath[] hole)
        {
            var clipper = new Clipper();

            clipper.AddPath(shape, PolyType.Subject);
            if (hole != null)
            {
                foreach (var s in hole)
                {
                    clipper.AddPath(s, PolyType.Clip);
                }
            }
            return clipper.Execute();
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
    }
}
