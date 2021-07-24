// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
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
            Assert.Equal((double)points[0].X, 10, 5);
            Assert.Equal((double)points[0].Y, 30, 5);
            Assert.Equal((double)segment.EndPoint.X, 20, 5);
            Assert.Equal((double)segment.EndPoint.Y, 10, 5);

        }
    }
}
