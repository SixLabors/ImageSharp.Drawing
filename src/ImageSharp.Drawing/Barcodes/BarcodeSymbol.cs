// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The device independent description of an encoded barcode, measured in modules. A module is the narrowest nominal element of a symbology, whose width the GS1
/// General Specifications and related specifications call the X-dimension. A symbol carries no pixel sizes, brushes or fonts. The emitter adds those when it
/// turns the symbol into canvas commands.
/// </summary>
/// <remarks>
/// An encoder writes the symbol once: it begins a linear, stacked or matrix symbol with the sizes that its validated input gives, fills the spans that the
/// begin methods return, adds the text and ends the symbol. The symbol keeps its values in three buffers rented from the shared <see cref="byte"/> pool. The
/// layout buffer holds the rows, runs and bars or the modules. <see cref="BeginLinear"/>, <see cref="BeginStacked"/> or
/// <see cref="BeginMatrix(int, int, int, double)"/> rents it, and <see cref="InsertRow"/> replaces it with a larger buffer. The text buffer holds the text
/// placements and their characters, and <see cref="ReserveText"/> rents it. The overlay buffer holds the edges of the overlay graphic, and
/// <see cref="AddOverlay"/> rents it. Each rent happens before the encoder's loops, with the exact size that its validated input gives. <see cref="Dispose"/>
/// returns all three buffers. A rented range is cleared, so every value that the encoder does not write is zero. The caller declares the symbol as a local
/// and passes it by reference.
/// </remarks>
internal struct BarcodeSymbol : IDisposable
{
    /// <summary>
    /// The layout buffer: the rows, then the runs, then the bar heights and the bar tops of a linear or stacked symbol, or the modules of a matrix symbol.
    /// </summary>
    private PooledBuffer<byte> layoutBuffer;

    /// <summary>
    /// The text buffer: the text placements, then their characters.
    /// </summary>
    private PooledBuffer<byte> textBuffer;

    /// <summary>
    /// The overlay buffer: the edges of the dark rectangles of the graphic centred on a matrix symbol.
    /// </summary>
    private PooledBuffer<byte> overlayBuffer;

    /// <summary>
    /// The number of edges in <see cref="overlayBuffer"/>, four for each rectangle.
    /// </summary>
    private int overlayEdgeCount;

    /// <summary>
    /// The byte offset of the runs in the layout buffer.
    /// </summary>
    private int runOffset;

    /// <summary>
    /// The byte offset of the bar heights in the layout buffer.
    /// </summary>
    private int barOffset;

    /// <summary>
    /// The number of bars reserved. The bar tops follow that many bar heights.
    /// </summary>
    private int barCapacity;

    /// <summary>
    /// The byte offset of the characters in the text buffer.
    /// </summary>
    private int charOffset;

    /// <summary>
    /// The number of modules of a matrix symbol.
    /// </summary>
    private int moduleCount;

    /// <summary>
    /// The number of runs the rows added so far take.
    /// </summary>
    private int runCount;

    /// <summary>
    /// The number of bars the rows added so far take.
    /// </summary>
    private int barCount;

    /// <summary>
    /// The number of rows added so far.
    /// </summary>
    private int rowCount;

    /// <summary>
    /// The number of text placements added so far.
    /// </summary>
    private int textCount;

    /// <summary>
    /// The number of characters the text placements added so far take.
    /// </summary>
    private int charCount;

    /// <summary>
    /// Gets the form of the symbol.
    /// </summary>
    public BarcodeSymbolKind Kind { get; private set; }

    /// <summary>
    /// Gets the width of the symbol in modules, excluding quiet zones.
    /// </summary>
    public float WidthInModules { get; private set; }

    /// <summary>
    /// Gets the height of the symbol in modules, excluding the human readable interpretation.
    /// </summary>
    public float HeightInModules { get; private set; }

    /// <summary>
    /// Gets the width in modules of the quiet zone that comes before the symbol. Each symbology sets its own quiet zone width. For example, Table 5-11 of the
    /// GS1 General Specifications gives 11 modules before an EAN-13 symbol, and a postal symbology states its clear zone in inches, which is not a whole number
    /// of modules.
    /// </summary>
    public float LeadingQuietZone { get; private set; }

    /// <summary>
    /// Gets the width of the quiet zone that must follow the symbol, in modules.
    /// </summary>
    public float TrailingQuietZone { get; private set; }

    /// <summary>
    /// Gets the height of the quiet zone above the symbol, in modules. A symbology whose specification gives no vertical quiet zone has none.
    /// </summary>
    public float TopQuietZone { get; private set; }

    /// <summary>
    /// Gets the height of the quiet zone below the symbol, in modules. A symbology whose specification gives no vertical quiet zone has none.
    /// </summary>
    public float BottomQuietZone { get; private set; }

    /// <summary>
    /// Gets the thickness in modules of the bars that close the top and the bottom of a linear or stacked symbol, or zero when there are none: the bearer bar
    /// of a linear symbol, which also closes the sides when the quiet zones are drawn, or the edge bars of a stacked symbol.
    /// </summary>
    public float FrameThickness { get; private set; }

    /// <summary>
    /// Gets the width of one run unit in modules. A run width counts run units.
    /// </summary>
    public float RunUnit { get; private set; }

    /// <summary>
    /// Gets the number of run units in one module of a symbol whose runs are each a whole number of modules with a correction of less than half a module, or 0
    /// for a symbol without such corrections. Table 5-10 of the GS1 General Specifications corrects the bars and spaces of some EAN/UPC characters by one
    /// thirteenth of a module. The emitter rounds the module edges and the corrections onto the pixel grid apart, so a correction never moves an edge further
    /// than its own nearest whole number of pixels.
    /// </summary>
    public int CorrectedModuleUnits { get; private set; }

    /// <summary>
    /// Gets a value indicating whether every bar of the symbol is one width at one pitch, so each run draws at a whole number of pixels of its own.
    /// </summary>
    public bool UniformBars { get; private set; }

    /// <summary>
    /// Gets the number of module columns of a matrix symbol.
    /// </summary>
    public int Columns { get; private set; }

    /// <summary>
    /// Gets the number of module rows of a matrix symbol.
    /// </summary>
    public int MatrixRows { get; private set; }

    /// <summary>
    /// Gets the X dimension in millimetres, the width of one module: the X dimension of the options, else the nominal X dimension the specification of the
    /// symbology gives for this symbol. The encoder resolves it once, measures every height it states in millimetres with it, and the emitter draws the modules
    /// at it.
    /// </summary>
    public double XDimension { get; private set; }

    /// <summary>
    /// Gets the side in modules of the square graphic centred on a matrix symbol, or 0 when the symbol has none. The graphic covers its square completely, so
    /// the modules under the square do not draw.
    /// </summary>
    public float OverlaySize { get; private set; }

    /// <summary>
    /// Gets the side of the overlay graphic in the units of <see cref="OverlayEdges"/>.
    /// </summary>
    public float OverlayUnit { get; private set; }

    /// <summary>
    /// Gets the dark rectangles of the overlay graphic, each as its left, top, right and bottom edge in the units of <see cref="OverlayUnit"/> from the top
    /// left corner of its square, the y axis down. The rest of the square is light.
    /// </summary>
    public readonly ReadOnlySpan<float> OverlayEdges => Slice<float>(this.overlayBuffer, 0, this.overlayEdgeCount);

    /// <summary>
    /// Gets the thickness in modules of the identification bars of a linear symbol, or 0 when the symbol has none. One bar stands above the topmost text line
    /// and one below the lowest, each spanning the symbol and its quiet zones.
    /// </summary>
    public float IdentificationBarThickness { get; private set; }

    /// <summary>
    /// Gets the clear space in modules between an identification bar and the ink of the text line next to it.
    /// </summary>
    public float IdentificationBarClearance { get; private set; }

    /// <summary>
    /// Gets the smallest printed height of the characters of the text lines in modules, or 0 when the symbology states none.
    /// </summary>
    public float TextHeightMinimum { get; private set; }

    /// <summary>
    /// Gets the largest printed height of the characters of the text lines in modules, or 0 when the symbology states none.
    /// </summary>
    public float TextHeightMaximum { get; private set; }

    /// <summary>
    /// Gets the smallest size of the caption in points, or 0 when the symbology states none.
    /// </summary>
    public float CaptionMinimumSize { get; private set; }

    /// <summary>
    /// Gets the font weight the text of the symbol renders at, or <see langword="null"/> for the weight of the font of the options. A symbology whose standard
    /// requires a weight sets it, and a font without a face of that weight renders it with a synthetic weight.
    /// </summary>
    public FontWeight? TextWeight { get; private set; }

    /// <summary>
    /// Gets the smallest size of the text lines in points, or 0 when the symbology states none.
    /// </summary>
    public float TextMinimumSize { get; private set; }

    /// <summary>
    /// Gets the largest size of the text lines in points, or 0 when the symbology states none.
    /// </summary>
    public float TextMaximumSize { get; private set; }

    /// <summary>
    /// Gets the rows of a linear or stacked symbol from the top down. A linear symbol has one row.
    /// </summary>
    public readonly ReadOnlySpan<BarcodeRow> Rows => Slice<BarcodeRow>(this.layoutBuffer, 0, this.rowCount);

    /// <summary>
    /// Gets the run widths of all rows in run units. The runs of a row alternate bar and space, starting with a bar.
    /// </summary>
    public readonly ReadOnlySpan<int> Runs => Slice<int>(this.layoutBuffer, this.runOffset, this.runCount);

    /// <summary>
    /// Gets the bar heights of all rows, in modules.
    /// </summary>
    public readonly ReadOnlySpan<float> BarHeights => Slice<float>(this.layoutBuffer, this.barOffset, this.barCount);

    /// <summary>
    /// Gets the bar tops of all rows, in modules from the top of their row.
    /// </summary>
    public readonly ReadOnlySpan<float> BarTops => Slice<float>(this.layoutBuffer, this.barOffset + (this.barCapacity * sizeof(float)), this.barCount);

    /// <summary>
    /// Gets the modules of a matrix symbol, row-major, true for a dark module.
    /// </summary>
    public readonly ReadOnlySpan<bool> Modules => Slice<bool>(this.layoutBuffer, 0, this.moduleCount);

    /// <summary>
    /// Gets the module colours of a colour symbol, row-major.
    /// </summary>
    public readonly ReadOnlySpan<BarcodeModuleColor> ModuleColors => Slice<BarcodeModuleColor>(this.layoutBuffer, 0, this.moduleCount);

    /// <summary>
    /// Gets the human readable interpretation. The span is empty when the symbol carries no text.
    /// </summary>
    public readonly ReadOnlySpan<BarcodeTextPlacement> Text => Slice<BarcodeTextPlacement>(this.textBuffer, 0, this.textCount);

    /// <summary>
    /// Returns the characters of a text placement of this symbol.
    /// </summary>
    /// <param name="placement">The placement.</param>
    /// <returns>The characters.</returns>
    public readonly ReadOnlySpan<char> TextOf(in BarcodeTextPlacement placement)
        => Slice<char>(this.textBuffer, this.charOffset + (placement.TextStart * sizeof(char)), placement.TextLength);

    /// <summary>
    /// Begins a linear symbol of one row.
    /// </summary>
    /// <param name="runCount">The number of runs.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="XDimension"/>.</param>
    /// <param name="barHeights">The bar heights to write, one for each run at an even index.</param>
    /// <param name="barTops">The bar tops to write, in modules from the top of the symbol.</param>
    /// <returns>The run widths to write, in run units.</returns>
    public Span<int> BeginLinear(int runCount, double xDimension, out Span<float> barHeights, out Span<float> barTops)
    {
        this.Kind = BarcodeSymbolKind.Linear;
        this.XDimension = xDimension;
        this.Reserve(1, runCount, (runCount + 1) >> 1);
        return this.AddRow(runCount, out barHeights, out barTops);
    }

    /// <summary>
    /// Begins a stacked symbol. The encoder then adds each row with <see cref="AddRow"/>.
    /// </summary>
    /// <param name="rowCount">The number of rows.</param>
    /// <param name="runCount">The number of runs of all rows.</param>
    /// <param name="barCount">The number of bars of all rows: for each row, half its run count rounded up.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="XDimension"/>.</param>
    public void BeginStacked(int rowCount, int runCount, int barCount, double xDimension)
    {
        this.Kind = BarcodeSymbolKind.Stacked;
        this.XDimension = xDimension;
        this.Reserve(rowCount, runCount, barCount);
    }

    /// <summary>
    /// Adds the next row of a stacked symbol, below the rows added before it.
    /// </summary>
    /// <param name="runCount">The number of runs of the row.</param>
    /// <param name="barHeights">The bar heights to write, one for each run at an even index.</param>
    /// <param name="barTops">The bar tops to write, in modules from the top of the row.</param>
    /// <returns>The run widths to write, in run units.</returns>
    public Span<int> AddRow(int runCount, out Span<float> barHeights, out Span<float> barTops)
    {
        int rowBars = (runCount + 1) >> 1;
        Slice<BarcodeRow>(this.layoutBuffer, this.rowCount * Unsafe.SizeOf<BarcodeRow>(), 1)[0] = new BarcodeRow(this.runCount, runCount, this.barCount);
        Span<int> runs = Slice<int>(this.layoutBuffer, this.runOffset + (this.runCount * sizeof(int)), runCount);
        int heightOffset = this.barOffset + (this.barCount * sizeof(float));
        barHeights = Slice<float>(this.layoutBuffer, heightOffset, rowBars);
        barTops = Slice<float>(this.layoutBuffer, heightOffset + (this.barCapacity * sizeof(float)), rowBars);
        this.rowCount++;
        this.runCount += runCount;
        this.barCount += rowBars;
        return runs;
    }

    /// <summary>
    /// Returns the width of a row of runs in run units, for an encoder that places its text or sizes its bars from the width before the symbol ends, and for
    /// the ink edges of each row.
    /// </summary>
    /// <param name="runs">The alternating bar and space run widths in run units.</param>
    /// <returns>The width in run units.</returns>
    public static int RunWidth(ReadOnlySpan<int> runs)
    {
        // Each lane of the vector adds every lane-count-th run, and the lanes add together at the end. Integer addition is exact, so the sum equals the
        // scalar sum in any order. The scalar loop adds the runs that do not fill a vector.
        int width = 0;
        int i = 0;
        if (Vector.IsHardwareAccelerated && runs.Length >= Vector<int>.Count)
        {
            Vector<int> lanes = Vector<int>.Zero;
            for (; i <= runs.Length - Vector<int>.Count; i += Vector<int>.Count)
            {
                lanes += new Vector<int>(runs[i..]);
            }

            width = Vector.Sum(lanes);
        }

        for (; i < runs.Length; i++)
        {
            width += runs[i];
        }

        return width;
    }

    /// <summary>
    /// Shortens the last row added to the given number of runs, for an encoder that reserves the most runs its data can take and learns the exact count as it
    /// writes. The storage stays reserved.
    /// </summary>
    /// <param name="runCount">The number of runs the row keeps, at most the number it was added with.</param>
    public void TrimLastRow(int runCount)
    {
        Span<BarcodeRow> rows = Slice<BarcodeRow>(this.layoutBuffer, 0, this.rowCount);
        BarcodeRow row = rows[^1];
        rows[^1] = new BarcodeRow(row.RunStart, runCount, row.BarStart);
        this.runCount = row.RunStart + runCount;
        this.barCount = row.BarStart + ((runCount + 1) >> 1);
    }

    /// <summary>
    /// Inserts a row into an ended linear or stacked symbol that has no bearer bar and no edge bars, so the symbol becomes a stacked symbol. The rows from the
    /// index down and the text lines at or below the top of the new row move down by its height. A GS1 Composite symbol joins its parts with such a row:
    /// section 11.4 of ISO/IEC 24723 puts a separator pattern between the linear component and the 2D component.
    /// </summary>
    /// <param name="index">The index the new row takes: 0 for a row above the symbol, or the row count for a row below it.</param>
    /// <param name="runs">The runs of the new row, as wide as the symbol.</param>
    /// <param name="height">The height of the new row in modules.</param>
    public void InsertRow(int index, ReadOnlySpan<int> runs, float height)
    {
        // The layout buffer holds the rows, the runs and the bars in one block sized when the symbol began, so the rows copy into a new block with room for the
        // new row. The old block stays rented until the copy ends.
        PooledBuffer<byte> old = this.layoutBuffer;
        int oldRowCount = this.rowCount;
        int oldRunCount = this.runCount;
        int oldBarCount = this.barCount;
        int oldRunOffset = this.runOffset;
        int oldBarOffset = this.barOffset;
        int oldBarCapacity = this.barCapacity;
        float insertTop = index < oldRowCount ? this.Rows[index].Top : this.HeightInModules;
        try
        {
            ReadOnlySpan<BarcodeRow> oldRows = Slice<BarcodeRow>(old, 0, oldRowCount);
            ReadOnlySpan<int> oldRuns = Slice<int>(old, oldRunOffset, oldRunCount);
            ReadOnlySpan<float> oldHeights = Slice<float>(old, oldBarOffset, oldBarCount);
            ReadOnlySpan<float> oldTops = Slice<float>(old, oldBarOffset + (oldBarCapacity * sizeof(float)), oldBarCount);

            this.Reserve(oldRowCount + 1, oldRunCount + runs.Length, oldBarCount + ((runs.Length + 1) >> 1));
            this.rowCount = 0;
            this.runCount = 0;
            this.barCount = 0;
            for (int r = 0; r <= oldRowCount; r++)
            {
                if (r == index)
                {
                    runs.CopyTo(this.AddRow(runs.Length, out Span<float> newHeights, out _));
                    newHeights.Fill(height);
                }

                if (r < oldRowCount)
                {
                    BarcodeRow row = oldRows[r];
                    oldRuns.Slice(row.RunStart, row.RunCount).CopyTo(this.AddRow(row.RunCount, out Span<float> heights, out Span<float> tops));
                    oldHeights.Slice(row.BarStart, row.BarCount).CopyTo(heights);
                    oldTops.Slice(row.BarStart, row.BarCount).CopyTo(tops);
                }
            }
        }
        finally
        {
            old.Dispose();
        }

        this.Kind = BarcodeSymbolKind.Stacked;
        this.HeightInModules = this.EndRows(0F);

        // A text line at or below the new row keeps its place relative to the bars it belongs to.
        Span<BarcodeTextPlacement> placements = Slice<BarcodeTextPlacement>(this.textBuffer, 0, this.textCount);
        for (int i = 0; i < placements.Length; i++)
        {
            BarcodeTextPlacement placement = placements[i];
            if (placement.TextEdge >= insertTop)
            {
                placements[i] = new BarcodeTextPlacement(
                    placement.TextStart,
                    placement.TextLength,
                    placement.Left,
                    placement.Right,
                    placement.Side,
                    placement.TextEdge + height,
                    placement.FontScale,
                    placement.IsCaption,
                    placement.Alignment,
                    placement.WithinSpan);
            }
        }
    }

    /// <summary>
    /// Ends a linear symbol with no bearer bar, whose runs are whole modules.
    /// </summary>
    /// <param name="leadingQuietZone">The quiet zone before the symbol, in modules.</param>
    /// <param name="trailingQuietZone">The quiet zone after the symbol, in modules.</param>
    public void EndLinear(float leadingQuietZone, float trailingQuietZone)
        => this.EndLinear(leadingQuietZone, trailingQuietZone, 0F);

    /// <summary>
    /// Ends a linear symbol whose runs are whole modules.
    /// </summary>
    /// <param name="leadingQuietZone">The quiet zone before the symbol, in modules.</param>
    /// <param name="trailingQuietZone">The quiet zone after the symbol, in modules.</param>
    /// <param name="bearerBarThickness">The thickness of the bearer bar in modules, or zero for none.</param>
    public void EndLinear(float leadingQuietZone, float trailingQuietZone, float bearerBarThickness)
        => this.EndLinear(leadingQuietZone, trailingQuietZone, bearerBarThickness, 1F, false);

    /// <summary>
    /// Ends a linear symbol.
    /// </summary>
    /// <param name="leadingQuietZone">The quiet zone before the symbol, in modules.</param>
    /// <param name="trailingQuietZone">The quiet zone after the symbol, in modules.</param>
    /// <param name="bearerBarThickness">The thickness of the bearer bar in modules, or zero for none.</param>
    /// <param name="runUnit">The width of one run unit in modules.</param>
    /// <param name="uniformBars">Whether every bar is one width at one pitch.</param>
    public void EndLinear(float leadingQuietZone, float trailingQuietZone, float bearerBarThickness, float runUnit, bool uniformBars)
        => this.EndLinear(leadingQuietZone, trailingQuietZone, 0F, 0F, bearerBarThickness, runUnit, uniformBars);

    /// <summary>
    /// Ends a linear symbol that has quiet zones above and below it.
    /// </summary>
    /// <param name="leadingQuietZone">The quiet zone before the symbol, in modules.</param>
    /// <param name="trailingQuietZone">The quiet zone after the symbol, in modules.</param>
    /// <param name="topQuietZone">The quiet zone above the symbol, in modules.</param>
    /// <param name="bottomQuietZone">The quiet zone below the symbol, in modules.</param>
    /// <param name="bearerBarThickness">The thickness of the bearer bar in modules, or zero for none.</param>
    /// <param name="runUnit">The width of one run unit in modules.</param>
    /// <param name="uniformBars">Whether every bar is one width at one pitch.</param>
    public void EndLinear(
        float leadingQuietZone,
        float trailingQuietZone,
        float topQuietZone,
        float bottomQuietZone,
        float bearerBarThickness,
        float runUnit,
        bool uniformBars)
    {
        this.LeadingQuietZone = leadingQuietZone;
        this.TrailingQuietZone = trailingQuietZone;
        this.TopQuietZone = topQuietZone;
        this.BottomQuietZone = bottomQuietZone;
        this.FrameThickness = bearerBarThickness;
        this.RunUnit = runUnit;
        this.UniformBars = uniformBars;

        // The bearer bar closes the bottom of the symbol below the lowest bar.
        this.HeightInModules = this.EndRows(0F) + bearerBarThickness;
    }

    /// <summary>
    /// Ends a stacked symbol with no vertical quiet zones.
    /// </summary>
    /// <param name="edgeBarHeight">The height of the bars that close the top and the bottom of the symbol, or zero for none.</param>
    /// <param name="leadingQuietZone">The quiet zone before the symbol, in modules.</param>
    /// <param name="trailingQuietZone">The quiet zone after the symbol, in modules.</param>
    public void EndStacked(float edgeBarHeight, float leadingQuietZone, float trailingQuietZone)
        => this.EndStacked(edgeBarHeight, leadingQuietZone, trailingQuietZone, 0F, 0F);

    /// <summary>
    /// Ends a stacked symbol. Every row has the width of the first row; its runs are whole modules.
    /// </summary>
    /// <param name="edgeBarHeight">The height of the bars that close the top and the bottom of the symbol, or zero for none.</param>
    /// <param name="leadingQuietZone">The quiet zone before the symbol, in modules.</param>
    /// <param name="trailingQuietZone">The quiet zone after the symbol, in modules.</param>
    /// <param name="topQuietZone">The quiet zone above the symbol, in modules.</param>
    /// <param name="bottomQuietZone">The quiet zone below the symbol, in modules.</param>
    public void EndStacked(float edgeBarHeight, float leadingQuietZone, float trailingQuietZone, float topQuietZone, float bottomQuietZone)
    {
        this.LeadingQuietZone = leadingQuietZone;
        this.TrailingQuietZone = trailingQuietZone;
        this.TopQuietZone = topQuietZone;
        this.BottomQuietZone = bottomQuietZone;
        this.FrameThickness = edgeBarHeight;
        this.RunUnit = 1F;

        // The rows stand between the top and the bottom edge bars.
        this.HeightInModules = this.EndRows(edgeBarHeight) + edgeBarHeight;
    }

    /// <summary>
    /// Begins a matrix symbol. The modules start light.
    /// </summary>
    /// <param name="columns">The number of module columns.</param>
    /// <param name="rows">The number of module rows.</param>
    /// <param name="quietZone">The quiet zone on each of the four sides, in modules.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="XDimension"/>.</param>
    /// <returns>The modules to write, row-major, true for a dark module.</returns>
    public Span<bool> BeginMatrix(int columns, int rows, int quietZone, double xDimension)
        => this.BeginMatrix(columns, rows, quietZone, quietZone, quietZone, quietZone, xDimension);

    /// <summary>
    /// Begins a matrix symbol with a quiet zone of its own on each side. The modules start light.
    /// </summary>
    /// <param name="columns">The number of module columns.</param>
    /// <param name="rows">The number of module rows.</param>
    /// <param name="leadingQuietZone">The quiet zone on the left, in modules.</param>
    /// <param name="trailingQuietZone">The quiet zone on the right, in modules.</param>
    /// <param name="topQuietZone">The quiet zone above, in modules.</param>
    /// <param name="bottomQuietZone">The quiet zone below, in modules.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="XDimension"/>.</param>
    /// <returns>The modules to write, row-major, true for a dark module.</returns>
    public Span<bool> BeginMatrix(int columns, int rows, int leadingQuietZone, int trailingQuietZone, int topQuietZone, int bottomQuietZone, double xDimension)
    {
        this.Kind = BarcodeSymbolKind.Matrix;
        this.XDimension = xDimension;
        this.Columns = columns;
        this.MatrixRows = rows;
        this.WidthInModules = columns;
        this.HeightInModules = rows;
        this.LeadingQuietZone = leadingQuietZone;
        this.TrailingQuietZone = trailingQuietZone;
        this.TopQuietZone = topQuietZone;
        this.BottomQuietZone = bottomQuietZone;
        this.RunUnit = 1F;

        this.moduleCount = columns * rows;
        this.layoutBuffer = Rent(this.moduleCount);
        return Slice<bool>(this.layoutBuffer, 0, this.moduleCount);
    }

    /// <summary>
    /// Begins a hexagonal symbol: rows of hexagonal modules X wide, every odd row half a module to the right, the rows √3/2 X apart. The modules start light.
    /// </summary>
    /// <param name="columns">The number of module columns.</param>
    /// <param name="rows">The number of module rows.</param>
    /// <param name="sideQuietZone">The quiet zone on the left and the right, in X.</param>
    /// <param name="endQuietZone">The quiet zone on the top and the bottom, in X.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="XDimension"/>.</param>
    /// <returns>The modules to write, row-major, true for a dark module.</returns>
    public Span<bool> BeginHexagonal(int columns, int rows, float sideQuietZone, float endQuietZone, double xDimension)
    {
        Span<bool> modules = this.BeginMatrix(columns, rows, 0, xDimension);
        this.Kind = BarcodeSymbolKind.Hexagonal;

        // The pointed hexagons are 2/√3 X tall, and the rows stand √3/2 X apart, so the symbol is that tall over its last row.
        this.HeightInModules = (float)(((rows - 1) * Math.Sqrt(3) / 2) + (2 / Math.Sqrt(3)));
        this.LeadingQuietZone = sideQuietZone;
        this.TrailingQuietZone = sideQuietZone;
        this.TopQuietZone = endQuietZone;
        this.BottomQuietZone = endQuietZone;
        return modules;
    }

    /// <summary>
    /// Begins a dotted symbol: a grid of cells X wide and X high, each dark module a round dot centred in its cell. The modules start light.
    /// </summary>
    /// <param name="columns">The number of module columns.</param>
    /// <param name="rows">The number of module rows.</param>
    /// <param name="quietZone">The quiet zone on each of the four sides, in modules.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="XDimension"/>.</param>
    /// <returns>The modules to write, row-major, true for a dark module.</returns>
    public Span<bool> BeginDotted(int columns, int rows, int quietZone, double xDimension)
    {
        Span<bool> modules = this.BeginMatrix(columns, rows, quietZone, xDimension);
        this.Kind = BarcodeSymbolKind.Dotted;
        return modules;
    }

    /// <summary>
    /// Begins a colour symbol: a grid of square modules, each a <see cref="BarcodeModuleColor"/>. The modules start light.
    /// </summary>
    /// <param name="columns">The number of module columns.</param>
    /// <param name="rows">The number of module rows.</param>
    /// <param name="quietZone">The quiet zone on each of the four sides, in modules.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="XDimension"/>.</param>
    /// <returns>The module colours to write, row-major.</returns>
    public Span<BarcodeModuleColor> BeginColored(int columns, int rows, int quietZone, double xDimension)
    {
        // A colour takes one byte, as a bool does, so the module buffer holds the colours.
        Span<bool> modules = this.BeginMatrix(columns, rows, quietZone, xDimension);
        this.Kind = BarcodeSymbolKind.Colored;
        return MemoryMarshal.Cast<bool, BarcodeModuleColor>(modules);
    }

    /// <summary>
    /// Reserves the text placements and their characters. The encoder calls it once, before it adds any text.
    /// </summary>
    /// <param name="placementCount">The number of placements.</param>
    /// <param name="charCount">The number of characters of all placements.</param>
    public void ReserveText(int placementCount, int charCount)
    {
        // A placement is a whole number of its widest field, four bytes, so the characters that follow the placements start on an even byte offset, which a
        // char needs.
        this.charOffset = placementCount * Unsafe.SizeOf<BarcodeTextPlacement>();
        this.textBuffer = Rent(this.charOffset + (charCount * sizeof(char)));
    }

    /// <summary>
    /// Adds a centred text placement at the full font size.
    /// </summary>
    /// <param name="text">The characters to render.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="textEdge">The edge of the ink of the line, in modules from the symbol top.</param>
    public void AddText(ReadOnlySpan<char> text, float left, float right, BarcodeTextSide side, float textEdge)
        => this.AddText(text, left, right, side, textEdge, 1F, false, BarcodeTextAlignment.Centered);

    /// <summary>
    /// Adds a centred text placement.
    /// </summary>
    /// <param name="text">The characters to render.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="textEdge">The edge of the ink of the line, in modules from the symbol top.</param>
    /// <param name="fontScale">The factor applied to the caller's font size for this placement.</param>
    public void AddText(ReadOnlySpan<char> text, float left, float right, BarcodeTextSide side, float textEdge, float fontScale)
        => this.AddText(text, left, right, side, textEdge, fontScale, false, BarcodeTextAlignment.Centered);

    /// <summary>
    /// Adds a text placement at the full font size.
    /// </summary>
    /// <param name="text">The characters to render.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="textEdge">The edge of the ink of the line, in modules from the symbol top.</param>
    /// <param name="alignment">Where the line stands within its span.</param>
    public void AddText(ReadOnlySpan<char> text, float left, float right, BarcodeTextSide side, float textEdge, BarcodeTextAlignment alignment)
        => this.AddText(text, left, right, side, textEdge, 1F, false, alignment);

    /// <summary>
    /// Adds a centred text placement.
    /// </summary>
    /// <param name="text">The characters to render.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="textEdge">The edge of the ink of the line, in modules from the symbol top.</param>
    /// <param name="fontScale">The factor applied to the caller's font size for this placement.</param>
    /// <param name="isCaption">Whether this placement is a data layer caption rendered with the caption font.</param>
    public void AddText(ReadOnlySpan<char> text, float left, float right, BarcodeTextSide side, float textEdge, float fontScale, bool isCaption)
        => this.AddText(text, left, right, side, textEdge, fontScale, isCaption, BarcodeTextAlignment.Centered);

    /// <summary>
    /// Adds a text placement. The characters copy into the character storage that <see cref="ReserveText"/> reserved.
    /// </summary>
    /// <param name="text">The characters to render.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="textEdge">The edge of the ink of the line, in modules from the symbol top.</param>
    /// <param name="fontScale">The factor applied to the caller's font size for this placement.</param>
    /// <param name="isCaption">Whether this placement is a data layer caption rendered with the caption font.</param>
    /// <param name="alignment">Where the line stands within its span.</param>
    public void AddText(
        ReadOnlySpan<char> text,
        float left,
        float right,
        BarcodeTextSide side,
        float textEdge,
        float fontScale,
        bool isCaption,
        BarcodeTextAlignment alignment)
        => this.AddText(text, left, right, side, textEdge, fontScale, isCaption, alignment, false);

    /// <summary>
    /// Adds a centred text placement at the full font size that must stay within its span. The line prints in a smaller font when it is wider than its span.
    /// The characters copy into the character storage that <see cref="ReserveText"/> reserved.
    /// </summary>
    /// <param name="text">The characters to render.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="textEdge">The edge of the ink of the line, in modules from the symbol top.</param>
    public void AddTextWithinSpan(ReadOnlySpan<char> text, float left, float right, BarcodeTextSide side, float textEdge)
        => this.AddText(text, left, right, side, textEdge, 1F, false, BarcodeTextAlignment.Centered, true);

    /// <summary>
    /// Adds a text placement at the full font size whose ink must stay within its span. The line prints in a smaller font when it is wider than its span. The
    /// characters copy into the character storage that <see cref="ReserveText"/> reserved.
    /// </summary>
    /// <param name="text">The characters to render.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="textEdge">The edge of the ink of the line, in modules from the symbol top.</param>
    /// <param name="alignment">Where the line stands within its span.</param>
    public void AddTextWithinSpan(ReadOnlySpan<char> text, float left, float right, BarcodeTextSide side, float textEdge, BarcodeTextAlignment alignment)
        => this.AddText(text, left, right, side, textEdge, 1F, false, alignment, true);

    /// <summary>
    /// Adds a centred text placement at the full font size whose characters are three parts in turn, so a caller that frames data with delimiters writes it
    /// with no buffer of its own.
    /// </summary>
    /// <param name="prefix">The characters before the body.</param>
    /// <param name="body">The body characters.</param>
    /// <param name="suffix">The characters after the body.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="textEdge">The edge of the ink of the line, in modules from the symbol top.</param>
    public void AddText(
        ReadOnlySpan<char> prefix,
        ReadOnlySpan<char> body,
        ReadOnlySpan<char> suffix,
        float left,
        float right,
        BarcodeTextSide side,
        float textEdge)
        => this.AddText(prefix, body, suffix, left, right, side, textEdge, 1F, false, BarcodeTextAlignment.Centered);

    /// <summary>
    /// Adds a text placement whose characters are three parts in turn, so a caller that frames data with delimiters writes it with no buffer of its own.
    /// </summary>
    /// <param name="prefix">The characters before the body.</param>
    /// <param name="body">The body characters.</param>
    /// <param name="suffix">The characters after the body.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="textEdge">The edge of the ink of the line, in modules from the symbol top.</param>
    /// <param name="fontScale">The factor applied to the caller's font size for this placement.</param>
    /// <param name="isCaption">Whether this placement is a data layer caption rendered with the caption font.</param>
    /// <param name="alignment">Where the line stands within its span.</param>
    public void AddText(
        ReadOnlySpan<char> prefix,
        ReadOnlySpan<char> body,
        ReadOnlySpan<char> suffix,
        float left,
        float right,
        BarcodeTextSide side,
        float textEdge,
        float fontScale,
        bool isCaption,
        BarcodeTextAlignment alignment)
        => this.AddText(prefix, body, suffix, left, right, side, textEdge, fontScale, isCaption, alignment, false);

    /// <summary>
    /// Adds a centred text placement at the full font size whose characters are three parts in turn and which must stay within its span. The line prints in a
    /// smaller font when it is wider than its span.
    /// </summary>
    /// <param name="prefix">The characters before the body.</param>
    /// <param name="body">The body characters.</param>
    /// <param name="suffix">The characters after the body.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="textEdge">The edge of the ink of the line, in modules from the symbol top.</param>
    public void AddTextWithinSpan(
        ReadOnlySpan<char> prefix,
        ReadOnlySpan<char> body,
        ReadOnlySpan<char> suffix,
        float left,
        float right,
        BarcodeTextSide side,
        float textEdge)
        => this.AddText(prefix, body, suffix, left, right, side, textEdge, 1F, false, BarcodeTextAlignment.Centered, true);

    /// <summary>
    /// Reserves and adds the human readable interpretation of a matrix symbol, one placement below the symbol across its width. The ink starts below the bottom
    /// quiet zone: rule 1a of section 4.14 of the GS1 General Specifications keeps the interpretation clear of the quiet zones, and section 4.1 of ANSI/HIBC
    /// 2.6 keeps all marking out of the symbol region, its quiet zones included. A symbol with a quiet zone narrower than
    /// <see cref="BarcodeTextPlacement.Clearance"/>, such as Aztec Code, which needs none, keeps that clear space instead.
    /// </summary>
    /// <param name="text">The interpretation, its lines separated by <see cref="BarcodeTextPlacement.LineBreak"/>.</param>
    /// <param name="alignment">Where the lines stand on the width of the symbol.</param>
    /// <param name="fontScale">The factor applied to the font size of the caller.</param>
    public void AddMatrixText(ReadOnlySpan<char> text, BarcodeTextAlignment alignment, float fontScale)
    {
        this.ReserveText(1, text.Length);
        this.AddText(
            text,
            0F,
            this.Columns,
            BarcodeTextSide.BelowBars,
            this.HeightInModules + MathF.Max(this.BottomQuietZone, BarcodeTextPlacement.Clearance),
            fontScale,
            false,
            alignment);
    }

    /// <summary>
    /// Reserves and adds the human readable interpretation of a stacked symbol, one placement below the symbol across its width. The ink starts below the
    /// bottom quiet zone, or <see cref="BarcodeTextPlacement.Clearance"/> below the symbol when the quiet zone is narrower.
    /// </summary>
    /// <param name="text">The interpretation, its lines separated by <see cref="BarcodeTextPlacement.LineBreak"/>.</param>
    /// <param name="alignment">Where the lines stand on the width of the symbol.</param>
    /// <param name="fontScale">The factor applied to the font size of the caller.</param>
    public void AddStackedText(ReadOnlySpan<char> text, BarcodeTextAlignment alignment, float fontScale)
    {
        this.ReserveText(1, text.Length);
        this.AddText(
            text,
            0F,
            this.WidthInModules,
            BarcodeTextSide.BelowBars,
            this.HeightInModules + MathF.Max(this.BottomQuietZone, BarcodeTextPlacement.Clearance),
            fontScale,
            false,
            alignment);
    }

    /// <summary>
    /// Adds a square graphic to the centre of a built matrix symbol.
    /// </summary>
    /// <param name="size">The side of the graphic in modules.</param>
    /// <param name="unit">The side of the graphic in the units of <paramref name="darkEdges"/>.</param>
    /// <param name="darkEdges">The dark rectangles of the graphic, as <see cref="OverlayEdges"/> holds them.</param>
    public void AddOverlay(float size, float unit, ReadOnlySpan<float> darkEdges)
    {
        this.OverlaySize = size;
        this.OverlayUnit = unit;
        this.overlayBuffer = Rent(darkEdges.Length * sizeof(float));
        this.overlayEdgeCount = darkEdges.Length;
        darkEdges.CopyTo(Slice<float>(this.overlayBuffer, 0, darkEdges.Length));
    }

    /// <summary>
    /// Adds identification bars to a built linear symbol that carries a text line above and below its bars.
    /// </summary>
    /// <param name="thickness">The thickness of each bar in modules.</param>
    /// <param name="clearance">The clear space between each bar and the ink of its text line in modules.</param>
    public void AddIdentificationBars(float thickness, float clearance)
    {
        this.IdentificationBarThickness = thickness;
        this.IdentificationBarClearance = clearance;
    }

    /// <summary>
    /// Sets the range of the printed height of the characters of the text lines, which the emitter measures as the height of the ink of each line.
    /// </summary>
    /// <param name="minimum">The smallest height in modules.</param>
    /// <param name="maximum">The largest height in modules.</param>
    public void SetTextHeight(float minimum, float maximum)
    {
        this.TextHeightMinimum = minimum;
        this.TextHeightMaximum = maximum;
    }

    /// <summary>
    /// Sets the smallest size of the caption in points.
    /// </summary>
    /// <param name="points">The smallest size in points.</param>
    public void SetCaptionMinimumSize(float points) => this.CaptionMinimumSize = points;

    /// <summary>
    /// Sets <see cref="TextWeight"/>, the font weight the standard of the symbology requires for its text.
    /// </summary>
    /// <param name="weight">The font weight.</param>
    public void SetTextWeight(FontWeight weight) => this.TextWeight = weight;

    /// <summary>
    /// Sets <see cref="CorrectedModuleUnits"/>, the number of run units in one module of runs that are whole modules with corrections.
    /// </summary>
    /// <param name="units">The number of run units in one module.</param>
    public void SetCorrectedModuleUnits(int units) => this.CorrectedModuleUnits = units;

    /// <summary>
    /// Sets the range of the size of the text lines in points.
    /// </summary>
    /// <param name="minimum">The smallest size in points, or 0 for none.</param>
    /// <param name="maximum">The largest size in points, or 0 for none.</param>
    public void SetTextSize(float minimum, float maximum)
    {
        this.TextMinimumSize = minimum;
        this.TextMaximumSize = maximum;
    }

    /// <summary>
    /// Returns the rented buffers to the shared pool.
    /// </summary>
    public void Dispose()
    {
        this.layoutBuffer.Dispose();
        this.textBuffer.Dispose();
        this.overlayBuffer.Dispose();
    }

    /// <summary>
    /// Returns a typed view of a range of a buffer. A buffer that was never rented gives an empty view when the count is zero, so a symbol with no text reads
    /// no text.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="buffer">The buffer.</param>
    /// <param name="byteOffset">The byte offset of the first element.</param>
    /// <param name="count">The number of elements.</param>
    /// <returns>The elements.</returns>
    private static Span<T> Slice<T>(PooledBuffer<byte> buffer, int byteOffset, int count)
        where T : unmanaged
        => MemoryMarshal.Cast<byte, T>(buffer.Span.Slice(byteOffset, count * Unsafe.SizeOf<T>()));

    /// <summary>
    /// Rents a buffer from the shared <see cref="byte"/> pool and clears it. The pool hands out used arrays and allocates new ones without clearing, so the
    /// symbol clears them itself.
    /// </summary>
    /// <param name="byteCount">The number of bytes the symbol uses.</param>
    /// <returns>The rented buffer.</returns>
    private static PooledBuffer<byte> Rent(int byteCount)
    {
        PooledBuffer<byte> rented = new(byteCount);
        rented.Span.Clear();
        return rented;
    }

    /// <summary>
    /// Adds a text placement. The characters copy into the character storage that <see cref="ReserveText"/> reserved.
    /// </summary>
    /// <param name="text">The characters to render.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="textEdge">The edge of the ink of the line, in modules from the symbol top.</param>
    /// <param name="fontScale">The factor applied to the caller's font size for this placement.</param>
    /// <param name="isCaption">Whether this placement is a data layer caption rendered with the caption font.</param>
    /// <param name="alignment">Where the line stands within its span.</param>
    /// <param name="withinSpan">Whether the ink of the line must stay within its span.</param>
    private void AddText(
        ReadOnlySpan<char> text,
        float left,
        float right,
        BarcodeTextSide side,
        float textEdge,
        float fontScale,
        bool isCaption,
        BarcodeTextAlignment alignment,
        bool withinSpan)
    {
        text.CopyTo(Slice<char>(this.textBuffer, this.charOffset + (this.charCount * sizeof(char)), text.Length));
        Slice<BarcodeTextPlacement>(this.textBuffer, this.textCount++ * Unsafe.SizeOf<BarcodeTextPlacement>(), 1)[0] =
            new BarcodeTextPlacement(this.charCount, text.Length, left, right, side, textEdge, fontScale, isCaption, alignment, withinSpan);

        this.charCount += text.Length;
    }

    /// <summary>
    /// Adds a text placement whose characters are three parts in turn. The characters copy into the character storage that <see cref="ReserveText"/> reserved.
    /// </summary>
    /// <param name="prefix">The characters before the body.</param>
    /// <param name="body">The body characters.</param>
    /// <param name="suffix">The characters after the body.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="textEdge">The edge of the ink of the line, in modules from the symbol top.</param>
    /// <param name="fontScale">The factor applied to the caller's font size for this placement.</param>
    /// <param name="isCaption">Whether this placement is a data layer caption rendered with the caption font.</param>
    /// <param name="alignment">Where the line stands within its span.</param>
    /// <param name="withinSpan">Whether the ink of the line must stay within its span.</param>
    private void AddText(
        ReadOnlySpan<char> prefix,
        ReadOnlySpan<char> body,
        ReadOnlySpan<char> suffix,
        float left,
        float right,
        BarcodeTextSide side,
        float textEdge,
        float fontScale,
        bool isCaption,
        BarcodeTextAlignment alignment,
        bool withinSpan)
    {
        int start = this.charCount;
        int length = prefix.Length + body.Length + suffix.Length;
        Span<char> destination = Slice<char>(this.textBuffer, this.charOffset + (start * sizeof(char)), length);
        prefix.CopyTo(destination);
        body.CopyTo(destination[prefix.Length..]);
        suffix.CopyTo(destination[(prefix.Length + body.Length)..]);
        Slice<BarcodeTextPlacement>(this.textBuffer, this.textCount++ * Unsafe.SizeOf<BarcodeTextPlacement>(), 1)[0] =
            new BarcodeTextPlacement(start, length, left, right, side, textEdge, fontScale, isCaption, alignment, withinSpan);

        this.charCount = start + length;
    }

    /// <summary>
    /// Reserves the rows, the runs and the bars of a linear or stacked symbol.
    /// </summary>
    /// <param name="rowCount">The number of rows.</param>
    /// <param name="runCount">The number of runs of all rows.</param>
    /// <param name="barCount">The number of bars of all rows.</param>
    private void Reserve(int rowCount, int runCount, int barCount)
    {
        // The rows, the runs, the bar heights and the bar tops follow each other in one buffer. Each holds four byte fields, so every part starts on a four
        // byte offset.
        this.runOffset = rowCount * Unsafe.SizeOf<BarcodeRow>();
        this.barOffset = this.runOffset + (runCount * sizeof(int));
        this.barCapacity = barCount;
        this.layoutBuffer = Rent(this.barOffset + (2 * barCount * sizeof(float)));
    }

    /// <summary>
    /// Places the rows from the top down and sets the width of the symbol from the first row.
    /// </summary>
    /// <param name="firstTop">The top of the first row in modules.</param>
    /// <returns>The bottom of the last row in modules.</returns>
    private float EndRows(float firstTop)
    {
        ReadOnlySpan<int> runs = this.Runs;
        ReadOnlySpan<float> heights = this.BarHeights;
        ReadOnlySpan<float> tops = this.BarTops;
        Span<BarcodeRow> rows = Slice<BarcodeRow>(this.layoutBuffer, 0, this.rowCount);

        // The product of a whole number of run units and the run unit is exact in a double, so the width carries no rounding error.
        BarcodeRow first = rows[0];
        this.WidthInModules = (float)(RunWidth(runs.Slice(first.RunStart, first.RunCount)) * (double)this.RunUnit);

        // Each row is as tall as its lowest bar bottom, and its ink starts at its highest bar top. The encoders give an empty bar the height of its row, so
        // the bars alone give both. Each row top adds every row above it, so the sum is in double and each top converts to float once.
        double top = firstTop;
        for (int r = 0; r < rows.Length; r++)
        {
            BarcodeRow row = rows[r];
            ReadOnlySpan<float> rowTops = tops.Slice(row.BarStart, row.BarCount);
            ReadOnlySpan<float> rowHeights = heights.Slice(row.BarStart, row.BarCount);
            float height = 0;
            float inkTop = float.MaxValue;
            for (int i = 0; i < rowTops.Length; i++)
            {
                height = MathF.Max(height, rowTops[i] + rowHeights[i]);
                inkTop = MathF.Min(inkTop, rowTops[i]);
            }

            // The ink edges are the left edge of the first bar that is not empty and the right edge of the last. A row can start with an empty bar, so
            // that it starts with a space, and it can end with a space. A short walk from each end of the row passes those runs, so no run between is read.
            // A row of spaces alone has no ink, and its ink start is -1.
            ReadOnlySpan<int> rowRuns = runs.Slice(row.RunStart, row.RunCount);
            int start = 0;
            int inkStart = 0;
            while (start < rowRuns.Length && rowRuns[start] == 0)
            {
                inkStart += start + 1 < rowRuns.Length ? rowRuns[start + 1] : 0;
                start += 2;
            }

            int inkEnd = 0;
            if (start >= rowRuns.Length)
            {
                inkStart = -1;
            }
            else
            {
                // Runs alternate bar and space from a bar, so the last bar is the last even index. The space after it, if the row ends with one, and each
                // empty bar before it with the space before that bar, are the trailing runs.
                int end = (rowRuns.Length - 1) & ~1;
                int trailing = end + 1 < rowRuns.Length ? rowRuns[end + 1] : 0;
                while (rowRuns[end] == 0)
                {
                    trailing += rowRuns[end - 1];
                    end -= 2;
                }

                inkEnd = RunWidth(rowRuns) - trailing;
            }

            rows[r] = new BarcodeRow(row, (float)top, height, inkTop, inkStart, inkEnd);
            top += height;
        }

        return (float)top;
    }
}
