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
    /// <param name="origin">The top left corner of the drawn area, in pixels.</param>
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

        // The drawn area grows to hold the human readable interpretation, the way the nominal ISO/IEC 15420
        // symbol is sized so its text region extends below the guard bars. The ascender of the full size
        // font also fixes the shared baseline: scaled placements shift down by their ascent difference so
        // every digit sits on one line, the way the specifications print the smaller UPC quiet zone digits.
        float heightInPixels = symbol.HeightInModules * moduleWidth;
        float fullSizeAscent = 0;
        if (options.Font is not null && symbol.Text.Length > 0)
        {
            LineMetrics lineMetrics = canvas.MeasureText(new RichTextOptions(options.Font), "0").LineMetrics[0];
            fullSizeAscent = lineMetrics.Ascender;
            foreach (BarcodeTextPlacement placement in symbol.Text)
            {
                heightInPixels = MathF.Max(heightInPixels, (placement.Y * moduleWidth) + lineMetrics.LineHeight);
            }
        }

        if (options.Background is not null)
        {
            canvas.Fill(options.Background, new RectanglePolygon(origin.X, origin.Y, widthInModules * moduleWidth, heightInPixels));
        }

        int[] runs = symbol.RunWidths;
        IPath[] bars = new IPath[(runs.Length + 1) / 2];
        float x = symbolLeft;
        for (int i = 0; i < runs.Length; i++)
        {
            float runWidth = runs[i] * moduleWidth;
            if ((i & 1) == 0)
            {
                int bar = i >> 1;
                bars[bar] = new RectanglePolygon(x, origin.Y + (symbol.BarTops[bar] * moduleWidth), runWidth, symbol.BarHeights[bar] * moduleWidth);
            }

            x += runWidth;
        }

        canvas.Fill(options.BarBrush, new PathCollection(bars));

        if (options.Font is null)
        {
            return;
        }

        foreach (BarcodeTextPlacement placement in symbol.Text)
        {
            Font font = placement.FontScale == 1F ? options.Font : new Font(options.Font, options.Font.Size * placement.FontScale);
            RichTextOptions textOptions = new(font)
            {
                Origin = new PointF(
                    symbolLeft + ((placement.Left + placement.Right) * 0.5F * moduleWidth),
                    origin.Y + (placement.Y * moduleWidth) + (fullSizeAscent * (1F - placement.FontScale))),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                HintingMode = HintingMode.Standard
            };

            canvas.DrawText(textOptions, placement.Text, options.BarBrush, null);
        }
    }
}
