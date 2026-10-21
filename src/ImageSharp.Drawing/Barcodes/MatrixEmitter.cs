// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Converts a matrix, dotted or colour <see cref="BarcodeSymbol"/> into canvas commands. In a matrix symbol each run of adjacent dark modules in a row becomes
/// one rectangle, and the dark modules render through a single fill of one <see cref="BarcodePath"/>. In a dotted symbol each dark module becomes one circle,
/// and the dots render through a single fill of one <see cref="BarcodePath"/>. In a colour symbol each run of one colour in a row becomes one rectangle, and
/// each colour renders through one fill of one <see cref="BarcodePath"/>. Each text placement draws through one text command.
/// </summary>
internal static class MatrixEmitter
{
    /// <summary>
    /// The number of values in <see cref="BarcodeSymbol.OverlayEdges"/> for each dark rectangle of an overlay graphic: its left, top, right and bottom edges.
    /// </summary>
    private const int OverlayRectangleEdges = 4;

    /// <summary>
    /// Measures the symbol without drawing anything.
    /// </summary>
    /// <param name="symbol">The encoded matrix symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <returns>The measured layout of the symbol.</returns>
    public static BarcodeMetrics Measure(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
    {
        using PooledBuffer<MeasuredLine> lineBuffer = BarcodeEmitter.CreateLineBuffer(ref symbol, options);

        // A TextBlock is a class, so the span holds object references and cannot come from the byte pool. A matrix symbol has at most one text placement,
        // also when its text has line breaks, so one stack slot holds its block.
        InlineArray1<TextBlock> blockStorage = default;
        Span<TextBlock> blocks = ((Span<TextBlock>)blockStorage)[..lineBuffer.Span.Length];
        Shape(ref symbol, options, lineBuffer.Span, blocks);
        return Measure(ref symbol, options, origin, lineBuffer.Span, out _);
    }

    /// <summary>
    /// Draws the symbol onto the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded matrix symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="background">The brush that fills the renderable bounds before the modules draw, or <see langword="null"/> for none.</param>
    /// <returns>The measured layout of the symbol, the same as <see cref="Measure(ref BarcodeSymbol, BarcodeOptions, PointF)"/> returns.</returns>
    public static BarcodeMetrics Draw(DrawingCanvas canvas, ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin, Brush? background)
    {
        using PooledBuffer<MeasuredLine> lineBuffer = BarcodeEmitter.CreateLineBuffer(ref symbol, options);

        // A TextBlock is a class, so the span holds object references and cannot come from the byte pool. A matrix symbol has at most one text placement,
        // also when its text has line breaks, so one stack slot holds its block.
        InlineArray1<TextBlock> blockStorage = default;
        Span<TextBlock> blocks = ((Span<TextBlock>)blockStorage)[..lineBuffer.Span.Length];
        Shape(ref symbol, options, lineBuffer.Span, blocks);
        return Draw(canvas, ref symbol, options, origin, lineBuffer.Span, blocks, background);
    }

    /// <summary>
    /// Shapes the text of the symbol one time: one text block and one measured line for its text placement. The layout reads the line and the draw renders
    /// the block, so neither shapes the text again.
    /// </summary>
    /// <param name="symbol">The encoded matrix symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="lines">The measured line to write, or empty when the options carry no font.</param>
    /// <param name="blocks">The text block to write, one for each line.</param>
    public static void Shape(ref BarcodeSymbol symbol, BarcodeOptions options, Span<MeasuredLine> lines, Span<TextBlock> blocks)
    {
        Font? font = options.Font;
        if (font is null || lines.IsEmpty)
        {
            return;
        }

        // A matrix symbol has at most one text placement, also when its text has line breaks. The block keeps the options it was prepared with, its font and
        // alignment included.
        BarcodeTextPlacement placement = symbol.Text[0];
        Font placementFont = placement.FontScale == 1F ? font : new Font(font, font.Size * placement.FontScale);
        RichTextOptions textOptions = BarcodeTextOptionsFactory.Create(placementFont, options.Dpi, symbol.TextWeight);
        textOptions.TextAlignment = placement.Alignment switch
        {
            BarcodeTextAlignment.Left => TextAlignment.Start,
            BarcodeTextAlignment.Right => TextAlignment.End,
            _ => TextAlignment.Center,
        };

        TextBlock block = new(symbol.TextOf(in placement), textOptions);
        blocks[0] = block;
        TextMetrics metrics = block.Measure(-1);
        lines[0] = new MeasuredLine(metrics.Advance, metrics.Bounds, metrics.RenderableBounds, 0, 0);
    }

    /// <summary>
    /// Measures the symbol from its shaped text without drawing anything.
    /// </summary>
    /// <param name="symbol">The encoded matrix symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="lines">The lines that <see cref="Shape"/> wrote. The layout writes the origin of each line.</param>
    /// <param name="symbolOrigin">The top left corner of the first module of the symbol, in pixels, before it rounds onto the pixel grid.</param>
    /// <returns>The measured layout of the symbol.</returns>
    public static BarcodeMetrics Measure(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin, Span<MeasuredLine> lines, out PointF symbolOrigin)
    {
        MatrixLayout layout = new(ref symbol, options, origin, lines);
        symbolOrigin = layout.SymbolOrigin;
        return layout.Metrics;
    }

    /// <summary>
    /// Draws the symbol from its shaped text onto the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded matrix symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="lines">The lines that <see cref="Shape"/> wrote. The layout writes the origin of each line.</param>
    /// <param name="blocks">The blocks that <see cref="Shape"/> wrote, one for each line.</param>
    /// <param name="background">The brush that fills the renderable bounds before the modules draw, or <see langword="null"/> for none.</param>
    /// <returns>The measured layout of the symbol.</returns>
    public static BarcodeMetrics Draw(
        DrawingCanvas canvas,
        ref BarcodeSymbol symbol,
        BarcodeOptions options,
        PointF origin,
        Span<MeasuredLine> lines,
        ReadOnlySpan<TextBlock> blocks,
        Brush? background)
    {
        MatrixLayout layout = new(ref symbol, options, origin, lines);
        BarcodeMetrics metrics = layout.Metrics;
        if (background is not null)
        {
            RectangleF renderable = metrics.RenderableBounds;
            canvas.Fill(background, new RectanglePolygon(renderable.X, renderable.Y, renderable.Width, renderable.Height));
        }

        // A dotted symbol draws each dark module as a dot, a colour symbol draws runs of each colour, and every other matrix symbol draws runs of dark modules.
        ModuleGrid grid = layout.Grid;
        switch (symbol.Kind)
        {
            case BarcodeSymbolKind.Dotted:
                FillDots(canvas, ref symbol, options.BarBrush, in grid);
                break;
            case BarcodeSymbolKind.Colored:
                FillColorRuns(canvas, ref symbol, options.BarBrush, in grid);
                break;
            default:
                FillRuns(canvas, ref symbol, options.BarBrush, in grid);
                break;
        }

        layout.DrawText(canvas, blocks, options.BarBrush);
        return metrics;
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
    /// Fills the modules of a colour symbol: each run of adjacent modules of one colour in a row is one rectangle, and each colour is one fill of one
    /// <see cref="BarcodePath"/>. Dark modules take the bar brush and light modules stay the background, as in every other symbol; the four data colours are
    /// fixed, because a reader tells them apart by hue.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded colour symbol.</param>
    /// <param name="barBrush">The brush of the dark modules.</param>
    /// <param name="grid">The position and the module width of the modules on the pixel grid.</param>
    private static void FillColorRuns(DrawingCanvas canvas, ref BarcodeSymbol symbol, Brush barBrush, in ModuleGrid grid)
    {
        int columns = symbol.Columns;
        int rows = symbol.MatrixRows;
        ReadOnlySpan<byte> modules = MemoryMarshal.Cast<BarcodeModuleColor, byte>(symbol.ModuleColors);

        // The first pass counts the runs of every colour, so each colour's rectangle array is sized exactly; the paths retain the arrays until the canvas
        // flushes. A run starts at the first module that is not light and ends where its colour changes. Light modules are the background.
        const int colorCount = (int)BarcodeModuleColor.Green + 1;
        InlineArray6<int> countStorage = default;
        Span<int> counts = countStorage;
        for (int r = 0; r < rows; r++)
        {
            ReadOnlySpan<byte> row = modules.Slice(r * columns, columns);
            int end = 0;
            while (NextColorRun(row, ref end, out int start))
            {
                counts[row[start]]++;
            }
        }

        InlineArray6<RectangleF[]> runStorage = default;
        Span<RectangleF[]> runs = runStorage;
        InlineArray6<RectangleF> boundsStorage = default;
        Span<RectangleF> bounds = boundsStorage;
        for (int c = (int)BarcodeModuleColor.Dark; c < colorCount; c++)
        {
            runs[c] = new RectangleF[counts[c]];
            counts[c] = 0;
        }

        // The second pass writes each run to the array of its colour, and grows the bounds of that colour's path.
        for (int r = 0; r < rows; r++)
        {
            ReadOnlySpan<byte> row = modules.Slice(r * columns, columns);
            float rowTop = grid.RowEdge(r);
            float rowBottom = grid.RowEdge(r + 1);
            int end = 0;
            while (NextColorRun(row, ref end, out int start))
            {
                int c = row[start];
                RectangleF run = RectangleF.FromLTRB(grid.ColumnEdge(start), rowTop, grid.ColumnEdge(end), rowBottom);
                bounds[c] = counts[c] == 0 ? run : RectangleF.Union(bounds[c], run);
                runs[c][counts[c]++] = run;
            }
        }

        for (int c = (int)BarcodeModuleColor.Dark; c < colorCount; c++)
        {
            if (counts[c] == 0)
            {
                continue;
            }

            Brush brush = (BarcodeModuleColor)c switch
            {
                BarcodeModuleColor.Dark => barBrush,
                BarcodeModuleColor.Cyan => ModuleBrushes.Cyan,
                BarcodeModuleColor.Magenta => ModuleBrushes.Magenta,
                BarcodeModuleColor.Yellow => ModuleBrushes.Yellow,
                _ => ModuleBrushes.Green,
            };

            canvas.Fill(brush, new BarcodePath(runs[c], bounds[c]));
        }
    }

    /// <summary>
    /// Finds the next run of one colour in a row of a colour symbol: a maximal sequence of modules of one colour other than light.
    /// </summary>
    /// <param name="row">The module colours of the row.</param>
    /// <param name="end">On entry, the column the search starts at. On exit, the column after the run, or the row length when no run remains.</param>
    /// <param name="start">The first column of the run.</param>
    /// <returns><see langword="true"/> when a run was found.</returns>
    private static bool NextColorRun(ReadOnlySpan<byte> row, ref int end, out int start)
    {
        int offset = row[end..].IndexOfAnyExcept((byte)BarcodeModuleColor.Light);
        if (offset < 0)
        {
            start = row.Length;
            end = row.Length;
            return false;
        }

        start = end + offset;
        int length = row[start..].IndexOfAnyExcept(row[start]);
        end = length < 0 ? row.Length : start + length;
        return true;
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
    /// The layout of a matrix symbol: where its modules and each text line stand, from one origin. The measure and the draw read one layout.
    /// </summary>
    private ref struct MatrixLayout
    {
        /// <summary>
        /// The measured lines, one for each text placement, or none when the options carry no font.
        /// </summary>
        private readonly Span<MeasuredLine> lines;

        /// <summary>
        /// Initializes a new instance of the <see cref="MatrixLayout"/> struct.
        /// </summary>
        /// <param name="symbol">The encoded matrix symbol.</param>
        /// <param name="options">The sizing and painting options.</param>
        /// <param name="origin">The top left corner of the barcode, in pixels.</param>
        /// <param name="lines">The shaped lines, one for each text placement, or empty when the options carry no font. The layout writes their origins.</param>
        public MatrixLayout(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin, Span<MeasuredLine> lines)
        {
            this.lines = lines;
            float moduleWidth = (float)(symbol.XDimension / BarcodeOptions.MillimetresPerInch * options.Dpi);

            // Most matrix symbols have the same quiet zone on all four sides. Code One versions S and T have none above.
            bool quiet = options.IncludeQuietZones;
            int leadingZone = quiet ? (int)symbol.LeadingQuietZone : 0;
            int trailingZone = quiet ? (int)symbol.TrailingQuietZone : 0;
            int topZone = quiet ? (int)symbol.TopQuietZone : 0;
            int bottomZone = quiet ? (int)symbol.BottomQuietZone : 0;
            int columns = symbol.Columns;
            int rows = symbol.MatrixRows;

            ReadOnlySpan<BarcodeTextPlacement> text = symbol.Text;

            // The layout box: the barcode and the line box of each line, in exact positions relative to the top left corner of the barcode. Nothing in this
            // box rounds, as nothing in the layout box of text rounds. The ink of each line starts on its text edge.
            float leadingOffset = leadingZone * moduleWidth;
            float topOffset = topZone * moduleWidth;
            float layoutLeft = 0;
            float layoutTop = 0;
            float layoutRight = (columns + leadingZone + trailingZone) * moduleWidth;
            float layoutBottom = (rows + topZone + bottomZone) * moduleWidth;
            for (int t = 0; t < lines.Length; t++)
            {
                BarcodeTextPlacement placement = text[t];
                MeasuredLine line = lines[t];
                float cellLeft = leadingOffset + (placement.Left * moduleWidth);
                float cellRight = leadingOffset + (placement.Right * moduleWidth);
                float x = placement.Alignment switch
                {
                    BarcodeTextAlignment.Left => cellLeft - line.Bounds.Left,
                    BarcodeTextAlignment.Right => cellRight - line.Bounds.Right,
                    _ => ((cellLeft + cellRight) * 0.5F) - (line.RenderableBounds.Width * 0.5F),
                };

                float y = topOffset + (placement.TextEdge * moduleWidth) - line.Bounds.Top;
                layoutLeft = MathF.Min(layoutLeft, x + line.Advance.Left);
                layoutTop = MathF.Min(layoutTop, y + line.Advance.Top);
                layoutRight = MathF.Max(layoutRight, x + line.Advance.Right);
                layoutBottom = MathF.Max(layoutBottom, y + line.Advance.Bottom);
            }

            // The origin is the top left corner of the layout box, as it is for text. A line wider than the symbol moves the symbol right.
            PointF symbolOrigin = new(origin.X - layoutLeft, origin.Y - layoutTop);
            RectangleF advance = new(origin.X, origin.Y, layoutRight - layoutLeft, layoutBottom - layoutTop);

            // Each edge rounds from its exact position onto the device pixel grid, so the modules stay crisp and rounding cannot drift across the symbol. The
            // positions are products of a whole module count and the module width, calculated in double from the corner of the symbol, and the edge two
            // neighbours share rounds to one pixel for both. The corner itself starts on a whole pixel. When a module is not a whole number of pixels, the
            // modules alternate between two widths, and a corner between pixels makes that pattern depend on the width of the quiet zone. The two outer rings
            // of a finder pattern can then round to different widths.
            this.SymbolOrigin = new PointF(symbolOrigin.X + leadingOffset, symbolOrigin.Y + topOffset);
            double symbolLeft = Math.Round(this.SymbolOrigin.X);
            double symbolTop = Math.Round(this.SymbolOrigin.Y);
            this.Grid = new ModuleGrid(symbolLeft, symbolTop, moduleWidth);

            // The bounds are the exact ink. A run of modules draws between two module edges, which round onto the pixel grid. A dot fills its cell exactly, so
            // its edges are the cell edges. The outermost dark modules set the ink, and the symbol always has at least one.
            ReadOnlySpan<byte> marks = symbol.Kind == BarcodeSymbolKind.Colored
                ? MemoryMarshal.Cast<BarcodeModuleColor, byte>(symbol.ModuleColors)
                : MemoryMarshal.Cast<bool, byte>(symbol.Modules);

            int firstRow = -1;
            int lastRow = -1;
            int firstColumn = columns;
            int lastColumn = -1;
            for (int r = 0; r < rows; r++)
            {
                // A light module is the byte 0 in both module forms: false, and BarcodeModuleColor.Light. The searches are vectorized.
                ReadOnlySpan<byte> row = marks.Slice(r * columns, columns);
                int first = row.IndexOfAnyExcept((byte)0);
                if (first < 0)
                {
                    continue;
                }

                firstRow = firstRow < 0 ? r : firstRow;
                lastRow = r;
                firstColumn = Math.Min(firstColumn, first);
                lastColumn = Math.Max(lastColumn, row.LastIndexOfAnyExcept((byte)0));
            }

            RectangleF bounds = symbol.Kind == BarcodeSymbolKind.Dotted
                ? RectangleF.FromLTRB(
                    (float)(symbolLeft + ((double)firstColumn * moduleWidth)),
                    (float)(symbolTop + ((double)firstRow * moduleWidth)),
                    (float)(symbolLeft + ((double)(lastColumn + 1) * moduleWidth)),
                    (float)(symbolTop + ((double)(lastRow + 1) * moduleWidth)))
                : RectangleF.FromLTRB(
                    this.Grid.ColumnEdge(firstColumn),
                    this.Grid.RowEdge(firstRow),
                    this.Grid.ColumnEdge(lastColumn + 1),
                    this.Grid.RowEdge(lastRow + 1));

            // The ink of the first line starts on the text edge, which rounds onto the pixel grid as the module edges do.
            for (int t = 0; t < lines.Length; t++)
            {
                BarcodeTextPlacement placement = text[t];
                ref MeasuredLine line = ref lines[t];
                float x = BarEmitter.TextOrigin(placement, line.Bounds, line.RenderableBounds.Width, (float)symbolLeft, moduleWidth);
                float y = (float)Math.Round(symbolTop + ((double)placement.TextEdge * moduleWidth)) - line.Bounds.Top;
                line.Origin = new Vector2(x, y);
                bounds = RectangleF.Union(bounds, new RectangleF(x + line.Bounds.X, y + line.Bounds.Y, line.Bounds.Width, line.Bounds.Height));
            }

            // As in text measuring, the advance is relative to the origin, and the bounds and the renderable bounds are absolute.
            this.Metrics = new BarcodeMetrics(new RectangleF(0, 0, advance.Width, advance.Height), bounds, RectangleF.Union(advance, bounds));
        }

        /// <summary>
        /// Gets the top left corner of the first module of the symbol, in pixels, before it rounds onto the pixel grid.
        /// </summary>
        public readonly PointF SymbolOrigin { get; }

        /// <summary>
        /// Gets the module grid of the symbol on the page.
        /// </summary>
        public readonly ModuleGrid Grid { get; }

        /// <summary>
        /// Gets the metrics of the layout.
        /// </summary>
        public readonly BarcodeMetrics Metrics { get; }

        /// <summary>
        /// Draws each text line where the layout put it. Each line renders its shaped block, so it is not shaped again.
        /// </summary>
        /// <param name="canvas">The canvas to render onto.</param>
        /// <param name="blocks">The shaped blocks, one for each line.</param>
        /// <param name="brush">The brush of the text.</param>
        public readonly void DrawText(DrawingCanvas canvas, ReadOnlySpan<TextBlock> blocks, Brush brush)
        {
            for (int t = 0; t < this.lines.Length; t++)
            {
                canvas.DrawText(blocks[t], this.lines[t].Origin, -1, brush, null);
            }
        }
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

    /// <summary>
    /// The brushes of the colored modules. Brushes are immutable, so every draw shares them. A nested class creates them on the first colored symbol only.
    /// </summary>
    private static class ModuleBrushes
    {
        /// <summary>
        /// The brush of a cyan module.
        /// </summary>
        public static readonly SolidBrush Cyan = Brushes.Solid(Color.Cyan);

        /// <summary>
        /// The brush of a magenta module.
        /// </summary>
        public static readonly SolidBrush Magenta = Brushes.Solid(Color.Magenta);

        /// <summary>
        /// The brush of a yellow module.
        /// </summary>
        public static readonly SolidBrush Yellow = Brushes.Solid(Color.Yellow);

        /// <summary>
        /// The brush of a green module.
        /// </summary>
        public static readonly SolidBrush Green = Brushes.Solid(Color.Lime);
    }
}
