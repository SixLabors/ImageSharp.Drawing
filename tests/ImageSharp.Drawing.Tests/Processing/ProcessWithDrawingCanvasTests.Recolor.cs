// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    [Theory]
    [WithFile(TestImages.Png.CalliphoraPartial, PixelTypes.Rgba32, "Yellow", "Pink", 0.2f)]
    [WithFile(TestImages.Png.CalliphoraPartial, PixelTypes.Bgra32, "Yellow", "Pink", 0.5f)]
    [WithTestPatternImage(100, 100, PixelTypes.Rgba32, "Red", "Blue", 0.2f)]
    [WithTestPatternImage(100, 100, PixelTypes.Rgba32, "Red", "Blue", 0.6f)]
    public void RecolorImage<TPixel>(TestImageProvider<TPixel> provider, string sourceColorName, string targetColorName, float threshold)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color sourceColor = TestUtils.GetColorByName(sourceColorName);
        Color targetColor = TestUtils.GetColorByName(targetColorName);
        RecolorBrush brush = new(sourceColor, targetColor, threshold);

        FormattableString testInfo = $"{sourceColorName}-{targetColorName}-{threshold}";
        provider.RunValidatingProcessorTest(x => x.ProcessWithCanvas(canvas => canvas.Fill(brush)), testInfo);
    }

    [Theory]
    [WithFile(TestImages.Png.CalliphoraPartial, PixelTypes.Bgra32, "Yellow", "Pink", 0.5f)]
    [WithTestPatternImage(100, 100, PixelTypes.Rgba32, "Red", "Blue", 0.2f)]
    public void RecolorImage_InBox<TPixel>(TestImageProvider<TPixel> provider, string sourceColorName, string targetColorName, float threshold)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color sourceColor = TestUtils.GetColorByName(sourceColorName);
        Color targetColor = TestUtils.GetColorByName(targetColorName);
        RecolorBrush brush = new(sourceColor, targetColor, threshold);

        FormattableString testInfo = $"{sourceColorName}-{targetColorName}-{threshold}";
        provider.RunValidatingProcessorTest(
            x => x.ProcessWithCanvas(canvas =>
            {
                Rectangle bounds = canvas.Bounds;
                Rectangle region = new(0, (bounds.Height / 2) - (bounds.Height / 4), bounds.Width, bounds.Height / 2);
                canvas.Fill(brush, region);
            }),
            testInfo);
    }
}
