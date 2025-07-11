// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;

[GroupOutput("Drawing/GradientBrushes")]
public class FillRadialGradientBrushTests
{
    public static ImageComparer TolerantComparer = ImageComparer.TolerantPercentage(0.01f);

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32)]
    public void WithEqualColorsReturnsUnicolorImage<TPixel>(
        TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (Image<TPixel> image = provider.GetImage())
        {
            Color red = Color.Red;

            RadialGradientBrush unicolorRadialGradientBrush =
                new(
                    new Point(0, 0),
                    100,
                    GradientRepetitionMode.None,
                    new ColorStop(0, red),
                    new ColorStop(1, red));

            image.Mutate(x => x.Fill(unicolorRadialGradientBrush));

            image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);

            // no need for reference image in this test:
            image.ComparePixelBufferTo(red);
        }
    }

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 100, 100)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0, 0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 100, 0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0, 100)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, -40, 100)]
    public void WithDifferentCentersReturnsImage<TPixel>(
        TestImageProvider<TPixel> provider,
        int centerX,
        int centerY)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        provider.VerifyOperation(
            TolerantComparer,
            image =>
                {
                    RadialGradientBrush brush = new(
                        new Point(centerX, centerY),
                        image.Width / 2f,
                        GradientRepetitionMode.None,
                        new ColorStop(0, Color.Red),
                        new ColorStop(1, Color.Yellow));

                    image.Mutate(x => x.Fill(brush));
                },
            $"center({centerX},{centerY})",
            false,
            false);
    }
}
