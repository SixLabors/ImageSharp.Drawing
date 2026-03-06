// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.


using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_367
{
    [Theory]
    [WithSolidFilledImages(512, 72, nameof(Color.White), PixelTypes.Rgba32)]
    public void BrushAndTextAlign<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!TestEnvironment.IsWindows)
        {
            return;
        }

        provider.RunValidatingProcessorTest(
            c => c.ProcessWithCanvas(canvas =>
            {
                Pen pen = Pens.Solid(Color.Green, 1);
                Brush brush = Brushes.Solid(Color.Red);

                Font font = SystemFonts.Get("Arial").CreateFont(64);
                RichTextOptions options = new(font);

                canvas.DrawText(options, "Hello, world!", brush, pen);
            }),
            appendSourceFileOrDescription: false);
    }
}
