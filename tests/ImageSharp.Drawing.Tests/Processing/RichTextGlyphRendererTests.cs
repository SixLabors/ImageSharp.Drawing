// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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

    /// <summary>
    /// Verifies that nested COLR v1 composite groups are lowered to isolated drawing operations.
    /// </summary>
    [Fact]
    public void RenderGlyph_NestedColrV1Composite_EmitsIsolatedGroups()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.NotoColorEmojiRegular, 128);
        RichGlyphOptions glyphOptions = new()
        {
            Font = font,
            ColorFontSupport = ColorFontSupport.ColrV1
        };

        List<DrawingOperation> operations = [];
        using RichTextGlyphRenderer renderer = new(
            new DrawingOptions(),
            path: null,
            pen: null,
            brush: Brushes.Solid(Color.Black),
            new DrawingTextCache(),
            operations);

        // This glyph is a SoftLight composite whose source is a nested SrcIn composite, and
        // the inner source is a linear gradient with no outline. Each composite lowers to one
        // isolating group holding a backdrop group and a source group, and the source group
        // carries the composite mode. The gradient arrives as an ordinary layer whose figure
        // is the clip bounds or the glyph bounds.
        TextRenderer.RenderTo(renderer, 2629, glyphOptions);

        Assert.Equal(
        [
            DrawingOperationKind.BeginGroup,
            DrawingOperationKind.BeginGroup,
            DrawingOperationKind.Fill,
            DrawingOperationKind.Fill,
            DrawingOperationKind.Fill,
            DrawingOperationKind.EndGroup,
            DrawingOperationKind.BeginGroup,
            DrawingOperationKind.BeginGroup,
            DrawingOperationKind.BeginGroup,
            DrawingOperationKind.Fill,
            DrawingOperationKind.Fill,
            DrawingOperationKind.Fill,
            DrawingOperationKind.EndGroup,
            DrawingOperationKind.BeginGroup,
            DrawingOperationKind.Fill,
            DrawingOperationKind.EndGroup,
            DrawingOperationKind.EndGroup,
            DrawingOperationKind.EndGroup,
            DrawingOperationKind.EndGroup
        ],
        operations.Select(x => x.Kind));

        Assert.True(operations[0].ApplyDrawingOptions);
        Assert.False(operations[1].ApplyDrawingOptions);
        Assert.Equal(PixelColorBlendingMode.SoftLight, operations[6].PixelColorBlendingMode);
        Assert.Equal(PixelAlphaCompositionMode.SrcOver, operations[6].PixelAlphaCompositionMode);
        Assert.Equal(PixelColorBlendingMode.Normal, operations[13].PixelColorBlendingMode);
        Assert.Equal(PixelAlphaCompositionMode.SrcIn, operations[13].PixelAlphaCompositionMode);
        Assert.IsType<LinearGradientBrush>(operations[14].Brush);
        Assert.False(operations[14].Path!.Bounds.IsEmpty);
    }

    /// <summary>
    /// Verifies that caller opacity is applied to the completed COLR v1 composite rather than to each paint leaf.
    /// </summary>
    [Fact]
    public void DrawGlyph_NestedColrV1Composite_AppliesCallerOpacityOnce()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.NotoColorEmojiRegular, 128);
        RichGlyphOptions glyphOptions = new()
        {
            Font = font,
            Origin = new Vector2(16, 16),
            ColorFontSupport = ColorFontSupport.ColrV1
        };

        using Image<Rgba32> fullOpacity = new(192, 192);
        fullOpacity.Mutate(context => context.Paint(
            new DrawingOptions(),
            canvas => canvas.DrawText(2629, glyphOptions, Brushes.Solid(Color.Black), pen: null)));

        DrawingOptions halfOpacityOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { BlendPercentage = .5F }
        };

        using Image<Rgba32> halfOpacity = new(192, 192);
        halfOpacity.Mutate(context => context.Paint(
            halfOpacityOptions,
            canvas => canvas.DrawText(2629, glyphOptions, Brushes.Solid(Color.Black), pen: null)));

        Rgba32 fullPixel = fullOpacity[96, 96];
        Rgba32 halfPixel = halfOpacity[96, 96];

        Assert.NotEqual(0, fullPixel.A);
        Assert.InRange(Math.Abs(halfPixel.R - fullPixel.R), 0, 1);
        Assert.InRange(Math.Abs(halfPixel.G - fullPixel.G), 0, 1);
        Assert.InRange(Math.Abs(halfPixel.B - fullPixel.B), 0, 1);
        Assert.InRange(Math.Abs(halfPixel.A - (fullPixel.A * .5F)), 0F, 1F);
    }

    /// <summary>
    /// Verifies that a layered COLR v1 cache hit replays the full operation sequence with no
    /// callbacks: kinds, positions, bounds, and modes match the fresh render, and outlined
    /// layer paths are the identical cached instances rather than rebuilt geometry.
    /// </summary>
    [Fact]
    public void RenderGlyph_LayeredColrV1CacheHit_ReplaysFreshOperationSequence()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.NotoColorEmojiRegular, 128);
        RichGlyphOptions glyphOptions = new()
        {
            Font = font,
            Origin = new Vector2(16.5F, 16.25F),
            ColorFontSupport = ColorFontSupport.ColrV1
        };

        DrawingTextCache cache = new();
        List<DrawingOperation> fresh = [];
        using RichTextGlyphRenderer freshRenderer = new(
            new DrawingOptions(),
            path: null,
            pen: null,
            brush: Brushes.Solid(Color.Black),
            cache,
            fresh);
        TextRenderer.RenderTo(freshRenderer, 2629, glyphOptions);

        List<DrawingOperation> cached = [];
        using RichTextGlyphRenderer cachedRenderer = new(
            new DrawingOptions(),
            path: null,
            pen: null,
            brush: Brushes.Solid(Color.Black),
            cache,
            cached);
        TextRenderer.RenderTo(cachedRenderer, 2629, glyphOptions);

        Assert.NotEmpty(fresh);
        Assert.Equal(fresh.Count, cached.Count);
        for (int i = 0; i < fresh.Count; i++)
        {
            DrawingOperation expected = fresh[i];
            DrawingOperation actual = cached[i];
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.RenderLocation, actual.RenderLocation);
            Assert.Equal(expected.SubPixelOffset, actual.SubPixelOffset);
            Assert.Equal(expected.CompositeBounds, actual.CompositeBounds);
            Assert.Equal(expected.ApplyDrawingOptions, actual.ApplyDrawingOptions);
            Assert.Equal(expected.GlyphClip, actual.GlyphClip);
            Assert.Equal(expected.IntersectionRule, actual.IntersectionRule);
            Assert.Equal(expected.PixelAlphaCompositionMode, actual.PixelAlphaCompositionMode);
            Assert.Equal(expected.PixelColorBlendingMode, actual.PixelColorBlendingMode);
            Assert.Equal(expected.Brush?.GetType(), actual.Brush?.GetType());

            if (expected.HasGlyphKey && expected.Path is not null)
            {
                // Outlined layers must reuse the identical cached path instance.
                Assert.Same(expected.Path, actual.Path);
            }
        }
    }

    /// <summary>
    /// Verifies that a layered COLR v1 cache hit renders pixel-identical output. The same
    /// glyph draws twice through one shared canvas cache, and the replayed second glyph must
    /// match a fresh render at that position exactly.
    /// </summary>
    [Fact]
    public void DrawGlyph_LayeredColrV1CacheHit_MatchesFreshOutput()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.NotoColorEmojiRegular, 128);
        RichGlyphOptions firstOptions = new()
        {
            Font = font,
            Origin = new Vector2(16.5F, 16.25F),
            ColorFontSupport = ColorFontSupport.ColrV1
        };

        // An integer-only origin delta keeps the replay's anchor math bit-identical to a
        // fresh render, so the replayed glyph must match to the pixel.
        RichGlyphOptions secondOptions = new()
        {
            Font = font,
            Origin = new Vector2(16.5F, 200.25F),
            ColorFontSupport = ColorFontSupport.ColrV1
        };

        using Image<Rgba32> doubleDraw = new(192, 384);
        doubleDraw.Mutate(context => context.Paint(new DrawingOptions(), canvas =>
        {
            canvas.DrawText(2629, firstOptions, Brushes.Solid(Color.Black), pen: null);
            canvas.DrawText(2629, secondOptions, Brushes.Solid(Color.Black), pen: null);
        }));

        using Image<Rgba32> freshDraw = new(192, 384);
        freshDraw.Mutate(context => context.Paint(
            new DrawingOptions(),
            canvas => canvas.DrawText(2629, secondOptions, Brushes.Solid(Color.Black), pen: null)));

        using Image<Rgba32> actualRegion = doubleDraw.Clone(context => context.Crop(new Rectangle(0, 184, 192, 200)));
        using Image<Rgba32> expectedRegion = freshDraw.Clone(context => context.Crop(new Rectangle(0, 184, 192, 200)));
        ImageComparer.Exact.VerifySimilarity(expectedRegion, actualRegion);
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
