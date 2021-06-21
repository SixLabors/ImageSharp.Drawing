// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.PolygonClipper;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.PolygonClipper
{
    public class ClipperTests
    {
        private readonly RectangularPolygon bigSquare = new RectangularPolygon(10, 10, 40, 40);
        private readonly RectangularPolygon hole = new RectangularPolygon(20, 20, 10, 10);
        private readonly RectangularPolygon topLeft = new RectangularPolygon(0, 0, 20, 20);
        private readonly RectangularPolygon topRight = new RectangularPolygon(30, 0, 20, 20);
        private readonly RectangularPolygon topMiddle = new RectangularPolygon(20, 0, 10, 20);

        private readonly Polygon bigTriangle = new Polygon(new LinearLineSegment(
                         new Vector2(10, 10),
                         new Vector2(200, 150),
                         new Vector2(50, 300)));

        private readonly Polygon littleTriangle = new Polygon(new LinearLineSegment(
                        new Vector2(37, 85),
                        new Vector2(130, 40),
                        new Vector2(65, 137)));

        private IEnumerable<IPath> Clip(IPath shape, params IPath[] hole)
        {
            var clipper = new Clipper();

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
            var triangle = new Polygon(new LinearLineSegment(
                new Vector2(0, 50),
                new Vector2(70, 0),
                new Vector2(50, 100)));

            var cutout = new Polygon(new LinearLineSegment(
                new Vector2(20, 0),
                new Vector2(70, 0),
                new Vector2(70, 100),
                new Vector2(20, 100)));

            IEnumerable<IPath> shapes = this.Clip(triangle, cutout);
            Assert.Single(shapes);
            Assert.DoesNotContain(triangle, shapes);
        }

        [Fact]
        public void OverlappingTriangles()
        {
            IEnumerable<IPath> shapes = this.Clip(this.bigTriangle, this.littleTriangle);
            Assert.Single(shapes);
            IReadOnlyList<PointF> path = shapes.Single().Flatten().First().Points.ToArray();
            Assert.Equal(7, path.Count);
            foreach (Vector2 p in this.bigTriangle.Flatten().First().Points.ToArray())
            {
                Assert.Contains(p, path);
            }
        }

        [Fact]
        public void NonOverlapping()
        {
            IEnumerable<RectangularPolygon> shapes = this.Clip(this.topLeft, this.topRight)
                .OfType<Polygon>().Select(x => (RectangularPolygon)x);

            Assert.Single(shapes);
            Assert.Contains(
                shapes, x => RectangularPolygonValueComparer.Equals(this.topLeft, x));

            Assert.DoesNotContain(this.topRight, shapes);
        }

        [Fact]
        public void OverLappingReturns1NewShape()
        {
            IEnumerable<IPath> shapes = this.Clip(this.bigSquare, this.topLeft);

            Assert.Single(shapes);
            Assert.DoesNotContain(shapes, x => RectangularPolygonValueComparer.Equals(this.bigSquare, x));
            Assert.DoesNotContain(shapes, x => RectangularPolygonValueComparer.Equals(this.topLeft, x));
        }

        [Fact]
        public void OverlappingButNotCrossingReturnsOrigionalShapes()
        {
            IEnumerable<RectangularPolygon> shapes = this.Clip(this.bigSquare, this.hole)
                .OfType<Polygon>().Select(x => (RectangularPolygon)x);

            Assert.Equal(2, shapes.Count());
            Assert.Contains(shapes, x => RectangularPolygonValueComparer.Equals(this.bigSquare, x));
            Assert.Contains(shapes, x => RectangularPolygonValueComparer.Equals(this.hole, x));
        }

        [Fact]
        public void TouchingButNotOverlapping()
        {
            IEnumerable<IPath> shapes = this.Clip(this.topMiddle, this.topLeft);
            Assert.Single(shapes);
            Assert.DoesNotContain(shapes, x => RectangularPolygonValueComparer.Equals(this.topMiddle, x));
            Assert.DoesNotContain(shapes, x => RectangularPolygonValueComparer.Equals(this.topLeft, x));
        }

        [Fact]
        public void ClippingRectanglesCreateCorrectNumberOfPoints()
        {
            IEnumerable<ISimplePath> paths = new RectangularPolygon(10, 10, 40, 40)
                .Clip(new RectangularPolygon(20, 0, 20, 20))
                .Flatten();

            Assert.Single(paths);
            IReadOnlyList<PointF> points = paths.First().Points.ToArray();

            Assert.Equal(8, points.Count);
        }

        [Fact]
        public void ClipperOffsetThrowsPublicException()
        {
            PointF naan = new PointF(float.NaN, float.NaN);
            Polygon path = new Polygon(new LinearLineSegment(new[] { naan, naan, naan, naan }));

            // Throws internal exception:
            try
            {
                path.GenerateOutline(10);
            }
            catch (System.Exception ex)
            {
                Assert.True(ex is ClipperException);
            }
        }
    }
}
