// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_462
{
    [Theory]
    [WithSolidFilledImages(492, 360, nameof(Color.White), PixelTypes.Rgba32, ColorFontSupport.ColrV1)]
    [WithSolidFilledImages(492, 360, nameof(Color.White), PixelTypes.Rgba32, ColorFontSupport.Svg)]
    public void CanDrawEmojiFont<TPixel>(TestImageProvider<TPixel> provider, ColorFontSupport support)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Font font = CreateFont(TestFonts.NotoColorEmojiRegular, 100);
        Font fallback = CreateFont(TestFonts.OpenSans, 100);
        const string text = "a😨 b😅\r\nc🥲 d🤩";

        RichTextOptions options = new(font)
        {
            ColorFontSupport = support,
            LineSpacing = 1.8F,
            FallbackFontFamilies = new[] { fallback.Family },
            TextRuns = new List<RichTextRun>
            {
                new()
                {
                    Start = 0,
                    End = text.GetGraphemeCount(),
                    TextDecorations = TextDecorations.Strikeout | TextDecorations.Underline | TextDecorations.Overline,
                    StrikeoutPen = new SolidPen(Color.Green, 11.3334F),
                    UnderlinePen = new SolidPen(Color.Blue, 15.5555F),
                    OverlinePen = new SolidPen(Color.Purple, 13.7777F)
                }
            }
        };

        provider.RunValidatingProcessorTest(
            c => c.DrawText(options, text, Brushes.Solid(Color.Black)),
            testOutputDetails: $"{support}-draw",
            comparer: ImageComparer.TolerantPercentage(0.002f));

        provider.RunValidatingProcessorTest(
            c =>
            {
                Pen pen = Pens.Solid(Color.Black, 2);
                c.Fill(pen.StrokeFill, pen, TextBuilder.GenerateGlyphs(text, options));
            },
            testOutputDetails: $"{support}-fill",
            comparer: ImageComparer.TolerantPercentage(0.002f));
    }

    private static Font CreateFont(string fontName, float size)
        => TestFontUtilities.GetFont(fontName, size);
}
