// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class DefaultRasterizerStrokeTests
{
    // Half widths of 12, 100 and 500 pixel pens.
    public static TheoryData<float> RoundArcRadii { get; } = new() { 6F, 50F, 250F };

    [Theory]
    [MemberData(nameof(RoundArcRadii))]
    public void GetArcSubdivisionCount_IncreasesWithArcDetailScale(float radius)
    {
        double[] scales = [0.01D, 0.5D, 1D, 4D, 16D];
        int previous = DefaultRasterizer.GetArcSubdivisionCount(radius, Math.PI, scales[0]);
        for (int i = 1; i < scales.Length; i++)
        {
            int count = DefaultRasterizer.GetArcSubdivisionCount(radius, Math.PI, scales[i]);

            Assert.True(count > previous, $"Scale {scales[i]} gave {count} vertices, scale {scales[i - 1]} gave {previous}.");
            previous = count;
        }
    }

    [Theory]
    [InlineData(0D)]
    [InlineData(-1D)]
    public void GetArcSubdivisionCount_FloorsScaleAtOneHundredth(double arcDetailScale)
    {
        int floored = DefaultRasterizer.GetArcSubdivisionCount(6F, Math.PI, 0.01D);

        Assert.Equal(floored, DefaultRasterizer.GetArcSubdivisionCount(6F, Math.PI, arcDetailScale));
    }

    [Fact]
    public void GetArcSubdivisionCount_LargeRadius_StaysBelowGpuBound()
    {
        // path_lowering.wgsl clamps the count at 1024 interior vertices. A 1000 pixel pen at the
        // highest scale under test must stay below that so both backends emit the same count.
        int count = DefaultRasterizer.GetArcSubdivisionCount(500F, Math.PI, 16D);

        Assert.InRange(count, 1, 1023);
    }
}
