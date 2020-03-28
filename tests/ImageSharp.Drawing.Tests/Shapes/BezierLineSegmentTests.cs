// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    public class BezierLineSegmentTests
    {
        [Fact]
        public void SingleSegmentConstructor()
        {
            var segment = new CubicBezierLineSegment(new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 0), new Vector2(20, 0));
            IReadOnlyList<PointF> points = segment.Flatten();
            Assert.Contains(new Vector2(0, 0), points);
            Assert.Contains(new Vector2(10, 0), points);
            Assert.Contains(new Vector2(20, 0), points);
        }

        [Fact]
        public void MustHaveAtleast4Points()
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezierLineSegment(new[] { new PointF(0, 0) }));
        }
    }
}
