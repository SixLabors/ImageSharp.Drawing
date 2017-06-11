using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.Shapes.Tests
{
    using SixLabors.Primitives;
    using System.Numerics;

    public class PolygonTests
    {
        public static TheoryData<TestPoint[], TestPoint, bool> PointInPolygonTheoryData =
            new TheoryData<TestPoint[], TestPoint, bool>
            {
                {
                    new TestPoint[] {new PointF(10, 10), new PointF(10, 100), new PointF(100, 100), new PointF(100, 10)},
                    // loc
                    new PointF(10, 10), // test
                    true
                }, //corner is inside
                {
                    new TestPoint[] {new PointF(10, 10), new PointF(10, 100), new PointF(100, 100), new PointF(100, 10)},
                    // loc
                    new PointF(10, 11), // test
                    true
                }, //on line
                {
                    new TestPoint[] {new PointF(10, 10), new PointF(10, 100), new PointF(100, 100), new PointF(100, 10)},
                    // loc
                    new PointF(9, 9), // test
                    false
                }, //corner is inside
            };

        [Theory]
        [MemberData(nameof(PointInPolygonTheoryData))]
        public void PointInPolygon(TestPoint[] controlPoints, TestPoint point, bool isInside)
        {
            Polygon shape = new Polygon(new LinearLineSegment(controlPoints.Select(x => (PointF)x).ToArray()));
            Assert.Equal(isInside, shape.Contains(point));
        }

        public static TheoryData<TestPoint[], TestPoint, float> DistanceTheoryData =
           new TheoryData<TestPoint[], TestPoint, float>
           {
                {
                    new TestPoint[] {new PointF(10, 10), new PointF(10, 100), new PointF(100, 100), new PointF(100, 10)},
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

        [Theory]
        [MemberData(nameof(DistanceTheoryData))]
        public void Distance(TestPoint[] controlPoints, TestPoint point, float expected)
        {
            Polygon shape = new Polygon(new LinearLineSegment(controlPoints.Select(x => (PointF)x).ToArray()));
            Assert.Equal(expected, shape.Distance(point).DistanceFromPath);
        }

        public static TheoryData<TestPoint, float, float> PathDistanceTheoryData =
            new TheoryData<TestPoint, float, float>
                {
                    { new PointF(0, 0), 0f, 0f },
                    { new PointF(1,  0), 0f, 1f },
                    { new PointF(9,  0), 0f, 9f },
                    { new PointF(10,  0), 0f, 10f },
                    { new PointF(10, 1), 0f, 11f },
                    { new PointF(10,  9), 0f, 19f },
                    { new PointF(10,  10), 0f, 20f },
                    { new PointF(9,  10), 0f, 21f },
                    { new PointF(1,  10), 0f, 29f },
                    { new PointF(0,  10), 0f, 30f },
                    { new PointF(0,  1), 0f, 39f },
                    { new PointF(4,  3), -3f, 4f },
                    { new PointF(3, 4), -3f, 36f },
                    { new PointF(-1,  0), 1f, 0f },
                    { new PointF(1,  -1), 1f, 1f },
                    { new PointF(9,  -1), 1f, 9f },
                    { new PointF(11,  0), 1f, 10f },
                    { new PointF(11, 1), 1f, 11f },
                    { new PointF(11,  9), 1f, 19f },
                    { new PointF(11,  10), 1f, 20f },
                    { new PointF(9,  11), 1f, 21f },
                    { new PointF(1,  11), 1f, 29f }
                };

        [Theory]
        [MemberData(nameof(PathDistanceTheoryData))]
        public void DistanceFromPath_Path(TestPoint point, float expectedDistance, float alongPath)
        {
            IPath path = new Polygon(new LinearLineSegment(new PointF(0, 0), new PointF(10, 0), new PointF(10, 10), new PointF(0, 10)));
            PointInfo info = path.Distance(point);
            Assert.Equal(expectedDistance, info.DistanceFromPath);
            Assert.Equal(alongPath, info.DistanceAlongPath);
        }

        [Fact]
        public void AsSimpleLinearPath()
        {
            Polygon poly = new Polygon(new LinearLineSegment(new PointF(0, 0), new PointF(0, 10), new PointF(5, 5)));
            var paths = poly.Flatten().First().Points;
            Assert.Equal(3, paths.Count);
            Assert.Equal(new PointF(0, 0), paths[0]);
            Assert.Equal(new PointF(0, 10), paths[1]);
            Assert.Equal(new PointF(5, 5), paths[2]);
        }

        [Fact]
        public void FindIntersectionsBuffer()
        {
            Polygon poly = new Polygon(new LinearLineSegment(new PointF(0, 0), new PointF(0, 10), new PointF(10, 10), new PointF(10, 0)));
            PointF[] buffer = new PointF[2];

            int hits = poly.FindIntersections(new PointF(5, -5), new PointF(5, 15), buffer);
            Assert.Equal(2, hits);
            Assert.Contains(new PointF(5, 10), buffer);
            Assert.Contains(new PointF(5, 0), buffer);
        }

        [Fact]
        public void FindIntersectionsCollection()
        {
            Polygon poly = new Polygon(new LinearLineSegment(new PointF(0, 0), new PointF(0, 10), new PointF(10, 10), new PointF(10, 0)));

            PointF[] buffer = poly.FindIntersections(new PointF(5, -5), new PointF(5, 15)).ToArray();
            Assert.Equal(2, buffer.Length);
            Assert.Contains(new PointF(5, 10), buffer);
            Assert.Contains(new PointF(5, 0), buffer);
        }

        [Fact]
        public void ReturnsWrapperOfSelfASOwnPath_SingleSegment()
        {
            Polygon poly = new Polygon(new LinearLineSegment(new PointF(0, 0), new PointF(0, 10), new PointF(5, 5)));
            var paths = poly.Flatten().ToArray();
            Assert.Equal(1, paths.Length);
            Assert.Equal(poly, paths[0]);
        }

        [Fact]
        public void ReturnsWrapperOfSelfASOwnPath_MultiSegment()
        {
            Polygon poly = new Polygon(new LinearLineSegment(new PointF(0, 0), new PointF(0, 10)), new LinearLineSegment(new PointF(2, 5), new PointF(5, 5)));
            var paths = poly.Flatten().ToArray();
            Assert.Equal(1, paths.Length);
            Assert.Equal(poly, paths[0]);
        }

        [Fact]
        public void Bounds()
        {
            Polygon poly = new Polygon(new LinearLineSegment(new PointF(0, 0), new PointF(0, 10), new PointF(5, 5)));
            RectangleF bounds = poly.Bounds;
            Assert.Equal(0, bounds.Left);
            Assert.Equal(0, bounds.Top);
            Assert.Equal(5, bounds.Right);
            Assert.Equal(10, bounds.Bottom);
        }

        [Fact]
        public void MaxIntersections()
        {
            Polygon poly = new Polygon(new LinearLineSegment(new PointF(0, 0), new PointF(0, 10)));

            // with linear polygons its the number of points the segments have
            Assert.Equal(2, poly.MaxIntersections);
        }

        [Fact]
        public void FindBothIntersections()
        {
            Polygon poly = new Polygon(new LinearLineSegment(
                            new PointF(10, 10),
                            new PointF(200, 150),
                            new PointF(50, 300)));
            IEnumerable<PointF> intersections = poly.FindIntersections(new PointF(float.MinValue, 55), new PointF(float.MaxValue, 55));
            Assert.Equal(2, intersections.Count());
        }

        [Fact]
        public void HandleClippingInnerCorner()
        {
            Polygon simplePath = new Polygon(new LinearLineSegment(
                             new PointF(10, 10),
                             new PointF(200, 150),
                             new PointF(50, 300)));

            Polygon hole1 = new Polygon(new LinearLineSegment(
                            new PointF(37, 85),
                            new PointF(130, 40),
                            new PointF(65, 137)));

            IPath poly = simplePath.Clip(hole1);

            IEnumerable<PointF> intersections = poly.FindIntersections(new PointF(float.MinValue, 137), new PointF(float.MaxValue, 137));

            // returns an even number of points
            Assert.Equal(4, intersections.Count());
        }


        [Fact]
        public void CrossingCorner()
        {
            Polygon simplePath = new Polygon(new LinearLineSegment(
                             new PointF(10, 10),
                             new PointF(200, 150),
                             new PointF(50, 300)));

            IEnumerable<PointF> intersections = simplePath.FindIntersections(new PointF(float.MinValue, 150), new PointF(float.MaxValue, 150));

            // returns an even number of points
            Assert.Equal(2, intersections.Count());
        }


        [Fact]
        public void ClippingEdgefromInside()
        {
            IPath simplePath = new RectangularePolygon(10, 10, 100, 100).Clip(new RectangularePolygon(20, 0, 20, 20));

            IEnumerable<PointF> intersections = simplePath.FindIntersections(new PointF(float.MinValue, 20), new PointF(float.MaxValue, 20));

            // returns an even number of points
            Assert.Equal(4, intersections.Count());
        }

        [Fact]
        public void ClippingEdgeFromOutside()
        {
            Polygon simplePath = new Polygon(new LinearLineSegment(
                             new PointF(10, 10),
                             new PointF(100, 10),
                             new PointF(50, 300)));

            IEnumerable<PointF> intersections = simplePath.FindIntersections(new PointF(float.MinValue, 10), new PointF(float.MaxValue, 10));

            // returns an even number of points
            Assert.Equal(0, intersections.Count() % 2);
        }

        [Fact]
        public void HandleClippingOutterCorner()
        {
            Polygon simplePath = new Polygon(new LinearLineSegment(
                             new PointF(10, 10),
                             new PointF(200, 150),
                             new PointF(50, 300)));

            Polygon hole1 = new Polygon(new LinearLineSegment(
                            new PointF(37, 85),
                            new PointF(130, 40),
                            new PointF(65, 137)));

            IPath poly = simplePath.Clip(hole1);

            IEnumerable<PointF> intersections = poly.FindIntersections(new PointF(float.MinValue, 300), new PointF(float.MaxValue, 300));

            // returns an even number of points
            Assert.Equal(2, intersections.Count());
        }

        [Fact]
        public void MissingIntersection()
        {
            Polygon simplePath = new Polygon(new LinearLineSegment(
                             new PointF(10, 10),
                             new PointF(200, 150),
                             new PointF(50, 300)));

            Polygon hole1 = new Polygon(new LinearLineSegment(
                            new PointF(37, 85),
                            new PointF(130, 40),
                            new PointF(65, 137)));

            IPath poly = simplePath.Clip(hole1);

            IEnumerable<PointF> intersections = poly.FindIntersections(new PointF(float.MinValue, 85), new PointF(float.MaxValue, 85));

            // returns an even number of points
            Assert.Equal(4, intersections.Count());
        }

        [Theory]
        [InlineData(243)]
        [InlineData(341)]
        [InlineData(199)]
        public void BezierPolygonReturning2Points(int y)
        {
            // missing bands in test from ImageSharp
            PointF[] simplePath = new[] {
                        new PointF(10, 400),
                        new PointF(30, 10),
                        new PointF(240, 30),
                        new PointF(300, 400)
            };

            Polygon poly = new Polygon(new CubicBezierLineSegment(simplePath));

            List<PointF> points = poly.FindIntersections(new PointF(float.MinValue, y), new PointF(float.MaxValue, y)).ToList();

            Assert.Equal(2, points.Count());
        }
    }
}
