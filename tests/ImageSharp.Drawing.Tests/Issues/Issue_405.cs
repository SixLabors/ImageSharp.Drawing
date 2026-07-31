// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

/// <summary>
/// Underlined text rendered with tracking must produce a continuous underline that spans the
/// letter spacing, matching browser behavior, rather than one segment per glyph.
/// See <see href="https://github.com/SixLabors/ImageSharp.Drawing/issues/405"/>.
/// </summary>
public class Issue_405
{
    [Theory]
    [WithSolidFilledImages(800, 200, nameof(Color.White), PixelTypes.Rgba32)]
    public void UnderlineWithTracking_SpansLetterSpacing<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        const string text = "Hello World!";

        RichTextOptions options = new(font)
        {
            Origin = new PointF(20, 80),
            Tracking = 2F,
            TextRuns =
            [
                new RichTextRun
                {
                    Start = 0,
                    End = text.Length,
                    TextDecorations = TextDecorations.Underline
                }
            ]
        };

        provider.RunValidatingProcessorTest(
            c => c.Paint(canvas => canvas.DrawText(options, text, Brushes.Solid(Color.Black), null)),
            comparer: ImageComparer.TolerantPercentage(0.002f));
    }
}
