// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    public class EllipticalArcLineSegmentTest
    {
        [Fact]
        public void ContainsStartandEnd()
        {
            var segment = new EllipticalArcLineSegment(new PointF(10, 10), 10, 20, 0, 0, 90, Matrix3x2.Identity);
            IReadOnlyList<PointF> points = segment.Flatten().ToArray();
            Assert.Equal(10, points[0].X, 5);
            Assert.Equal(30, points[0].Y, 5);
            Assert.Equal(20, segment.EndPoint.X, 5);
            Assert.Equal(10, segment.EndPoint.Y, 5);
        }
    }
}
