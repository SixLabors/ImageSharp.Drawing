// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes
{
    public class PolygonTests
    {
        public static TheoryData<TestPoint[], TestPoint, bool> PointInPolygonTheoryData =
            new ()
            {
                {
                    new TestPoint[] { new PointF(10, 10), new PointF(10, 100), new PointF(100, 100), new PointF(100, 10) },

                    // loc
                    new PointF(10, 10), // test
                    true
                }, // corner is inside
                {
                    new TestPoint[] { new PointF(10, 10), new PointF(10, 100), new PointF(100, 100), new PointF(100, 10) },

                    // loc
                    new PointF(10, 11), // test
                    true
                }, // on line
                {
                    new TestPoint[] { new PointF(10, 10), new PointF(10, 100), new PointF(100, 100), new PointF(100, 10) },

                    // loc
                    new PointF(9, 9), // test
                    false
                }, // corner is inside
            };

        public static TheoryData<TestPoint[], TestPoint, float> DistanceTheoryData =
           new ()
           {
                {
                    new TestPoint[] { new PointF(10, 10), new PointF(10, 100), new PointF(100, 100), new PointF(100, 10) },
                    new PointF(10, 10),
                    0
                },
                {
                   new TestPoint[] { new PointF(10, 10), new PointF(10, 100), new PointF(100, 100), new PointF(100, 10) },
                   new PointF(10, 11), 0
                },
                {
                   new TestPoint[] { new PointF(10, 10), new PointF(10, 100), new PointF(100, 100), new PointF(100, 10) },
                   new PointF(11, 11), -1
                },
                {
                   new TestPoint[] { new PointF(10, 10), new PointF(10, 100), new PointF(100, 100), new PointF(100, 10) },
                   new PointF(9, 10), 1
                },
           };

        [Fact]
        public void AsSimpleLinearPath()
        {
            var poly = new Polygon(new LinearLineSegment(new PointF(0, 0), new PointF(0, 10), new PointF(5, 5)));
            IReadOnlyList<PointF> paths = poly.Flatten().First().Points.ToArray();
            Assert.Equal(3, paths.Count);
            Assert.Equal(new PointF(0, 0), paths[0]);
            Assert.Equal(new PointF(0, 10), paths[1]);
            Assert.Equal(new PointF(5, 5), paths[2]);
        }

        [Fact]
        public void ReturnsWrapperOfSelfASOwnPath_SingleSegment()
        {
            var poly = new Polygon(new LinearLineSegment(new PointF(0, 0), new PointF(0, 10), new PointF(5, 5)));
            ISimplePath[] paths = poly.Flatten().ToArray();
            Assert.Single(paths);
            Assert.Equal(poly, paths[0]);
        }

        [Fact]
        public void ReturnsWrapperOfSelfASOwnPath_MultiSegment()
        {
            var poly = new Polygon(new LinearLineSegment(new PointF(0, 0), new PointF(0, 10)), new LinearLineSegment(new PointF(2, 5), new PointF(5, 5)));
            ISimplePath[] paths = poly.Flatten().ToArray();
            Assert.Single(paths);
            Assert.Equal(poly, paths[0]);
        }

        [Fact]
        public void Bounds()
        {
            var poly = new Polygon(new LinearLineSegment(new PointF(0, 0), new PointF(0, 10), new PointF(5, 5)));
            RectangleF bounds = poly.Bounds;
            Assert.Equal(0, bounds.Left);
            Assert.Equal(0, bounds.Top);
            Assert.Equal(5, bounds.Right);
            Assert.Equal(10, bounds.Bottom);
        }

        [Fact]
        public void MaxIntersections()
        {
            var poly = new Polygon(new LinearLineSegment(new PointF(0, 0), new PointF(0, 10)));

            // with linear polygons its the number of points the segments have
            Assert.Equal(2, poly.MaxIntersections);
        }
    }
}
