// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests;

/// <summary>
/// see https://github.com/SixLabors/ImageSharp.Drawing/issues/224
/// </summary>
public class Issue_224
{
    [Fact]
    public async Task OutliningWithZeroWidth_MultiplePatterns()
    {
        var shape = new RectangularPolygon(10, 10, 10, 10);

        await this.CompletesIn(TimeSpan.FromSeconds(1), () =>
        {
            _ = shape.GenerateOutline(0, new float[] { 1, 2 });
        });
    }

    [Fact]
    public async Task OutliningWithZeroWidth_SinglePAttern()
    {
        var shape = new RectangularPolygon(10, 10, 10, 10);

        await this.CompletesIn(TimeSpan.FromSeconds(1), () =>
        {
            _ = shape.GenerateOutline(0, new float[] { 1 });
        });
    }

    [Fact]
    public async Task OutliningWithZeroWidth_NoPattern()
    {
        var shape = new RectangularPolygon(10, 10, 10, 10);

        await this.CompletesIn(TimeSpan.FromSeconds(1), () =>
        {
            _ = shape.GenerateOutline(0);
        });
    }

    [Fact]
    public async Task OutliningWithLessThanZeroWidth_MultiplePatterns()
    {
        var shape = new RectangularPolygon(10, 10, 10, 10);

        await this.CompletesIn(TimeSpan.FromSeconds(1), () =>
        {
            _ = shape.GenerateOutline(-10, new float[] { 1, 2 });
        });
    }

    [Fact]
    public async Task OutliningWithLessThanZeroWidth_SinglePAttern()
    {
        var shape = new RectangularPolygon(10, 10, 10, 10);

        await this.CompletesIn(TimeSpan.FromSeconds(1), () =>
        {
            _ = shape.GenerateOutline(-10, new float[] { 1 });
        });
    }

    [Fact]
    public async Task OutliningWithLessThanZeroWidth_NoPattern()
    {
        var shape = new RectangularPolygon(10, 10, 10, 10);

        await this.CompletesIn(TimeSpan.FromSeconds(1), () =>
        {
            _ = shape.GenerateOutline(-10);
        });
    }

    private async Task CompletesIn(TimeSpan span, Action action)
    {
        var task = Task.Run(action);
        var timeout = Task.Delay(span);

        var completed = await Task.WhenAny(task, timeout);

        Assert.True(task == completed, $"Failed to compelete in {span}");
    }
}
