// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

[GroupOutput("Drawing")]
public class RecolorImageTests
{
    [Theory]
    [WithFile(TestImages.Png.CalliphoraPartial, PixelTypes.Rgba32, "Yellow", "Pink", 0.2f)]
    [WithFile(TestImages.Png.CalliphoraPartial, PixelTypes.Bgra32, "Yellow", "Pink", 0.5f)]
    [WithTestPatternImage(100, 100, PixelTypes.Rgba32, "Red", "Blue", 0.2f)]
    [WithTestPatternImage(100, 100, PixelTypes.Rgba32, "Red", "Blue", 0.6f)]
    public void Recolor<TPixel>(TestImageProvider<TPixel> provider, string sourceColorName, string targetColorName, float threshold)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color sourceColor = TestUtils.GetColorByName(sourceColorName);
        Color targetColor = TestUtils.GetColorByName(targetColorName);
        var brush = new RecolorBrush(sourceColor, targetColor, threshold);

        FormattableString testInfo = $"{sourceColorName}-{targetColorName}-{threshold}";
        provider.RunValidatingProcessorTest(x => x.Fill(brush), testInfo);
    }

    [Theory]
    [WithFile(TestImages.Png.CalliphoraPartial, PixelTypes.Bgra32, "Yellow", "Pink", 0.5f)]
    [WithTestPatternImage(100, 100, PixelTypes.Rgba32, "Red", "Blue", 0.2f)]
    public void Recolor_InBox<TPixel>(TestImageProvider<TPixel> provider, string sourceColorName, string targetColorName, float threshold)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color sourceColor = TestUtils.GetColorByName(sourceColorName);
        Color targetColor = TestUtils.GetColorByName(targetColorName);
        var brush = new RecolorBrush(sourceColor, targetColor, threshold);

        FormattableString testInfo = $"{sourceColorName}-{targetColorName}-{threshold}";
        provider.RunValidatingProcessorTest(
            x =>
            {
                Size size = x.GetCurrentSize();
                var rectangle = new Rectangle(0, (size.Height / 2) - (size.Height / 4), size.Width, size.Height / 2);
                x.Fill(brush, rectangle);
            },
            testInfo);
    }
}
