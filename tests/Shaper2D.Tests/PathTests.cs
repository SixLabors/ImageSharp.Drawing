using Xunit;

namespace Shaper2D.Tests
{
    using System.Linq;
    using System.Numerics;

    /// <summary>
    /// The internal path tests.
    /// </summary>
    public class PathTests
    {
        [Fact]
        public void Length()
        {
            var seg1 = new LinearLineSegment(new Point(0, 0), new Point(0, 2));

            var path = new Path(seg1);

            Assert.Equal(2, path.Length);
        }

        [Fact]
        public void Bounds()
        {
            var seg1 = new LinearLineSegment(new Point(0, 0), new Point(2, 2));
            var seg2 = new LinearLineSegment(new Point(4, 4), new Point(5, 5));

            var path = new Path(seg1, seg2);

            Assert.Equal(0, path.Bounds.Left);
            Assert.Equal(5, path.Bounds.Right);
            Assert.Equal(0, path.Bounds.Top);
            Assert.Equal(5, path.Bounds.Bottom);
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
                    { new Point(0,  1), 1f, 0f },
                    { new Point(4,  3), 3f, 4f },
                    { new Point(3, 4), 4f, 3f },
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
            var path = new Path(new LinearLineSegment(new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10)));
            var info = path.Distance(point);
            Assert.Equal(expectedDistance, info.DistanceFromPath);
            Assert.Equal(alongPath, info.DistanceAlongPath);
        }

        [Fact]
        public void SimplePath()
        {
            var path = new Path(new LinearLineSegment(new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10)));
            var points = path.Flatten();

            Assert.Equal(4, points.Length);
            Assert.Equal(new Point(0, 0), points[0]);
            Assert.Equal(new Point(10, 0), points[1]);
            Assert.Equal(new Point(10, 10), points[2]);
            Assert.Equal(new Point(0, 10), points[3]);
        }
    }
}
