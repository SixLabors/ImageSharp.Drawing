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
