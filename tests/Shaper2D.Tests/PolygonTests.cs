using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Shaper2D.Tests
{
    public class PolygonTests
    {
        public static TheoryData<TestPoint[], TestPoint, bool> PointInPolygonTheoryData =
            new TheoryData<TestPoint[], TestPoint, bool>
            {
                {
                    new TestPoint[] {new Point(10, 10), new Point(10, 100), new Point(100, 100), new Point(100, 10)},
                    // loc
                    new Point(10, 10), // test
                    true
                }, //corner is inside
                {
                    new TestPoint[] {new Point(10, 10), new Point(10, 100), new Point(100, 100), new Point(100, 10)},
                    // loc
                    new Point(10, 11), // test
                    true
                }, //on line
                {
                    new TestPoint[] {new Point(10, 10), new Point(10, 100), new Point(100, 100), new Point(100, 10)},
                    // loc
                    new Point(9, 9), // test
                    false
                }, //corner is inside
            };

        [Theory]
        [MemberData(nameof(PointInPolygonTheoryData))]
        public void PointInPolygon(TestPoint[] controlPoints, TestPoint point, bool isInside)
        {
            var shape = new Polygon(new LinearLineSegment(controlPoints.Select(x => (Point)x).ToArray()));
            Assert.Equal(isInside, shape.Contains(point));
        }

        public static TheoryData<TestPoint[], TestPoint, float> DistanceTheoryData =
           new TheoryData<TestPoint[], TestPoint, float>
           {
                {
                    new TestPoint[] {new Point(10, 10), new Point(10, 100), new Point(100, 100), new Point(100, 10)},
                    new Point(10, 10),
                    0

                },
               {
                   new TestPoint[] { new Point(10, 10), new Point(10, 100), new Point(100, 100), new Point(100, 10) },
                   new Point(10, 11), 0
               },
               {
                   new TestPoint[] { new Point(10, 10), new Point(10, 100), new Point(100, 100), new Point(100, 10) },
                   new Point(11, 11), 0
                },
               {
                   new TestPoint[] { new Point(10, 10), new Point(10, 100), new Point(100, 100), new Point(100, 10) },
                   new Point(9, 10), 1
                },
           };

        [Theory]
        [MemberData(nameof(DistanceTheoryData))]
        public void Distance(TestPoint[] controlPoints, TestPoint point, float expected)
        {
            var shape = new Polygon(new LinearLineSegment(controlPoints.Select(x => (Point)x).ToArray()));
            Assert.Equal(expected, shape.Distance(point));
        }

        public static TheoryData<TestPoint, float, float> PathDistanceTheoryData =
            new TheoryData<TestPoint, float, float>
                {
                    { new Point(0, 0), 0f, 0f },
                    { new Point(1,  0), 0f, 1f },
                    { new Point(9,  0), 0f, 9f },
                    { new Point(10,  0), 0f, 10f },
                    { new Point(10, 1), 0f, 11f },
                    { new Point(10,  9), 0f, 19f },
                    { new Point(10,  10), 0f, 20f },
                    { new Point(9,  10), 0f, 21f },
                    { new Point(1,  10), 0f, 29f },
                    { new Point(0,  10), 0f, 30f },
                    { new Point(0,  1), 0f, 39f },
                    { new Point(4,  3), 3f, 4f },
                    { new Point(3, 4), 3f, 36f },
                    { new Point(-1,  0), 1f, 0f },
                    { new Point(1,  -1), 1f, 1f },
                    { new Point(9,  -1), 1f, 9f },
                    { new Point(11,  0), 1f, 10f },
                    { new Point(11, 1), 1f, 11f },
                    { new Point(11,  9), 1f, 19f },
                    { new Point(11,  10), 1f, 20f },
                    { new Point(9,  11), 1f, 21f },
                    { new Point(1,  11), 1f, 29f }
                };

        [Theory]
        [MemberData(nameof(PathDistanceTheoryData))]
        public void DistanceFromPath_Path(TestPoint point, float expectedDistance, float alongPath)
        {
            IPath path = new Polygon(new LinearLineSegment(new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10)));
            var info = path.Distance(point);
            Assert.Equal(expectedDistance, info.DistanceFromPath);
            Assert.Equal(alongPath, info.DistanceAlongPath);
        }

        [Fact]
        public void AsSimpleLinearPath()
        {
            var poly = new Polygon(new LinearLineSegment(new Point(0, 0), new Point(0, 10), new Point(5, 5)));
            var paths = poly.AsSimpleLinearPath();
            Assert.Equal(3, paths.Length);
            Assert.Equal(new Point(0, 0), paths[0]);
            Assert.Equal(new Point(0, 10), paths[1]);
            Assert.Equal(new Point(5, 5), paths[2]);
        }

        [Fact]
        public void FindIntersectionsBuffer()
        {
            var poly = new Polygon(new LinearLineSegment(new Point(0, 0), new Point(0, 10), new Point(10, 10), new Point(10, 0)));
            var buffer = new Point[2];

            var hits = poly.FindIntersections(new Point(5, -5), new Point(5, 15), buffer, 2, 0);
            Assert.Equal(2, hits);
            Assert.Contains(new Point(5, 10), buffer);
            Assert.Contains(new Point(5, 0), buffer);
        }

        [Fact]
        public void FindIntersectionsCollection()
        {
            var poly = new Polygon(new LinearLineSegment(new Point(0, 0), new Point(0, 10), new Point(10, 10), new Point(10, 0)));
            
            var buffer = poly.FindIntersections(new Point(5, -5), new Point(5, 15)).ToArray();
            Assert.Equal(2, buffer.Length);
            Assert.Contains(new Point(5, 10), buffer);
            Assert.Contains(new Point(5, 0), buffer);
        }

        [Fact]
        public void ReturnsSelfASOwnPath_SingleSegment()
        {
            var poly = new Polygon(new LinearLineSegment(new Point(0, 0), new Point(0, 10), new Point(5, 5)));
            var paths = poly.Paths;
            Assert.Equal(1, paths.Length);
            Assert.Equal(poly, paths[0]);
        }

        [Fact]
        public void ReturnsSelfASOwnPath_MultiSegment()
        {
            var poly = new Polygon(new LinearLineSegment(new Point(0, 0), new Point(0, 10)), new LinearLineSegment(new Point(2, 5), new Point(5, 5)));
            var paths = poly.Paths;
            Assert.Equal(1, paths.Length);
            Assert.Equal(poly, paths[0]);
        }

        [Fact]
        public void Bounds()
        {
            var poly = new Polygon(new LinearLineSegment(new Point(0, 0), new Point(0, 10), new Point(5, 5)));
            var bounds = poly.Bounds;
            Assert.Equal(0, bounds.Left);
            Assert.Equal(0, bounds.Top);
            Assert.Equal(5, bounds.Right);
            Assert.Equal(10, bounds.Bottom);
        }

        [Fact]
        public void Length()
        {
            var poly = new Polygon(new LinearLineSegment(new Point(0, 0), new Point(0, 10)));
            Assert.Equal(20, poly.Length);
        }

        [Fact]
        public void MaxIntersections()
        {
            var poly = new Polygon(new LinearLineSegment(new Point(0, 0), new Point(0, 10)));

            // with linear polygons its the number of points the segments have
            Assert.Equal(2, poly.MaxIntersections);
        }
    }
}
