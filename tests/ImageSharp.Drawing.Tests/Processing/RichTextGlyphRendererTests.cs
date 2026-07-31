// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class RichTextGlyphRendererTests
{
    [Fact]
    public void SetDecoration_ContiguousRun_EmitsSingleDecorationOperation()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        const string text = "lllll";

        int plainCount = CountOperations(font, text, runs: null);
        int underlinedCount = CountOperations(
            font,
            text,
            runs:
            [
                new RichTextRun { Start = 0, End = text.Length, TextDecorations = TextDecorations.Underline }
            ]);

        // Contiguous cells styled by one run merge into a single decoration operation;
        // per-glyph emission would add one operation per glyph.
        Assert.Equal(1, underlinedCount - plainCount);
    }

    [Fact]
    public void SetDecoration_RunBoundary_FlushesSegment()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        const string text = "llllll";

        int plainCount = CountOperations(font, text, runs: null);
        int underlinedCount = CountOperations(
            font,
            text,
            runs:
            [
                new RichTextRun { Start = 0, End = 3, TextDecorations = TextDecorations.Underline, UnderlinePen = Pens.Solid(Color.Red, 2) },
                new RichTextRun { Start = 3, End = text.Length, TextDecorations = TextDecorations.Underline, UnderlinePen = Pens.Solid(Color.Blue, 2) }
            ]);

        // A run boundary is a styling boundary: cells accumulate per run and flush where the
        // pen changes, so each run contributes exactly one decoration operation.
        Assert.Equal(2, underlinedCount - plainCount);
    }

    private static int CountOperations(Font font, string text, List<RichTextRun>? runs)
    {
        RichTextOptions options = new(font);
        if (runs is not null)
        {
            options.TextRuns = [.. runs];
        }

        List<DrawingOperation> operations = [];
        using RichTextGlyphRenderer renderer = new(
            new DrawingOptions(),
            path: null,
            pen: null,
            brush: Brushes.Solid(Color.Black),
            new DrawingTextCache(),
            operations);

        TextRenderer.RenderTo(renderer, text, options);

        // Dispose clears the caller-owned operation list, so count before leaving scope.
        return operations.Count;
    }
}
