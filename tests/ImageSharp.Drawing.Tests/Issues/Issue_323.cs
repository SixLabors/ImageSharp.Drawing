// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_323
{
    [Theory]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 3f)]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 1f)]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 0.3f)]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 0.7f)]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 0.003f)]
    public void DrawPolygonMustDrawoutlineOnly<TPixel>(TestImageProvider<TPixel> provider, float scale)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = Color.RebeccaPurple;
        provider.RunValidatingProcessorTest(
            x => x.DrawPolygon(
                color,
                scale,
                new PointF[] {
                    new(5, 5),
                    new(5, 150),
                    new(190, 150),
                }),
            new { scale });
    }

    [Theory]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 3f)]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 1f)]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 0.3f)]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 0.7f)]
    [WithSolidFilledImages(300, 300, "White", PixelTypes.Rgba32, 0.003f)]
    public void DrawPolygonMustDrawoutlineOnly_Pattern<TPixel>(TestImageProvider<TPixel> provider, float scale)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = Color.RebeccaPurple;
        var pen = Pens.DashDot(color, scale);
        provider.RunValidatingProcessorTest(
                    x => x.DrawPolygon(
                      pen,
                      new PointF[] {
                            new(5, 5),
                            new(5, 150),
                            new(190, 150),
                        }),
                    new { scale });
    }
}
