// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes
{
    public class EllipticalArcLineSegmentTest
    {
        [Fact]
        public void ContainsStartAndEnd()
        {
            var segment = new EllipticalArcLineSegment(10, 10, 10, 20, 0, 0, 90, Matrix3x2.Identity);
            IReadOnlyList<PointF> points = segment.Flatten().ToArray();
            Assert.Equal(10, points[0].X, 5);
            Assert.Equal(30, points[0].Y, 5);
            Assert.Equal(20, segment.EndPoint.X, 5);
            Assert.Equal(10, segment.EndPoint.Y, 5);
        }

        [Fact]
        public void CheckZeroRadii()
        {
            IReadOnlyCollection<PointF> xRadiusZero = new EllipticalArcLineSegment(20, 10, 0, 20, 0, 0, 360, Matrix3x2.Identity).Flatten().ToArray();
            IReadOnlyCollection<PointF> yRadiusZero = new EllipticalArcLineSegment(20, 10, 30, 0, 0, 0, 360, Matrix3x2.Identity).Flatten().ToArray();
            IReadOnlyCollection<PointF> bothRadiiZero = new EllipticalArcLineSegment(20, 10, 0, 0, 0, 0, 360, Matrix3x2.Identity).Flatten().ToArray();
            foreach (PointF point in xRadiusZero)
            {
                Assert.Equal(20, point.X);
            }

            foreach (PointF point in yRadiusZero)
            {
                Assert.Equal(10, point.Y);
            }

            foreach (PointF point in bothRadiiZero)
            {
                Assert.Equal(20, point.X);
                Assert.Equal(10, point.Y);
            }
        }
    }
}
