// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawGlyphs(Brushes.Solid(Color.Black), Pens.Solid(Color.Black, 2F), glyphs);
        }

        target.DebugSave(provider, $"{support}-draw-glyphs", appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, $"{support}-draw-glyphs", appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(420, 180, nameof(Color.White), PixelTypes.Rgba32)]
    public void DrawText_Inter_OverlappingContours_NoHoles<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();

        // Inter Light is the exact font the Avalonia sample renders (it ships Inter as its embedded UI
        // font). Inter draws glyphs such as 'A' and 't' with overlapping contours, so if the glyph fill
        // applies the wrong winding those overlaps render as holes. This isolates the fill issue at the
        // library level (CPU canvas), independent of the Avalonia/WebGPU backends where it was observed.
        Font font = TestFontUtilities.GetFont(TestFonts.InterLight, 32);
        const string text = "Avalonia Test";

        RichTextOptions textOptions = new(font) { Origin = new PointF(16, 16) };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawText(textOptions, text, Brushes.Solid(Color.Black), pen: null);
        }

        // No reference comparison yet: the bug being isolated is still present, so DebugSave the output for
        // inspection. Promote to CompareToReferenceOutput once the overlap-hole fill issue is fixed.
        target.DebugSave(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(420, 180, nameof(Color.White), PixelTypes.Rgba32)]
    public void DrawGlyphById_Inter_OverlappingContours_NoHoles<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();

        // The string-based DrawText path renders Inter cleanly; the Avalonia sample instead renders glyph
        // by glyph through DrawText(glyphId, RichGlyphOptions, ...) (the RenderGlyph path). This test drives
        // that exact path so we can see whether the holes originate there rather than in the shared fill.
        Font font = TestFontUtilities.GetFont(TestFonts.InterLight, 32);
        const string text = "Avalonia Test";

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));

            float penX = 16F;
            const float baselineY = 130F;
            foreach (char c in text)
            {
                if (!font.FontMetrics.TryGetGlyphMetrics(
                        new CodePoint(c),
                        TextAttributes.None,
                        TextDecorations.None,
                        LayoutMode.HorizontalTopBottom,
                        ColorFontSupport.None,
                        out FontGlyphMetrics metrics))
                {
                    continue;
                }

                RichGlyphOptions glyphOptions = new()
                {
                    Font = font,
                    Origin = new Vector2(penX, baselineY)
                };

                canvas.DrawText(metrics.GlyphId, glyphOptions, Brushes.Solid(Color.Black), pen: null);

                penX += metrics.AdvanceWidth * font.Size / metrics.UnitsPerEm;
            }
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(420, 180, nameof(Color.White), PixelTypes.Rgba32)]
    public void DrawGlyphById_Inter_EvenOddCanvasState_NoHoles<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();

        // The Avalonia backend wraps its glyph loop in PushDrawingState(), whose IntersectionRule defaults to
        // EvenOdd. The previous glyph-id test uses a default (NonZero) canvas state and renders cleanly;
        // this one replicates the sample's EvenOdd state to confirm the glyph fill's forced non-zero winding
        // holds even when the canvas requests even-odd. If holes appear here, the force has a gap.
        DrawingOptions options = new()
        {
            IntersectionRule = IntersectionRule.EvenOdd
        };

        Font font = TestFontUtilities.GetFont(TestFonts.InterLight, 32);
        const string text = "Avalonia Test";

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, options))
        {
            canvas.Clear(Brushes.Solid(Color.White));

            float penX = 16F;
            const float baselineY = 130F;
            foreach (char c in text)
            {
                if (!font.FontMetrics.TryGetGlyphMetrics(
                        new CodePoint(c),
                        TextAttributes.None,
                        TextDecorations.None,
                        LayoutMode.HorizontalTopBottom,
                        ColorFontSupport.None,
                        out FontGlyphMetrics metrics))
                {
                    continue;
                }

                RichGlyphOptions glyphOptions = new()
                {
                    Font = font,
                    Origin = new Vector2(penX, baselineY)
                };

                canvas.DrawText(metrics.GlyphId, glyphOptions, Brushes.Solid(Color.Black), pen: null);

                penX += metrics.AdvanceWidth * font.Size / metrics.UnitsPerEm;
            }
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(420, 180, nameof(Color.Black), PixelTypes.Rgba32)]
    public void DrawGlyphById_Inter_EvenOddCanvasState_MatchesNonZeroCanvasState<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> expected = provider.GetImage();
        using Image<TPixel> actual = provider.GetImage();
        Font font = TestFontUtilities.GetFont(TestFonts.InterLight, 32);
        const string text = "Avalonia Test";
        PointF origin = new(16F, 130F);

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, expected, new DrawingOptions()))
        {
            DrawGlyphs(canvas, text, font, origin);
        }

        DrawingOptions evenOddOptions = new()
        {
            IntersectionRule = IntersectionRule.EvenOdd
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, actual, evenOddOptions))
        {
            DrawGlyphs(canvas, text, font, origin);
        }

        expected.DebugSave(provider, "expected", appendSourceFileOrDescription: false);
        actual.DebugSave(provider, "actual", appendSourceFileOrDescription: false);

        ImageComparer.TolerantPercentage(0.005F).VerifySimilarity(expected, actual);
    }

    [Theory]
    [WithSolidFilledImages(420, 180, nameof(Color.Black), PixelTypes.Rgba32)]
    public void DrawGlyphById_SubjectNonZero_ClipNonZeroRect_DoesNotChangeGlyph<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> expected = provider.GetImage();
        using Image<TPixel> actual = provider.GetImage();
        Font font = TestFontUtilities.GetFont(TestFonts.InterLight, 32);
        const string text = "Avalonia Test";
        PointF origin = new(16F, 130F);

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, expected, new DrawingOptions()))
        {
            DrawGlyphs(canvas, text, font, origin);
        }

        DrawingOptions clipOptions = new()
        {
            IntersectionRule = IntersectionRule.NonZero
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, actual, new DrawingOptions()))
        {
            _ = canvas.Save(clipOptions);
            canvas.Clip(new RectanglePolygon(0, 0, 420, 180));
            DrawGlyphs(canvas, text, font, origin);
            canvas.Restore();
        }

        expected.DebugSave(provider, "expected-unclipped", appendSourceFileOrDescription: false);
        actual.DebugSave(provider, "actual-clipped", appendSourceFileOrDescription: false);

        ImageComparer.TolerantPercentage(0.0027F).VerifySimilarity(expected, actual);
    }

    [Theory]
    [WithSolidFilledImages(420, 180, nameof(Color.Black), PixelTypes.Rgba32)]
    public void DrawGlyphById_SubjectNonZero_ClipEvenOddRect_DoesNotChangeGlyph<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> expected = provider.GetImage();
        using Image<TPixel> actual = provider.GetImage();
        Font font = TestFontUtilities.GetFont(TestFonts.InterLight, 32);
        const string text = "Avalonia Test";
        PointF origin = new(16F, 130F);

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, expected, new DrawingOptions()))
        {
            DrawGlyphs(canvas, text, font, origin);
        }

        DrawingOptions evenOddClipOptions = new()
        {
            IntersectionRule = IntersectionRule.EvenOdd
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, actual, new DrawingOptions()))
        {
            _ = canvas.Save(evenOddClipOptions);
            canvas.Clip(new RectanglePolygon(0, 0, 420, 180));
            DrawGlyphs(canvas, text, font, origin);
            canvas.Restore();
        }

        expected.DebugSave(provider, "expected-unclipped", appendSourceFileOrDescription: false);
        actual.DebugSave(provider, "actual-clipped", appendSourceFileOrDescription: false);

        ImageComparer.TolerantPercentage(0.0027F).VerifySimilarity(expected, actual);
    }

    [Theory]
    [WithSolidFilledImages(420, 180, nameof(Color.Black), PixelTypes.Rgba32)]
    public void DrawGlyphRun_Inter_MatchesGlyphByIdLoop<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> expected = provider.GetImage();
        using Image<TPixel> actual = provider.GetImage();
        Font font = TestFontUtilities.GetFont(TestFonts.InterLight, 32);
        const string text = "Avalonia Test";
        PointF origin = new(16F, 130F);
        ushort[] glyphIds = new ushort[text.Length];
        Vector2[] origins = new Vector2[text.Length];
        float penX = origin.X;

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, expected, new DrawingOptions()))
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (!font.FontMetrics.TryGetGlyphMetrics(
                        new CodePoint(text[i]),
                        TextAttributes.None,
                        TextDecorations.None,
                        LayoutMode.HorizontalTopBottom,
                        ColorFontSupport.None,
                        out FontGlyphMetrics metrics))
                {
                    continue;
                }

                RichGlyphOptions options = new()
                {
                    Font = font,
                    Origin = new Vector2(penX, origin.Y)
                };

                glyphIds[i] = metrics.GlyphId;
                origins[i] = options.Origin;
                canvas.DrawText(metrics.GlyphId, options, Brushes.Solid(Color.White), pen: null);

                penX += metrics.AdvanceWidth * font.Size / metrics.UnitsPerEm;
            }
        }

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, actual, new DrawingOptions()))
        {
            RichGlyphOptions options = new() { Font = font };
            canvas.DrawText(new GlyphRun(glyphIds, origins), options, Brushes.Solid(Color.White), pen: null);
        }

        expected.DebugSave(provider, "per-glyph-loop", appendSourceFileOrDescription: false);
        actual.DebugSave(provider, "batched-glyph-run", appendSourceFileOrDescription: false);

        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    [Theory]
    [WithBlankImage(760, 320, PixelTypes.Rgba32)]
    public void DrawText_Multiline_WithLineMetricsGuides_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();

        DrawingOptions options = new()
        {
            Transform = Matrix4x4.CreateTranslation(24F, 22F, 0)
        };

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 32);

        string text = "Quick wafting zephyrs vex bold Jim.\n" +
            "How quickly daft jumping zebras vex.\n" +
            "Sphinx of black quartz, judge my vow.";

        RichTextOptions textOptions = new(font)
        {
            Origin = PointF.Empty,
            LineSpacing = 1.45F
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, options))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.LightSteelBlue.WithAlpha(0.25F)), new Rectangle(0, 0, 712, 276));
            canvas.DrawText(textOptions, text, Brushes.Solid(Color.Black), pen: null);

            ReadOnlySpan<LineMetrics> lineMetrics = canvas.MeasureText(textOptions, text).LineMetrics;
            float lineOriginY = textOptions.Origin.Y;
            for (int i = 0; i < lineMetrics.Length; i++)
            {
                LineMetrics metrics = lineMetrics[i];
                float startX = metrics.Start.X;
                float endX = metrics.Start.X + metrics.Extent.X;
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
        }

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
            Transform = new Matrix4x4(Matrix3x2.CreateRotation(-0.08F, new Vector2(210, 110)))
        };

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 36);
        RichTextOptions textOptions = new(font)
        {
            Origin = new PointF(24, 36),
            WrappingLength = 372
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, options))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawText(
                textOptions,
                "Canvas text\nwith fill + stroke",
                Brushes.Solid(Color.MidnightBlue.WithAlpha(0.82F)),
                Pens.Solid(Color.Gold, 2F));
            canvas.Draw(Pens.Solid(Color.DimGray, 3), new Rectangle(10, 10, 400, 200));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(ImageComparer.TolerantPercentage(0.0001F), provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(320, 180, PixelTypes.Rgba32)]
    public void DrawText_PenOnly_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 52);
        RichTextOptions textOptions = new(font)
        {
            Origin = new PointF(18, 42)
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.LightSkyBlue.WithAlpha(0.45F)), new Rectangle(12, 14, 296, 152));
            canvas.DrawText(textOptions, "OUTLINE", brush: null, pen: Pens.Solid(Color.SeaGreen, 3.5F));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(360, 220, PixelTypes.Rgba32)]
    public void DrawText_AlongPathWithOrigin_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        IPath textPath = new EllipsePolygon(new PointF(172, 112), new SizeF(246, 112));
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 21);
        RichTextOptions textOptions = new(font)
        {
            Origin = new PointF(16, -10),
            WrappingLength = textPath.ComputeLength(),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Draw(Pens.Solid(Color.SlateGray, 2), textPath);
            canvas.DrawText(
                textOptions,
                "Sphinx of black quartz, judge my vow.",
                textPath,
                Brushes.Solid(Color.DarkRed.WithAlpha(0.9F)),
                pen: null);
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(620, 260, PixelTypes.Rgba32)]
    public void DrawText_TextBlockAlongPath_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        IPath textPath = new Path(new CubicBezierLineSegment(
            new PointF(82, 166),
            new PointF(190, 46),
            new PointF(420, 248),
            new PointF(556, 106)));

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 26);
        RichTextOptions textOptions = new(font)
        {
            WrappingLength = textPath.ComputeLength(),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        TextBlock textBlock = new("Prepared text blocks can ride along an explicit curve.", textOptions);

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Draw(Pens.Solid(Color.LightSlateGray, 2), textPath);

            // The prepared block keeps shaping and runs, while the explicit path
            // controls glyph placement at draw time.
            canvas.DrawText(
                textBlock,
                textPath,
                textOptions.WrappingLength,
                Brushes.Solid(Color.MidnightBlue.WithAlpha(0.9F)),
                Pens.Solid(Color.Goldenrod.WithAlpha(0.55F), 1.2F));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(640, 340, PixelTypes.Rgba32)]
    public void DrawText_LineLayoutsAlongDifferentPaths_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        RichTextOptions textOptions = new(font)
        {
            WrappingLength = -1,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        const string text = "First prepared line\nSecond prepared line\nThird prepared line";
        TextBlock textBlock = new(text, textOptions);
        LineLayoutEnumerator enumerator = textBlock.EnumerateLineLayouts();
        IPath[] paths =
        [
            new Path(new CubicBezierLineSegment(
                new PointF(38, 108),
                new PointF(170, 46),
                new PointF(388, 156),
                new PointF(602, 90))),
            new Path(new CubicBezierLineSegment(
                new PointF(38, 188),
                new PointF(176, 256),
                new PointF(392, 120),
                new PointF(602, 198))),
            new Path(new CubicBezierLineSegment(
                new PointF(38, 272),
                new PointF(192, 204),
                new PointF(390, 340),
                new PointF(602, 264)))
        ];

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));

            for (int i = 0; i < paths.Length; i++)
            {
                enumerator.MoveNext(-1);

                canvas.Draw(Pens.Solid(Color.LightSlateGray, 1.5F), paths[i]);

                // Each prepared line is rendered independently against its own
                // path, which is the scenario manual flow callers need.
                canvas.DrawText(
                    enumerator.Current,
                    paths[i],
                    Brushes.Solid(Color.DarkGreen.WithAlpha(0.9F)),
                    Pens.Solid(Color.DarkOrange.WithAlpha(0.55F), 1F));
            }
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(840, 420, PixelTypes.Rgba32)]
    public void DrawText_WithWrappingAlignmentAndLineSpacing_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
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

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.LightGoldenrodYellow.WithAlpha(0.45F)), layoutBounds);
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
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithSolidFilledImages(240, 120, nameof(Color.White), PixelTypes.Rgba32)]
    public void DrawText_OffscreenLines_CulledOutputMatchesUnculled<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> expected = provider.GetImage();
        using Image<TPixel> actual = provider.GetImage();
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        string text = BuildNumberedLines(60);

        // Caller-supplied visible bounds always win, so an effectively unbounded rectangle
        // renders every line and provides the unculled reference.
        RichTextOptions unculledOptions = new(font)
        {
            Origin = new Vector2(8, 8),
            VisibleBounds = new FontRectangle(-1e6F, -1e6F, 2e6F, 2e6F)
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, expected, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawText(unculledOptions, text, Brushes.Solid(Color.Black), pen: null);
        }

        RichTextOptions culledOptions = new(font) { Origin = new Vector2(8, 8) };
        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, actual, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawText(culledOptions, text, Brushes.Solid(Color.Black), pen: null);
        }

        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    [Theory]
    [WithSolidFilledImages(240, 120, nameof(Color.White), PixelTypes.Rgba32)]
    public void DrawText_TranslatedTransform_CulledOutputMatchesUnculled<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> expected = provider.GetImage();
        using Image<TPixel> actual = provider.GetImage();
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        string text = BuildNumberedLines(60);

        // The translation scrolls lines 240 to 360 of the text into view, so the culling band
        // must follow the transform; a band left at the image rectangle would cull every
        // visible line and produce a blank image.
        DrawingOptions scrolled = new()
        {
            Transform = Matrix4x4.CreateTranslation(0, -240, 0)
        };

        RichTextOptions unculledOptions = new(font)
        {
            Origin = new Vector2(8, 8),
            VisibleBounds = new FontRectangle(-1e6F, -1e6F, 2e6F, 2e6F)
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, expected, scrolled))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawText(unculledOptions, text, Brushes.Solid(Color.Black), pen: null);
        }

        RichTextOptions culledOptions = new(font) { Origin = new Vector2(8, 8) };
        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, actual, scrolled))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawText(culledOptions, text, Brushes.Solid(Color.Black), pen: null);
        }

        // Culling changes the run origin the batched glyph geometry is keyed against, and
        // run-relative caching computes vertex positions as (absolute - origin) at bake time
        // plus origin at raster time. Floating point addition is not associative, so the two
        // renders' vertices differ by ULPs, and the rasterizer's sub-pixel grid snap can
        // amplify an ULP into a single least-significant-bit coverage step on an occasional
        // antialiased edge pixel.
        ImageComparer.TolerantPercentage(0.005F).VerifySimilarity(expected, actual);
    }

    [Theory]
    [WithSolidFilledImages(240, 120, nameof(Color.White), PixelTypes.Rgba32)]
    public void DrawText_RotatedTransform_CulledOutputMatchesUnculled<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> expected = provider.GetImage();
        using Image<TPixel> actual = provider.GetImage();
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        string text = BuildNumberedLines(60);

        // Rotation moves text space away from device space, so culling must stand down; the
        // rotation about the image center swings lines from below the straight-line band into
        // view and any band applied regardless would drop them.
        DrawingOptions rotated = new()
        {
            Transform =
                Matrix4x4.CreateTranslation(-120, -60, 0) *
                Matrix4x4.CreateRotationZ(MathF.PI / 2F) *
                Matrix4x4.CreateTranslation(120, 60, 0)
        };

        RichTextOptions unculledOptions = new(font)
        {
            Origin = new Vector2(8, 8),
            VisibleBounds = new FontRectangle(-1e6F, -1e6F, 2e6F, 2e6F)
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, expected, rotated))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawText(unculledOptions, text, Brushes.Solid(Color.Black), pen: null);
        }

        RichTextOptions culledOptions = new(font) { Origin = new Vector2(8, 8) };
        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, actual, rotated))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawText(culledOptions, text, Brushes.Solid(Color.Black), pen: null);
        }

        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    [Theory]
    [WithSolidFilledImages(240, 120, nameof(Color.White), PixelTypes.Rgba32)]
    public void DrawText_TextBlock_OffscreenLines_CulledOutputMatchesUnculled<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> actual = provider.GetImage();
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        string text = BuildNumberedLines(60);
        TextBlock block = new(text, new TextOptions(font));

        // On a target tall enough for every line nothing is culled, so its top band is the
        // unculled reference for the small target where most lines lie below the bottom edge.
        using Image<TPixel> tall = new(provider.Configuration, actual.Width, 2200);
        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, tall, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawText(block, new PointF(8, 8), -1, Brushes.Solid(Color.Black), pen: null);
        }

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, actual, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawText(block, new PointF(8, 8), -1, Brushes.Solid(Color.Black), pen: null);
        }

        using Image<TPixel> expected = tall.Clone(ctx => ctx.Crop(new Rectangle(0, 0, actual.Width, actual.Height)));
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    /// <summary>
    /// Builds multi-line text whose numbered lines make any culled-but-visible line an exact
    /// pixel difference.
    /// </summary>
    /// <param name="count">The number of lines.</param>
    /// <returns>The text.</returns>
    private static string BuildNumberedLines(int count)
        => string.Join('\n', Enumerable.Range(0, count).Select(static i => $"line {i}"));

    /// <summary>
    /// Draws text through the glyph-id API used by the glyph regression tests.
    /// </summary>
    /// <param name="canvas">The canvas under test.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="font">The font used to resolve glyph ids and advances.</param>
    /// <param name="origin">The baseline origin in the current canvas coordinate space.</param>
    private static void DrawGlyphs<TPixel>(DrawingCanvas<TPixel> canvas, string text, Font font, PointF origin)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        float penX = origin.X;
        for (int i = 0; i < text.Length; i++)
        {
            if (!font.FontMetrics.TryGetGlyphMetrics(
                    new CodePoint(text[i]),
                    TextAttributes.None,
                    TextDecorations.None,
                    LayoutMode.HorizontalTopBottom,
                    ColorFontSupport.None,
                    out FontGlyphMetrics metrics))
            {
                continue;
            }

            RichGlyphOptions options = new()
            {
                Font = font,
                Origin = new Vector2(penX, origin.Y)
            };

            canvas.DrawText(metrics.GlyphId, options, Brushes.Solid(Color.White), pen: null);

            penX += metrics.AdvanceWidth * font.Size / metrics.UnitsPerEm;
        }
    }
}
