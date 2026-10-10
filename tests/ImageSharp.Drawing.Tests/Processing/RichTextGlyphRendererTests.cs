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
    public void TryCreateBrush_RadialGradient_KeepsThePaintSpaceCirclesAndComposesTheTransforms()
    {
        // The paint's own transform carries the skew and squash of a COLR PaintTransform, and
        // the glyph transform follows it. Neither is baked into the circles.
        Matrix3x2 paintTransform = new(0.99F, 0.03F, -0.02F, 0.75F, 4F, 5F);
        RadialGradientPaint paint = new()
        {
            Center0 = new Vector2(10F, 20F),
            Radius0 = 0F,
            Center1 = new Vector2(10F, 20F),
            Radius1 = 30F,
            Stops = [new GradientStop(0F, new GlyphColor(255, 0, 0, 255)), new GradientStop(1F, new GlyphColor(0, 0, 255, 255))],
            Transform = paintTransform
        };

        Matrix4x4 glyphTransform = Matrix4x4.CreateRotationZ(0.5F) * Matrix4x4.CreateTranslation(100F, 50F, 0F);

        Assert.True(RichTextGlyphRenderer.TryCreateBrush(paint, glyphTransform, out Brush? brush));
        RadialGradientBrush radial = Assert.IsType<RadialGradientBrush>(brush);
        Assert.Equal(new PointF(10F, 20F), radial.Center0);
        Assert.Equal(0F, radial.Radius0);
        Assert.Equal(new PointF(10F, 20F), radial.Center1);
        Assert.Equal(30F, radial.Radius1);
        Assert.Equal(new Matrix4x4(paintTransform) * glyphTransform, radial.GradientTransform);
    }

    [Fact]
    public void TryCreateBrush_SweepGradient_KeepsThePaintSpaceCenterAndComposesTheTransforms()
    {
        Matrix3x2 paintTransform = new(0.5F, 0.86F, -0.86F, 0.5F, 7F, 9F);
        SweepGradientPaint paint = new()
        {
            Center = new Vector2(15F, 25F),
            StartAngle = 30F,
            EndAngle = 300F,
            Stops = [new GradientStop(0F, new GlyphColor(255, 0, 0, 255)), new GradientStop(1F, new GlyphColor(0, 0, 255, 255))],
            Transform = paintTransform
        };

        Matrix4x4 glyphTransform = Matrix4x4.CreateScale(2F, 1F, 1F) * Matrix4x4.CreateTranslation(100F, 50F, 0F);

        Assert.True(RichTextGlyphRenderer.TryCreateBrush(paint, glyphTransform, out Brush? brush));
        SweepGradientBrush sweep = Assert.IsType<SweepGradientBrush>(brush);
        Assert.Equal(new PointF(15F, 25F), sweep.Center);
        Assert.Equal(30F, sweep.StartAngleDegrees);
        Assert.Equal(300F, sweep.EndAngleDegrees);
        Assert.Equal(new Matrix4x4(paintTransform) * glyphTransform, sweep.GradientTransform);
    }

    [Fact]
    public void TryCreateBrush_LinearGradient_KeepsThePaintSpacePointsAndComposesTheTransforms()
    {
        Matrix3x2 paintTransform = new(1F, 0F, 0.5F, 1F, 3F, 4F);
        LinearGradientPaint paint = new()
        {
            P0 = new Vector2(0F, 0F),
            P1 = new Vector2(100F, 0F),
            Stops = [new GradientStop(0F, new GlyphColor(255, 0, 0, 255)), new GradientStop(1F, new GlyphColor(0, 0, 255, 255))],
            Transform = paintTransform
        };

        Matrix4x4 glyphTransform = Matrix4x4.CreateTranslation(100F, 50F, 0F);

        Assert.True(RichTextGlyphRenderer.TryCreateBrush(paint, glyphTransform, out Brush? brush));
        LinearGradientBrush linear = Assert.IsType<LinearGradientBrush>(brush);
        Assert.Equal(new PointF(0F, 0F), linear.StartPoint);
        Assert.Equal(new PointF(100F, 0F), linear.EndPoint);
        Assert.Equal(new Matrix4x4(paintTransform) * glyphTransform, linear.GradientTransform);
    }

    /// <summary>
    /// Verifies moved text retains cached outline identity and updates its destination.
    /// </summary>
    [Fact]
    public void MovedText_CacheHitReusesOutline()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        DrawingTextCache cache = new();
        using RichTextGlyphRenderer first = new(new DrawingOptions(), null, null, Brushes.Solid(Color.Red), cache);
        TextRenderer.RenderTo(first, "Hello World", new RichTextOptions(font) { Origin = new Vector2(11, 8) });
        using RichTextGlyphRenderer cached = new(new DrawingOptions(), null, null, Brushes.Solid(Color.Red), cache);
        TextRenderer.RenderTo(cached, "Hello World", new RichTextOptions(font) { Origin = new Vector2(8, 8) });

        Assert.NotEmpty(first.DrawingOperations);
        Assert.Equal(first.DrawingOperations.Count, cached.DrawingOperations.Count);
        for (int i = 0; i < first.DrawingOperations.Count; i++)
        {
            DrawingOperation expected = first.DrawingOperations[i];
            DrawingOperation actual = cached.DrawingOperations[i];

            // Moving text must reuse the vector outline while moving its destination.
            // Rebuilding an outline at the new origin is not the reference for a cache hit.
            Assert.Same(expected.Path, actual.Path);
            Assert.Equal(expected.RenderLocation.X - 3, actual.RenderLocation.X);
            Assert.Equal(expected.RenderLocation.Y, actual.RenderLocation.Y);
        }
    }

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

        using RichTextGlyphRenderer renderer = new(
            new DrawingOptions(),
            path: null,
            pen: null,
            brush: Brushes.Solid(Color.Black),
            new DrawingTextCache());

        List<DrawingOperation> operations = renderer.DrawingOperations;

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
        using RichTextGlyphRenderer freshRenderer = new(
            new DrawingOptions(),
            path: null,
            pen: null,
            brush: Brushes.Solid(Color.Black),
            cache);

        List<DrawingOperation> fresh = freshRenderer.DrawingOperations;
        TextRenderer.RenderTo(freshRenderer, 2629, glyphOptions);

        using RichTextGlyphRenderer cachedRenderer = new(
            new DrawingOptions(),
            path: null,
            pen: null,
            brush: Brushes.Solid(Color.Black),
            cache);

        List<DrawingOperation> cached = cachedRenderer.DrawingOperations;
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

        using RichTextGlyphRenderer renderer = new(
            new DrawingOptions(),
            path: null,
            pen: null,
            brush: Brushes.Solid(Color.Black),
            new DrawingTextCache());

        List<DrawingOperation> operations = renderer.DrawingOperations;

        TextRenderer.RenderTo(renderer, text, options);

        // Dispose clears the leased operation list, so count before leaving scope.
        return operations.Count;
    }

    private static DrawingOperation RenderSingleGlyph(Font font, PointF origin, DrawingTextCache cache)
    {
        RichTextOptions options = new(font)
        {
            HintingMode = HintingMode.Full,
            Origin = origin,
        };

        using RichTextGlyphRenderer renderer = new(
            new DrawingOptions(),
            path: null,
            pen: null,
            brush: Brushes.Solid(Color.Black),
            cache);

        List<DrawingOperation> operations = renderer.DrawingOperations;

        TextRenderer.RenderTo(renderer, "H", options);

        // Return the value copy before disposing the renderer, which clears the leased list.
        return Assert.Single(operations);
    }
}
