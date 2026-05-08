// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;
using Path = SixLabors.ImageSharp.Drawing.Path;

namespace SixLabors.Shapes.DrawShapesWithImageSharp;

/// <summary>
/// Generates composed ImageSharp.Drawing samples.
/// </summary>
public static class Program
{
    private const string OutputDirectory = "Output";
    private const string FontsDirectory = "Fonts";
    private const string ArabicFontFile = "me_quran_volt_newmet.ttf";
    private const string CjkFontFile = "NotoSansKR-Regular.otf";
    private const string TextFontFile = "OpenSans-Regular.ttf";
    private const string DisplayFontFile = "WendyOne-Regular.ttf";

    private static readonly Size SampleSize = new(960, 640);
    private static readonly FontCollection SampleFonts = new();
    private static readonly FontFamily ArabicFontFamily = LoadFontFamily(ArabicFontFile);
    private static readonly FontFamily CjkFontFamily = LoadFontFamily(CjkFontFile);
    private static readonly FontFamily TextFontFamily = LoadFontFamily(TextFontFile);
    private static readonly FontFamily DisplayFontFamily = LoadFontFamily(DisplayFontFile);

    /// <summary>
    /// Runs every sample and writes the generated PNG files under the sample's artifacts output directory.
    /// </summary>
    public static void Main()
    {
        DrawPosterComposition();
        DrawTransitMap();
        DrawTypographySheet();
        DrawImageProcessingMask();
    }

    /// <summary>
    /// Draws a poster-style composition using gradients, path building, clipping, layers, and text.
    /// </summary>
    private static void DrawPosterComposition()
    {
        Font titleFont = DisplayFontFamily.CreateFont(54);
        Font bodyFont = TextFontFamily.CreateFont(22);
        Brush skyBrush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(0, SampleSize.Height),
            GradientRepetitionMode.None,
            new ColorStop(0, Color.CornflowerBlue),
            new ColorStop(.58F, Color.SkyBlue),
            new ColorStop(1, Color.LightCyan));

        Brush sunBrush = new RadialGradientBrush(
            new PointF(760, 150),
            170,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.White),
            new ColorStop(.42F, Color.Gold),
            new ColorStop(1, Color.OrangeRed.WithAlpha(.12F)));

        Brush lakeBrush = new LinearGradientBrush(
            new PointF(0, 405),
            new PointF(0, SampleSize.Height),
            GradientRepetitionMode.None,
            new ColorStop(0, Color.SkyBlue.WithAlpha(.82F)),
            new ColorStop(1, Color.RoyalBlue));

        SaveSample("01-poster-composition.png", SampleSize, canvas =>
        {
            // Background fill: passing a brush to Fill with no shape paints the entire canvas.
            // The sun is then composited on top using a radial gradient brush clipped to an ellipse.
            canvas.Fill(skyBrush);
            canvas.Fill(sunBrush, new EllipsePolygon(760, 150, 170, 170));

            // Distant mountain range: PathBuilder creates a closed filled polygon from explicit points.
            PathBuilder farMountains = new();
            farMountains.AddLines(
                new PointF(0, 415),
                new PointF(120, 300),
                new PointF(230, 375),
                new PointF(345, 245),
                new PointF(500, 420),
                new PointF(660, 270),
                new PointF(790, 405),
                new PointF(960, 310),
                new PointF(960, 640),
                new PointF(0, 640));

            farMountains.CloseFigure();

            canvas.Fill(Brushes.Solid(Color.SlateBlue.WithAlpha(.78F)), farMountains);

            // Foreground mountain range: the same path is filled and then stroked to demonstrate path reuse.
            PathBuilder nearMountains = new();
            nearMountains.AddLines(
                new PointF(0, 455),
                new PointF(165, 250),
                new PointF(310, 390),
                new PointF(470, 220),
                new PointF(665, 430),
                new PointF(820, 300),
                new PointF(960, 420),
                new PointF(960, 640),
                new PointF(0, 640));

            nearMountains.CloseFigure();

            canvas.Fill(Brushes.Solid(Color.DarkSlateBlue), nearMountains);
            canvas.Draw(Pens.Solid(Color.LightSteelBlue.WithAlpha(.55F), 4), nearMountains);

            // The lake is a plain rectangle filled with a vertical LinearGradientBrush. Building it
            // with PathBuilder keeps the construction style identical to the surrounding shapes
            // even though a RectangularPolygon would also work for an axis-aligned rectangle.
            PathBuilder lakeShape = new();
            lakeShape.AddLines(
                new PointF(0, 432),
                new PointF(960, 432),
                new PointF(960, 640),
                new PointF(0, 640));

            lakeShape.CloseFigure();

            canvas.Fill(lakeBrush, lakeShape);

            // Shoreline strip: a single closed figure with two scalloped edges. PathBuilder.AddLines
            // appends connected straight segments, and CloseFigure links the last point back to the
            // first so the polygon can be filled as one contiguous shape.
            PathBuilder shorelineShape = new();
            shorelineShape.AddLines(
                new PointF(0, 418),
                new PointF(150, 420),
                new PointF(300, 426),
                new PointF(470, 418),
                new PointF(640, 428),
                new PointF(820, 420),
                new PointF(960, 426),
                new PointF(960, 448),
                new PointF(810, 440),
                new PointF(640, 446),
                new PointF(470, 438),
                new PointF(300, 444),
                new PointF(150, 436),
                new PointF(0, 442));

            shorelineShape.CloseFigure();

            canvas.Fill(Brushes.Solid(Color.SeaGreen), shorelineShape);

            // Clipping demo: canvas.Save accepts a clip path plus DrawingOptions whose
            // BooleanOperation controls how the new clip combines with the existing one. Using
            // Intersection means subsequent draws are masked to the oval highlight shape, so a
            // rectangular gradient fill can be reused without re-shaping it as an ellipse.
            EllipsePolygon lakeHighlight = new(725, 512, 285, 86);
            DrawingOptions lakeHighlightClipOptions = new()
            {
                ShapeOptions = new ShapeOptions
                {
                    BooleanOperation = BooleanOperation.Intersection
                }
            };

            RectangleF lakeHighlightBounds = lakeHighlight.Bounds;

            // Save pushes a clipping state; Restore pops it. Anything drawn between the two is
            // confined to lakeHighlight even though the brush spans its full bounding rectangle.
            canvas.Save(lakeHighlightClipOptions, lakeHighlight);
            canvas.Fill(Brushes.ForwardDiagonal(Color.White.WithAlpha(.36F), Color.Transparent), new RectangularPolygon(lakeHighlightBounds));
            canvas.Restore();

            // Title panel: pushing posterPanelOptions onto the canvas with Save activates a
            // small Z rotation around (360, 150) for everything drawn until Restore. SaveLayer
            // is then opened inside that rotation: it saves the current state and begins an
            // isolated compositing layer bounded to the supplied subregion. Subsequent draw
            // commands (the panel background and the text) are recorded into that layer. The
            // matching Restore closes the layer in the deferred scene; the actual composition
            // against the parent canvas runs on the next Flush / Dispose using the supplied
            // GraphicsOptions, so BlendPercentage 0.94 fades the panel and text as a single
            // semi-transparent block rather than fading each draw individually.
            DrawingOptions posterPanelOptions = new()
            {
                Transform = Matrix4x4.CreateRotationZ(-.055F, new Vector3(360, 150, 0))
            };

            const string posterTitle = "Alpine Lake";
            const string posterBody = "Transformed layer\nClipped lake highlight, gradient paths";
            string posterText = $"{posterTitle}\n{posterBody}";
            SizeF posterPanelPadding = new(18, 12);
            RichTextOptions posterTextOptions = new(bodyFont)
            {
                Origin = new PointF(114, 38),
                LineSpacing = 1.1F,
                TextRuns =
                [
                    new RichTextRun
                    {
                        Start = 0,
                        End = posterTitle.Length,
                        Font = titleFont,
                        Brush = Brushes.Solid(Color.MidnightBlue),
                    },

                    new RichTextRun
                    {
                        Start = posterTitle.Length + 1,
                        End = posterText.Length,
                        Font = bodyFont,
                        Brush = Brushes.Solid(Color.DarkSlateGray),
                    }
                ]
            };

            // Sizing a container around laid-out text needs MeasureRenderableBounds: it returns
            // the union of the logical advance rectangle and the ink bounds, so trailing
            // whitespace and side-bearing overhang stay inside the panel. MeasureBounds returns
            // ink only and would clip; MeasureAdvance returns advance only and would leave ink
            // hanging out. The padding values are then added equally on every side.
            FontRectangle measuredPosterText = TextMeasurer.MeasureRenderableBounds(posterText, posterTextOptions);
            RectangleF posterPanelBounds = new(
                measuredPosterText.X,
                measuredPosterText.Y,
                measuredPosterText.Width + (posterPanelPadding.Width * 2),
                measuredPosterText.Height + (posterPanelPadding.Height * 2));

            // SaveLayer's bounds argument is in local (pre-transform) canvas coordinates. The
            // active drawing transform on the saved state is applied to them when the layer
            // opens, so the rotated panel is allocated at the correct screen-space position
            // without doing the rotation math here. Bounds limit allocation and compositing
            // only; the coordinate system used by commands inside the layer is unchanged.
            Rectangle posterPanelLayerBounds = Rectangle.FromLTRB(
                (int)MathF.Floor(posterPanelBounds.X),
                (int)MathF.Floor(posterPanelBounds.Y),
                (int)MathF.Ceiling(posterPanelBounds.X + posterPanelBounds.Width),
                (int)MathF.Ceiling(posterPanelBounds.Y + posterPanelBounds.Height));

            posterTextOptions.Origin = new PointF(
                posterTextOptions.Origin.X + posterPanelPadding.Width,
                posterTextOptions.Origin.Y + posterPanelPadding.Height);

            canvas.Save(posterPanelOptions);
            canvas.SaveLayer(new GraphicsOptions { BlendPercentage = .94F }, posterPanelLayerBounds);
            canvas.Fill(Brushes.Solid(Color.White.WithAlpha(.86F)), new RectangularPolygon(posterPanelBounds));
            canvas.DrawText(posterTextOptions, posterText, Brushes.Solid(Color.DarkSlateGray), pen: null);
            canvas.Restore();
            canvas.Restore();
        });
    }

    /// <summary>
    /// Draws a transit-map style scene using thick strokes, round joins, dashed paths, labels, and stations.
    /// </summary>
    private static void DrawTransitMap()
    {
        Font titleFont = DisplayFontFamily.CreateFont(34);
        Font labelFont = TextFontFamily.CreateFont(18);
        Font keyFont = TextFontFamily.CreateFont(15);
        Pen harborLinePen = CreateRoutePen(Color.RoyalBlue, 18);
        Pen gardenLoopPen = CreateRoutePen(Color.SeaGreen, 18);
        Pen airportLinePen = CreateRoutePen(Color.OrangeRed, 18);
        Pen walkingTransferPen = Pens.Dash(Color.DimGray, 4);

        SaveSample("02-transit-map.png", SampleSize, canvas =>
        {
            canvas.Fill(Brushes.Solid(Color.AliceBlue));

            // Faint reference grid drawn line-by-line. DrawLine takes a pen plus two end points;
            // this loop reuses the same translucent pen instance for every grid line so the
            // alpha and stroke width stay consistent across the whole background.
            for (int x = 80; x <= 880; x += 80)
            {
                canvas.DrawLine(Pens.Solid(Color.LightSteelBlue.WithAlpha(.55F), 1), new PointF(x, 64), new PointF(x, 576));
            }

            for (int y = 96; y <= 560; y += 80)
            {
                canvas.DrawLine(Pens.Solid(Color.LightSteelBlue.WithAlpha(.55F), 1), new PointF(64, y), new PointF(896, y));
            }

            // RichTextOptions.Origin is the anchor for the laid-out text. DrawText takes a brush
            // for the glyph fill and an optional pen for the glyph outline; passing pen: null is
            // the common case where the text is filled but not stroked.
            canvas.DrawText(
                new RichTextOptions(titleFont) { Origin = new PointF(72, 50) },
                "City Loop Transit",
                Brushes.Solid(Color.MidnightBlue),
                pen: null);

            // Legend panel: reuse the route pens so the key is drawn with the same stroke options as the map.
            canvas.Fill(Brushes.Solid(Color.White.WithAlpha(.9F)), new RectangularPolygon(680, 46, 220, 126));
            canvas.Draw(Pens.Solid(Color.LightSlateGray, 2), new RectangularPolygon(680, 46, 220, 126));
            canvas.DrawLine(harborLinePen, new PointF(708, 78), new PointF(768, 78));
            canvas.DrawLine(gardenLoopPen, new PointF(708, 112), new PointF(768, 112));
            canvas.DrawLine(airportLinePen, new PointF(708, 146), new PointF(768, 146));
            canvas.DrawText(new RichTextOptions(keyFont) { Origin = new PointF(785, 66) }, "Harbor Line", Brushes.Solid(Color.Black), pen: null);
            canvas.DrawText(new RichTextOptions(keyFont) { Origin = new PointF(785, 100) }, "Garden Loop", Brushes.Solid(Color.Black), pen: null);
            canvas.DrawText(new RichTextOptions(keyFont) { Origin = new PointF(785, 134) }, "Airport Line", Brushes.Solid(Color.Black), pen: null);

            // Harbor Line: route pens carry StrokeOptions, so round caps and joins apply to every segment.
            canvas.DrawLine(
                harborLinePen,
                new PointF(105, 398),
                new PointF(235, 398),
                new PointF(365, 398),
                new PointF(505, 398),
                new PointF(650, 398),
                new PointF(815, 398));

            // Garden Loop: the same rounded route pen handles vertical and diagonal segments.
            canvas.DrawLine(
                gardenLoopPen,
                new PointF(505, 148),
                new PointF(505, 260),
                new PointF(505, 398),
                new PointF(650, 520),
                new PointF(815, 520));

            // Airport Line: this route shares transfer points with the other lines.
            canvas.DrawLine(
                airportLinePen,
                new PointF(145, 220),
                new PointF(285, 220),
                new PointF(365, 300),
                new PointF(505, 398),
                new PointF(650, 398),
                new PointF(835, 250));

            // Walking transfer: Pens.Dash uses the same DrawLine API while changing only the pen definition.
            canvas.DrawLine(walkingTransferPen, new PointF(650, 398), new PointF(650, 520));

            // Stations are filled and stroked ellipses layered over the route strokes.
            DrawStation(canvas, new PointF(105, 398), Color.RoyalBlue);
            DrawStation(canvas, new PointF(235, 398), Color.RoyalBlue);
            DrawStation(canvas, new PointF(365, 398), Color.RoyalBlue);
            DrawStation(canvas, new PointF(505, 398), Color.Black);
            DrawStation(canvas, new PointF(650, 398), Color.Black);
            DrawStation(canvas, new PointF(815, 398), Color.RoyalBlue);
            DrawStation(canvas, new PointF(505, 148), Color.SeaGreen);
            DrawStation(canvas, new PointF(505, 260), Color.SeaGreen);
            DrawStation(canvas, new PointF(650, 520), Color.Black);
            DrawStation(canvas, new PointF(815, 520), Color.SeaGreen);
            DrawStation(canvas, new PointF(145, 220), Color.OrangeRed);
            DrawStation(canvas, new PointF(285, 220), Color.OrangeRed);
            DrawStation(canvas, new PointF(365, 300), Color.OrangeRed);
            DrawStation(canvas, new PointF(835, 250), Color.OrangeRed);

            // Station labels: each call positions a single piece of text at an explicit Origin.
            // For one-off labels with no rich runs this is simpler than building a full layout;
            // when a label needs alignment relative to another point, set HorizontalAlignment
            // and VerticalAlignment on the RichTextOptions instead of computing offsets by hand.
            canvas.DrawText(
                new RichTextOptions(labelFont) { Origin = new PointF(457, 418) },
                "Central",
                Brushes.Solid(Color.Black),
                pen: null);

            canvas.DrawText(
                new RichTextOptions(labelFont) { Origin = new PointF(610, 358) },
                "Market",
                Brushes.Solid(Color.Black),
                pen: null);

            canvas.DrawText(
                new RichTextOptions(labelFont) { Origin = new PointF(788, 214) },
                "Airport",
                Brushes.Solid(Color.Black),
                pen: null);

            // Smaller terminus labels reuse keyFont so they match the legend rather than the
            // major-station labels, giving the route ends a deliberately quieter visual weight.
            canvas.DrawText(
                new RichTextOptions(keyFont) { Origin = new PointF(96, 430) },
                "Harbor",
                Brushes.Solid(Color.Black),
                pen: null);

            canvas.DrawText(
                new RichTextOptions(keyFont) { Origin = new PointF(470, 112) },
                "Park",
                Brushes.Solid(Color.Black),
                pen: null);

            canvas.DrawText(
                new RichTextOptions(keyFont) { Origin = new PointF(772, 552) },
                "Gardens",
                Brushes.Solid(Color.Black),
                pen: null);
        });
    }

    /// <summary>
    /// Draws a typography sheet that demonstrates rich text runs, wrapping, bidi, decorations, vertical text, and path text.
    /// </summary>
    private static void DrawTypographySheet()
    {
        Font headlineFont = DisplayFontFamily.CreateFont(48);
        Font sectionFont = TextFontFamily.CreateFont(18);
        Font runFont = TextFontFamily.CreateFont(38);
        Font bodyFont = TextFontFamily.CreateFont(17);
        Font smallFont = TextFontFamily.CreateFont(13);
        Font verticalFont = CjkFontFamily.CreateFont(24);
        Font pathFont = TextFontFamily.CreateFont(24);

        Size typographySize = new(1120, 860);

        SaveSample("03-typography.png", typographySize, canvas =>
        {
            Color paperColor = Color.ParseHex("#F7F7F2");
            Color inkColor = Color.ParseHex("#17202A");
            Color secondaryTextColor = Color.ParseHex("#53616C");
            Color ruleColor = Color.ParseHex("#CCD3D8");
            Color primaryAccentColor = Color.ParseHex("#147D83");
            Color warmAccentColor = Color.ParseHex("#D95D39");
            Color coolAccentColor = Color.ParseHex("#6E4B7E");
            Color highlightAccentColor = Color.ParseHex("#B88917");

            Brush inkBrush = Brushes.Solid(inkColor);
            Brush secondaryTextBrush = Brushes.Solid(secondaryTextColor);
            Brush primaryAccentBrush = Brushes.Solid(primaryAccentColor);
            Brush warmAccentBrush = Brushes.Solid(warmAccentColor);
            Pen rulePen = Pens.Solid(ruleColor, 1);

            canvas.Fill(Brushes.Solid(paperColor));

            const string headerText = "Typography\nA single paragraph can mix fonts, brushes, decorations, and scripts; the layout engine handles wrapping, bidi, and vertical flow on top.";

            // The headline and subtitle share a single layout instead of two DrawText calls.
            // The newline character starts a new line of the same paragraph, and per-run fonts
            // (headlineFont on the title, bodyFont on the subtitle) let the layout engine pick a
            // line height that fits the largest font on each line. LineSpacing is a multiplier
            // applied to that natural line height: 1.0 leaves it untouched, values above 1.0 add
            // breathing room, values below it tighten the lines together.
            RichTextOptions headerOptions = new(headlineFont)
            {
                Origin = new PointF(58, 72),
                WrappingLength = 1000,
                LineSpacing = 1.02F,
                TextRuns =
                [
                    new RichTextRun
                    {
                        Start = 0,
                        End = 10,
                        Font = headlineFont,
                        Brush = primaryAccentBrush,
                        Pen = Pens.Solid(inkColor, 1.2F)
                    },

                    new RichTextRun
                    {
                        Start = 11,
                        End = headerText.Length,
                        Font = bodyFont,
                        Brush = secondaryTextBrush
                    }
                ]
            };

            canvas.DrawText(headerOptions, headerText, inkBrush, null);

            DrawPanel(canvas, "RICH RUNS", new PointF(62, 166), 382, 168, sectionFont, secondaryTextBrush, rulePen);

            // RichTextRun.Start and End are GRAPHEME indices (inclusive Start, exclusive End),
            // not char or code-point indices. For pure ASCII text every char is one grapheme so
            // the numbers happen to match string indices, but for emoji, combining marks, or
            // surrogate pairs the two diverge. SixLabors.Fonts exposes GetGraphemeCount() for
            // exactly this; locating run boundaries dynamically (instead of hard-coding offsets)
            // also keeps the runs correct when the surrounding copy changes. Graphemes outside
            // any run inherit the defaults on the parent RichTextOptions (runFont here, so the
            // line-1 spaces stay heading-sized). Runs may not overlap and should be ordered by
            // Start. Line one contrasts a filled glyph (brush + thin pen) with a hollow glyph
            // (paper-colored fill + bold pen). Line two does the same for decorations: each
            // decoration word is rendered with its own decoration pen, and short body-font runs
            // cover the connector words so line two stays at the body text size.
            const string runText = "Filled Outlined\nUse underline, overline, or strikeout.";

            int filledStart = runText.AsSpan(0, runText.IndexOf("Filled", StringComparison.Ordinal)).GetGraphemeCount();
            int filledEnd = filledStart + "Filled".GetGraphemeCount();
            int outlinedStart = runText.AsSpan(0, runText.IndexOf("Outlined", StringComparison.Ordinal)).GetGraphemeCount();
            int outlinedEnd = outlinedStart + "Outlined".GetGraphemeCount();
            int useStart = runText.AsSpan(0, runText.IndexOf("Use ", StringComparison.Ordinal)).GetGraphemeCount();
            int underlineStart = runText.AsSpan(0, runText.IndexOf("underline", StringComparison.Ordinal)).GetGraphemeCount();
            int underlineEnd = underlineStart + "underline".GetGraphemeCount();
            int overlineStart = runText.AsSpan(0, runText.IndexOf("overline", StringComparison.Ordinal)).GetGraphemeCount();
            int overlineEnd = overlineStart + "overline".GetGraphemeCount();
            int strikeoutStart = runText.AsSpan(0, runText.IndexOf("strikeout", StringComparison.Ordinal)).GetGraphemeCount();
            int strikeoutEnd = strikeoutStart + "strikeout".GetGraphemeCount();
            int runTotal = runText.GetGraphemeCount();

            RichTextOptions runOptions = new(runFont)
            {
                Origin = new PointF(82, 230),
                LineSpacing = 1.4F,
                TextRuns =
                [
                    new RichTextRun
                    {
                        Start = filledStart,
                        End = filledEnd,
                        Font = runFont,
                        Brush = warmAccentBrush,
                        Pen = Pens.Solid(inkColor, 1.8F)
                    },

                    new RichTextRun
                    {
                        Start = outlinedStart,
                        End = outlinedEnd,
                        Font = runFont,
                        Brush = Brushes.Solid(paperColor),
                        Pen = Pens.Solid(primaryAccentColor, 2.2F)
                    },

                    new RichTextRun
                    {
                        Start = useStart,
                        End = underlineStart,
                        Font = bodyFont,
                        Brush = inkBrush
                    },

                    new RichTextRun
                    {
                        Start = underlineStart,
                        End = underlineEnd,
                        Font = bodyFont,
                        Brush = inkBrush,
                        TextDecorations = TextDecorations.Underline,
                        UnderlinePen = Pens.Solid(warmAccentColor, 2)
                    },

                    new RichTextRun
                    {
                        Start = underlineEnd,
                        End = overlineStart,
                        Font = bodyFont,
                        Brush = inkBrush
                    },

                    new RichTextRun
                    {
                        Start = overlineStart,
                        End = overlineEnd,
                        Font = bodyFont,
                        Brush = inkBrush,
                        TextDecorations = TextDecorations.Overline,
                        OverlinePen = Pens.Solid(coolAccentColor, 2)
                    },

                    new RichTextRun
                    {
                        Start = overlineEnd,
                        End = strikeoutStart,
                        Font = bodyFont,
                        Brush = inkBrush
                    },

                    new RichTextRun
                    {
                        Start = strikeoutStart,
                        End = strikeoutEnd,
                        Font = bodyFont,
                        Brush = inkBrush,
                        TextDecorations = TextDecorations.Strikeout,
                        StrikeoutPen = Pens.Solid(highlightAccentColor, 2)
                    },

                    new RichTextRun
                    {
                        Start = strikeoutEnd,
                        End = runTotal,
                        Font = bodyFont,
                        Brush = inkBrush
                    }
                ]
            };

            canvas.DrawText(runOptions, runText, inkBrush, null);

            DrawPanel(canvas, "WRAPPING AND MEASUREMENT", new PointF(500, 166), 560, 158, sectionFont, secondaryTextBrush, rulePen);

            // Two text-shaping concerns meet here. WrappingLength caps each line's advance, so
            // the layout engine inserts soft line breaks when a line would otherwise overflow.
            // TextMeasurer then reports back the size that the laid-out text actually occupies,
            // which lets the caller draw a panel or callout that fits the wrapped paragraph
            // exactly, no matter how the breaks fell.
            const string measuredText = "Wrapping length caps each line's advance, and the layout engine inserts a soft break whenever the next word would push the line past that limit.";
            RichTextOptions measuredOptions = new(bodyFont)
            {
                Origin = new PointF(520, 222),
                WrappingLength = 500,
                LineSpacing = 1.22F,
                TextRuns =
                [
                    new RichTextRun
                    {
                        Start = 0,
                        End = 15,
                        Font = bodyFont,
                        Brush = inkBrush,
                        TextDecorations = TextDecorations.Underline,
                        UnderlinePen = Pens.Solid(primaryAccentColor, 2)
                    },

                    new RichTextRun
                    {
                        Start = 15,
                        End = measuredText.Length,
                        Font = bodyFont,
                        Brush = inkBrush
                    }
                ]
            };

            // MeasureRenderableBounds returns the union of the logical advance rectangle and the
            // ink bounds, so a panel sized from it contains every glyph (including trailing
            // whitespace, italic overhang, and any side bearings) without clipping. Pass the same
            // options the draw call uses, otherwise WrappingLength, LineSpacing, or font fallback
            // can change where the lines break and the box drifts off the text.
            FontRectangle measuredBox = TextMeasurer.MeasureRenderableBounds(measuredText, measuredOptions);
            RectangularPolygon measuredBackground = new(
                measuredBox.X - 10,
                measuredBox.Y - 8,
                measuredBox.Width + 20,
                measuredBox.Height + 16);

            canvas.Fill(Brushes.Solid(Color.White.WithAlpha(.8F)), measuredBackground);
            canvas.Draw(Pens.Solid(ruleColor, 1), measuredBackground);
            canvas.DrawText(measuredOptions, measuredText, inkBrush, null);

            DrawPanel(canvas, "BIDI AND FALLBACK", new PointF(62, 352), 382, 300, sectionFont, secondaryTextBrush, rulePen);

            const string bidiBody = "Mixed-script paragraphs stay readable because the Unicode Bidirectional Algorithm resolves direction per visual line. الكلمات العربية تتدفق من اليمين إلى اليسار. הטקסט בעברית זורם גם מימין לשמאל. Embedded numbers like 2026 keep their left-to-right order even inside a right-to-left run.";
            const string bidiCaption = "RichTextRun indices target the logical (typed) order; layout flips them visually.";
            string bidiText = $"{bidiBody}\n{bidiCaption}";

            // RichTextRun.Start and End count graphemes, not chars and not code points. For text
            // containing only basic Arabic, Hebrew, and Latin letters one grapheme equals one
            // code point, so a code-point count would coincide here, but content with combining
            // marks, emoji ZWJ sequences, or surrogate pairs would diverge. SixLabors.Fonts
            // exposes GetGraphemeCount() on string and ReadOnlySpan<char> for exactly this.
            int bidiArabicStart = bidiText.AsSpan(0, bidiText.IndexOf("الكلمات", StringComparison.Ordinal)).GetGraphemeCount();
            int bidiHebrewStart = bidiText.AsSpan(0, bidiText.IndexOf("הטקסט", StringComparison.Ordinal)).GetGraphemeCount();
            int bidiNumbersStart = bidiText.AsSpan(0, bidiText.IndexOf("Embedded", StringComparison.Ordinal)).GetGraphemeCount();
            int bidiBodyLength = bidiBody.GetGraphemeCount();
            int bidiTotalLength = bidiText.GetGraphemeCount();

            RichTextOptions bidiOptions = new(bodyFont)
            {
                Origin = new PointF(82, 422),
                FallbackFontFamilies = [ArabicFontFamily],
                WrappingLength = 322,
                LineSpacing = 1.25F,
                TextRuns =
                [
                    new RichTextRun
                    {
                        Start = 0,
                        End = bidiArabicStart,
                        Font = bodyFont,
                        Brush = inkBrush
                    },

                    new RichTextRun
                    {
                        Start = bidiArabicStart,
                        End = bidiHebrewStart,
                        Font = bodyFont,
                        Brush = primaryAccentBrush
                    },

                    new RichTextRun
                    {
                        Start = bidiHebrewStart,
                        End = bidiNumbersStart,
                        Font = bodyFont,
                        Brush = warmAccentBrush
                    },

                    new RichTextRun
                    {
                        Start = bidiNumbersStart,
                        End = bidiBodyLength,
                        Font = bodyFont,
                        Brush = inkBrush
                    },

                    new RichTextRun
                    {
                        Start = bidiBodyLength + 1,
                        End = bidiTotalLength,
                        Font = smallFont,
                        Brush = secondaryTextBrush
                    }
                ]
            };

            // The string is held in LOGICAL order, the order a person types or reads it. At
            // shape time the Unicode Bidirectional Algorithm assigns each run of characters a
            // visual direction, so the Arabic and Hebrew sentences render right-to-left while
            // surrounding Latin text and embedded numbers keep their own left-to-right order.
            // FallbackFontFamilies works per-glyph: when bodyFont has no glyph for an Arabic or
            // Hebrew codepoint, ArabicFontFamily supplies it without splitting the parent run.
            canvas.DrawText(bidiOptions, bidiText, inkBrush, null);

            DrawPanel(canvas, "VERTICAL MIXED LAYOUT", new PointF(500, 352), 560, 300, sectionFont, secondaryTextBrush, rulePen);

            const string verticalText = "한국어 세로쓰기는 위에서 아래로 흐릅니다.\nImageSharp Drawing은 영문 단어를 옆으로 회전시켜 함께 배치합니다.\n숫자 2026과 같은 아라비아 숫자는 가로 방향을 그대로 유지합니다.\nRichTextRun을 사용하면 단어마다 색과 장식을 다르게 줄 수 있습니다.\n한국어와 English가 한 단락 안에서 자연스럽게 어우러집니다.";
            int verticalEnglishStart = verticalText.AsSpan(0, verticalText.IndexOf("ImageSharp", StringComparison.Ordinal)).GetGraphemeCount();
            int verticalNumberStart = verticalText.AsSpan(0, verticalText.IndexOf("숫자", StringComparison.Ordinal)).GetGraphemeCount();
            int verticalRunStart = verticalText.AsSpan(0, verticalText.IndexOf("RichTextRun", StringComparison.Ordinal)).GetGraphemeCount();
            int verticalLength = verticalText.GetGraphemeCount();

            RichTextOptions verticalOptions = new(verticalFont)
            {
                Origin = new PointF(520, 410),
                LayoutMode = LayoutMode.VerticalMixedRightLeft,
                WrappingLength = 205,
                LineSpacing = 1.1F,
                FallbackFontFamilies = [TextFontFamily],
                TextRuns =
                [
                    new RichTextRun
                    {
                        Start = 0,
                        End = verticalEnglishStart,
                        Font = verticalFont,
                        Brush = inkBrush
                    },

                    new RichTextRun
                    {
                        Start = verticalEnglishStart,
                        End = verticalNumberStart,
                        Font = verticalFont,
                        Brush = primaryAccentBrush,
                        TextDecorations = TextDecorations.Underline,
                        UnderlinePen = Pens.Solid(primaryAccentColor, 2)
                    },

                    new RichTextRun
                    {
                        Start = verticalNumberStart,
                        End = verticalRunStart,
                        Font = verticalFont,
                        Brush = warmAccentBrush,
                        TextDecorations = TextDecorations.Overline,
                        OverlinePen = Pens.Solid(warmAccentColor, 2)
                    },

                    new RichTextRun
                    {
                        Start = verticalRunStart,
                        End = verticalLength,
                        Font = verticalFont,
                        Brush = inkBrush
                    }
                ]
            };

            // LayoutMode.VerticalMixedRightLeft stacks each line top-to-bottom and advances new
            // lines from right to left, the conventional layout for traditional Korean and
            // Japanese vertical typography (the first line sits on the right, later lines fall
            // progressively to its left). RichTextOptions.Origin is the alignment anchor of the
            // laid-out box, not always its top-left: with the default HorizontalAlignment.Left
            // and VerticalAlignment.Top it coincides with the top-left, but Right anchors the
            // box's right edge to Origin and Center pivots the box around it, and the same is
            // true vertically. CJK glyphs render upright; Latin words and digits are rotated
            // sideways so they remain legible inside the vertical flow. WrappingLength is the
            // per-line budget along the vertical axis; generous values keep each line whole and
            // let the explicit \n characters control where breaks fall.
            canvas.DrawText(verticalOptions, verticalText, inkBrush, null);

            Path textPath = new(new CubicBezierLineSegment(
                new PointF(70, 805),
                new PointF(265, 735),
                new PointF(710, 845),
                new PointF(1050, 760)));

            const string pathText = "Path text follows the curve while runs vary colour, weight, and decoration.";
            RichTextOptions pathOptions = new(pathFont)
            {
                WrappingLength = textPath.ComputeLength(),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                TextRuns =
                [
                    new RichTextRun
                    {
                        Start = 0,
                        End = 4,
                        Font = pathFont,
                        Brush = warmAccentBrush,
                        Pen = Pens.Solid(inkColor, 1.1F)
                    },

                    new RichTextRun
                    {
                        Start = 4,
                        End = 22,
                        Font = pathFont,
                        Brush = inkBrush
                    },

                    new RichTextRun
                    {
                        Start = 22,
                        End = 27,
                        Font = pathFont,
                        Brush = primaryAccentBrush,
                        TextDecorations = TextDecorations.Underline,
                        UnderlinePen = Pens.Solid(primaryAccentColor, 2)
                    },

                    new RichTextRun
                    {
                        Start = 27,
                        End = pathText.Length,
                        Font = pathFont,
                        Brush = inkBrush
                    }
                ]
            };

            // Passing a path to DrawText tells the renderer to advance each glyph along the curve
            // rather than along a straight baseline. WrappingLength = textPath.ComputeLength()
            // matches the wrap budget to the curve's arc length, so the text fills the path
            // without overflowing past its end. The guide path can be drawn as well as used as
            // a baseline — that is a design choice. Strokes, ribbons, dashed leaders, or a
            // decorative trail under the glyphs all work, and so does omitting the stroke and
            // rendering only the text. The two operations are independent.
            canvas.Draw(Pens.Solid(ruleColor, 1.5F), textPath);
            canvas.DrawText(pathOptions, pathText, textPath, inkBrush, null);
        });

        static void DrawPanel(
            DrawingCanvas canvas,
            string title,
            PointF origin,
            float width,
            float height,
            Font font,
            Brush titleBrush,
            Pen rulePen)
        {
            RectangularPolygon panel = new(origin.X, origin.Y, width, height);

            // RectangularPolygon implements IPath. The same shape can be both filled (with a
            // semi-transparent brush) and stroked (with a pen), which keeps the panel framing
            // consistent across the sheet without re-allocating geometry.
            canvas.Fill(Brushes.Solid(Color.White.WithAlpha(.54F)), panel);
            canvas.Draw(rulePen, panel);
            RichTextOptions titleOptions = new(font)
            {
                Origin = new PointF(origin.X + 18, origin.Y + 30),
                TextRuns =
                [
                    new RichTextRun
                    {
                        Start = 0,
                        End = title.Length,
                        Font = font,
                        Brush = titleBrush
                    }
                ]
            };

            canvas.DrawText(titleOptions, title, titleBrush, null);
        }
    }

    /// <summary>
    /// Demonstrates how a photograph and an IPath combine through the drawing API: a
    /// before/after Apply wipe, an Apply pixelate redaction, an ImageBrush filling a
    /// star, and a TextBuilder glyph clip masking DrawImage to letterforms.
    /// </summary>
    private static void DrawImageProcessingMask()
    {
        using Image<Rgba32> source = CreateSourceImage();
        Font headerFont = DisplayFontFamily.CreateFont(34);
        Font subtitleFont = TextFontFamily.CreateFont(14);
        Font panelTitleFont = TextFontFamily.CreateFont(18);
        Font panelCaptionFont = TextFontFamily.CreateFont(12);
        Font glyphMaskFont = DisplayFontFamily.CreateFont(118);

        Color paperColor = Color.ParseHex("#F4F4EE");
        Color inkColor = Color.ParseHex("#15202A");
        Color secondaryColor = Color.ParseHex("#5A6470");
        Color accentColor = Color.ParseHex("#147D83");
        Color warmColor = Color.ParseHex("#D9551A");

        SaveSample("04-image-processing-mask.png", SampleSize, canvas =>
        {
            canvas.Fill(Brushes.Solid(paperColor));

            canvas.DrawText(
                new RichTextOptions(headerFont) { Origin = new PointF(40, 30) },
                "Image compositing",
                Brushes.Solid(inkColor),
                pen: null);

            canvas.DrawText(
                new RichTextOptions(subtitleFont) { Origin = new PointF(40, 66) },
                "Apply() runs a processor inside an IPath, ImageBrush fills one with a photo, and Save() clips drawing to one.",
                Brushes.Solid(secondaryColor),
                pen: null);

            // Four panels in a 2x2 grid. Each demonstrates one technique against the same
            // photograph (Lake.jpg, linked from the test fixtures via the csproj), so the
            // visual difference between panels is the API call, not the input.
            Rectangle panelTopLeft = new(40, 90, 430, 250);
            Rectangle panelTopRight = new(490, 90, 430, 250);
            Rectangle panelBottomLeft = new(40, 360, 430, 250);
            Rectangle panelBottomRight = new(490, 360, 430, 250);

            DrawBeforeAfterWipePanel(canvas, panelTopLeft, source, panelTitleFont, panelCaptionFont, inkColor, secondaryColor, accentColor);
            DrawRedactionPanel(canvas, panelTopRight, source, panelTitleFont, panelCaptionFont, inkColor, secondaryColor, warmColor);
            DrawImageBrushPanel(canvas, panelBottomLeft, source, panelTitleFont, panelCaptionFont, inkColor, secondaryColor, accentColor);
            DrawPhotoInTextPanel(canvas, panelBottomRight, source, glyphMaskFont, panelTitleFont, panelCaptionFont, inkColor, secondaryColor);
        });

        static void DrawBeforeAfterWipePanel(
            DrawingCanvas canvas,
            Rectangle panel,
            Image<Rgba32> source,
            Font titleFont,
            Font captionFont,
            Color titleColor,
            Color captionColor,
            Color dividerColor)
        {
            float imageTop = DrawPanelChrome(canvas, panel, "Before / after wipe", "Apply() takes its own IPath, so a half-width rectangle is enough to scope OilPaint to the right of the divider.", titleFont, captionFont, titleColor, captionColor);

            RectangleF imageArea = ImageAreaBelow(panel, imageTop);
            canvas.DrawImage(source, source.Bounds, imageArea, null);

            // Apply masks the processor to its IPath argument; the saved canvas state is not
            // involved. A simple right-half rectangle is the entire mask, so OilPaint runs only
            // on those pixels and the left half stays as the original photograph.
            float midX = imageArea.X + (imageArea.Width / 2F);
            RectangularPolygon afterRegion = new(
                midX,
                imageArea.Y,
                imageArea.Width / 2F,
                imageArea.Height);

            // Saturate(amount > 1) boosts colour intensity before OilPaint smears the pixels
            // into painterly strokes; the saturated input gives the strokes more visible
            // contrast. OilPaint defaults are (levels: 10, brushSize: 15); the smaller
            // brushSize and slightly higher levels here produce tighter strokes.
            canvas.Apply(afterRegion, ctx => ctx.Saturate(1.6F).OilPaint(levels: 15, brushSize: 5));

            // Divider line so the eye can lock onto the wipe boundary even on flat regions of
            // the photograph (sky, water) where the colour change alone would be subtle.
            canvas.DrawLine(
                Pens.Solid(dividerColor, 2),
                new PointF(midX, imageArea.Y),
                new PointF(midX, imageArea.Y + imageArea.Height));
        }

        static void DrawRedactionPanel(
            DrawingCanvas canvas,
            Rectangle panel,
            Image<Rgba32> source,
            Font titleFont,
            Font captionFont,
            Color titleColor,
            Color captionColor,
            Color markerColor)
        {
            float imageTop = DrawPanelChrome(canvas, panel, "Privacy redaction", "Apply() runs an ImageSharp processor inside any IPath. An ellipse pixelates a face-shaped region and leaves the rest alone.", titleFont, captionFont, titleColor, captionColor);

            RectangleF imageArea = ImageAreaBelow(panel, imageTop);
            canvas.DrawImage(source, source.Bounds, imageArea, null);

            // Any IPath works as a redaction region; Pixelate is just one IImageProcessingContext
            // extension. Brightness, GaussianBlur, Hue, Invert, BoxBlur, and friends plug in
            // identically. Pixels outside the path keep their original values, and using an
            // ellipse (as you would for a face) feathers the redaction at the corners so the
            // surrounding pixels do not square off the censored area.
            EllipsePolygon redaction = new(
                imageArea.X + (imageArea.Width * 0.5F),
                imageArea.Y + (imageArea.Height * 0.55F),
                imageArea.Width * 0.32F,
                imageArea.Height * 0.28F);

            canvas.Apply(redaction, ctx => ctx.Pixelate(10));
            canvas.Draw(Pens.Solid(markerColor, 2), redaction);
        }

        static void DrawImageBrushPanel(
            DrawingCanvas canvas,
            Rectangle panel,
            Image<Rgba32> source,
            Font titleFont,
            Font captionFont,
            Color titleColor,
            Color captionColor,
            Color outlineColor)
        {
            float imageTop = DrawPanelChrome(canvas, panel, "Image as a brush", "ImageBrush wraps a photograph as a Brush, so any IPath can be filled with it as a texture instead of a solid colour.", titleFont, captionFont, titleColor, captionColor);

            RectangleF imageArea = ImageAreaBelow(panel, imageTop);

            // A 5-pointed star sized to fill the available image area and centred inside it.
            // The brush below carries the photograph; this path determines which pixels of it
            // appear on screen.
            PointF starCenter = new(
                imageArea.X + (imageArea.Width / 2F),
                imageArea.Y + (imageArea.Height / 2F));
            float outerRadius = (MathF.Min(imageArea.Width, imageArea.Height) / 2F) - 6F;
            float innerRadius = outerRadius * 0.5F;
            Star star = new(starCenter.X, starCenter.Y, 5, innerRadius, outerRadius);

            // ImageBrush samples the source image in world coordinates: a destination pixel at
            // (x, y) reads source pixel (x - offset.X, y - offset.Y) inside SourceRegion. The
            // offset here aligns Lake.jpg's mountain (≈ image (640, 200)) with the star centre
            // so the texture inside the star is recognisable rather than an arbitrary crop.
            // No DrawImage call and no clip — the brush itself carries the photograph and the
            // star path defines exactly which pixels of it appear on screen.
            Point brushOffset = new(
                (int)starCenter.X - 640,
                (int)starCenter.Y - 200);

            ImageBrush<Rgba32> photoBrush = new(source, source.Bounds, brushOffset);

            canvas.Fill(photoBrush, star);
            canvas.Draw(Pens.Solid(outlineColor, 2), star);
        }

        static void DrawPhotoInTextPanel(
            DrawingCanvas canvas,
            Rectangle panel,
            Image<Rgba32> source,
            Font glyphFont,
            Font titleFont,
            Font captionFont,
            Color titleColor,
            Color captionColor)
        {
            float imageTop = DrawPanelChrome(canvas, panel, "Photo in text", "TextBuilder.GeneratePaths returns one IPath per glyph; pass them as a clip and the photo only shows through the letters.", titleFont, captionFont, titleColor, captionColor);

            RectangleF imageArea = ImageAreaBelow(panel, imageTop);

            // Pick a glyph size that fills most of the image area, then centre the laid-out text
            // inside imageArea. Measuring with Origin = (0,0) gives ink-relative bounds; the
            // final Origin is adjusted by both the centring offset and the negative ink offset
            // so the rendered glyphs land in the middle of imageArea.
            float glyphHeight = imageArea.Height * 0.85F;
            Font sizedGlyphFont = new(glyphFont, glyphHeight);

            TextOptions probe = new(sizedGlyphFont) { Origin = PointF.Empty };
            FontRectangle textBounds = TextMeasurer.MeasureRenderableBounds("MASK", probe);

            TextOptions glyphOptions = new(sizedGlyphFont)
            {
                Origin = new PointF(
                    imageArea.X + ((imageArea.Width - textBounds.Width) / 2F) - textBounds.X,
                    imageArea.Y + ((imageArea.Height - textBounds.Height) / 2F) - textBounds.Y),
            };

            // GeneratePaths returns one IPath per glyph. canvas.Save accepts params IPath[] so
            // the whole collection becomes a compound clip, but ShapeOptions.BooleanOperation
            // must be set to Intersection: the default Difference would cut the glyph shapes
            // OUT of the photograph, the opposite of "image inside text".
            IPathCollection letters = TextBuilder.GeneratePaths("MASK", glyphOptions);
            IPath[] glyphClips = [.. letters];

            DrawingOptions clipToGlyphs = new()
            {
                ShapeOptions = new ShapeOptions { BooleanOperation = BooleanOperation.Intersection },
            };

            canvas.Fill(Brushes.Solid(Color.ParseHex("#E2DCC2")), new RectangularPolygon(imageArea));
            canvas.Save(clipToGlyphs, glyphClips);
            canvas.DrawImage(source, source.Bounds, imageArea, null);
            canvas.Restore();

            canvas.Draw(Pens.Solid(titleColor, 1), letters);
        }

        static RectangleF ImageAreaBelow(Rectangle panel, float top)
        {
            const float horizontalPadding = 15F;
            const float bottomPadding = 12F;
            return new RectangleF(
                panel.X + horizontalPadding,
                top,
                panel.Width - (horizontalPadding * 2F),
                panel.Bottom - bottomPadding - top);
        }

        static float DrawPanelChrome(
            DrawingCanvas canvas,
            Rectangle panel,
            string title,
            string caption,
            Font titleFont,
            Font captionFont,
            Color titleColor,
            Color captionColor)
        {
            RectangularPolygon panelShape = new(panel);
            canvas.Fill(Brushes.Solid(Color.White), panelShape);
            canvas.Draw(Pens.Solid(Color.ParseHex("#D7D2C0"), 1), panelShape);

            RichTextOptions titleOptions = new(titleFont) { Origin = new PointF(panel.X + 15, panel.Y + 24) };
            canvas.DrawText(titleOptions, title, Brushes.Solid(titleColor), pen: null);

            RichTextOptions captionOptions = new(captionFont)
            {
                Origin = new PointF(panel.X + 15, panel.Y + 46),
                WrappingLength = panel.Width - 30,
                LineSpacing = 1.15F,
            };
            canvas.DrawText(captionOptions, caption, Brushes.Solid(captionColor), pen: null);

            // Measure the laid-out caption (post-wrap) so the image area starts under it.
            // MeasureRenderableBounds covers advance and ink, so multi-line captions don't clip
            // the image and short captions don't waste vertical space.
            FontRectangle captionBounds = TextMeasurer.MeasureRenderableBounds(caption, captionOptions);
            return captionBounds.Y + captionBounds.Height + 10;
        }
    }

    /// <summary>
    /// Creates a route pen with round joins and caps.
    /// </summary>
    /// <param name="color">The route color.</param>
    /// <param name="width">The route stroke width.</param>
    /// <returns>The configured route pen.</returns>
    private static SolidPen CreateRoutePen(Color color, float width)
    {
        PenOptions options = new(Brushes.Solid(color), width, null)
        {
            StrokeOptions = new StrokeOptions
            {
                LineCap = LineCap.Round,
                LineJoin = LineJoin.Round
            }
        };

        return new SolidPen(options);
    }

    /// <summary>
    /// Draws a transit station marker.
    /// </summary>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="location">The station center.</param>
    /// <param name="color">The station outline color.</param>
    private static void DrawStation(DrawingCanvas canvas, PointF location, Color color)
    {
        canvas.Fill(Brushes.Solid(Color.White), new EllipsePolygon(location, 13));
        canvas.Draw(Pens.Solid(color, 5), new EllipsePolygon(location, 13));
    }

    /// <summary>
    /// Loads the photographic source used by the masking sample. The image is one of the JPEGs
    /// that already ship with the test fixtures and is wired into this project's csproj as a
    /// linked content file, so no extra asset has to be added to the repository.
    /// </summary>
    private static Image<Rgba32> CreateSourceImage()
    {
        string fullPath = IOPath.Combine(AppContext.BaseDirectory, "Images", "Lake.jpg");
        return Image.Load<Rgba32>(fullPath);
    }

    /// <summary>
    /// Creates an image, draws a sample into a canvas, and saves the PNG output.
    /// </summary>
    /// <param name="fileName">The output file name.</param>
    /// <param name="size">The output image size.</param>
    /// <param name="draw">The drawing callback that records canvas commands.</param>
    private static void SaveSample(string fileName, Size size, CanvasAction draw)
    {
        using Image<Rgba32> image = new(size.Width, size.Height);
        image.Mutate(ctx => ctx.Paint(draw));

        SaveImage(image, fileName);
    }

    /// <summary>
    /// Saves an image into the sample output directory.
    /// </summary>
    /// <param name="image">The image to save.</param>
    /// <param name="fileName">The output file name.</param>
    private static void SaveImage(Image image, string fileName)
    {
        string fullPath = IOPath.Combine(AppContext.BaseDirectory, OutputDirectory, fileName);
        IODirectory.CreateDirectory(IOPath.GetDirectoryName(fullPath));
        image.Save(fullPath);
    }

    /// <summary>
    /// Loads a sample font family from the copied font assets.
    /// </summary>
    /// <param name="fileName">The font file name.</param>
    /// <returns>The loaded font family.</returns>
    private static FontFamily LoadFontFamily(string fileName)
        => SampleFonts.Add(IOPath.Combine(AppContext.BaseDirectory, FontsDirectory, fileName));
}
