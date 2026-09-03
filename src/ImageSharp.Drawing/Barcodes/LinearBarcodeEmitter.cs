// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Converts a <see cref="LinearBarcodeSymbol"/> into canvas commands. All bars render through a single fill of
/// one path collection. Each text placement draws through one text command.
/// </summary>
internal static class LinearBarcodeEmitter
{
    /// <summary>
    /// The clear space in modules between a line of the human readable interpretation and the bar edge
    /// that line faces. Section 5.2.5 of the GS1 General Specifications sets the minimum at 0.5X both
    /// below the main symbol and above an add-on symbol, and states: "Normally the minimum is one module,
    /// which is close enough to keep the human readable interpretation associated with the symbol." The
    /// same space applies above a caption, where no document gives a figure.
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
        => Layout(canvas, symbol, options, origin);

    /// <summary>
    /// Measures the area the symbol covers without drawing anything, so a caller can size the image
    /// before it draws and no glyph falls outside it.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner the symbol would draw from, in pixels.</param>
    /// <returns>The area the symbol covers, including any text that overhangs the bars.</returns>
    public static RectangleF Measure(LinearBarcodeSymbol symbol, BarcodeOptions options, PointF origin)
        => Layout(null, symbol, options, origin);

    /// <summary>
    /// Lays the symbol out and, when a canvas is supplied, draws it. Both callers share one pass, so the
    /// measured area and the drawn area cannot drift apart.
    /// </summary>
    /// <param name="canvas">The canvas to render onto, or <see langword="null"/> to measure only.</param>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of everything the call draws, in pixels.</param>
    /// <returns>The area the symbol covers, including any text that overhangs the bars.</returns>
    private static RectangleF Layout(DrawingCanvas? canvas, LinearBarcodeSymbol symbol, BarcodeOptions options, PointF origin)
    {
        float moduleWidth = options.ModuleWidth;
        Guard.MustBeGreaterThan(moduleWidth, 0, nameof(options.ModuleWidth));

        float widthInModules = symbol.WidthInModules;
        float symbolLeft = origin.X;
        float frameLeft = origin.X;
        float bearer = symbol.BearerBarThickness;
        bool bearerSides = false;
        if (options.IncludeQuietZones)
        {
            widthInModules += symbol.LeadingQuietZone + symbol.TrailingQuietZone;
            symbolLeft += symbol.LeadingQuietZone * moduleWidth;

            // Section 5.3.2.4 of the GS1 General Specifications runs the bearer bar around the quiet zones,
            // so its vertical sections need the quiet zones on the page. Without them the same clause
            // permits the horizontal sections alone.
            bearerSides = bearer > 0;
            if (bearerSides)
            {
                widthInModules += bearer + bearer;
                symbolLeft += bearer * moduleWidth;
            }
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
            RichTextOptions capOptions = BarcodeTextOptionsFactory.Create(digitFont);
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

        // One rule places every line: it sits TextGap modules from the bar edge it names and takes one
        // line height. A line above the bars pushes the whole bar block down by the room it needs, which
        // is why the band is measured here, where the fonts are known, rather than guessed at as a
        // constant by each encoder.
        float topBand = 0;
        float bottomInModules = symbol.HeightInModules;

        // A symbol resolves to at most three fonts: the digits, the scaled quiet zone digits and the
        // caption. Each keeps one options instance, and the measuring pass and the drawing pass share it,
        // so the rectangle measured for a line is the rectangle the line draws into.
        RichTextOptions? digitOptions = null;
        RichTextOptions? scaledOptions = null;
        RichTextOptions? captionOptions = null;

        // Every line drawn in one font shares one rise, so the lines share a baseline and their glyphs sit
        // level with each other. The rise is the tallest ink any of those lines carries, which a cap height
        // metric misses for an ascender or the overshoot of a round glyph.
        // A symbol prints one line below the bars and at most one above. Each line takes one line height,
        // and its ink sits somewhere inside that. The clear space is measured to the ink, so the line
        // moves by the offset of its ink edge to put that edge on the gap whatever the font or its size.
        float belowHeight = 0;
        float belowInk = float.MaxValue;
        float belowBaseline = 0;
        float aboveInk = 0;
        if (digitFont is not null)
        {
            for (int i = 0; i < symbol.Text.Length; i++)
            {
                BarcodeTextPlacement placement = symbol.Text[i];
                Font font = ResolveFont(placement, digitFont, ref captionFont, ref scaledFont, options);
                RichTextOptions measureOptions;
                if (placement.IsCaption)
                {
                    measureOptions = captionOptions ??= BarcodeTextOptionsFactory.Create(font);
                }
                else if (ReferenceEquals(font, digitFont))
                {
                    measureOptions = digitOptions ??= BarcodeTextOptionsFactory.Create(font);
                }
                else
                {
                    measureOptions = scaledOptions ??= BarcodeTextOptionsFactory.Create(font);
                }

                // The rectangle is the union of the glyph bounds and the advance, and both are anchored at
                // the origin, so the line is measured from its top left corner and centred on the cell
                // here. An alignment would move the glyphs without moving the advance.
                float center = (MathF.Round(symbolLeft + (placement.Left * moduleWidth)) + MathF.Round(symbolLeft + (placement.Right * moduleWidth))) * 0.5F;
                measureOptions.Origin = PointF.Empty;

                TextMetrics metrics = TextMeasurer.Measure(placement.Text, measureOptions);
                backgroundLeft = MathF.Min(backgroundLeft, center - (metrics.RenderableBounds.Width * 0.5F));
                backgroundRight = MathF.Max(backgroundRight, center + (metrics.RenderableBounds.Width * 0.5F));

                if (placement.Side == BarcodeTextSide.AboveBars)
                {
                    aboveInk = MathF.Max(aboveInk, metrics.Bounds.Bottom);
                }
                else
                {
                    // The quiet zone digits of a UPC symbol print smaller than the rest of the line. The
                    // tallest line sets where the line sits, and every line on it shares that baseline.
                    LineMetrics line = metrics.LineMetrics[0];
                    if (line.LineHeight > belowHeight)
                    {
                        belowHeight = line.LineHeight;
                        belowBaseline = line.Baseline;
                    }

                    belowInk = MathF.Min(belowInk, metrics.Bounds.Top);
                }
            }

            // Section 5.2.5 of the GS1 General Specifications measures the clear space to the ink of the
            // line. The room a line needs is therefore the rise its font draws with, which the second pass
            // reads now that every line of that font has been measured.
            for (int i = 0; i < symbol.Text.Length; i++)
            {
                BarcodeTextPlacement placement = symbol.Text[i];
                if (placement.Side == BarcodeTextSide.AboveBars)
                {
                    topBand = MathF.Max(topBand, TextGap + (aboveInk / moduleWidth) - placement.BarEdge);
                }
                else
                {
                    bottomInModules = MathF.Max(bottomInModules, placement.BarEdge + TextGap + ((belowHeight - belowInk) / moduleWidth));
                }
            }
        }

        // A baseline rounds onto the device pixel grid, which lifts its ink by up to half a pixel. The band
        // starts the bar block on a whole pixel so that lift cannot carry ink above the reserved room.
        topBand = MathF.Ceiling(topBand * moduleWidth) / moduleWidth;

        float heightInPixels = (topBand + bottomInModules) * moduleWidth;

        // The origin is the top left corner of everything this call draws. When text overhangs the symbol on
        // the left (an ISBN caption wider than the bars), the whole rendering shifts right by a whole number
        // of pixels so the leftmost element starts at the origin without moving the bars off the pixel grid.
        // Nothing renders above the origin because the band already holds every line above the bars.
        float shift = MathF.Ceiling(origin.X - backgroundLeft);
        if (shift > 0)
        {
            symbolLeft += shift;
            frameLeft += shift;
            backgroundLeft += shift;
            backgroundRight += shift;
        }

        // Every bar edge rounds onto the pixel grid, so the bounds snap outward on both axes to contain
        // the rounded edges.
        float backgroundTop = MathF.Floor(origin.Y);
        RectangleF bounds = new(
            MathF.Floor(backgroundLeft),
            backgroundTop,
            MathF.Ceiling(backgroundRight) - MathF.Floor(backgroundLeft),
            MathF.Ceiling(origin.Y + heightInPixels) - backgroundTop);

        if (canvas is null)
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
        int barCount = (runs.Length + 1) / 2;
        int bearerCount = bearer > 0 ? (bearerSides ? 4 : 2) : 0;
        IPath[] bars = new IPath[barCount + bearerCount];

        // Section 5.3.2.4 of the GS1 General Specifications gives the bearer bar "a constant thickness", so
        // the four sections of the frame share one pixel thickness. On every side where the frame is the
        // outermost ink its outer edge snaps outward with the measured bounds, so no background shows past
        // it. Where text follows below, the lower edge rounds as the bar edges do, which keeps the clear
        // space. The bars butt against the inner edges, so the bar height absorbs the fraction of a pixel
        // that the snapping moves, never the frame.
        float thickness = 0;
        float frameTop = 0;
        float frameBottom = 0;
        float upperBottom = 0;
        float lowerTop = 0;
        if (bearer > 0)
        {
            thickness = MathF.Round(bearer * moduleWidth);
            float exactTop = origin.Y + (topBand * moduleWidth);
            float exactBottom = origin.Y + ((topBand + symbol.HeightInModules) * moduleWidth);
            frameTop = topBand > 0 ? MathF.Round(exactTop) : MathF.Floor(exactTop);
            frameBottom = bottomInModules > symbol.HeightInModules ? MathF.Round(exactBottom) : MathF.Ceiling(exactBottom);
            upperBottom = frameTop + thickness;
            lowerTop = frameBottom - thickness;
        }

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
                float barTop;
                float barBottom;
                if (bearer > 0)
                {
                    barTop = upperBottom + MathF.Round((symbol.BarTops[bar] - bearer) * moduleWidth);
                    barBottom = lowerTop - MathF.Round((symbol.HeightInModules - bearer - symbol.BarTops[bar] - symbol.BarHeights[bar]) * moduleWidth);
                }
                else
                {
                    barTop = MathF.Round(origin.Y + ((topBand + symbol.BarTops[bar]) * moduleWidth));
                    barBottom = MathF.Round(origin.Y + ((topBand + symbol.BarTops[bar] + symbol.BarHeights[bar]) * moduleWidth));
                }

                bars[bar] = new RectanglePolygon(barLeft, barTop, barRight - barLeft, barBottom - barTop);
            }

            x += runWidth;
        }

        if (bearer > 0)
        {
            // When the quiet zones are drawn the frame closes around them, and the quiet zones absorb the
            // fraction of a pixel between the snapped outer edge and the exact one. Without them the frame
            // ends on the bars. The vertical sections run between the horizontal ones so no two rectangles
            // overlap.
            float left;
            float right;
            if (bearerSides)
            {
                left = MathF.Floor(frameLeft);
                right = MathF.Ceiling(frameLeft + (widthInModules * moduleWidth));
            }
            else
            {
                left = MathF.Round(symbolLeft);
                right = MathF.Round(symbolLeft + (symbol.WidthInModules * moduleWidth));
            }

            bars[barCount] = new RectanglePolygon(left, frameTop, right - left, thickness);
            bars[barCount + 1] = new RectanglePolygon(left, lowerTop, right - left, thickness);
            if (bearerSides)
            {
                bars[barCount + 2] = new RectanglePolygon(left, upperBottom, thickness, lowerTop - upperBottom);
                bars[barCount + 3] = new RectanglePolygon(right - thickness, upperBottom, thickness, lowerTop - upperBottom);
            }
        }

        canvas.Fill(options.BarBrush, new PathCollection(bars));

        if (digitFont is null)
        {
            return bounds;
        }

        for (int i = 0; i < symbol.Text.Length; i++)
        {
            BarcodeTextPlacement placement = symbol.Text[i];
            Font font = ResolveFont(placement, digitFont, ref captionFont, ref scaledFont, options);

            // The edge the reader sees is what rounds onto the device pixel grid, as the bar edges do. The
            // line hangs from its top, so a line below the bars starts at the bar edge and a line above the
            // bars ends there, one line height higher.
            float barEdge = origin.Y + ((topBand + placement.BarEdge) * moduleWidth);
            float baseline = MathF.Round(barEdge + (TextGap * moduleWidth)) - belowInk + belowBaseline;

            // The text centres on the cell edges the bars actually drew on, not on the exact fractional
            // position, so a digit stays over its own symbol character. Centring on the unrounded edges
            // lets the text and the bars disagree by up to half a module.
            float center = (MathF.Round(symbolLeft + (placement.Left * moduleWidth)) + MathF.Round(symbolLeft + (placement.Right * moduleWidth))) * 0.5F;

            RichTextOptions textOptions;
            if (placement.IsCaption)
            {
                textOptions = captionOptions ??= BarcodeTextOptionsFactory.Create(font);
            }
            else if (ReferenceEquals(font, digitFont))
            {
                textOptions = digitOptions ??= BarcodeTextOptionsFactory.Create(font);
            }
            else
            {
                textOptions = scaledOptions ??= BarcodeTextOptionsFactory.Create(font);
            }

            textOptions.Origin = PointF.Empty;
            TextMetrics lineMetrics = TextMeasurer.Measure(placement.Text, textOptions);
            float textY = placement.Side == BarcodeTextSide.AboveBars
                ? MathF.Round(barEdge - (TextGap * moduleWidth)) - aboveInk
                : baseline - lineMetrics.LineMetrics[0].Baseline;

            textOptions.Origin = new PointF(center - (lineMetrics.RenderableBounds.Width * 0.5F), textY);
            canvas.DrawText(textOptions, placement.Text, options.BarBrush, null);
        }

        return bounds;
    }

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
}
