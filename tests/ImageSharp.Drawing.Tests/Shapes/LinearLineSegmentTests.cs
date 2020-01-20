// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace SixLabors.ImageSharp.Tests
{
    public class LinearLineSegmentTests
    {
        [Fact]
        public void SingleSegmentConstructor()
        {
            var segment = new LinearLineSegment(new Vector2(0, 0), new Vector2(10, 10));
            IReadOnlyList<PointF> flatPath = segment.Flatten();
            Assert.Equal(2, flatPath.Count);
            Assert.Equal(new PointF(0, 0), flatPath[0]);
            Assert.Equal(new PointF(10, 10), flatPath[1]);
        }

        [Fact]
        public void MustHaveAtleast2Points()
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => new LinearLineSegment(new[] { new PointF(0, 0) }));
        }

        [Fact]
        public void NullPointsArrayThrowsCountException()
        {
            Assert.ThrowsAny<ArgumentNullException>(() => new LinearLineSegment(null));
        }
    }
}
