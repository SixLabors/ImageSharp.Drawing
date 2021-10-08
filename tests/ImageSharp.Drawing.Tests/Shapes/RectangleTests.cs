// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes
{
    public class RectangleTests
    {
        public static TheoryData<TestPoint, TestSize, TestPoint, bool> PointInPolygonTheoryData =
            new TheoryData<TestPoint, TestSize, TestPoint, bool>
            {
               {
                    new PointF(10, 10), // loc
                    new SizeF(100, 100), // size
                    new PointF(10, 10), // test
                    true
               }, // corner is inside
               {
                    new PointF(10, 10), // loc
                    new SizeF(100, 100), // size
                    new PointF(9, 9), // test
                    false
               }, // corner is inside
            };

        public static TheoryData<TestPoint, TestSize, TestPoint, float> DistanceTheoryData =
            new TheoryData<TestPoint, TestSize, TestPoint, float>
            {
               {
                    new PointF(10, 10), // loc
                    new SizeF(100, 100), // size
                    new PointF(10, 10), // test
                    0f
               }, // corner is inside
               {
                    new PointF(10, 10), // loc
                    new SizeF(100, 100), // size
                    new PointF(9, 10), // test
                    1f
               },
               {
                    new PointF(10, 10), // loc
                    new SizeF(100, 100), // size
                    new PointF(10, 13), // test
                    0f
               },
               {
                    new PointF(10, 10), // loc
                    new SizeF(100, 100), // size
                    new PointF(14, 13), // test
                    -3f
               },
               {
                    new PointF(10, 10), // loc
                    new SizeF(100, 100), // size
                    new PointF(13, 14), // test
                    -3f
               },
               {
                    new PointF(10, 10), // loc
                    new SizeF(100, 100), // size
                    new PointF(7, 6), // test
                    5f
               },
            };

        [Fact]
        public void Left()
        {
            var shape = new RectangularPolygon(10, 11, 12, 13);
            Assert.Equal(10, shape.Left);
        }

        [Fact]
        public void Right()
        {
            var shape = new RectangularPolygon(10, 11, 12, 13);
            Assert.Equal(22, shape.Right);
        }

        [Fact]
        public void Top()
        {
            var shape = new RectangularPolygon(10, 11, 12, 13);
            Assert.Equal(11, shape.Top);
        }

        [Fact]
        public void Bottom()
        {
            var shape = new RectangularPolygon(10, 11, 12, 13);
            Assert.Equal(24, shape.Bottom);
        }

        [Fact]
        public void SizeF()
        {
            var shape = new RectangularPolygon(10, 11, 12, 13);
            Assert.Equal(12, shape.Size.Width);
            Assert.Equal(13, shape.Size.Height);
        }

        [Fact]
        public void Bounds_Shape()
        {
            IPath shape = new RectangularPolygon(10, 11, 12, 13);
            Assert.Equal(10, shape.Bounds.Left);
            Assert.Equal(22, shape.Bounds.Right);
            Assert.Equal(11, shape.Bounds.Top);
            Assert.Equal(24, shape.Bounds.Bottom);
        }

        [Fact]
        public void LienearSegements()
        {
            IPath shape = new RectangularPolygon(10, 11, 12, 13);
            IReadOnlyList<PointF> segemnts = shape.Flatten().ToArray()[0].Points.ToArray();
            Assert.Equal(new PointF(10, 11), segemnts[0]);
            Assert.Equal(new PointF(22, 11), segemnts[1]);
            Assert.Equal(new PointF(22, 24), segemnts[2]);
            Assert.Equal(new PointF(10, 24), segemnts[3]);
        }

        [Fact]
        public void Bounds_Path()
        {
            IPath shape = new RectangularPolygon(10, 11, 12, 13);
            Assert.Equal(10, shape.Bounds.Left);
            Assert.Equal(22, shape.Bounds.Right);
            Assert.Equal(11, shape.Bounds.Top);
            Assert.Equal(24, shape.Bounds.Bottom);
        }

        [Fact]
        public void ShapePaths()
        {
            IPath shape = new RectangularPolygon(10, 11, 12, 13);

            Assert.Equal(shape, shape.AsClosedPath());
        }

        [Fact]
        public void TransformIdnetityReturnsSahpeObject()
        {
            IPath shape = new RectangularPolygon(0, 0, 200, 60);
            IPath transformdShape = shape.Transform(Matrix3x2.Identity);

            Assert.Same(shape, transformdShape);
        }

        [Fact]
        public void Transform()
        {
            IPath shape = new RectangularPolygon(0, 0, 200, 60);

            IPath newShape = shape.Transform(new Matrix3x2(0, 1, 1, 0, 20, 2));

            Assert.Equal(new PointF(20, 2), newShape.Bounds.Location);
            Assert.Equal(new SizeF(60, 200), newShape.Bounds.Size);
        }

        [Fact]
        public void Center()
        {
            var shape = new RectangularPolygon(50, 50, 200, 60);

            Assert.Equal(new PointF(150, 80), shape.Center);
        }

        private const float HalfPi = (float)(Math.PI / 2);
        private const float Pi = (float)Math.PI;

        [Theory]
        [InlineData(0, 50, 50, Pi)]
        [InlineData(100, 150, 50, Pi)]
        [InlineData(200, 250, 50, -HalfPi)]
        [InlineData(259, 250, 109, -HalfPi)]
        [InlineData(261, 249, 110, 0)]
        [InlineData(620, 150, 50, Pi)] // wrap about end of path
        public void PointOnPath(float distance, float expectedX, float expectedY, float expectedAngle)
        {
            var shape = new RectangularPolygon(50, 50, 200, 60);
            SegmentInfo point = ((IPathInternals)shape).PointAlongPath(distance);
            Assert.Equal(expectedX, point.Point.X);
            Assert.Equal(expectedY, point.Point.Y);
            Assert.Equal(expectedAngle, point.Angle);
        }
    }
}
