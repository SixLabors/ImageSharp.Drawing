// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Converts a matrix or dotted <see cref="BarcodeSymbol"/> into canvas commands. In a matrix symbol each run of adjacent dark modules in a row becomes one
/// rectangle; in a dotted symbol each dark module becomes one circle. Either way the dark modules render through a single fill of one
/// <see cref="BarcodePath"/>. Each text placement draws through one text command.
/// </summary>
internal static class MatrixEmitter
{
    /// <summary>
    /// The number of values in <see cref="BarcodeSymbol.OverlayEdges"/> for each dark rectangle of an overlay graphic: its left, top, right and bottom edges.
    /// </summary>
    private const int OverlayRectangleEdges = 4;

    /// <summary>
    /// Renders the symbol onto the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded matrix symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of everything the call draws, in pixels.</param>
    public static void Emit(DrawingCanvas canvas, ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
        => Layout(canvas, ref symbol, options, origin);

    /// <summary>
    /// Measures the area the symbol covers without drawing anything, so a caller can size the image before it draws.
    /// </summary>
    /// <param name="symbol">The encoded matrix symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner the symbol would draw from, in pixels.</param>
    /// <returns>The area the symbol covers, including the quiet zones when they are drawn and the text.</returns>
    public static RectangleF Measure(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
        => Layout(null, ref symbol, options, origin);

    /// <summary>
    /// Lays the symbol out and, when a canvas is supplied, draws it. Both callers share one pass, so the measured area and the drawn area cannot drift apart.
    /// </summary>
    /// <param name="canvas">The canvas to render onto, or <see langword="null"/> to measure only.</param>
    /// <param name="symbol">The encoded matrix symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of everything the call draws, in pixels.</param>
    /// <returns>The area the symbol covers.</returns>
    private static RectangleF Layout(DrawingCanvas? canvas, ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
    {
        float moduleWidth = (float)(symbol.XDimension / BarcodeOptions.MillimetresPerInch * options.Dpi);

        // Most matrix symbols have the same quiet zone on all four sides; Code One versions S and T have none above.
        bool quiet = options.IncludeQuietZones;
        int leadingZone = quiet ? (int)symbol.LeadingQuietZone : 0;
        int trailingZone = quiet ? (int)symbol.TrailingQuietZone : 0;
        int topZone = quiet ? (int)symbol.TopQuietZone : 0;
        int bottomZone = quiet ? (int)symbol.BottomQuietZone : 0;
        int columns = symbol.Columns;
        int rows = symbol.MatrixRows;

        // Each edge rounds from its exact position onto the device pixel grid, so the modules stay crisp and rounding cannot drift across the symbol. The
        // positions are products of a whole module count and the module width, calculated in double from the corner of the symbol, and the edge two neighbours
        // share rounds to one pixel for both. The corner itself starts on a whole pixel: when a module is not a whole number of pixels, the modules alternate
        // between two widths, and a corner between pixels would make that pattern depend on the width of the quiet zone, so the two outer rings of a finder
        // pattern could round to different widths.
        double symbolLeft = Math.Round(origin.X + ((double)leadingZone * moduleWidth));
        double symbolTop = Math.Round(origin.Y + ((double)topZone * moduleWidth));

        // The quiet zones and the modules set the drawn area, and the human readable interpretation below the symbol extends it: downwards by its lines, and to
        // either side by a line wider than the symbol. The lines of a placement stand within their block as the placement stands on its span. The quiet zone
        // surrounds the ink of the text as it surrounds the modules, so the clear space beside a line is the clear space beside the symbol, whatever room the
        // font leaves around its glyphs.
        float leftClearSpace = leadingZone * moduleWidth;
        float rightClearSpace = trailingZone * moduleWidth;
        float bottomClearSpace = bottomZone * moduleWidth;
        float areaLeft = origin.X;
        float areaRight = origin.X + ((columns + leadingZone + trailingZone) * moduleWidth);
        float areaBottom = origin.Y + ((rows + topZone + bottomZone) * moduleWidth);
        ReadOnlySpan<BarcodeTextPlacement> text = symbol.Text;
        Font? font = text.Length == 0 ? null : options.Font;
        RichTextOptions? textOptions = font is null ? null : BarcodeTextOptionsFactory.Create(font, options.Dpi);
        Font? scaledFont = null;

        // The drawing pass places each line from what the measuring pass measured.
        using PooledBuffer<MeasuredLine> lineBuffer = font is null ? default : new(text.Length);
        Span<MeasuredLine> lines = lineBuffer.Span;
        if (font is not null && textOptions is not null)
        {
            for (int t = 0; t < text.Length; t++)
            {
                BarcodeTextPlacement placement = text[t];
                textOptions.Font = ScaledFont(font, placement.FontScale, ref scaledFont);
                textOptions.Origin = PointF.Empty;
                textOptions.TextAlignment = placement.Alignment == BarcodeTextAlignment.Left ? TextAlignment.Start : TextAlignment.Center;
                TextMetrics metrics = TextMeasurer.Measure(symbol.TextOf(in placement), textOptions);
                MeasuredLine line = new(metrics.Bounds.Left, metrics.Bounds.Top, metrics.RenderableBounds.Width);
                lines[t] = line;

                // The ink of the first line starts on the text edge, which rounds onto the pixel grid as the module edges do.
                float textLeft = BarEmitter.TextOrigin(placement, line.InkLeft, line.RenderableWidth, (float)symbolLeft, moduleWidth);
                float textTop = (float)Math.Round(symbolTop + ((double)placement.TextEdge * moduleWidth)) - metrics.Bounds.Top;
                areaLeft = MathF.Min(areaLeft, textLeft + metrics.Bounds.Left - leftClearSpace);
                areaRight = MathF.Max(areaRight, textLeft + metrics.Bounds.Right + rightClearSpace);
                areaBottom = MathF.Max(areaBottom, textTop + metrics.Bounds.Bottom + bottomClearSpace);
            }
        }

        // When a line overhangs the symbol on the left, the whole rendering shifts right by a whole number of pixels so the leftmost element starts at the
        // origin without moving the modules off the pixel grid.
        float shift = MathF.Ceiling(origin.X - areaLeft);
        if (shift > 0)
        {
            symbolLeft += shift;
            areaLeft += shift;
            areaRight += shift;
        }

        // Every module edge rounds onto the pixel grid, so the bounds snap outward on both axes to contain the rounded edges.
        float left = MathF.Floor(areaLeft);
        float top = MathF.Floor(origin.Y);
        RectangleF bounds = new(left, top, MathF.Ceiling(areaRight) - left, MathF.Ceiling(areaBottom) - top);

        if (canvas is null)
        {
            return bounds;
        }

        if (options.Background is not null)
        {
            canvas.Fill(options.Background, new RectanglePolygon(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        // A dotted symbol draws each dark module as a dot; every other matrix symbol draws runs of modules.
        ModuleGrid grid = new(symbolLeft, symbolTop, moduleWidth);
        if (symbol.Kind == BarcodeSymbolKind.Dotted)
        {
            FillDots(canvas, ref symbol, options.BarBrush, in grid);
        }
        else
        {
            FillRuns(canvas, ref symbol, options.BarBrush, in grid);
        }

        if (font is not null && textOptions is not null)
        {
            for (int t = 0; t < text.Length; t++)
            {
                BarcodeTextPlacement placement = text[t];
                ReadOnlySpan<char> characters = symbol.TextOf(in placement);
                MeasuredLine line = lines[t];
                textOptions.Font = ScaledFont(font, placement.FontScale, ref scaledFont);
                textOptions.TextAlignment = placement.Alignment == BarcodeTextAlignment.Left ? TextAlignment.Start : TextAlignment.Center;
                textOptions.Origin = new PointF(
                    BarEmitter.TextOrigin(placement, line.InkLeft, line.RenderableWidth, (float)symbolLeft, moduleWidth),
                    (float)Math.Round(symbolTop + ((double)placement.TextEdge * moduleWidth)) - line.InkTop);

                canvas.DrawText(textOptions, characters, options.BarBrush, null);
            }
        }

        return bounds;
    }

    /// <summary>
    /// Fills the dark modules of a matrix symbol: each run of adjacent dark modules in a row is one rectangle, and the dark parts of an overlay graphic join
    /// them, all in one fill of one <see cref="BarcodePath"/>.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded matrix symbol.</param>
    /// <param name="brush">The brush of the dark modules.</param>
    /// <param name="grid">The position and the module width of the modules on the pixel grid.</param>
    private static void FillRuns(DrawingCanvas canvas, ref BarcodeSymbol symbol, Brush brush, in ModuleGrid grid)
    {
        int columns = symbol.Columns;
        int rows = symbol.MatrixRows;
        float moduleWidth = grid.ModuleWidth;

        // A run is a maximal sequence of dark modules in a row, so its left neighbour is light or the edge. The first pass counts the runs to size the
        // rectangle array exactly. A bool is the byte 0 or 1, so a run start is a byte that is 1 where the byte before it is 0: current AND NOT previous is 1
        // exactly at a start. The scan runs over the whole grid as one row, a vector of bytes at a time, and adds the lanes of each result; a lane sum is at
        // most the lane count, which a byte holds. The scalar loop finishes the bytes that do not fill a vector, with the same test.
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

        // The flat scan reads the first module of a row as following the last module of the row above, so a row that starts dark below a row that ends dark has
        // one start the scan did not count.
        for (int r = 1; r < rows; r++)
        {
            int rowStart = r * columns;
            runCount += bytes[rowStart] & bytes[rowStart - 1];
        }

        // An overlay graphic covers a square at the centre of the symbol whose edges need not fall on module edges. A run under the square splits into its
        // parts outside it, up to four, and the dark parts of the graphic join the runs, so one fill draws both. The square and the graphic edges round onto
        // the pixel grid as the module edges do. This pass counts the parts, so the array is sized exactly.
        float overlaySize = symbol.OverlaySize;
        ReadOnlySpan<float> edges = symbol.OverlayEdges;
        double overlayLeft = grid.Left + ((columns - overlaySize) / 2D * moduleWidth);
        double overlayTop = grid.Top + ((rows - overlaySize) / 2D * moduleWidth);
        double overlaySide = (double)overlaySize * moduleWidth;
        RectangleF hole = RectangleF.Empty;
        InlineArray4<RectangleF> pieceStorage = default;
        Span<RectangleF> pieces = pieceStorage;
        if (overlaySize > 0)
        {
            hole = RectangleF.FromLTRB(
                (float)Math.Round(overlayLeft),
                (float)Math.Round(overlayTop),
                (float)Math.Round(overlayLeft + overlaySide),
                (float)Math.Round(overlayTop + overlaySide));

            runCount += edges.Length / OverlayRectangleEdges;
            for (int r = 0; r < rows; r++)
            {
                float rowTop = grid.RowEdge(r);
                float rowBottom = grid.RowEdge(r + 1);
                if (rowBottom <= hole.Top || rowTop >= hole.Bottom)
                {
                    continue;
                }

                ReadOnlySpan<bool> row = modules.Slice(r * columns, columns);
                int end = 0;
                while (NextRun(row, ref end, out int start))
                {
                    RectangleF run = RectangleF.FromLTRB(grid.ColumnEdge(start), rowTop, grid.ColumnEdge(end), rowBottom);
                    runCount += Subtract(run, hole, pieces) - 1;
                }
            }
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
            float rowTop = grid.RowEdge(r);
            float rowBottom = grid.RowEdge(r + 1);
            int end = 0;
            while (NextRun(row, ref end, out int start))
            {
                float runLeft = grid.ColumnEdge(start);
                float runRight = grid.ColumnEdge(end);
                RectangleF run = RectangleF.FromLTRB(runLeft, rowTop, runRight, rowBottom);
                pathLeft = MathF.Min(pathLeft, runLeft);
                pathTop = MathF.Min(pathTop, rowTop);
                pathRight = MathF.Max(pathRight, runRight);
                pathBottom = MathF.Max(pathBottom, rowBottom);
                if (overlaySize == 0)
                {
                    runs[index++] = run;
                    continue;
                }

                // The parts lie within the run, so the run edges still bound the path.
                int count = Subtract(run, hole, pieces);
                pieces[..count].CopyTo(runs.AsSpan(index));
                index += count;
            }
        }

        // The graphic scales from its own units to the side of the square. Its dark parts lie within the square, which lies within the symbol, so the module
        // edges still bound the path.
        if (overlaySize > 0)
        {
            double scale = overlaySide / symbol.OverlayUnit;
            for (int e = 0; e < edges.Length; e += OverlayRectangleEdges)
            {
                runs[index++] = RectangleF.FromLTRB(
                    (float)Math.Round(overlayLeft + (edges[e] * scale)),
                    (float)Math.Round(overlayTop + (edges[e + 1] * scale)),
                    (float)Math.Round(overlayLeft + (edges[e + 2] * scale)),
                    (float)Math.Round(overlayTop + (edges[e + 3] * scale)));
            }
        }

        canvas.Fill(brush, new BarcodePath(runs, RectangleF.FromLTRB(pathLeft, pathTop, pathRight, pathBottom)));
    }

    /// <summary>
    /// Fills the dark modules of a dotted symbol: each dark module is a circle one module across, centred in its cell, all in one fill of one
    /// <see cref="BarcodePath"/>. Dark modules stand only where the column and the row sum to an even number, so the nearest two are diagonal neighbours, the
    /// square root of 2 modules apart, and no two circles touch.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded dotted symbol.</param>
    /// <param name="brush">The brush of the dots.</param>
    /// <param name="grid">The position and the module width of the cells on the pixel grid.</param>
    private static void FillDots(DrawingCanvas canvas, ref BarcodeSymbol symbol, Brush brush, in ModuleGrid grid)
    {
        int columns = symbol.Columns;
        int rows = symbol.MatrixRows;
        double symbolLeft = grid.Left;
        double symbolTop = grid.Top;
        float moduleWidth = grid.ModuleWidth;
        ReadOnlySpan<bool> modules = symbol.Modules;

        // The path retains the centres until the canvas flushes, so it owns this array, sized by a count of the dark modules.
        PointF[] centres = new PointF[modules.Count(true)];
        int index = 0;
        for (int r = 0; r < rows; r++)
        {
            ReadOnlySpan<bool> row = modules.Slice(r * columns, columns);
            float centreY = (float)(symbolTop + ((r + 0.5) * moduleWidth));
            for (int c = 0; c < columns; c++)
            {
                if (row[c])
                {
                    centres[index++] = new PointF((float)(symbolLeft + ((c + 0.5) * moduleWidth)), centreY);
                }
            }
        }

        // The cells hold every circle, so they bound the path.
        RectangleF cells = new((float)symbolLeft, (float)symbolTop, columns * moduleWidth, rows * moduleWidth);
        canvas.Fill(brush, new BarcodePath(centres, moduleWidth / 2, cells));
    }

    /// <summary>
    /// Finds the next run of dark modules in a row, a maximal sequence of dark modules.
    /// </summary>
    /// <param name="row">The modules of the row.</param>
    /// <param name="end">On entry, the column the search starts at. On exit, the column after the run, or the row length when no run remains.</param>
    /// <param name="start">The first column of the run.</param>
    /// <returns><see langword="true"/> when a run was found.</returns>
    private static bool NextRun(ReadOnlySpan<bool> row, ref int end, out int start)
    {
        int offset = row[end..].IndexOf(true);
        if (offset < 0)
        {
            start = row.Length;
            end = row.Length;
            return false;
        }

        start = end + offset;
        int length = row[start..].IndexOf(false);
        end = length < 0 ? row.Length : start + length;
        return true;
    }

    /// <summary>
    /// Returns the font a placement renders in: the font itself at a scale of 1, or the scaled font, built on first use and again only when the scale changes.
    /// </summary>
    /// <param name="font">The font of the human readable interpretation.</param>
    /// <param name="scale">The scale of the placement.</param>
    /// <param name="scaledFont">The scaled font of the last scaled placement, or <see langword="null"/>.</param>
    /// <returns>The font for the placement.</returns>
    private static Font ScaledFont(Font font, float scale, ref Font? scaledFont)
    {
        if (scale == 1F)
        {
            return font;
        }

        float size = font.Size * scale;
        if (scaledFont is null || scaledFont.Size != size)
        {
            scaledFont = new Font(font, size);
        }

        return scaledFont;
    }

    /// <summary>
    /// Writes the parts of a run outside a hole: the part above the hole, the part below it, and the parts to its left and right within its height.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="hole">The hole.</param>
    /// <param name="pieces">The buffer that receives the parts, at least four long.</param>
    /// <returns>The number of parts, 0 to 4. A run clear of the hole is its only part.</returns>
    private static int Subtract(RectangleF run, RectangleF hole, Span<RectangleF> pieces)
    {
        if (run.Right <= hole.Left || run.Left >= hole.Right || run.Bottom <= hole.Top || run.Top >= hole.Bottom)
        {
            pieces[0] = run;
            return 1;
        }

        int count = 0;
        if (run.Top < hole.Top)
        {
            pieces[count++] = RectangleF.FromLTRB(run.Left, run.Top, run.Right, hole.Top);
        }

        if (run.Bottom > hole.Bottom)
        {
            pieces[count++] = RectangleF.FromLTRB(run.Left, hole.Bottom, run.Right, run.Bottom);
        }

        float top = MathF.Max(run.Top, hole.Top);
        float bottom = MathF.Min(run.Bottom, hole.Bottom);
        if (run.Left < hole.Left)
        {
            pieces[count++] = RectangleF.FromLTRB(run.Left, top, hole.Left, bottom);
        }

        if (run.Right > hole.Right)
        {
            pieces[count++] = RectangleF.FromLTRB(hole.Right, top, run.Right, bottom);
        }

        return count;
    }

    /// <summary>
    /// What the measuring pass measured of one line, so the drawing pass places the line without measuring it again.
    /// </summary>
    private readonly struct MeasuredLine
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MeasuredLine"/> struct.
        /// </summary>
        /// <param name="inkLeft">The left edge of the ink, from <see cref="TextMetrics.Bounds"/>.</param>
        /// <param name="inkTop">The top edge of the ink, from <see cref="TextMetrics.Bounds"/>.</param>
        /// <param name="renderableWidth">The width of the line, from <see cref="TextMetrics.RenderableBounds"/>.</param>
        public MeasuredLine(float inkLeft, float inkTop, float renderableWidth)
        {
            this.InkLeft = inkLeft;
            this.InkTop = inkTop;
            this.RenderableWidth = renderableWidth;
        }

        /// <summary>
        /// Gets the left edge of the ink, from <see cref="TextMetrics.Bounds"/>.
        /// </summary>
        public float InkLeft { get; }

        /// <summary>
        /// Gets the top edge of the ink, from <see cref="TextMetrics.Bounds"/>.
        /// </summary>
        public float InkTop { get; }

        /// <summary>
        /// Gets the width of the line, from <see cref="TextMetrics.RenderableBounds"/>.
        /// </summary>
        public float RenderableWidth { get; }
    }

    /// <summary>
    /// The module grid of a matrix symbol on the page. Each edge rounds from its exact position onto the device pixel grid. The position is a product of a
    /// whole module count and the module width, calculated in double from the corner of the symbol, so the edge two neighbours share rounds to one pixel for
    /// both and rounding cannot drift across the symbol.
    /// </summary>
    private readonly struct ModuleGrid
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ModuleGrid"/> struct.
        /// </summary>
        /// <param name="left">The left edge of the modules, on the pixel grid.</param>
        /// <param name="top">The top edge of the modules, on the pixel grid.</param>
        /// <param name="moduleWidth">The width of a module in pixels.</param>
        public ModuleGrid(double left, double top, float moduleWidth)
        {
            this.Left = left;
            this.Top = top;
            this.ModuleWidth = moduleWidth;
        }

        /// <summary>
        /// Gets the left edge of the modules, on the pixel grid.
        /// </summary>
        public double Left { get; }

        /// <summary>
        /// Gets the top edge of the modules, on the pixel grid.
        /// </summary>
        public double Top { get; }

        /// <summary>
        /// Gets the width of a module in pixels.
        /// </summary>
        public float ModuleWidth { get; }

        /// <summary>
        /// Returns the x of the left edge of a column, rounded onto the pixel grid.
        /// </summary>
        /// <param name="column">The column, 0 to the column count.</param>
        /// <returns>The edge in pixels.</returns>
        public float ColumnEdge(int column) => (float)Math.Round(this.Left + ((double)column * this.ModuleWidth));

        /// <summary>
        /// Returns the y of the top edge of a row, rounded onto the pixel grid.
        /// </summary>
        /// <param name="row">The row, 0 to the row count.</param>
        /// <returns>The edge in pixels.</returns>
        public float RowEdge(int row) => (float)Math.Round(this.Top + ((double)row * this.ModuleWidth));
    }
}
