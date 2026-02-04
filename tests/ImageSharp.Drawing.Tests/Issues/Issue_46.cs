// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_46
{
    [Fact]
    public void CanRenderCustomFont()
    {
        Font font = CreateFont(TestFonts.IcoMoonEvents, 175);

        RichTextOptions options = new(font)
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        const int imageSize = 300;

        using Image<Rgba32> image = new(imageSize, imageSize);

        string iconText = char.ConvertFromUtf32(int.Parse("e926", NumberStyles.HexNumber));

        FontRectangle rect = TextMeasurer.MeasureSize(iconText, options);

        float textX = ((imageSize - rect.Width) * 0.5F) + rect.Left;
        float textY = ((imageSize - rect.Height) * 0.5F) + (rect.Top * 0.25F);

        image.Mutate(x => x.DrawText(iconText, font, Color.Black, new PointF(textX, textY)));
        image.Save(TestFontUtilities.GetPath("e96.png"));
    }

    private static Font CreateFont(string fontName, int size)
    {
        FontCollection fontCollection = new();
        string fontPath = TestFontUtilities.GetPath(fontName);
        return fontCollection.Add(fontPath).CreateFont(size);
    }
}
