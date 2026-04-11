// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_37
{
    [Fact]
    public void CanRenderLargeFont()
    {
        if (!TestEnvironment.IsWindows)
        {
            return;
        }

        using (Image<Rgba32> image = new(300, 200))
        {
            string text = "TEST text foiw|\\";

            Fonts.Font font = Fonts.SystemFonts.CreateFont("Arial", 40, Fonts.FontStyle.Regular);
            GraphicsOptions graphicsOptions = new() { Antialias = false };
            DrawingOptions drawingOptions = new() { GraphicsOptions = graphicsOptions };
            RichTextOptions textOptions = new(font) { Origin = new PointF(50, 50) };
            image.Mutate(
                x => x.ProcessWithCanvas(
                    drawingOptions,
                    canvas =>
                    {
                        canvas.Clear(Brushes.Solid(Color.White));
                        canvas.DrawLine(
                            Pens.Solid(Color.Black, 1),
                            new PointF(0, 50),
                            new PointF(150, 50));
                        canvas.DrawText(textOptions, text, Brushes.Solid(Color.Black), pen: null);
                    }));
        }
    }
}
