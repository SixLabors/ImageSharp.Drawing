using Xunit;

namespace SixLabors.Shapes.Tests
{
    using SixLabors.Primitives;
    using System;
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
            LinearLineSegment seg1 = new LinearLineSegment(new PointF(0, 0), new PointF(2, 2));
            LinearLineSegment seg2 = new LinearLineSegment(new PointF(4, 4), new PointF(5, 5));

            InternalPath path = new InternalPath(new ILineSegment[] { seg1, seg2 }, true);

            Assert.Contains(new PointF(0, 0), path.Points());
            Assert.DoesNotContain(new PointF(2, 2), path.Points());
            Assert.DoesNotContain(new PointF(4, 4), path.Points());
            Assert.Contains(new PointF(5, 5), path.Points());
        }

        [Fact]
        public void Length_Closed()
        {
            LinearLineSegment seg1 = new LinearLineSegment(new PointF(0, 0), new PointF(0, 2));

            InternalPath path = new InternalPath(seg1, true);

            Assert.Equal(4, path.Length);
        }

        [Fact]
        public void Length_Open()
        {
            LinearLineSegment seg1 = new LinearLineSegment(new PointF(0, 0), new PointF(0, 2));

            InternalPath path = new InternalPath(seg1, false);

            Assert.Equal(2, path.Length);
        }

        [Fact]
        public void Bounds()
        {
            LinearLineSegment seg1 = new LinearLineSegment(new PointF(0, 0), new PointF(2, 2));
            LinearLineSegment seg2 = new LinearLineSegment(new PointF(4, 4), new PointF(5, 5));

            InternalPath path = new InternalPath(new ILineSegment[] { seg1, seg2 }, true);

            Assert.Equal(0, path.Bounds.Left);
            Assert.Equal(5, path.Bounds.Right);
            Assert.Equal(0, path.Bounds.Top);
            Assert.Equal(5, path.Bounds.Bottom);
        }

        private static InternalPath Create(PointF location, SizeF size, bool closed = true)
        {
            LinearLineSegment seg1 = new LinearLineSegment(location, location + new PointF(size.Width, 0));
            LinearLineSegment seg2 = new LinearLineSegment(location + new PointF(size.Width, size.Height), location + new PointF(0, size.Height));

            return new InternalPath(new ILineSegment[] { seg1, seg2 }, closed);
        }

        public static TheoryData<TestPoint, TestSize, TestPoint, bool> PointInPolygonTheoryData =
          new TheoryData<TestPoint, TestSize, TestPoint, bool>
          {
               {
                    new PointF(10,10), // loc
                    new SizeF(100,100), // size
                    new PointF(10,10), // test
                    true
                }, //corner is inside
                {
                    new PointF(10,10), // loc
                    new SizeF(100,100), // size
                    new PointF(9,9), // test
                    false
                }, 
          };

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
                    { new PointF(0,  9), 0f, 31f },
                    { new PointF(0,  1), 0f, 39f },
                    { new PointF(4,  3), 3f, 4f },
                    { new PointF(3, 4), 3f, 36f },
                    { new PointF(-1,  0), 1f, 0f },
                    { new PointF(1,  -1), 1f, 1f },
                    { new PointF(9,  -1), 1f, 9f },
                    { new PointF(11,  0), 1f, 10f },
                    { new PointF(11, 1), 1f, 11f },
                    { new PointF(11,  9), 1f, 19f },
                    { new PointF(11,  10), 1f, 20f },
                    { new PointF(9,  11), 1f, 21f },
                    { new PointF(1,  11), 1f, 29f },
                    { new PointF(-1,  10), 1f, 30f },
                    { new PointF(-1,  9), 1f, 31f },
                    { new PointF(-1,  1), 1f, 39f },
                };

        [Theory]
        [MemberData(nameof(PointInPolygonTheoryData))]
        public void PointInPolygon(TestPoint location, TestSize size, TestPoint point, bool isInside)
        {
            InternalPath shape = Create(location, size);
            Assert.Equal(isInside, shape.PointInPolygon(point));
        }

        [Fact]
        public void PointInPolygon_OpenPath()
        {
            LinearLineSegment seg1 = new LinearLineSegment(new PointF(0, 0), new PointF(0, 10), new PointF(10, 10), new PointF(10, 0));

            InternalPath p = new InternalPath(seg1, false);
            Assert.False(p.PointInPolygon(new PointF(5, 5)));

            InternalPath p2 = new InternalPath(seg1, true);
            Assert.True(p2.PointInPolygon(new PointF(5, 5f)));
        }

        const float HalfPi = (float)(Math.PI / 2);
        const float Pi = (float)(Math.PI);

        [Theory]
        [InlineData(0, 50, 50, Pi)]
        [InlineData(100, 150, 50, Pi)]
        [InlineData(200, 250, 50, -HalfPi)]
        [InlineData(259, 250, 109, -HalfPi)]
        [InlineData(261, 249, 110, 0)]
        [InlineData(620, 150, 50, Pi)] // wrap about end of path
        public void PointOnPath(float distance, float expectedX, float expectedY, float expectedAngle)
        {
            InternalPath shape = Create(new PointF(50, 50), new Size(200, 60));
            var point = shape.PointAlongPath(distance);
            Assert.Equal(expectedX, point.Point.X, 4);
            Assert.Equal(expectedY, point.Point.Y, 4);
            Assert.Equal(expectedAngle, point.Angle, 4);
        }

        [Theory]
        [MemberData(nameof(PathDistanceTheoryData))]
        public void DistanceFromPath_Path(TestPoint point, float expectedDistance, float alongPath)
        {
            InternalPath shape = Create(new PointF(0, 0), new Size(10, 10));
            PointInfo info = shape.DistanceFromPath(point);
            Assert.Equal(expectedDistance, info.DistanceFromPath);
            Assert.Equal(alongPath, info.DistanceAlongPath);
        }

        [Fact]
        public void DistanceFromPath_Path_Closed()
        {
            InternalPath shape = Create(new PointF(0, 0), new Size(10, 10), false);
            PointInfo info = shape.DistanceFromPath(new PointF(5, 5));
            Assert.Equal(5, info.DistanceFromPath);
            Assert.Equal(5, info.DistanceAlongPath);
        }

        [Fact]
        public void Intersections_buffer()
        {
            InternalPath shape = Create(new PointF(0, 0), new Size(10, 10));
            PointF[] buffer = new PointF[shape.PointCount];
            int hits = shape.FindIntersections(new PointF(5, -10), new PointF(5, 20), buffer);

            Assert.Equal(2, hits);
            Assert.Equal(new PointF(5, 0), buffer[0]);
            Assert.Equal(new PointF(5, 10), buffer[1]);
        }

        [Fact]
        public void Intersections_enumerabe()
        {
            InternalPath shape = Create(new PointF(0, 0), new Size(10, 10));
            PointF[] buffer = shape.FindIntersections(new PointF(5, -10), new PointF(5, 20)).ToArray();

            Assert.Equal(2, buffer.Length);
            Assert.Equal(new PointF(5, 0), buffer[0]);
            Assert.Equal(new PointF(5, 10), buffer[1]);
        }

        [Fact]
        public void Intersections_enumerabe_openpath()
        {
            InternalPath shape = Create(new PointF(0, 0), new Size(10, 10), false);
            PointF[] buffer = shape.FindIntersections(new PointF(5, -10), new PointF(5, 20)).ToArray();

            Assert.Equal(2, buffer.Length);
            Assert.Equal(new PointF(5, 0), buffer[0]);
            Assert.Equal(new PointF(5, 10), buffer[1]);
        }

        [Fact]
        public void Intersections_Diagonal()
        {
            InternalPath shape = new InternalPath(new LinearLineSegment(new PointF(0, 0), new PointF(10, 10)), false);

            PointF[] buffer = shape.FindIntersections(new PointF(0, 10), new PointF(10, 0)).ToArray();

            Assert.Equal(1, buffer.Length);
            Assert.Equal(new PointF(5, 5), buffer[0]);
        }

        [Fact]
        public void Intersections_Diagonal_NoHit()
        {
            InternalPath shape = new InternalPath(new LinearLineSegment(new PointF(0, 0), new PointF(4, 4)), false);

            PointF[] buffer = shape.FindIntersections(new PointF(0, 10), new PointF(10, 0)).ToArray();

            Assert.Equal(0, buffer.Length);
        }
        [Fact]
        public void Intersections_Diagonal_and_straight_Hit()
        {
            InternalPath shape = new InternalPath(new LinearLineSegment(new PointF(0, 0), new PointF(4, 4)), false);

            PointF[] buffer = shape.FindIntersections(new PointF(3, 10), new PointF(3, 0)).ToArray();

            Assert.Equal(1, buffer.Length);
            Assert.Equal(new PointF(3, 3), buffer[0]);
        }
        [Fact]
        public void Intersections_Diagonal_and_straight_NoHit()
        {
            InternalPath shape = new InternalPath(new LinearLineSegment(new PointF(0, 0), new PointF(4, 4)), false);

            PointF[] buffer = shape.FindIntersections(new PointF(3, 10), new PointF(3, 3.5f)).ToArray();

            Assert.Equal(0, buffer.Length);
        }
    }
}
