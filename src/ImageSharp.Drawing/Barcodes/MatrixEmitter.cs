// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Converts a matrix <see cref="BarcodeSymbol"/> into canvas commands. Each run of adjacent dark modules in
/// a row becomes one rectangle, and all rectangles render through a single fill of one
/// <see cref="BarcodePath"/>. Each text placement draws through one text command.
/// </summary>
internal static class MatrixEmitter
{
    /// <summary>
    /// Renders the symbol onto the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded matrix symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="xDimension">The width of one module in millimetres.</param>
    /// <param name="origin">The top left corner of everything the call draws, in pixels.</param>
    public static void Emit(DrawingCanvas canvas, ref BarcodeSymbol symbol, BarcodeOptions options, double xDimension, PointF origin)
        => Layout(canvas, ref symbol, options, xDimension, origin);

    /// <summary>
    /// Measures the area the symbol covers without drawing anything, so a caller can size the image
    /// before it draws.
    /// </summary>
    /// <param name="symbol">The encoded matrix symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="xDimension">The width of one module in millimetres.</param>
    /// <param name="origin">The top left corner the symbol would draw from, in pixels.</param>
    /// <returns>The area the symbol covers, including the quiet zones when they are drawn and the text.</returns>
    public static RectangleF Measure(ref BarcodeSymbol symbol, BarcodeOptions options, double xDimension, PointF origin)
        => Layout(null, ref symbol, options, xDimension, origin);

    /// <summary>
    /// Lays the symbol out and, when a canvas is supplied, draws it. Both callers share one pass, so the
    /// measured area and the drawn area cannot drift apart.
    /// </summary>
    /// <param name="canvas">The canvas to render onto, or <see langword="null"/> to measure only.</param>
    /// <param name="symbol">The encoded matrix symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="xDimension">The width of one module in millimetres.</param>
    /// <param name="origin">The top left corner of everything the call draws, in pixels.</param>
    /// <returns>The area the symbol covers.</returns>
    private static RectangleF Layout(DrawingCanvas? canvas, ref BarcodeSymbol symbol, BarcodeOptions options, double xDimension, PointF origin)
    {
        Guard.MustBeGreaterThan(options.Dpi, 0, nameof(options.Dpi));
        Guard.MustBeGreaterThan(xDimension, 0, nameof(xDimension));
        float moduleWidth = (float)(xDimension / 25.4 * options.Dpi);

        // A matrix symbol has the same quiet zone on all four sides.
        int quietZone = options.IncludeQuietZones ? (int)symbol.LeadingQuietZone : 0;
        int columns = symbol.Columns;
        int rows = symbol.MatrixRows;

        // Each edge rounds from its exact position onto the device pixel grid, so the modules stay crisp
        // and rounding cannot drift across the symbol. The positions are products of a whole module count
        // and the module width, calculated in double from the origin, and the edge two neighbours share
        // rounds to one pixel for both.
        double symbolLeft = origin.X + ((double)quietZone * moduleWidth);
        double symbolTop = origin.Y + ((double)quietZone * moduleWidth);

        // The quiet zones and the modules set the drawn area, and the human readable interpretation below
        // the symbol extends it: downwards by its lines, and to either side by a line wider than the
        // symbol. The lines of a placement stand within their block as the placement stands on its span.
        // The quiet zone surrounds the ink of the text as it surrounds the modules, so the clear space
        // beside a line is the clear space beside the symbol, whatever room the font leaves around its
        // glyphs.
        float clearSpace = quietZone * moduleWidth;
        float areaLeft = origin.X;
        float areaRight = origin.X + ((columns + quietZone + quietZone) * moduleWidth);
        float areaBottom = origin.Y + ((rows + quietZone + quietZone) * moduleWidth);
        ReadOnlySpan<BarcodeTextPlacement> text = symbol.Text;
        Font? font = text.Length == 0 ? null : options.Font;
        RichTextOptions? textOptions = font is null ? null : BarcodeTextOptionsFactory.Create(font, options.Dpi);
        if (font is not null && textOptions is not null)
        {
            for (int t = 0; t < text.Length; t++)
            {
                BarcodeTextPlacement placement = text[t];
                textOptions.Font = placement.FontScale == 1F ? font : new Font(font, font.Size * placement.FontScale);
                textOptions.Origin = PointF.Empty;
                textOptions.TextAlignment = placement.Alignment == BarcodeTextAlignment.Left ? TextAlignment.Start : TextAlignment.Center;
                TextMetrics metrics = TextMeasurer.Measure(symbol.TextOf(in placement), textOptions);

                // The ink of the first line starts on the text edge, which rounds onto the pixel grid as the
                // module edges do.
                float textLeft = BarEmitter.TextOrigin(placement, metrics, (float)symbolLeft, moduleWidth);
                float textTop = (float)Math.Round(symbolTop + ((double)placement.TextEdge * moduleWidth)) - metrics.Bounds.Top;
                areaLeft = MathF.Min(areaLeft, textLeft + metrics.Bounds.Left - clearSpace);
                areaRight = MathF.Max(areaRight, textLeft + metrics.Bounds.Right + clearSpace);
                areaBottom = MathF.Max(areaBottom, textTop + metrics.Bounds.Bottom + clearSpace);
            }
        }

        // When a line overhangs the symbol on the left, the whole rendering shifts right by a whole number
        // of pixels so the leftmost element starts at the origin without moving the modules off the pixel
        // grid.
        float shift = MathF.Ceiling(origin.X - areaLeft);
        if (shift > 0)
        {
            symbolLeft += shift;
            areaLeft += shift;
            areaRight += shift;
        }

        // Every module edge rounds onto the pixel grid, so the bounds snap outward on both axes to contain
        // the rounded edges.
        float left = MathF.Floor(areaLeft);
        float top = MathF.Floor(origin.Y);
        RectangleF bounds = new(
            left,
            top,
            MathF.Ceiling(areaRight) - left,
            MathF.Ceiling(areaBottom) - top);

        if (canvas is null)
        {
            return bounds;
        }

        if (options.Background is not null)
        {
            canvas.Fill(options.Background, new RectanglePolygon(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        // A run is a maximal sequence of dark modules in a row, so its left neighbour is light or the
        // edge. The first pass counts the runs to size the rectangle array exactly. A bool is the byte 0
        // or 1, so a run start is a byte that is 1 where the byte before it is 0: current AND NOT previous
        // is 1 exactly at a start. The scan runs over the whole grid as one row, a vector of bytes at a
        // time, and adds the lanes of each result; a lane sum is at most the lane count, which a byte
        // holds. The scalar loop finishes the bytes that do not fill a vector, with the same test.
        ReadOnlySpan<bool> modules = symbol.Modules;
        ReadOnlySpan<byte> bytes = MemoryMarshal.Cast<bool, byte>(modules);
        int runCount = bytes[0];
        int i = 1;
        if (Vector.IsHardwareAccelerated)
        {
            int lanes = Vector<byte>.Count;
            for (; i + lanes <= bytes.Length; i += lanes)
            {
                Vector<byte> starts = Vector.AndNot(new Vector<byte>(bytes[i..]), new Vector<byte>(bytes[(i - 1)..]));
                runCount += Vector.Sum(starts);
            }
        }

        for (; i < bytes.Length; i++)
        {
            runCount += bytes[i] & ~bytes[i - 1];
        }

        // The flat scan reads the first module of a row as following the last module of the row above, so
        // a row that starts dark below a row that ends dark has one start the scan did not count.
        for (int r = 1; r < rows; r++)
        {
            int rowStart = r * columns;
            runCount += bytes[rowStart] & bytes[rowStart - 1];
        }

        // The path retains the rectangles until the canvas flushes, so it owns this array.
        RectangleF[] runs = new RectangleF[runCount];
        float pathLeft = float.MaxValue;
        float pathTop = float.MaxValue;
        float pathRight = float.MinValue;
        float pathBottom = float.MinValue;
        int index = 0;
        for (int r = 0; r < rows; r++)
        {
            ReadOnlySpan<bool> row = modules.Slice(r * columns, columns);
            float rowTop = (float)Math.Round(symbolTop + ((double)r * moduleWidth));
            float rowBottom = (float)Math.Round(symbolTop + ((double)(r + 1) * moduleWidth));
            int c = 0;
            while (c < columns)
            {
                if (!row[c])
                {
                    c++;
                    continue;
                }

                int start = c;
                while (c < columns && row[c])
                {
                    c++;
                }

                float runLeft = (float)Math.Round(symbolLeft + ((double)start * moduleWidth));
                float runRight = (float)Math.Round(symbolLeft + ((double)c * moduleWidth));
                runs[index++] = RectangleF.FromLTRB(runLeft, rowTop, runRight, rowBottom);
                pathLeft = MathF.Min(pathLeft, runLeft);
                pathTop = MathF.Min(pathTop, rowTop);
                pathRight = MathF.Max(pathRight, runRight);
                pathBottom = MathF.Max(pathBottom, rowBottom);
            }
        }

        canvas.Fill(options.BarBrush, new BarcodePath(runs, RectangleF.FromLTRB(pathLeft, pathTop, pathRight, pathBottom)));

        if (font is not null && textOptions is not null)
        {
            for (int t = 0; t < text.Length; t++)
            {
                BarcodeTextPlacement placement = text[t];
                ReadOnlySpan<char> characters = symbol.TextOf(in placement);
                textOptions.Font = placement.FontScale == 1F ? font : new Font(font, font.Size * placement.FontScale);
                textOptions.Origin = PointF.Empty;
                textOptions.TextAlignment = placement.Alignment == BarcodeTextAlignment.Left ? TextAlignment.Start : TextAlignment.Center;
                TextMetrics metrics = TextMeasurer.Measure(characters, textOptions);
                textOptions.Origin = new PointF(
                    BarEmitter.TextOrigin(placement, metrics, (float)symbolLeft, moduleWidth),
                    (float)Math.Round(symbolTop + ((double)placement.TextEdge * moduleWidth)) - metrics.Bounds.Top);

                canvas.DrawText(textOptions, characters, options.BarBrush, null);
            }
        }

        return bounds;
    }
}
