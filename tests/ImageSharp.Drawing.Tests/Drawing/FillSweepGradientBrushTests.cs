// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

[GroupOutput("Drawing/GradientBrushes")]
public class FillSweepGradientBrushTests
{
    private static readonly ImageComparer TolerantComparer = ImageComparer.TolerantPercentage(0.01f);

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0f, 360f)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 90f, 450f)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 180f, 540f)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 270f, 630f)]
    public void SweepGradientBrush_RendersFullSweep_Every90Degrees<TPixel>(TestImageProvider<TPixel> provider, float start, float end)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            TolerantComparer,
            image =>
            {
                Color red = Color.Red;
                Color green = Color.Green;
                Color blue = Color.Blue;
                Color yellow = Color.Yellow;

                SweepGradientBrush brush = new(
                    new Point(100, 100),
                    start,
                    end,
                    GradientRepetitionMode.None,
                    new ColorStop(0, red),
                    new ColorStop(0.25F, yellow),
                    new ColorStop(0.5F, green),
                    new ColorStop(0.75F, blue),
                    new ColorStop(1, red));

                image.Mutate(x => x.Fill(brush));
            },
            $"start({start},end{end})",
            false,
            false);
}
