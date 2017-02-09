using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;

namespace SixLabors.Shapes.Tests
{
    public class RectangleTests
    {
        public static TheoryData<TestPoint, TestSize, TestPoint, bool> PointInPolygonTheoryData =
            new TheoryData<TestPoint, TestSize, TestPoint, bool>
            {
               {
                    new Vector2(10,10), // loc
                    new Size(100,100), // size
                    new Vector2(10,10), // test
                    true
                }, //corner is inside
                {
                    new Vector2(10,10), // loc
                    new Size(100,100), // size
                    new Vector2(9,9), // test
                    false
                }, //corner is inside
            };
        public static TheoryData<TestPoint, TestSize, TestPoint, float> DistanceTheoryData =
            new TheoryData<TestPoint, TestSize, TestPoint, float>
            {
               {
                    new Vector2(10,10), // loc
                    new Size(100,100), // size
                    new Vector2(10,10), // test
                    0f
                }, //corner is inside
                {
                    new Vector2(10,10), // loc
                    new Size(100,100), // size
                    new Vector2(9,10), // test
                    1f
                },
                {
                    new Vector2(10,10), // loc
                    new Size(100,100), // size
                    new Vector2(10,13), // test
                    0f
                },
                {
                    new Vector2(10,10), // loc
                    new Size(100,100), // size
                    new Vector2(14,13), // test
                    -3f
                },
                {
                    new Vector2(10,10), // loc
                    new Size(100,100), // size
                    new Vector2(13,14), // test
                    -3f
                },
                {
                    new Vector2(10,10), // loc
                    new Size(100,100), // size
                    new Vector2(7,6), // test
                    5f
                },
            };

        public static TheoryData<TestPoint, float, float> PathDistanceTheoryData =
            new TheoryData<TestPoint, float, float>
            {
               { new Vector2(0,0), 0f, 0f },
               { new Vector2(1,0), 0f, 1f },
               { new Vector2(9,0), 0f, 9f },
               { new Vector2(10,0), 0f, 10f },
               { new Vector2(10, 1), 0f, 11f },
               { new Vector2(10,9), 0f, 19f },
               { new Vector2(10,10), 0f, 20f },
               { new Vector2(9,10), 0f, 21f },
               { new Vector2(1,10), 0f, 29f },
               { new Vector2(0,10), 0f, 30f },
               { new Vector2(0,9), 0f, 31f },
               { new Vector2(0,1), 0f, 39f },

               { new Vector2(4,3), -3f, 4f },
               { new Vector2(3, 4), -3f, 36f },

               { new Vector2(-1,0), 1f, 0f },
               { new Vector2(1,-1), 1f, 1f },
               { new Vector2(9,-1), 1f, 9f },
               { new Vector2(11,0), 1f, 10f },
               { new Vector2(11, 1), 1f, 11f },
               { new Vector2(11,9), 1f, 19f },
               { new Vector2(11,10), 1f, 20f },
               { new Vector2(9,11), 1f, 21f },
               { new Vector2(1,11), 1f, 29f },
               { new Vector2(-1,10), 1f, 30f },
               { new Vector2(-1,9), 1f, 31f },
               { new Vector2(-1,1), 1f, 39f },
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
            IPath shape = new Rectangle(location, size);

            Assert.Equal(expectecDistance, shape.Distance(point).DistanceFromPath);
        }

        [Theory]
        [MemberData(nameof(PathDistanceTheoryData))]
        public void DistanceFromPath_Path(TestPoint point, float expectecDistance, float alongPath)
        {
            IPath shape = new Rectangle(0, 0, 10, 10).AsPath();
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
            IPath shape = new Rectangle(10, 11, 12, 13);
            Assert.Equal(10, shape.Bounds.Left);
            Assert.Equal(22, shape.Bounds.Right);
            Assert.Equal(11, shape.Bounds.Top);
            Assert.Equal(24, shape.Bounds.Bottom);
        }

        [Fact]
        public void LienearSegements()
        {
            IPath shape = new Rectangle(10, 11, 12, 13).AsPath();
            var segemnts = shape.Flatten()[0].Points;
            Assert.Equal(new Vector2(10, 11), segemnts[0]);
            Assert.Equal(new Vector2(22, 11), segemnts[1]);
            Assert.Equal(new Vector2(22, 24), segemnts[2]);
            Assert.Equal(new Vector2(10, 24), segemnts[3]);
        }

        [Fact]
        public void Intersections_2()
        {
            IPath shape = new Rectangle(1, 1, 10, 10);
            var intersections = shape.FindIntersections(new Vector2(0, 5), new Vector2(20, 5));

            Assert.Equal(2, intersections.Count());
            Assert.Equal(new Vector2(1, 5), intersections.First());
            Assert.Equal(new Vector2(11, 5), intersections.Last());
        }

        [Fact]
        public void Intersections_1()
        {
            IPath shape = new Rectangle(1, 1, 10, 10);
            var intersections = shape.FindIntersections(new Vector2(0, 5), new Vector2(5, 5));

            Assert.Equal(1, intersections.Count());
            Assert.Equal(new Vector2(1, 5), intersections.First());
        }

        [Fact]
        public void Intersections_0()
        {
            IPath shape = new Rectangle(1, 1, 10, 10);
            var intersections = shape.FindIntersections(new Vector2(0, 5), new Vector2(-5, 5));

            Assert.Equal(0, intersections.Count());
        }

        [Fact]
        public void Bounds_Path()
        {
            IPath shape = new Rectangle(10, 11, 12, 13).AsPath();
            Assert.Equal(10, shape.Bounds.Left);
            Assert.Equal(22, shape.Bounds.Right);
            Assert.Equal(11, shape.Bounds.Top);
            Assert.Equal(24, shape.Bounds.Bottom);
        }
        
        [Fact]
        public void MaxIntersections_Shape()
        {
            IPath shape = new Rectangle(10, 11, 12, 13);
            Assert.Equal(4, shape.MaxIntersections);
        }

        [Fact]
        public void ShapePaths()
        {
            IPath shape = new Rectangle(10, 11, 12, 13);

            Assert.Equal(shape, shape.AsClosedPath());
        }

        [Fact]
        public void TransformIdnetityReturnsSahpeObject()
        {

            IPath shape = new Rectangle(0, 0, 200, 60);
            var transformdShape =  shape.Transform(Matrix3x2.Identity);

            Assert.Same(shape, transformdShape);
        }

        [Fact]
        public void Transform()
        {
            IPath shape = new Rectangle(0, 0, 200, 60);

            var newShape = shape.Transform(new Matrix3x2(0, 1, 1, 0, 20, 2));

            Assert.Equal(new Vector2(20, 2), newShape.Bounds.Location);
            Assert.Equal(new Size(60, 200), newShape.Bounds.Size);
        }

        [Fact]
        public void Center()
        {
            Rectangle shape = new Rectangle(50, 50, 200, 60);
            
            Assert.Equal(new Vector2(150, 80), shape.Center);
        }
    }
}
