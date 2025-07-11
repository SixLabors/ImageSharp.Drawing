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
        RectangularPolygon shape = new(10, 10, 10, 10);

        await this.CompletesIn(TimeSpan.FromSeconds(1), () =>
        {
            _ = shape.GenerateOutline(0, [1, 2]);
        });
    }

    [Fact]
    public async Task OutliningWithZeroWidth_SinglePAttern()
    {
        RectangularPolygon shape = new(10, 10, 10, 10);

        await this.CompletesIn(TimeSpan.FromSeconds(1), () =>
        {
            _ = shape.GenerateOutline(0, [1]);
        });
    }

    [Fact]
    public async Task OutliningWithZeroWidth_NoPattern()
    {
        RectangularPolygon shape = new(10, 10, 10, 10);

        await this.CompletesIn(TimeSpan.FromSeconds(1), () =>
        {
            _ = shape.GenerateOutline(0);
        });
    }

    [Fact]
    public async Task OutliningWithLessThanZeroWidth_MultiplePatterns()
    {
        RectangularPolygon shape = new(10, 10, 10, 10);

        await this.CompletesIn(TimeSpan.FromSeconds(1), () =>
        {
            _ = shape.GenerateOutline(-10, [1, 2]);
        });
    }

    [Fact]
    public async Task OutliningWithLessThanZeroWidth_SinglePAttern()
    {
        RectangularPolygon shape = new(10, 10, 10, 10);

        await this.CompletesIn(TimeSpan.FromSeconds(1), () =>
        {
            _ = shape.GenerateOutline(-10, [1]);
        });
    }

    [Fact]
    public async Task OutliningWithLessThanZeroWidth_NoPattern()
    {
        RectangularPolygon shape = new(10, 10, 10, 10);

        await this.CompletesIn(TimeSpan.FromSeconds(1), () =>
        {
            _ = shape.GenerateOutline(-10);
        });
    }

    private async Task CompletesIn(TimeSpan span, Action action)
    {
        Task task = Task.Run(action);
        Task timeout = Task.Delay(span);

        Task completed = await Task.WhenAny(task, timeout);

        Assert.True(task == completed, $"Failed to compelete in {span}");
    }
}
