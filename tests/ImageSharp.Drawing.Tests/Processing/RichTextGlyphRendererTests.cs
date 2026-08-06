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

    [Fact]
    public void FullHinting_CacheHitMatchesFreshRasterizationAtFractionalOrigin()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        DrawingTextCache sharedCache = new();

        _ = RenderSingleGlyph(font, new PointF(10.3F, 10.7F), sharedCache);
        Assert.Equal(1, sharedCache.Count);

        DrawingOperation cached = RenderSingleGlyph(font, new PointF(13.8F, 14.1F), sharedCache);
        DrawingOperation fresh = RenderSingleGlyph(font, new PointF(13.8F, 14.1F), new DrawingTextCache());

        // Fonts resolves the final hinted origin before BeginGlyph, so the ordinary bounds
        // offset path must reproduce both components of the fresh operation's device position.
        Assert.Equal(fresh.RenderLocation, cached.RenderLocation);
        Assert.Equal(fresh.SubPixelOffset, cached.SubPixelOffset);
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

    private static DrawingOperation RenderSingleGlyph(Font font, PointF origin, DrawingTextCache cache)
    {
        RichTextOptions options = new(font)
        {
            HintingMode = HintingMode.Full,
            Origin = origin,
        };

        List<DrawingOperation> operations = [];
        using RichTextGlyphRenderer renderer = new(
            new DrawingOptions(),
            path: null,
            pen: null,
            brush: Brushes.Solid(Color.Black),
            cache,
            operations);

        TextRenderer.RenderTo(renderer, "H", options);

        // Return the value copy before disposing the renderer, which clears the caller-owned list.
        return Assert.Single(operations);
    }
}
