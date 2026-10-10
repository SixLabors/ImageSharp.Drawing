// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class StrokeOptionsTests
{
    [Fact]
    public void ArcDetailScale_DefaultsToOne()
    {
        StrokeOptions options = new();

        Assert.Equal(1D, options.ArcDetailScale);
    }

    [Theory]
    [InlineData(double.Epsilon)]
    [InlineData(0.01D)]
    [InlineData(0.5D)]
    [InlineData(4D)]
    [InlineData(16D)]
    public void ArcDetailScale_AcceptsPositiveValues(double value)
    {
        StrokeOptions options = new() { ArcDetailScale = value };

        Assert.Equal(value, options.ArcDetailScale);
    }

    [Theory]
    [InlineData(0D)]
    [InlineData(-0.01D)]
    [InlineData(-1D)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    public void ArcDetailScale_RejectsValuesNotGreaterThanZero(double value)
    {
        StrokeOptions options = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.ArcDetailScale = value);
        Assert.Equal(1D, options.ArcDetailScale);
    }
}
