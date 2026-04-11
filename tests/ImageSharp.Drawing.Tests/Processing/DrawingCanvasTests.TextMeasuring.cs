// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Theory]
    [WithBlankImage(600, 400, PixelTypes.Rgba32)]
    public void TextMeasuring_RenderedMetrics_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions());

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 36);
        const string text = "Sphinx of black quartz,\njudge my vow.";

        RichTextOptions textOptions = new(font)
        {
            Origin = new PointF(60, 60),
            LineSpacing = 1.8F
        };

        canvas.Clear(Brushes.Solid(Color.White));

        PointF origin = textOptions.Origin;

        // Line metrics: colored bands with ascender/baseline/descender guides.
        int lineCount = canvas.CountTextLines(textOptions, text);
        LineMetrics[] lineMetrics = canvas.GetTextLineMetrics(textOptions, text);
        Assert.Equal(lineCount, lineMetrics.Length);

        float lineOriginY = origin.Y;
        Color[] bandColors =
        [
            Color.LightCoral.WithAlpha(0.4F),
            Color.Khaki.WithAlpha(0.6F),
            Color.LightGreen.WithAlpha(0.4F),
        ];

        for (int i = 0; i < lineMetrics.Length; i++)
        {
            LineMetrics metrics = lineMetrics[i];
            float startX = origin.X + metrics.Start;
            float endX = startX + metrics.Extent;

            canvas.Fill(
                Brushes.Solid(bandColors[i % bandColors.Length]),
                new RectangularPolygon(startX, lineOriginY, endX - startX, metrics.LineHeight));

            canvas.DrawLine(
                Pens.Solid(Color.Teal.WithAlpha(0.9F), 1.5F),
                new PointF(startX, lineOriginY + metrics.Ascender),
                new PointF(endX, lineOriginY + metrics.Ascender));

            canvas.DrawLine(
                Pens.Solid(Color.Crimson.WithAlpha(0.9F), 1.5F),
                new PointF(startX, lineOriginY + metrics.Baseline),
                new PointF(endX, lineOriginY + metrics.Baseline));

            canvas.DrawLine(
                Pens.Solid(Color.DarkOrange.WithAlpha(0.9F), 1.5F),
                new PointF(startX, lineOriginY + metrics.Descender),
                new PointF(endX, lineOriginY + metrics.Descender));

            lineOriginY += metrics.LineHeight;
        }

        // Character renderable bounds: outlined rectangles positioned at each glyph.
        if (canvas.TryMeasureCharacterRenderableBounds(textOptions, text, out ReadOnlySpan<GlyphBounds> charRenderableBounds))
        {
            Color[] renderableColors =
            [
                Color.Black,
                Color.Black
            ];

            for (int i = 0; i < charRenderableBounds.Length; i++)
            {
                FontRectangle rb = charRenderableBounds[i].Bounds;
                canvas.Draw(
                    Pens.Solid(renderableColors[i % renderableColors.Length], 1),
                    new RectangularPolygon(rb.X, rb.Y, rb.Width, rb.Height));
            }
        }

        // Character bounds: alternating filled rectangles behind the glyphs.
        if (canvas.TryMeasureCharacterBounds(textOptions, text, out ReadOnlySpan<GlyphBounds> charBounds))
        {
            Color[] charColors =
            [
                Color.Gold.WithAlpha(0.5F),
                Color.MediumPurple.WithAlpha(0.5F),
            ];

            for (int i = 0; i < charBounds.Length; i++)
            {
                FontRectangle b = charBounds[i].Bounds;
                canvas.Fill(
                    Brushes.Solid(charColors[i % charColors.Length]),
                    new RectangularPolygon(b.X, b.Y, b.Width, b.Height));
            }
        }

        // Render the text.
        canvas.DrawText(textOptions, text, Brushes.Solid(Color.Black), pen: null);

        // Advance rectangle (green outline).
        RectangleF advance = canvas.MeasureTextAdvance(textOptions, text);
        canvas.Draw(
            Pens.Solid(Color.SeaGreen, 2),
            new RectangularPolygon(origin.X + advance.X, origin.Y + advance.Y, advance.Width, advance.Height));

        // Bounds rectangle (dodger blue outline).
        RectangleF bounds = canvas.MeasureTextBounds(textOptions, text);
        canvas.Draw(
            Pens.Solid(Color.DodgerBlue, 2),
            new RectangularPolygon(bounds.X, bounds.Y, bounds.Width, bounds.Height));

        // Renderable bounds rectangle (black outline).
        RectangleF renderableBounds = canvas.MeasureTextRenderableBounds(textOptions, text);
        canvas.Draw(
            Pens.Solid(Color.Black, 2),
            new RectangularPolygon(renderableBounds.X, renderableBounds.Y, renderableBounds.Width, renderableBounds.Height));

        // Origin crosshair.
        canvas.DrawLine(Pens.Solid(Color.Gray, 1), new PointF(origin.X - 12, origin.Y), new PointF(origin.X + 12, origin.Y));
        canvas.DrawLine(Pens.Solid(Color.Gray, 1), new PointF(origin.X, origin.Y - 12), new PointF(origin.X, origin.Y + 12));

        // Key.
        Font keyFont = TestFontUtilities.GetFont(TestFonts.OpenSans, 13);
        float keyX = 16;
        float keyY = 280;
        const float swatchW = 24;
        const float swatchH = 12;
        const float rowHeight = 20;
        const float labelOffset = swatchW + 6;

        (string Label, Color Color1, Color? Color2, bool IsFill)[] keyEntries =
        [
            ("Advance", Color.SeaGreen, null, false),
            ("Bounds", Color.DodgerBlue, null, false),
            ("Renderable Bounds", Color.Black, null, false),
            ("Ascender", Color.Teal.WithAlpha(0.9F), null, true),
            ("Baseline", Color.Crimson.WithAlpha(0.9F), null, true),
            ("Descender", Color.DarkOrange.WithAlpha(0.9F), null, true),
            ("Char Bounds", Color.Gold.WithAlpha(0.5F), Color.MediumPurple.WithAlpha(0.5F), true),
            ("Char Renderable Bounds", Color.Black, null, false),
            ("Line Band", Color.LightCoral.WithAlpha(0.4F), Color.Khaki.WithAlpha(0.6F), true),
            ("Origin", Color.Gray, null, false),
        ];

        for (int i = 0; i < keyEntries.Length; i++)
        {
            float col = i < 5 ? 0 : 300;
            float row = i < 5 ? i : i - 5;
            float x = keyX + col;
            float y = keyY + (row * rowHeight);
            float halfW = swatchW / 2F;

            if (keyEntries[i].IsFill)
            {
                if (keyEntries[i].Color2 is Color c2)
                {
                    canvas.Fill(
                        Brushes.Solid(keyEntries[i].Color1),
                        new RectangularPolygon(x, y, halfW, swatchH));
                    canvas.Fill(
                        Brushes.Solid(c2),
                        new RectangularPolygon(x + halfW, y, halfW, swatchH));
                }
                else
                {
                    canvas.Fill(
                        Brushes.Solid(keyEntries[i].Color1),
                        new RectangularPolygon(x, y, swatchW, swatchH));
                }
            }
            else
            {
                canvas.Draw(
                    Pens.Solid(keyEntries[i].Color1, 2),
                    new RectangularPolygon(x, y, swatchW, swatchH));
            }

            RichTextOptions keyTextOptions = new(keyFont) { Origin = new PointF(x + labelOffset, y - 1) };
            canvas.DrawText(keyTextOptions, keyEntries[i].Label, Brushes.Solid(Color.Black), pen: null);
        }

        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Fact]
    public void MeasureTextSize_ReturnsNonEmptyRectangle()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        RichTextOptions textOptions = new(font) { Origin = new PointF(0, 0) };

        RectangleF size = canvas.MeasureTextSize(textOptions, "Hello");

        Assert.True(size.Width > 0, "Width should be positive.");
        Assert.True(size.Height > 0, "Height should be positive.");
    }

    [Fact]
    public void MeasureTextSize_EmptyText_ReturnsEmpty()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        RichTextOptions textOptions = new(font) { Origin = new PointF(0, 0) };

        RectangleF size = canvas.MeasureTextSize(textOptions, ReadOnlySpan<char>.Empty);

        Assert.Equal(RectangleF.Empty, size);
    }

    [Fact]
    public void MeasureTextSize_LongerText_IsWider()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        RichTextOptions textOptions = new(font) { Origin = new PointF(0, 0) };

        RectangleF shortSize = canvas.MeasureTextSize(textOptions, "Hi");
        RectangleF longSize = canvas.MeasureTextSize(textOptions, "Hello World");

        Assert.True(longSize.Width > shortSize.Width, "Longer text should produce a wider measurement.");
    }

    [Fact]
    public void TryMeasureCharacterAdvances_ReturnsAdvancesForEachCharacter()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        RichTextOptions textOptions = new(font) { Origin = new PointF(0, 0) };

        const string text = "ABC";
        bool result = canvas.TryMeasureCharacterAdvances(textOptions, text, out ReadOnlySpan<GlyphBounds> advances);

        Assert.True(result);
        Assert.Equal(text.Length, advances.Length);

        for (int i = 0; i < advances.Length; i++)
        {
            Assert.True(advances[i].Bounds.Width > 0, $"Advance width for character {i} should be positive.");
        }
    }

    [Fact]
    public void TryMeasureCharacterAdvances_EmptyText_ReturnsFalse()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        RichTextOptions textOptions = new(font) { Origin = new PointF(0, 0) };

        bool result = canvas.TryMeasureCharacterAdvances(textOptions, ReadOnlySpan<char>.Empty, out ReadOnlySpan<GlyphBounds> advances);

        Assert.False(result);
        Assert.True(advances.IsEmpty);
    }
}
