// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

public class ComputeLength
{
    [Fact]
    public void CanComputeUnrolledLength()
    {
        RectanglePolygon polygon = new(PointF.Empty, new PointF(100, 200));

        Assert.Equal(600, polygon.ComputeLength());
    }

    [Fact]
    public void CanComputeUnrolledLengthComplexPath()
    {
        ComplexPolygon polygon = new(
            new RectanglePolygon(PointF.Empty, new PointF(100, 200)),
            new RectanglePolygon(new PointF(1000, 1000), new PointF(1100, 1200)));

        Assert.Equal(1200, polygon.ComputeLength());
    }
}
