// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Converts a <see cref="LinearBarcodeSymbol"/> into canvas commands. All bars render through a single fill of
/// one path collection; each text placement renders through one text command.
/// </summary>
internal static class LinearBarcodeEmitter
{
    /// <summary>
    /// The clear space in modules between the ink of a line of the human readable interpretation and the
    /// bar edge that line faces. Section 5.2.5 of the GS1 General Specifications sets the minimum at 0.5X
    /// both below the main symbol and above an add-on symbol, and states: "Normally the minimum is one
    /// module, which is close enough to keep the human readable interpretation associated with the
    /// symbol." The same space applies above a caption, where no document gives a figure.
    /// </summary>
    private const float TextGap = 1F;

    /// <summary>
    /// Renders the symbol onto the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of everything the call draws, including any text that overhangs the symbol, in pixels.</param>
    public static void Emit(DrawingCanvas canvas, LinearBarcodeSymbol symbol, BarcodeOptions options, PointF origin)
        => Layout(canvas, symbol, options, origin, true);

    /// <summary>
    /// Measures the area the symbol covers without drawing anything, so a caller can size the image
    /// before it draws and no glyph falls outside it.
    /// </summary>
    /// <param name="canvas">The canvas whose font metrics measure the text.</param>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner the symbol would draw from, in pixels.</param>
    /// <returns>The area the symbol covers, including any text that overhangs the bars.</returns>
    public static RectangleF Measure(DrawingCanvas canvas, LinearBarcodeSymbol symbol, BarcodeOptions options, PointF origin)
        => Layout(canvas, symbol, options, origin, false);

    /// <summary>
    /// Lays the symbol out and, when asked, draws it. Both callers share one pass, so the measured area
    /// and the drawn area cannot drift apart.
    /// </summary>
    /// <param name="canvas">The canvas to measure with and, when drawing, to render onto.</param>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of everything the call draws, in pixels.</param>
    /// <param name="draw">Whether to render, rather than only measure.</param>
    /// <returns>The area the symbol covers, including any text that overhangs the bars.</returns>
    private static RectangleF Layout(DrawingCanvas canvas, LinearBarcodeSymbol symbol, BarcodeOptions options, PointF origin, bool draw)
    {
        float moduleWidth = options.ModuleWidth;
        Guard.MustBeGreaterThan(moduleWidth, 0, nameof(options.ModuleWidth));

        float widthInModules = symbol.WidthInModules;
        float symbolLeft = origin.X;
        if (options.IncludeQuietZones)
        {
            widthInModules += symbol.LeadingQuietZone + symbol.TrailingQuietZone;
            symbolLeft += symbol.LeadingQuietZone * moduleWidth;
        }

        // The drawn area grows to hold the human readable interpretation in both axes: text hangs below the
        // bars the way the nominal ISO/IEC 15420 symbol reserves its text region, and a caption wider than
        // the symbol, an ISBN line for example, widens the background.
        float backgroundLeft = origin.X;
        float backgroundRight = origin.X + (widthInModules * moduleWidth);
        Font? captionFont = null;
        Font? scaledFont = null;

        // Every text line prints at 9 point or larger, the one size floor the standards state.
        Font? digitFont = options.Font is null || options.Font.Size >= EanUpcEncoder.MinimumTextPoints
            ? options.Font
            : new Font(options.Font, EanUpcEncoder.MinimumTextPoints);

        // A digit prints inside its own symbol character, so its size is capped to that cell and the
        // text cannot run into its neighbour. The floor wins over the cap where the two disagree.
        if (digitFont is not null)
        {
            float cap = float.MaxValue;
            TextOptions capOptions = new(digitFont);
            for (int i = 0; i < symbol.Text.Length; i++)
            {
                BarcodeTextPlacement capPlacement = symbol.Text[i];
                if (capPlacement.IsCaption)
                {
                    continue;
                }

                // What runs into a neighbour is the glyph bounds, so the cap measures those. The
                // renderable bounds are their union with the advance, and the advance is space the glyph
                // reserves rather than covers, so capping on it shrinks the text for no gain.
                float cellText = TextMeasurer.MeasureBounds(capPlacement.Text, capOptions).Width;
                if (cellText <= 0)
                {
                    continue;
                }

                float cell = (capPlacement.Right - capPlacement.Left) * moduleWidth;
                cap = MathF.Min(cap, digitFont.Size * cell / (cellText * capPlacement.FontScale));
            }

            float capped = MathF.Max(MathF.Min(digitFont.Size, cap), EanUpcEncoder.MinimumTextPoints);
            if (capped != digitFont.Size)
            {
                digitFont = new Font(digitFont, capped);
            }
        }

        // One rule places every line: the ink edge facing the bars sits TextGap modules from the bar edge
        // the line names, and the room a line needs is its own ink plus that gap. A line above the bars
        // therefore pushes the whole bar block down by the room it needs, which is why the band is
        // measured here, where the fonts are known, rather than guessed at as a constant by each encoder.
        float topBand = 0;
        float bottomInModules = symbol.HeightInModules;
        if (digitFont is not null)
        {
            // A symbol resolves to at most three fonts: the digits, the scaled quiet zone digits and the
            // caption. Each keeps its own measuring options, so a symbol that alternates between them,
            // as UPC-A does, still builds one set per font.
            TextOptions digitOptions = new(digitFont);
            TextOptions? scaledOptions = null;
            TextOptions? captionOptions = null;
            for (int i = 0; i < symbol.Text.Length; i++)
            {
                BarcodeTextPlacement placement = symbol.Text[i];
                Font font = ResolveFont(placement, digitFont, ref captionFont, ref scaledFont, options);
                TextOptions measureOptions;
                if (placement.IsCaption)
                {
                    measureOptions = captionOptions ??= new TextOptions(font);
                }
                else if (ReferenceEquals(font, digitFont))
                {
                    measureOptions = digitOptions;
                }
                else
                {
                    measureOptions = scaledOptions ??= new TextOptions(font);
                }

                float inkInModules = InkRise(font) / moduleWidth;

                if (placement.Side == BarcodeTextSide.AboveBars)
                {
                    topBand = MathF.Max(topBand, inkInModules + TextGap - placement.BarEdge);
                }
                else
                {
                    bottomInModules = MathF.Max(bottomInModules, placement.BarEdge + TextGap + inkInModules);
                }

                float textWidth = TextMeasurer.MeasureRenderableBounds(placement.Text, measureOptions).Width;
                float center = (MathF.Round(symbolLeft + (placement.Left * moduleWidth)) + MathF.Round(symbolLeft + (placement.Right * moduleWidth))) * 0.5F;
                backgroundLeft = MathF.Min(backgroundLeft, center - (textWidth * 0.5F));
                backgroundRight = MathF.Max(backgroundRight, center + (textWidth * 0.5F));
            }
        }

        float heightInPixels = (topBand + bottomInModules) * moduleWidth;

        // The origin is the top left corner of everything this call draws. When text overhangs the symbol on
        // the left (an ISBN caption wider than the bars), the whole rendering shifts right by a whole number
        // of pixels so the leftmost element starts at the origin without moving the bars off the pixel grid.
        // Nothing renders above the origin because the band already holds every line above the bars.
        float shift = MathF.Ceiling(origin.X - backgroundLeft);
        if (shift > 0)
        {
            symbolLeft += shift;
            backgroundLeft += shift;
            backgroundRight += shift;
        }

        RectangleF bounds = new(
            MathF.Floor(backgroundLeft),
            origin.Y,
            MathF.Ceiling(backgroundRight) - MathF.Floor(backgroundLeft),
            MathF.Ceiling(heightInPixels));

        if (!draw)
        {
            return bounds;
        }

        if (options.Background is not null)
        {
            // The background is a coverage area, not symbol geometry, so it takes the measured bounds:
            // those snap outward to whole pixels, which keeps the edges crisp and stops a fractionally
            // measured text extent leaving a seam.
            canvas.Fill(options.Background, new RectanglePolygon(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        int[] runs = symbol.RunWidths;
        IPath[] bars = new IPath[(runs.Length + 1) / 2];
        float x = symbolLeft;
        for (int i = 0; i < runs.Length; i++)
        {
            float runWidth = runs[i] * moduleWidth;
            if ((i & 1) == 0)
            {
                // Each bar edge rounds from its exact accumulated position onto the device pixel grid, so
                // bars stay crisp for scanning at any module width and rounding cannot drift across the
                // symbol. Integral module widths, heights and origins round to themselves.
                int bar = i >> 1;
                float barLeft = MathF.Round(x);
                float barRight = MathF.Round(x + runWidth);
                float barTop = MathF.Round(origin.Y + ((topBand + symbol.BarTops[bar]) * moduleWidth));
                float barBottom = MathF.Round(origin.Y + ((topBand + symbol.BarTops[bar] + symbol.BarHeights[bar]) * moduleWidth));
                bars[bar] = new RectanglePolygon(barLeft, barTop, barRight - barLeft, barBottom - barTop);
            }

            x += runWidth;
        }

        canvas.Fill(options.BarBrush, new PathCollection(bars));

        if (digitFont is null)
        {
            return bounds;
        }

        // Each of the three fonts a symbol can resolve keeps its own draw options, which carry the same
        // settings for every line and differ only in the origin, so a line sets that and draws.
        RichTextOptions digitDrawOptions = CreateDrawOptions(digitFont);
        RichTextOptions? scaledDrawOptions = null;
        RichTextOptions? captionDrawOptions = null;
        for (int i = 0; i < symbol.Text.Length; i++)
        {
            BarcodeTextPlacement placement = symbol.Text[i];
            Font font = ResolveFont(placement, digitFont, ref captionFont, ref scaledFont, options);

            // The edge the reader sees is what rounds onto the device pixel grid, as the bar edges do. For
            // a line below the bars that edge is the top of its ink, so the placement line rounds and the
            // ink rise is added after; for a line above the bars the ink bottom is the baseline itself.
            float barEdge = origin.Y + ((topBand + placement.BarEdge) * moduleWidth);
            float textY = placement.Side == BarcodeTextSide.AboveBars
                ? MathF.Round(barEdge - (TextGap * moduleWidth))
                : MathF.Round(barEdge + (TextGap * moduleWidth)) + InkRise(font);

            // The text centres on the cell edges the bars actually drew on, not on the exact fractional
            // position, so a digit stays over its own symbol character. Centring on the unrounded edges
            // lets the text and the bars disagree by up to half a module.
            float textX = (MathF.Round(symbolLeft + (placement.Left * moduleWidth)) + MathF.Round(symbolLeft + (placement.Right * moduleWidth))) * 0.5F;

            RichTextOptions textOptions;
            if (placement.IsCaption)
            {
                textOptions = captionDrawOptions ??= CreateDrawOptions(font);
            }
            else if (ReferenceEquals(font, digitFont))
            {
                textOptions = digitDrawOptions;
            }
            else
            {
                textOptions = scaledDrawOptions ??= CreateDrawOptions(font);
            }

            textOptions.Origin = new PointF(textX, textY);
            canvas.DrawText(textOptions, placement.Text, options.BarBrush, null);
        }

        return bounds;
    }

    /// <summary>
    /// Returns the options every line of the human readable interpretation draws with. A line anchors on
    /// its alphabetic baseline and centers on its span, so only the origin changes from line to line.
    /// </summary>
    /// <param name="font">The font the line renders in.</param>
    /// <returns>The draw options.</returns>
    private static RichTextOptions CreateDrawOptions(Font font)
        => new(font)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            TextBaseline = TextBaseline.Alphabetic,
            HintingMode = HintingMode.Standard
        };

    /// <summary>
    /// Returns the font a placement renders in. A caption renders in the caption font, resolved once for
    /// the symbol so that both the band and the drawing use one size.
    /// </summary>
    /// <param name="placement">The placement to resolve for.</param>
    /// <param name="digitFont">The font the digit lines render in.</param>
    /// <param name="captionFont">The resolved caption font, filled in on first use.</param>
    /// <param name="scaledFont">The scaled digit font, filled in on first use and reused while the scale holds.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <returns>The font for the placement.</returns>
    private static Font ResolveFont(BarcodeTextPlacement placement, Font digitFont, ref Font? captionFont, ref Font? scaledFont, BarcodeOptions options)
    {
        if (placement.IsCaption)
        {
            captionFont ??= EanUpcEncoder.ResolveCaptionFont(placement.Text, placement.Right - placement.Left, options);
            return captionFont;
        }

        if (placement.FontScale == 1F)
        {
            return digitFont;
        }

        // A symbol scales at most a handful of placements, and the UPC quiet zone digits all scale by the
        // same factor, so the scaled font is built once and reused while that factor holds.
        float scaledSize = digitFont.Size * placement.FontScale;
        if (scaledFont is null || scaledFont.Size != scaledSize)
        {
            scaledFont = new Font(digitFont, scaledSize);
        }

        return scaledFont;
    }

    /// <summary>
    /// Returns how far the ink of a line of digits or capitals stands above its baseline, in pixels.
    /// Section 5.2.5 of the GS1 General Specifications measures the clear space to that ink, and the
    /// characters of the human readable interpretation all rest their ink on the baseline.
    /// </summary>
    /// <param name="font">The font the line renders in.</param>
    /// <returns>The rise in pixels.</returns>
    private static float InkRise(Font font)
        => font.FontMetrics.CapHeight * font.Size / font.FontMetrics.UnitsPerEm;
}
