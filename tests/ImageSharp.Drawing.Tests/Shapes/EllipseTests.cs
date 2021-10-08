// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes
{
    public class EllipseTests
    {
        [Theory]
        [InlineData(-1, true)]
        [InlineData(0, true)]
        [InlineData(0.00001, false)]
        [InlineData(1, false)]
        public void WidthMustBeGreaterThan0(float width, bool throws)
        {
            if (throws)
            {
                ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new EllipsePolygon(0, 0, width, 99));

                Assert.Equal("width", ex.ParamName);
            }
            else
            {
                var p = new EllipsePolygon(0, 0, width, 99);
                Assert.NotNull(p);
            }
        }

        [Theory]
        [InlineData(-1, true)]
        [InlineData(0, true)]
        [InlineData(0.00001, false)]
        [InlineData(1, false)]
        public void HeightMustBeGreaterThan0(float height, bool throws)
        {
            if (throws)
            {
                ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new EllipsePolygon(0, 0, 99, height));

                Assert.Equal("height", ex.ParamName);
            }
            else
            {
                var p = new EllipsePolygon(0, 0, 99, height);
                Assert.NotNull(p);
            }
        }
    }
}
