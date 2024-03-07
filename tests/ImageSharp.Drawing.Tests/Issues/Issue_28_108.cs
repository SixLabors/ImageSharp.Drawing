// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_28_108
{
    [Theory]
    [InlineData(1F)]
    [InlineData(1.5F)]
    [InlineData(2F)]
    [InlineData(3F)]
    public void DrawingLineAtTopShouldDisplay(float stroke)
    {
        using Image<Rgba32> image = new(Configuration.Default, 100, 100, Color.Black.ToPixel<Rgba32>());
        image.Mutate(x => x
                .SetGraphicsOptions(g => g.Antialias = false)
                .DrawLine(
                    Color.Red,
                    stroke,
                    new PointF(0, 0),
                    new PointF(100, 0)));

        IEnumerable<(int X, int Y)> locations = Enumerable.Range(0, 100).Select(i => (x: i, y: 0));
        Assert.All(locations, l => Assert.Equal(Color.Red.ToPixel<Rgba32>(), image[l.X, l.Y]));
    }

    [Theory]
    [InlineData(1F)]
    [InlineData(1.5F)]
    [InlineData(2F)]
    [InlineData(3F)]
    public void DrawingLineAtBottomShouldDisplay(float stroke)
    {
        using Image<Rgba32> image = new(Configuration.Default, 100, 100, Color.Black.ToPixel<Rgba32>());
        image.Mutate(x => x
                .SetGraphicsOptions(g => g.Antialias = false)
                .DrawLine(
                    Color.Red,
                    stroke,
                    new PointF(0, 99),
                    new PointF(100, 99)));

        IEnumerable<(int X, int Y)> locations = Enumerable.Range(0, 100).Select(i => (x: i, y: 99));
        Assert.All(locations, l => Assert.Equal(Color.Red.ToPixel<Rgba32>(), image[l.X, l.Y]));
    }

    [Theory]
    [InlineData(1F)]
    [InlineData(1.5F)]
    [InlineData(2F)]
    [InlineData(3F)]
    public void DrawingLineAtLeftShouldDisplay(float stroke)
    {
        using Image<Rgba32> image = new(Configuration.Default, 100, 100, Color.Black.ToPixel<Rgba32>());
        image.Mutate(x => x
                .SetGraphicsOptions(g => g.Antialias = false)
                .DrawLine(
                    Color.Red,
                    stroke,
                    new PointF(0, 0),
                    new PointF(0, 99)));

        IEnumerable<(int X, int Y)> locations = Enumerable.Range(0, 100).Select(i => (x: 0, y: i));
        Assert.All(locations, l => Assert.Equal(Color.Red.ToPixel<Rgba32>(), image[l.X, l.Y]));
    }

    [Theory]
    [InlineData(1F)]
    [InlineData(1.5F)]
    [InlineData(2F)]
    [InlineData(3F)]
    public void DrawingLineAtRightShouldDisplay(float stroke)
    {
        using Image<Rgba32> image = new(Configuration.Default, 100, 100, Color.Black.ToPixel<Rgba32>());
        image.Mutate(x => x
                .SetGraphicsOptions(g => g.Antialias = false)
                .DrawLine(
                    Color.Red,
                    stroke,
                    new PointF(99, 0),
                    new PointF(99, 99)));

        IEnumerable<(int X, int Y)> locations = Enumerable.Range(0, 100).Select(i => (x: 99, y: i));
        Assert.All(locations, l => Assert.Equal(Color.Red.ToPixel<Rgba32>(), image[l.X, l.Y]));
    }
}
