// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_134
{
    // The issue renders 8pt Tahoma at 100 dpi on a 128x64 white panel, wrapped at the image
    // width and centred both ways about (0, 31) under the era's API, which centred every
    // line within the wrap box anchored at the origin. The
    // era measured the text wider than the panel and broke before "amet"; corrected metrics
    // fit the whole string in the panel, so the wrap length here is the one that reproduces
    // the issue's two line output rather than the literal value that no longer would. The
    // modern API centres on the origin, so the equivalent origin is the box centre (64, 31).
    // Full hinting grid fits the outlines and, without antialiasing, the renderer samples
    // them at pixel centres, so the panel matches the classic bi-level clarity the issue
    // compares against.
    [Theory]
    [WithSolidFilledImages(128, 64, nameof(Color.White), PixelTypes.Rgb24, HintingMode.None, true)]
    [WithSolidFilledImages(128, 64, nameof(Color.White), PixelTypes.Rgb24, HintingMode.Full, true)]
    [WithSolidFilledImages(128, 64, nameof(Color.White), PixelTypes.Rgb24, HintingMode.Full, false)]
    public void LowFontSizeRenderOK<TPixel>(TestImageProvider<TPixel> provider, HintingMode hintingMode, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!TestEnvironment.IsWindows)
        {
            return;
        }

        provider.RunValidatingProcessorTest(
        c =>
        {
            c.SetGraphicsOptions(new GraphicsOptions { Antialias = antialias });

            c.Paint(canvas =>
            {
                Brush brush = Brushes.Solid(Color.Black);
                Font font = SystemFonts.Get("Tahoma").CreateFont(8);
                RichTextOptions options = new(font)
                {
                    Dpi = 100,
                    HintingMode = hintingMode,
                    WrappingLength = 110,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Origin = new PointF(c.GetCurrentSize().Width / 2, 31)
                };

                canvas.DrawText(options, "Lorem ipsum dolor sit amet", brush, null);
            });
        },
        testOutputDetails: $"{hintingMode}_{(antialias ? "Antialiased" : "Aliased")}",
        appendSourceFileOrDescription: false);
    }
}
