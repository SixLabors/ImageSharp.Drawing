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
    }
}
