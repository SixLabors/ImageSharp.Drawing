using SixLabors.Primitives;
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
                }, //corner is inside
            };
        public static TheoryData<TestPoint, TestSize, TestPoint, float> DistanceTheoryData =
            new TheoryData<TestPoint, TestSize, TestPoint, float>
            {
               {
                    new PointF(10,10), // loc
                    new SizeF(100,100), // size
                    new PointF(10,10), // test
                    0f
                }, //corner is inside
                {
                    new PointF(10,10), // loc
                    new SizeF(100,100), // size
                    new PointF(9,10), // test
                    1f
                },
                {
                    new PointF(10,10), // loc
                    new SizeF(100,100), // size
                    new PointF(10,13), // test
                    0f
                },
                {
                    new PointF(10,10), // loc
                    new SizeF(100,100), // size
                    new PointF(14,13), // test
                    -3f
                },
                {
                    new PointF(10,10), // loc
                    new SizeF(100,100), // size
                    new PointF(13,14), // test
                    -3f
                },
                {
                    new PointF(10,10), // loc
                    new SizeF(100,100), // size
                    new PointF(7,6), // test
                    5f
                },
            };

        public static TheoryData<TestPoint, float, float> PathDistanceTheoryData =
            new TheoryData<TestPoint, float, float>
            {
               { new PointF(0,0), 0f, 0f },
               { new PointF(1,0), 0f, 1f },
               { new PointF(9,0), 0f, 9f },
               { new PointF(10,0), 0f, 10f },
               { new PointF(10, 1), 0f, 11f },
               { new PointF(10,9), 0f, 19f },
               { new PointF(10,10), 0f, 20f },
               { new PointF(9,10), 0f, 21f },
               { new PointF(1,10), 0f, 29f },
               { new PointF(0,10), 0f, 30f },
               { new PointF(0,9), 0f, 31f },
               { new PointF(0,1), 0f, 39f },

               { new PointF(4,3), -3f, 4f },
               { new PointF(3, 4), -3f, 36f },

               { new PointF(-1,0), 1f, 0f },
               { new PointF(1,-1), 1f, 1f },
               { new PointF(9,-1), 1f, 9f },
               { new PointF(11,0), 1f, 10f },
               { new PointF(11, 1), 1f, 11f },
               { new PointF(11,9), 1f, 19f },
               { new PointF(11,10), 1f, 20f },
               { new PointF(9,11), 1f, 21f },
               { new PointF(1,11), 1f, 29f },
               { new PointF(-1,10), 1f, 30f },
               { new PointF(-1,9), 1f, 31f },
               { new PointF(-1,1), 1f, 39f },
            };

        [Theory]
        [MemberData(nameof(PointInPolygonTheoryData))]
        public void PointInPolygon(TestPoint location, TestSize size, TestPoint point, bool isInside)
        {
            RectangularePolygon shape = new RectangularePolygon(location, size);
            Assert.Equal(isInside, shape.Contains(point));
        }

        [Theory]
        [MemberData(nameof(DistanceTheoryData))]
        public void Distance(TestPoint location, TestSize size, TestPoint point, float expectecDistance)
        {
            IPath shape = new RectangularePolygon(location, size);

            Assert.Equal(expectecDistance, shape.Distance(point).DistanceFromPath);
        }

        [Theory]
        [MemberData(nameof(PathDistanceTheoryData))]
        public void DistanceFromPath_Path(TestPoint point, float expectecDistance, float alongPath)
        {
            IPath shape = new RectangularePolygon(0, 0, 10, 10).AsPath();
            PointInfo info = shape.Distance(point);
            Assert.Equal(expectecDistance, info.DistanceFromPath);
            Assert.Equal(alongPath, info.DistanceAlongPath);
        }

        [Fact]
        public void Left()
        {
            RectangularePolygon shape = new RectangularePolygon(10, 11, 12, 13);
            Assert.Equal(10, shape.Left);
        }

        [Fact]
        public void Right()
        {
            RectangularePolygon shape = new RectangularePolygon(10, 11, 12, 13);
            Assert.Equal(22, shape.Right);
        }

        [Fact]
        public void Top()
        {
            RectangularePolygon shape = new RectangularePolygon(10, 11, 12, 13);
            Assert.Equal(11, shape.Top);
        }

        [Fact]
        public void Bottom()
        {
            RectangularePolygon shape = new RectangularePolygon(10, 11, 12, 13);
            Assert.Equal(24, shape.Bottom);
        }

        [Fact]
        public void SizeF()
        {
            RectangularePolygon shape = new RectangularePolygon(10, 11, 12, 13);
            Assert.Equal(12, shape.Size.Width);
            Assert.Equal(13, shape.Size.Height);
        }

        [Fact]
        public void Bounds_Shape()
        {
            IPath shape = new RectangularePolygon(10, 11, 12, 13);
            Assert.Equal(10, shape.Bounds.Left);
            Assert.Equal(22, shape.Bounds.Right);
            Assert.Equal(11, shape.Bounds.Top);
            Assert.Equal(24, shape.Bounds.Bottom);
        }

        [Fact]
        public void LienearSegements()
        {
            IPath shape = new RectangularePolygon(10, 11, 12, 13).AsPath();
            var segemnts = shape.Flatten().ToArray()[0].Points;
            Assert.Equal(new PointF(10, 11), segemnts[0]);
            Assert.Equal(new PointF(22, 11), segemnts[1]);
            Assert.Equal(new PointF(22, 24), segemnts[2]);
            Assert.Equal(new PointF(10, 24), segemnts[3]);
        }

        [Fact]
        public void Intersections_2()
        {
            IPath shape = new RectangularePolygon(1, 1, 10, 10);
            IEnumerable<PointF> intersections = shape.FindIntersections(new PointF(0, 5), new PointF(20, 5));

            Assert.Equal(2, intersections.Count());
            Assert.Equal(new PointF(1, 5), intersections.First());
            Assert.Equal(new PointF(11, 5), intersections.Last());
        }

        [Fact]
        public void Intersections_1()
        {
            IPath shape = new RectangularePolygon(1, 1, 10, 10);
            IEnumerable<PointF> intersections = shape.FindIntersections(new PointF(0, 5), new PointF(5, 5));

            Assert.Equal(1, intersections.Count());
            Assert.Equal(new PointF(1, 5), intersections.First());
        }

        [Fact]
        public void Intersections_0()
        {
            IPath shape = new RectangularePolygon(1, 1, 10, 10);
            IEnumerable<PointF> intersections = shape.FindIntersections(new PointF(0, 5), new PointF(-5, 5));

            Assert.Equal(0, intersections.Count());
        }

        [Fact]
        public void Bounds_Path()
        {
            IPath shape = new RectangularePolygon(10, 11, 12, 13).AsPath();
            Assert.Equal(10, shape.Bounds.Left);
            Assert.Equal(22, shape.Bounds.Right);
            Assert.Equal(11, shape.Bounds.Top);
            Assert.Equal(24, shape.Bounds.Bottom);
        }
        
        [Fact]
        public void MaxIntersections_Shape()
        {
            IPath shape = new RectangularePolygon(10, 11, 12, 13);
            Assert.Equal(4, shape.MaxIntersections);
        }

        [Fact]
        public void ShapePaths()
        {
            IPath shape = new RectangularePolygon(10, 11, 12, 13);

            Assert.Equal(shape, shape.AsClosedPath());
        }

        [Fact]
        public void TransformIdnetityReturnsSahpeObject()
        {

            IPath shape = new RectangularePolygon(0, 0, 200, 60);
            IPath transformdShape =  shape.Transform(Matrix3x2.Identity);

            Assert.Same(shape, transformdShape);
        }

        [Fact]
        public void Transform()
        {
            IPath shape = new RectangularePolygon(0, 0, 200, 60);

            IPath newShape = shape.Transform(new Matrix3x2(0, 1, 1, 0, 20, 2));

            Assert.Equal(new PointF(20, 2), newShape.Bounds.Location);
            Assert.Equal(new SizeF(60, 200), newShape.Bounds.Size);
        }

        [Fact]
        public void Center()
        {
            RectangularePolygon shape = new RectangularePolygon(50, 50, 200, 60);

            Assert.Equal(new PointF(150, 80), shape.Center);
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
            IPath shape = new RectangularePolygon(50, 50, 200, 60);
            var point = shape.PointAlongPath(distance);
            Assert.Equal(expectedX, point.Point.X);
            Assert.Equal(expectedY, point.Point.Y);
            Assert.Equal(expectedAngle, point.Angle);
        }


    }
}
