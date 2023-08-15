// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_270
{
    [Fact]
    public void DoesNotThrowArgumentOutOfRangeException()
    {
        if (!TestEnvironment.IsWindows)
        {
            return;
        }

        const int sourceImageWidth = 256;
        const int sourceImageHeight = 256;
        const int targetImageWidth = 350;
        const int targetImageHeight = 350;
        const float minimumCrashingFontSize = 47;
        const string text = "Hello, World!";

        Font font = SystemFonts.CreateFont("Arial", minimumCrashingFontSize);
        Pen pen = Pens.Solid(Color.Black, 1);

        using Image<Rgba32> targetImage = new(targetImageWidth, targetImageHeight, Color.Wheat);
        using Image<Rgba32> imageBrushImage = new(sourceImageWidth, sourceImageHeight, Color.Black);
        ImageBrush imageBrush = new(imageBrushImage);

        targetImage.Mutate(x => x.DrawText(CreateTextOptions(font, targetImageWidth), text, imageBrush, pen));
    }

    private static RichTextOptions CreateTextOptions(Font font, int wrappingLength)
        => new(font)
        {
            Origin = new Vector2(175, 175),
            TextAlignment = TextAlignment.Center,
            LayoutMode = LayoutMode.HorizontalTopBottom,
            WrappingLength = wrappingLength,
            WordBreaking = WordBreaking.Standard,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            KerningMode = KerningMode.None,
        };
}
