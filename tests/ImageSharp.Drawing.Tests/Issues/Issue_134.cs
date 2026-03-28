// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_134
{
    [Theory]
    [WithSolidFilledImages(128, 64, nameof(Color.White), PixelTypes.Rgba32, true)]
    [WithSolidFilledImages(128, 64, nameof(Color.White), PixelTypes.Rgba32, false)]
    public void LowFontSizeRenderOK<TPixel>(TestImageProvider<TPixel> provider, bool antialias)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!TestEnvironment.IsWindows)
        {
            return;
        }

        provider.RunValidatingProcessorTest(
        c =>
        {
            c.SetGraphicsOptions(
                new GraphicsOptions
                {
                    Antialias = antialias,
                    AntialiasThreshold = .33F
                });

            c.ProcessWithCanvas(canvas =>
            {
                Brush brush = Brushes.Solid(Color.Black);
                Font font = SystemFonts.Get("Tahoma").CreateFont(8);
                RichTextOptions options = new(font)
                {
                    WrappingLength = c.GetCurrentSize().Width / 2,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Origin = new PointF(c.GetCurrentSize().Width / 2, c.GetCurrentSize().Height / 2)
                };

                canvas.DrawText(options, "Lorem ipsum dolor sit amet", brush, null);
            });
        },
        testOutputDetails: $"{antialias}",
        appendSourceFileOrDescription: false);
    }
}
