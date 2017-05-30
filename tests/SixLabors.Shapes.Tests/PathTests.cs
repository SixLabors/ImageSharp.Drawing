using Xunit;

namespace SixLabors.Shapes.Tests
{
    using System.Linq;
    using System.Numerics;

    /// <summary>
    /// The internal path tests.
    /// </summary>
    public class PathTests
    {
        [Fact]
        public void Bounds()
        {
            LinearLineSegment seg1 = new LinearLineSegment(new Vector2(0, 0), new Vector2(2, 2));
            LinearLineSegment seg2 = new LinearLineSegment(new Vector2(4, 4), new Vector2(5, 5));

            Path path = new Path(seg1, seg2);

            Assert.Equal(0, path.Bounds.Left);
            Assert.Equal(5, path.Bounds.Right);
            Assert.Equal(0, path.Bounds.Top);
            Assert.Equal(5, path.Bounds.Bottom);
        }

        public static TheoryData<TestPoint, float, float> PathDistanceTheoryData =
            new TheoryData<TestPoint, float, float>
                {
                    { new Vector2(0, 0), 0f, 0f },
                    { new Vector2(1,  0), 0f, 1f },
                    { new Vector2(9,  0), 0f, 9f },
                    { new Vector2(10,  0), 0f, 10f },
                    { new Vector2(10, 1), 0f, 11f },
                    { new Vector2(10,  9), 0f, 19f },
                    { new Vector2(10,  10), 0f, 20f },
                    { new Vector2(9,  10), 0f, 21f },
                    { new Vector2(1,  10), 0f, 29f },
                    { new Vector2(0,  10), 0f, 30f },
                    { new Vector2(0,  1), 1f, 0f },
                    { new Vector2(4,  3), 3f, 4f },
                    { new Vector2(3, 4), 4f, 3f },
                    { new Vector2(-1,  0), 1f, 0f },
                    { new Vector2(1,  -1), 1f, 1f },
                    { new Vector2(9,  -1), 1f, 9f },
                    { new Vector2(11,  0), 1f, 10f },
                    { new Vector2(11, 1), 1f, 11f },
                    { new Vector2(11,  9), 1f, 19f },
                    { new Vector2(11,  10), 1f, 20f },
                    { new Vector2(9,  11), 1f, 21f },
                    { new Vector2(1,  11), 1f, 29f }
                };

        [Theory]
        [MemberData(nameof(PathDistanceTheoryData))]
        public void DistanceFromPath_Path(TestPoint point, float expectedDistance, float alongPath)
        {
            Path path = new Path(new LinearLineSegment(new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10)));
            PointInfo info = path.Distance(point);
            Assert.Equal(expectedDistance, info.DistanceFromPath);
            Assert.Equal(alongPath, info.DistanceAlongPath);
        }

        [Fact]
        public void SimplePath()
        {
            Path path = new Path(new LinearLineSegment(new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10)));
            System.Collections.Immutable.ImmutableArray<Vector2> points = path.Flatten().Single().Points;

            Assert.Equal(4, points.Length);
            Assert.Equal(new Vector2(0, 0), points[0]);
            Assert.Equal(new Vector2(10, 0), points[1]);
            Assert.Equal(new Vector2(10, 10), points[2]);
            Assert.Equal(new Vector2(0, 10), points[3]);
        }
    }
}
