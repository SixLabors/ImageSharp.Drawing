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
        // the symbol (an ISBN line, for example) widens the background. The ascender of the full size font
        // also fixes the shared baseline: scaled placements shift down by their ascent difference so every
        // digit sits on one line, the way the specifications print the smaller UPC quiet zone digits.
        float backgroundLeft = origin.X;
        float backgroundRight = origin.X + (widthInModules * moduleWidth);
        float heightInPixels = symbol.HeightInModules * moduleWidth;
        float digitCapHeight = 0;
        Font? captionFont = null;

        // Every text line prints at 9 point or larger, the one size floor the standards state.
        Font? digitFont = options.Font is null || options.Font.Size >= EanUpcEncoder.MinimumTextPoints
            ? options.Font
            : new Font(options.Font, EanUpcEncoder.MinimumTextPoints);

        // A digit prints inside its own symbol character, so its size is capped to that cell and the
        // text cannot run into its neighbour. The floor wins over the cap where the two disagree.
        if (digitFont is not null)
        {
            float cap = float.MaxValue;
            for (int i = 0; i < symbol.Text.Length; i++)
            {
                BarcodeTextPlacement capPlacement = symbol.Text[i];
                if (capPlacement.IsCaption)
                {
                    continue;
                }

                float cell = (capPlacement.Right - capPlacement.Left) * moduleWidth;
                float cellText = canvas.MeasureText(new RichTextOptions(digitFont), capPlacement.Text).LineMetrics[0].Extent.X;
                cap = MathF.Min(cap, digitFont.Size * cell / (cellText * capPlacement.FontScale));
            }

            float capped = MathF.Max(MathF.Min(digitFont.Size, cap), EanUpcEncoder.MinimumTextPoints);
            if (capped != digitFont.Size)
            {
                digitFont = new Font(digitFont, capped);
            }
        }

        // A caption prints in the strip the symbology clears above the bars, which reaches from the top
        // of the drawing down to the highest bar.
        float captionStrip = 0;
        for (int i = 0; i < symbol.BarTops.Length; i++)
        {
            captionStrip = i == 0 ? symbol.BarTops[i] : MathF.Min(captionStrip, symbol.BarTops[i]);
        }

        if (digitFont is not null && symbol.Text.Length > 0)
        {
            LineMetrics lineMetrics = canvas.MeasureText(new RichTextOptions(digitFont), "0").LineMetrics[0];

            // Section 5.2.5 of the GS1 General Specifications measures the clear space to the top of the
            // digits, which is their ink. A digit stands one cap height above its baseline, so anchoring
            // on the baseline and adding the cap height puts the ink exactly on the placement line. The
            // scale factor carries the 72 of the default resolution, which the size cancels.
            digitCapHeight = digitFont.FontMetrics.CapHeight * digitFont.Size / digitFont.FontMetrics.UnitsPerEm;
            for (int i = 0; i < symbol.Text.Length; i++)
            {
                BarcodeTextPlacement placement = symbol.Text[i];
                Font font;
                if (placement.IsCaption)
                {
                    // The symbology clears a strip of exactly this caption's height above the bars, so
                    // both sides resolve the caption font by the same rule.
                    captionFont ??= EanUpcEncoder.ResolveCaptionFont(placement.Text, placement.Right - placement.Left, options);
                    font = captionFont;
                }
                else
                {
                    font = placement.FontScale == 1F ? digitFont : new Font(digitFont, digitFont.Size * placement.FontScale);
                }

                float textWidth = canvas.MeasureText(new RichTextOptions(font), placement.Text).LineMetrics[0].Extent.X;
                float center = symbolLeft + ((placement.Left + placement.Right) * 0.5F * moduleWidth);
                backgroundLeft = MathF.Min(backgroundLeft, center - (textWidth * 0.5F));
                backgroundRight = MathF.Max(backgroundRight, center + (textWidth * 0.5F));
                heightInPixels = MathF.Max(heightInPixels, (placement.Y * moduleWidth) + lineMetrics.LineHeight);
            }
        }

        // The origin is the top left corner of everything this call draws. When text overhangs the symbol on
        // the left (an ISBN caption wider than the bars), the whole rendering shifts right by a whole number
        // of pixels so the leftmost element starts at the origin without moving the bars off the pixel grid.
        // Nothing renders above the origin because every text line sits at or below the symbol top.
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
                float barTop = MathF.Round(origin.Y + (symbol.BarTops[bar] * moduleWidth));
                float barBottom = MathF.Round(origin.Y + (symbol.BarTops[bar] * moduleWidth) + (symbol.BarHeights[bar] * moduleWidth));
                bars[bar] = new RectanglePolygon(barLeft, barTop, barRight - barLeft, barBottom - barTop);
            }

            x += runWidth;
        }

        canvas.Fill(options.BarBrush, new PathCollection(bars));

        if (digitFont is null)
        {
            return bounds;
        }

        for (int i = 0; i < symbol.Text.Length; i++)
        {
            BarcodeTextPlacement placement = symbol.Text[i];
            Font font = placement.IsCaption
                ? captionFont ?? digitFont
                : placement.FontScale == 1F ? digitFont : new Font(digitFont, digitFont.Size * placement.FontScale);

            // Every line anchors on its alphabetic baseline, so the origin places that baseline and no
            // line box arithmetic is involved. Capitals and digits rest their ink on the baseline, which
            // makes a caption's baseline its clear space above the bars. The digits below the bars hang
            // from their ink top, one cap height over the baseline, and a scaled placement shares that
            // baseline with the rest without any further correction.
            // Every baseline rounds onto the device pixel grid, as the bar edges do, so the text stays
            // crisp at any module width and a measured strip or cap height cannot land a row on a half
            // pixel.
            float textY = placement.IsCaption
                ? MathF.Round(origin.Y + ((captionStrip - EanUpcEncoder.TextGap) * moduleWidth))
                : MathF.Round(origin.Y + (placement.Y * moduleWidth) + digitCapHeight);

            RichTextOptions textOptions = new(font)
            {
                Origin = new PointF(symbolLeft + ((placement.Left + placement.Right) * 0.5F * moduleWidth), textY),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextBaseline = TextBaseline.Alphabetic,
                HintingMode = HintingMode.Standard
            };

            canvas.DrawText(textOptions, placement.Text, options.BarBrush, null);
        }

        return bounds;
    }
}
