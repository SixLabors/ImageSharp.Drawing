using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Shaper2D.Tests
{
    public class RectangleTests
    {
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
        public static TheoryData<TestPoint, TestSize, TestPoint, float> DistanceTheoryData =
            new TheoryData<TestPoint, TestSize, TestPoint, float>
            {
               {
                    new Point(10,10), // loc
                    new Size(100,100), // size
                    new Point(10,10), // test
                    0f
                }, //corner is inside
                {
                    new Point(10,10), // loc
                    new Size(100,100), // size
                    new Point(9,10), // test
                    1f
                },
                {
                    new Point(10,10), // loc
                    new Size(100,100), // size
                    new Point(10,13), // test
                    0f
                },
                {
                    new Point(10,10), // loc
                    new Size(100,100), // size
                    new Point(14,13), // test
                    -3f
                },
                {
                    new Point(10,10), // loc
                    new Size(100,100), // size
                    new Point(13,14), // test
                    -3f
                },
                {
                    new Point(10,10), // loc
                    new Size(100,100), // size
                    new Point(7,6), // test
                    5f
                },
            };

        public static TheoryData<TestPoint, float, float> PathDistanceTheoryData =
            new TheoryData<TestPoint, float, float>
            {
               { new Point(0,0), 0f, 0f },
               { new Point(1,0), 0f, 1f },
               { new Point(9,0), 0f, 9f },
               { new Point(10,0), 0f, 10f },
               { new Point(10, 1), 0f, 11f },
               { new Point(10,9), 0f, 19f },
               { new Point(10,10), 0f, 20f },
               { new Point(9,10), 0f, 21f },
               { new Point(1,10), 0f, 29f },
               { new Point(0,10), 0f, 30f },
               { new Point(0,9), 0f, 31f },
               { new Point(0,1), 0f, 39f },

               { new Point(4,3), 3f, 4f },
               { new Point(3, 4), 3f, 36f },

               { new Point(-1,0), 1f, 0f },
               { new Point(1,-1), 1f, 1f },
               { new Point(9,-1), 1f, 9f },
               { new Point(11,0), 1f, 10f },
               { new Point(11, 1), 1f, 11f },
               { new Point(11,9), 1f, 19f },
               { new Point(11,10), 1f, 20f },
               { new Point(9,11), 1f, 21f },
               { new Point(1,11), 1f, 29f },
               { new Point(-1,10), 1f, 30f },
               { new Point(-1,9), 1f, 31f },
               { new Point(-1,1), 1f, 39f },
            };

        [Theory]
        [MemberData(nameof(PointInPolygonTheoryData))]
        public void PointInPolygon(TestPoint location, TestSize size, TestPoint point, bool isInside)
        {
            var shape = new Rectangle(location, size);
            Assert.Equal(isInside, shape.Contains(point));
        }

        [Theory]
        [MemberData(nameof(DistanceTheoryData))]
        public void Distance(TestPoint location, TestSize size, TestPoint point, float expectecDistance)
        {
            IShape shape = new Rectangle(location, size);

            Assert.Equal(expectecDistance, shape.Distance(point));
        }

        [Theory]
        [MemberData(nameof(PathDistanceTheoryData))]
        public void DistanceFromPath_Path(TestPoint point, float expectecDistance, float alongPath)
        {
            IPath shape = new Rectangle(0, 0, 10, 10);
            var info = shape.Distance(point);
            Assert.Equal(expectecDistance, info.DistanceFromPath);
            Assert.Equal(alongPath, info.DistanceAlongPath);
        }

        [Fact]
        public void Left()
        {
            var shape = new Rectangle(10, 11, 12, 13);
            Assert.Equal(10, shape.Left);
        }

        [Fact]
        public void Right()
        {
            var shape = new Rectangle(10, 11, 12, 13);
            Assert.Equal(22, shape.Right);
        }

        [Fact]
        public void Top()
        {
            var shape = new Rectangle(10, 11, 12, 13);
            Assert.Equal(11, shape.Top);
        }

        [Fact]
        public void Bottom()
        {
            var shape = new Rectangle(10, 11, 12, 13);
            Assert.Equal(24, shape.Bottom);
        }

        [Fact]
        public void Size()
        {
            var shape = new Rectangle(10, 11, 12, 13);
            Assert.Equal(12, shape.Size.Width);
            Assert.Equal(13, shape.Size.Height);
        }

        [Fact]
        public void Bounds_Shape()
        {
            IShape shape = new Rectangle(10, 11, 12, 13);
            Assert.Equal(10, shape.Bounds.Left);
            Assert.Equal(22, shape.Bounds.Right);
            Assert.Equal(11, shape.Bounds.Top);
            Assert.Equal(24, shape.Bounds.Bottom);
        }

        [Fact]
        public void LienearSegements()
        {
            IPath shape = new Rectangle(10, 11, 12, 13);
            var segemnts = shape.AsSimpleLinearPath();
            Assert.Equal(new Point(10, 11), segemnts[0]);
            Assert.Equal(new Point(22, 11), segemnts[1]);
            Assert.Equal(new Point(22, 24), segemnts[2]);
            Assert.Equal(new Point(10, 24), segemnts[3]);
        }

        [Fact]
        public void Intersections_2()
        {
            IShape shape = new Rectangle(1, 1, 10, 10);
            var intersections = shape.FindIntersections(new Point(0, 5), new Point(20, 5));

            Assert.Equal(2, intersections.Count());
            Assert.Equal(new Point(1, 5), intersections.First());
            Assert.Equal(new Point(11, 5), intersections.Last());
        }

        [Fact]
        public void Intersections_1()
        {
            IShape shape = new Rectangle(1, 1, 10, 10);
            var intersections = shape.FindIntersections(new Point(0, 5), new Point(5, 5));

            Assert.Equal(1, intersections.Count());
            Assert.Equal(new Point(1, 5), intersections.First());
        }

        [Fact]
        public void Intersections_0()
        {
            IShape shape = new Rectangle(1, 1, 10, 10);
            var intersections = shape.FindIntersections(new Point(0, 5), new Point(-5, 5));

            Assert.Equal(0, intersections.Count());
        }

        [Fact]
        public void Bounds_Path()
        {
            IPath shape = new Rectangle(10, 11, 12, 13);
            Assert.Equal(10, shape.Bounds.Left);
            Assert.Equal(22, shape.Bounds.Right);
            Assert.Equal(11, shape.Bounds.Top);
            Assert.Equal(24, shape.Bounds.Bottom);
        }

        [Fact]
        public void IsClosed_Path()
        {
            IPath shape = new Rectangle(10, 11, 12, 13);
            Assert.Equal(true, shape.IsClosed);
        }

        [Fact]
        public void Length_Path()
        {
            IPath shape = new Rectangle(10, 11, 12, 13);
            Assert.Equal(50, shape.Length);
        }

        [Fact]
        public void MaxIntersections_Shape()
        {
            IShape shape = new Rectangle(10, 11, 12, 13);
            Assert.Equal(4, shape.MaxIntersections);
        }

        [Fact]
        public void ShapePaths()
        {
            IShape shape = new Rectangle(10, 11, 12, 13);

            Assert.Equal((IPath)shape, shape.Paths.Single());
        }
    }
}
