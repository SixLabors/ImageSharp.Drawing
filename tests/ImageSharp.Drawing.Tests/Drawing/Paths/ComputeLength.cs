// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths
{
    public class ComputeLength
    {
        [Fact]
        public void CanComputeUnrolledLength()
        {
            var polygon = new RectangularPolygon(PointF.Empty, new PointF(100, 200));

            Assert.Equal(600, polygon.ComputeLength());
        }

        [Fact]
        public void CanComputeUnrolledLengthComplexPath()
        {
            var polygon = new ComplexPolygon(
                new RectangularPolygon(PointF.Empty, new PointF(100, 200)),
                new RectangularPolygon(new PointF(1000, 1000), new PointF(1100, 1200)));

            Assert.Equal(1200, polygon.ComputeLength());
        }
    }
}
