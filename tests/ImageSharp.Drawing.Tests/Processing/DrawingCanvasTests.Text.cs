// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Theory]
    [WithSolidFilledImages(492, 360, nameof(Color.White), PixelTypes.Rgba32, ColorFontSupport.ColrV1)]
    [WithSolidFilledImages(492, 360, nameof(Color.White), PixelTypes.Rgba32, ColorFontSupport.Svg)]
    public void DrawGlyphs_EmojiFont_MatchesReference<TPixel>(TestImageProvider<TPixel> provider, ColorFontSupport support)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions());

        Font font = TestFontUtilities.GetFont(TestFonts.NotoColorEmojiRegular, 100);
        Font fallback = TestFontUtilities.GetFont(TestFonts.OpenSans, 100);
        const string text = "a😨 b😅\r\nc🥲 d🤩";

        RichTextOptions textOptions = new(font)
        {
            ColorFontSupport = support,
            LineSpacing = 1.8F,
            FallbackFontFamilies = [fallback.Family],
            TextRuns =
            [
                new RichTextRun
                {
                    Start = 0,
                    End = text.GetGraphemeCount(),
                    TextDecorations = TextDecorations.Strikeout | TextDecorations.Underline | TextDecorations.Overline
                }
            ]
        };

        IReadOnlyList<GlyphPathCollection> glyphs = TextBuilder.GenerateGlyphs(text, textOptions);

        canvas.Clear(Brushes.Solid(Color.White));
        canvas.DrawGlyphs(Brushes.Solid(Color.Black), Pens.Solid(Color.Black, 2F), glyphs);
        canvas.Flush();

        target.DebugSave(provider, $"{support}-draw-glyphs", appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, $"{support}-draw-glyphs", appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(760, 320, PixelTypes.Rgba32)]
    public void DrawText_Multiline_WithLineMetricsGuides_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();

        DrawingOptions options = new()
        {
            Transform = Matrix3x2.CreateTranslation(24F, 22F)
        };

        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, options);
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 32);

        string text = "Quick wafting zephyrs vex bold Jim.\n" +
            "How quickly daft jumping zebras vex.\n" +
            "Sphinx of black quartz, judge my vow.";

        RichTextOptions textOptions = new(font)
        {
            Origin = PointF.Empty,
            LineSpacing = 1.45F
        };

        canvas.Clear(Brushes.Solid(Color.White));
        canvas.Fill(new Rectangle(0, 0, 712, 276), Brushes.Solid(Color.LightSteelBlue.WithAlpha(0.25F)));
        canvas.DrawText(textOptions, text, Brushes.Solid(Color.Black), pen: null);

        LineMetrics[] lineMetrics = canvas.GetTextLineMetrics(textOptions, text);
        float lineOriginY = textOptions.Origin.Y;
        for (int i = 0; i < lineMetrics.Length; i++)
        {
            LineMetrics metrics = lineMetrics[i];
            float startX = metrics.Start;
            float endX = metrics.Start + metrics.Extent;
            float topY = lineOriginY;
            float ascenderY = lineOriginY + metrics.Ascender;
            float baselineY = lineOriginY + metrics.Baseline;
            float descenderY = lineOriginY + metrics.Descender;
            float lineHeightY = lineOriginY + metrics.LineHeight;

            canvas.DrawLine(Pens.Solid(Color.DimGray.WithAlpha(0.8F), 1), new PointF(startX, topY), new PointF(endX, topY));
            canvas.DrawLine(Pens.Solid(Color.RoyalBlue.WithAlpha(0.9F), 1), new PointF(startX, ascenderY), new PointF(endX, ascenderY));
            canvas.DrawLine(Pens.Solid(Color.Crimson.WithAlpha(0.9F), 1), new PointF(startX, baselineY), new PointF(endX, baselineY));
            canvas.DrawLine(Pens.Solid(Color.DarkOrange.WithAlpha(0.9F), 1), new PointF(startX, descenderY), new PointF(endX, descenderY));
            canvas.DrawLine(Pens.Solid(Color.SeaGreen.WithAlpha(0.9F), 1), new PointF(startX, lineHeightY), new PointF(endX, lineHeightY));
            canvas.DrawLine(Pens.Solid(Color.DimGray.WithAlpha(0.8F), 1), new PointF(startX, topY), new PointF(startX, lineHeightY));
            canvas.DrawLine(Pens.Solid(Color.DimGray.WithAlpha(0.8F), 1), new PointF(endX, topY), new PointF(endX, lineHeightY));

            lineOriginY += metrics.LineHeight;
        }

        canvas.Draw(Pens.Solid(Color.Black, 2), new Rectangle(0, 0, 712, 276));
        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(420, 220, PixelTypes.Rgba32)]
    public void DrawText_FillAndStroke_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();

        DrawingOptions options = new()
        {
            Transform = Matrix3x2.CreateRotation(-0.08F, new Vector2(210, 110))
        };

        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, options);
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 36);
        RichTextOptions textOptions = new(font)
        {
            Origin = new PointF(24, 36),
            WrappingLength = 372
        };

        canvas.Clear(Brushes.Solid(Color.White));
        canvas.DrawText(
            textOptions,
            "Canvas text\nwith fill + stroke",
            Brushes.Solid(Color.MidnightBlue.WithAlpha(0.82F)),
            Pens.Solid(Color.Gold, 2F));
        canvas.Draw(Pens.Solid(Color.DimGray, 3), new Rectangle(10, 10, 400, 200));
        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(320, 180, PixelTypes.Rgba32)]
    public void DrawText_PenOnly_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions());

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 52);
        RichTextOptions textOptions = new(font)
        {
            Origin = new PointF(18, 42)
        };

        canvas.Clear(Brushes.Solid(Color.White));
        canvas.Fill(new Rectangle(12, 14, 296, 152), Brushes.Solid(Color.LightSkyBlue.WithAlpha(0.45F)));
        canvas.DrawText(textOptions, "OUTLINE", brush: null, pen: Pens.Solid(Color.SeaGreen, 3.5F));
        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(360, 220, PixelTypes.Rgba32)]
    public void DrawText_AlongPathWithOrigin_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions());

        IPath textPath = new EllipsePolygon(new PointF(172, 112), new SizeF(246, 112));
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 21);
        RichTextOptions textOptions = new(font)
        {
            Path = textPath,
            Origin = new PointF(16, -10),
            WrappingLength = textPath.ComputeLength(),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        canvas.Clear(Brushes.Solid(Color.White));
        canvas.Draw(Pens.Solid(Color.SlateGray, 2), textPath);
        canvas.DrawText(
            textOptions,
            "Sphinx of black quartz, judge my vow.",
            Brushes.Solid(Color.DarkRed.WithAlpha(0.9F)),
            pen: null);
        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(840, 420, PixelTypes.Rgba32)]
    public void DrawText_WithWrappingAlignmentAndLineSpacing_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions());

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 28);
        Rectangle layoutBounds = new(120, 50, 600, 320);

        RichTextOptions textOptions = new(font)
        {
            Origin = new PointF(
                layoutBounds.Left + (layoutBounds.Width / 2F),
                layoutBounds.Top + (layoutBounds.Height / 2F)),
            WrappingLength = layoutBounds.Width - 64F,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            LineSpacing = 2.1F
        };

        string text =
            "Pack my box with five dozen liquor jugs while zephyrs drift across the bay.\n" +
            "Sphinx of black quartz, judge my vow.";

        canvas.Clear(Brushes.Solid(Color.White));
        canvas.Fill(layoutBounds, Brushes.Solid(Color.LightGoldenrodYellow.WithAlpha(0.45F)));
        canvas.Draw(Pens.Solid(Color.SlateGray, 2F), layoutBounds);
        canvas.DrawLine(
            Pens.Dash(Color.Gray.WithAlpha(0.8F), 1.5F),
            new PointF(textOptions.Origin.X, layoutBounds.Top),
            new PointF(textOptions.Origin.X, layoutBounds.Bottom));
        canvas.DrawLine(
            Pens.Dash(Color.Gray.WithAlpha(0.8F), 1.5F),
            new PointF(layoutBounds.Left, textOptions.Origin.Y),
            new PointF(layoutBounds.Right, textOptions.Origin.Y));

        canvas.DrawText(
            textOptions,
            text,
            Brushes.Solid(Color.DarkBlue.WithAlpha(0.86F)),
            Pens.Solid(Color.DarkRed.WithAlpha(0.55F), 1.1F));

        canvas.Draw(Pens.Solid(Color.Black, 3F), new Rectangle(10, 10, 820, 400));
        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }
}
