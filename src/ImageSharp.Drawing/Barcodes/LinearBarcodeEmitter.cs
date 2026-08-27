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
        float fullSizeAscent = 0;
        Font? captionFont = options.CaptionFont;
        if (options.Font is not null && symbol.Text.Length > 0)
        {
            LineMetrics lineMetrics = canvas.MeasureText(new RichTextOptions(options.Font), "0").LineMetrics[0];
            fullSizeAscent = lineMetrics.Ascender;
            for (int i = 0; i < symbol.Text.Length; i++)
            {
                BarcodeTextPlacement placement = symbol.Text[i];
                Font font;
                if (placement.IsCaption)
                {
                    if (captionFont is null)
                    {
                        // The book industry barcoding guidelines size the caption so it extends the full
                        // width of the main body of the symbol, which is the placement span, so the
                        // derived caption font scales the measured text onto that span.
                        float spanWidth = (placement.Right - placement.Left) * moduleWidth;
                        float captionWidth = canvas.MeasureText(new RichTextOptions(options.Font), placement.Text).LineMetrics[0].Extent.X;
                        captionFont = new Font(options.Font, options.Font.Size * spanWidth / captionWidth);
                    }

                    font = captionFont;
                }
                else
                {
                    font = placement.FontScale == 1F ? options.Font : new Font(options.Font, options.Font.Size * placement.FontScale);
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

        if (options.Background is not null)
        {
            // The background is a coverage area, not symbol geometry: it snaps outward to whole pixels so
            // its edges are always crisp and a fractionally measured text extent cannot leave a seam.
            float left = MathF.Floor(backgroundLeft);
            float right = MathF.Ceiling(backgroundRight);
            canvas.Fill(options.Background, new RectanglePolygon(left, origin.Y, right - left, MathF.Ceiling(heightInPixels)));
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

        if (options.Font is null)
        {
            return;
        }

        for (int i = 0; i < symbol.Text.Length; i++)
        {
            BarcodeTextPlacement placement = symbol.Text[i];
            Font font = placement.IsCaption
                ? captionFont ?? options.Font
                : placement.FontScale == 1F ? options.Font : new Font(options.Font, options.Font.Size * placement.FontScale);
            RichTextOptions textOptions = new(font)
            {
                Origin = new PointF(
                    symbolLeft + ((placement.Left + placement.Right) * 0.5F * moduleWidth),
                    origin.Y + (placement.Y * moduleWidth) + (placement.IsCaption ? 0F : fullSizeAscent * (1F - placement.FontScale))),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                HintingMode = HintingMode.Standard
            };

            canvas.DrawText(textOptions, placement.Text, options.BarBrush, null);
        }
    }
}
