using Xunit;

namespace Shaper2D.Tests
{
    using System.Linq;
    using System.Numerics;

    /// <summary>
    /// The internal path tests.
    /// </summary>
    public class InternalPathTests
    {
        [Fact]
        public void MultipleLineSegmentsSimplePathsAreMerged()
        {
            var seg1 = new LinearLineSegment(new Point(0, 0), new Point(2, 2));
            var seg2 = new LinearLineSegment(new Point(4, 4), new Point(5, 5));

            var path = new InternalPath(new ILineSegment[] { seg1, seg2 }, true);

            Assert.Equal(new Point(0, 0), path.Points[0]);
            Assert.Equal(new Point(2, 2), path.Points[1]);
            Assert.Equal(new Point(4, 4), path.Points[2]);
            Assert.Equal(new Point(5, 5), path.Points[3]);
        }

        [Fact]
        public void Length_Closed()
        {
            var seg1 = new LinearLineSegment(new Point(0, 0), new Point(0, 2));

            var path = new InternalPath(seg1, true);

            Assert.Equal(4, path.Length);
        }

        [Fact]
        public void Length_Open()
        {
            var seg1 = new LinearLineSegment(new Point(0, 0), new Point(0, 2));

            var path = new InternalPath(seg1, false);

            Assert.Equal(2, path.Length);
        }

        [Fact]
        public void Bounds()
        {
            var seg1 = new LinearLineSegment(new Point(0, 0), new Point(2, 2));
            var seg2 = new LinearLineSegment(new Point(4, 4), new Point(5, 5));

            var path = new InternalPath(new ILineSegment[] { seg1, seg2 }, true);

            Assert.Equal(0, path.Bounds.Left);
            Assert.Equal(5, path.Bounds.Right);
            Assert.Equal(0, path.Bounds.Top);
            Assert.Equal(5, path.Bounds.Bottom);
        }

        private static InternalPath Create(Point location, Size size, bool closed = true)
        {
            var seg1 = new LinearLineSegment(location, location + new Point(size.Width, 0));
            var seg2 = new LinearLineSegment(location + new Point(size.Width, size.Height), location + new Point(0, size.Height));

            return new InternalPath(new ILineSegment[] { seg1, seg2 }, closed);
        }

        public static TheoryData<TestPoint, TestSize, TestPoint, bool> PointInPolygonTheoryData =
          new TheoryData<TestPoint, TestSize, TestPoint, bool>
          {
               {
                    new Point(10,10), // loc
                    new Size(100,100), // size
                    new Point(10,10), // test
                    true
                }, //corner is inside
                {
                    new Point(10,10), // loc
                    new Size(100,100), // size
                    new Point(9,9), // test
                    false
                }, //corner is inside
          };

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
                    { new Point(0,  9), 0f, 31f },
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
                    { new Point(1,  11), 1f, 29f },
                    { new Point(-1,  10), 1f, 30f },
                    { new Point(-1,  9), 1f, 31f },
                    { new Point(-1,  1), 1f, 39f },
                };

        [Theory]
        [MemberData(nameof(PointInPolygonTheoryData))]
        public void PointInPolygon(TestPoint location, TestSize size, TestPoint point, bool isInside)
        {
            var shape = Create(location, size);
            Assert.Equal(isInside, shape.PointInPolygon(point));
        }

        [Fact]
        public void PointInPolygon_OpenPath()
        {
            var seg1 = new LinearLineSegment(new Point(0, 0), new Point(0, 10), new Point(10, 10), new Point(10, 0));

            var p = new InternalPath(seg1, false);
            Assert.False(p.PointInPolygon(new Point(5, 5)));

            var p2 = new InternalPath(seg1, true);
            Assert.True(p2.PointInPolygon(new Point(5, 5f)));
        }

        [Theory]
        [MemberData(nameof(PathDistanceTheoryData))]
        public void DistanceFromPath_Path(TestPoint point, float expectedDistance, float alongPath)
        {
            var shape = Create(new Point(0, 0), new Size(10, 10));
            var info = shape.DistanceFromPath(point);
            Assert.Equal(expectedDistance, info.DistanceFromPath);
            Assert.Equal(alongPath, info.DistanceAlongPath);
        }

        [Fact]
        public void DistanceFromPath_Path_Closed()
        {
            var shape = Create(new Point(0, 0), new Size(10, 10), false);
            var info = shape.DistanceFromPath(new Point(5, 5));
            Assert.Equal(5, info.DistanceFromPath);
            Assert.Equal(5, info.DistanceAlongPath);
        }

        [Fact]
        public void Intersections_buffer()
        {
            var shape = Create(new Point(0, 0), new Size(10, 10));
            var buffer = new Point[shape.Points.Length];
            var hits = shape.FindIntersections(new Point(5, -10), new Vector2(5, 20), buffer, 4, 0);

            Assert.Equal(2, hits);
            Assert.Equal(new Point(5, 0), buffer[0]);
            Assert.Equal(new Point(5, 10), buffer[1]);
        }

        [Fact]
        public void Intersections_enumerabe()
        {
            var shape = Create(new Point(0, 0), new Size(10, 10));
            var buffer = shape.FindIntersections(new Point(5, -10), new Vector2(5, 20)).ToArray();

            Assert.Equal(2, buffer.Length);
            Assert.Equal(new Point(5, 0), buffer[0]);
            Assert.Equal(new Point(5, 10), buffer[1]);
        }

        [Fact]
        public void Intersections_enumerabe_openpath()
        {
            var shape = Create(new Point(0, 0), new Size(10, 10), false);
            var buffer = shape.FindIntersections(new Point(5, -10), new Vector2(5, 20)).ToArray();

            Assert.Equal(2, buffer.Length);
            Assert.Equal(new Point(5, 0), buffer[0]);
            Assert.Equal(new Point(5, 10), buffer[1]);
        }

        [Fact]
        public void Intersections_Diagonal()
        {
            var shape = new InternalPath(new LinearLineSegment(new Point(0, 0), new Point(10, 10)), false);

            var buffer = shape.FindIntersections(new Point(0, 10), new Vector2(10, 0)).ToArray();

            Assert.Equal(1, buffer.Length);
            Assert.Equal(new Point(5, 5), buffer[0]);
        }

        [Fact]
        public void Intersections_Diagonal_NoHit()
        {
            var shape = new InternalPath(new LinearLineSegment(new Point(0, 0), new Point(4, 4)), false);

            var buffer = shape.FindIntersections(new Point(0, 10), new Vector2(10, 0)).ToArray();

            Assert.Equal(0, buffer.Length);
        }
        [Fact]
        public void Intersections_Diagonal_and_straight_Hit()
        {
            var shape = new InternalPath(new LinearLineSegment(new Point(0, 0), new Point(4, 4)), false);

            var buffer = shape.FindIntersections(new Point(3, 10), new Vector2(3, 0)).ToArray();

            Assert.Equal(1, buffer.Length);
            Assert.Equal(new Point(3, 3), buffer[0]);
        }
        [Fact]
        public void Intersections_Diagonal_and_straight_NoHit()
        {
            var shape = new InternalPath(new LinearLineSegment(new Point(0, 0), new Point(4, 4)), false);

            var buffer = shape.FindIntersections(new Point(3, 10), new Vector2(3, 3.5f)).ToArray();

            Assert.Equal(0, buffer.Length);
        }
    }
}
