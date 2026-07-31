// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_28_108
{
    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 1F)]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 1.5F)]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 2F)]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 3F)]
    public void DrawingLineAtTopShouldDisplay<TPixel>(TestImageProvider<TPixel> provider, float stroke)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        DrawingOptions options = CreateAliasedDrawingOptions();
        image.Mutate(x => x.Paint(
            options,
            canvas => canvas.DrawLine(
                Pens.Solid(Color.Red, stroke),
                new PointF(0, 0),
                new PointF(100, 0))));

        image.DebugSave(provider, $"stroke-{stroke}", appendSourceFileOrDescription: false);

        IEnumerable<(int X, int Y)> locations = Enumerable.Range(0, 100).Select(i => (x: i, y: 0));
        Assert.All(locations, l => Assert.Equal(Color.Red.ToPixel<TPixel>(), image[l.X, l.Y]));
    }

    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 1F)]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 1.5F)]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 2F)]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 3F)]
    public void DrawingLineAtBottomShouldDisplay<TPixel>(TestImageProvider<TPixel> provider, float stroke)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        DrawingOptions options = CreateAliasedDrawingOptions();
        image.Mutate(x => x.Paint(
            options,
            canvas => canvas.DrawLine(
                Pens.Solid(Color.Red, stroke),
                new PointF(0, 99),
                new PointF(100, 99))));

        image.DebugSave(provider, $"stroke-{stroke}", appendSourceFileOrDescription: false);

        IEnumerable<(int X, int Y)> locations = Enumerable.Range(0, 100).Select(i => (x: i, y: 99));
        Assert.All(locations, l => Assert.Equal(Color.Red.ToPixel<TPixel>(), image[l.X, l.Y]));
    }

    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 1F)]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 1.5F)]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 2F)]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 3F)]
    public void DrawingLineAtLeftShouldDisplay<TPixel>(TestImageProvider<TPixel> provider, float stroke)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        DrawingOptions options = CreateAliasedDrawingOptions();
        image.Mutate(x => x.Paint(
            options,
            canvas => canvas.DrawLine(
                Pens.Solid(Color.Red, stroke),
                new PointF(0, 0),
                new PointF(0, 100))));

        image.DebugSave(provider, $"stroke-{stroke}", appendSourceFileOrDescription: false);

        IEnumerable<(int X, int Y)> locations = Enumerable.Range(0, 100).Select(i => (x: 0, y: i));
        Assert.All(locations, l => Assert.Equal(Color.Red.ToPixel<TPixel>(), image[l.X, l.Y]));
    }

    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 1F)]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 1.5F)]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 2F)]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32, 3F)]
    public void DrawingLineAtRightShouldDisplay<TPixel>(TestImageProvider<TPixel> provider, float stroke)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        DrawingOptions options = CreateAliasedDrawingOptions();
        image.Mutate(x => x.Paint(
            options,
            canvas => canvas.DrawLine(
                Pens.Solid(Color.Red, stroke),
                new PointF(99, 0),
                new PointF(99, 100))));

        image.DebugSave(provider, $"stroke-{stroke}", appendSourceFileOrDescription: false);

        IEnumerable<(int X, int Y)> locations = Enumerable.Range(0, 100).Select(i => (x: 99, y: i));
        Assert.All(locations, l => Assert.Equal(Color.Red.ToPixel<TPixel>(), image[l.X, l.Y]));
    }

    private static DrawingOptions CreateAliasedDrawingOptions() =>
        new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false
            }
        };
}
