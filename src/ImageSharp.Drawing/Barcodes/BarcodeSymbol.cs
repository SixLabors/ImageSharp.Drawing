// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The device independent description of an encoded barcode, measured in modules. A module is the
/// narrowest nominal element of a symbology, which ISO/IEC 15420 and related specifications call the
/// X-dimension. A symbol carries no pixel sizes, brushes or fonts. The emitter adds those when it turns
/// the symbol into canvas commands.
/// </summary>
/// <remarks>
/// An encoder writes the symbol once: it begins a linear, stacked or matrix symbol with the sizes that its
/// validated input gives, fills the spans that the begin methods return, adds the text and ends the symbol.
/// The symbol keeps its values in two buffers rented from the shared <see cref="byte"/> pool: the layout
/// buffer, which <see cref="BeginLinear"/>, <see cref="BeginStacked"/> or <see cref="BeginMatrix"/> rents
/// for the rows, runs and bars or for the modules, and the text buffer, which <see cref="ReserveText"/>
/// rents for the text placements and their characters. Each rent happens once, before the encoder's loops,
/// with the exact size its validated input gives, and <see cref="Dispose"/> returns both. A rented range is
/// cleared, so every value the encoder does not write is zero. The caller declares the symbol as a local
/// and passes it by reference.
/// </remarks>
internal struct BarcodeSymbol : IDisposable
{
    /// <summary>
    /// The layout buffer: the rows, then the runs, then the bar heights and the bar tops of a linear or
    /// stacked symbol, or the modules of a matrix symbol.
    /// </summary>
    private PooledBuffer<byte> layoutBuffer;

    /// <summary>
    /// The text buffer: the text placements, then their characters.
    /// </summary>
    private PooledBuffer<byte> textBuffer;

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
    /// Gets the width in modules of the quiet zone that comes before the symbol. Each symbology sets its
    /// own quiet zone width. For example, ISO/IEC 15420 requires 11 modules before an EAN-13 symbol, and
    /// a postal symbology states its clear zone in inches, which is not a whole number of modules.
    /// </summary>
    public float LeadingQuietZone { get; private set; }

    /// <summary>
    /// Gets the width of the quiet zone that must follow the symbol, in modules.
    /// </summary>
    public float TrailingQuietZone { get; private set; }

    /// <summary>
    /// Gets the height of the quiet zone above the symbol, in modules. A symbology whose specification
    /// gives no vertical quiet zone has none.
    /// </summary>
    public float TopQuietZone { get; private set; }

    /// <summary>
    /// Gets the height of the quiet zone below the symbol, in modules. A symbology whose specification
    /// gives no vertical quiet zone has none.
    /// </summary>
    public float BottomQuietZone { get; private set; }

    /// <summary>
    /// Gets the thickness in modules of the bars that close the top and the bottom of a linear or stacked
    /// symbol, or zero when there are none: the bearer bar of a linear symbol, which also closes the sides
    /// when the quiet zones are drawn, or the edge bars of a stacked symbol.
    /// </summary>
    public float FrameThickness { get; private set; }

    /// <summary>
    /// Gets the width of one run unit in modules. A run width counts run units.
    /// </summary>
    public float RunUnit { get; private set; }

    /// <summary>
    /// Gets a value indicating whether every bar of the symbol is one width at one pitch, so each run draws
    /// at a whole number of pixels of its own.
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
    /// Gets the X dimension in millimetres, the width of one module: the X dimension of the options, else
    /// the nominal X dimension the specification of the symbology gives for this symbol. The encoder
    /// resolves it once, measures every height it states in millimetres with it, and the emitter draws the
    /// modules at it.
    /// </summary>
    public double XDimension { get; private set; }

    /// <summary>
    /// Gets the side in modules of the Swiss cross centred on a matrix symbol, or 0 when the symbol has
    /// none. The cross covers its square completely, so the modules under the square do not draw.
    /// </summary>
    public float SwissCrossSize { get; private set; }

    /// <summary>
    /// Gets the thickness in modules of the identification bars of a linear symbol, or 0 when the symbol
    /// has none. One bar stands above the topmost text line and one below the lowest, each spanning the
    /// symbol and its quiet zones.
    /// </summary>
    public float IdentificationBarThickness { get; private set; }

    /// <summary>
    /// Gets the clear space in modules between an identification bar and the ink of the text line next to it.
    /// </summary>
    public float IdentificationBarClearance { get; private set; }

    /// <summary>
    /// Gets the smallest printed height of the characters of the text lines in modules, or 0 when the
    /// symbology states none.
    /// </summary>
    public float TextHeightMinimum { get; private set; }

    /// <summary>
    /// Gets the largest printed height of the characters of the text lines in modules, or 0 when the
    /// symbology states none.
    /// </summary>
    public float TextHeightMaximum { get; private set; }

    /// <summary>
    /// Gets the rows of a linear or stacked symbol from the top down. A linear symbol has one row.
    /// </summary>
    public readonly ReadOnlySpan<BarcodeRow> Rows => Slice<BarcodeRow>(this.layoutBuffer, 0, this.rowCount);

    /// <summary>
    /// Gets the run widths of all rows in run units. The runs of a row alternate bar and space, starting
    /// with a bar.
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
    /// Shortens the last row added to the given number of runs, for an encoder that reserves the most
    /// runs its data can take and learns the exact count as it writes. The storage stays reserved.
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
    {
        this.LeadingQuietZone = leadingQuietZone;
        this.TrailingQuietZone = trailingQuietZone;
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
    {
        this.Kind = BarcodeSymbolKind.Matrix;
        this.XDimension = xDimension;
        this.Columns = columns;
        this.MatrixRows = rows;
        this.WidthInModules = columns;
        this.HeightInModules = rows;
        this.LeadingQuietZone = quietZone;
        this.TrailingQuietZone = quietZone;
        this.TopQuietZone = quietZone;
        this.BottomQuietZone = quietZone;
        this.RunUnit = 1F;

        this.moduleCount = columns * rows;
        this.layoutBuffer = Rent(this.moduleCount);
        return Slice<bool>(this.layoutBuffer, 0, this.moduleCount);
    }

    /// <summary>
    /// Reserves the text placements and their characters. The encoder calls it once, before it adds any
    /// text.
    /// </summary>
    /// <param name="placementCount">The number of placements.</param>
    /// <param name="charCount">The number of characters of all placements.</param>
    public void ReserveText(int placementCount, int charCount)
    {
        // A placement is a whole number of its widest field, four bytes, so the characters that follow the
        // placements start on an even byte offset, which a char needs.
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
    /// Adds a text placement. The characters copy into the character storage that
    /// <see cref="ReserveText"/> reserved.
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
    {
        text.CopyTo(Slice<char>(this.textBuffer, this.charOffset + (this.charCount * sizeof(char)), text.Length));
        Slice<BarcodeTextPlacement>(this.textBuffer, this.textCount++ * Unsafe.SizeOf<BarcodeTextPlacement>(), 1)[0] =
            new BarcodeTextPlacement(this.charCount, text.Length, left, right, side, textEdge, fontScale, isCaption, alignment);

        this.charCount += text.Length;
    }

    /// <summary>
    /// Adds a centred text placement at the full font size whose characters are three parts in turn, so a
    /// caller that frames data with delimiters writes it with no buffer of its own.
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
    /// Adds a text placement whose characters are three parts in turn, so a caller that frames data with
    /// delimiters writes it with no buffer of its own.
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
    {
        int start = this.charCount;
        int length = prefix.Length + body.Length + suffix.Length;
        Span<char> destination = Slice<char>(this.textBuffer, this.charOffset + (start * sizeof(char)), length);
        prefix.CopyTo(destination);
        body.CopyTo(destination[prefix.Length..]);
        suffix.CopyTo(destination[(prefix.Length + body.Length)..]);
        Slice<BarcodeTextPlacement>(this.textBuffer, this.textCount++ * Unsafe.SizeOf<BarcodeTextPlacement>(), 1)[0] =
            new BarcodeTextPlacement(start, length, left, right, side, textEdge, fontScale, isCaption, alignment);

        this.charCount = start + length;
    }

    /// <summary>
    /// Reserves and adds the human readable interpretation of a matrix symbol, one placement below the
    /// symbol across its width. The ink starts below the bottom quiet zone: rule 1a of section 4.14 of the
    /// GS1 General Specifications keeps the interpretation clear of the quiet zones, and section 4.1 of
    /// ANSI/HIBC 2.6 keeps all marking out of the symbol region, its quiet zones included. A symbol with a
    /// quiet zone narrower than <see cref="BarcodeTextPlacement.Clearance"/>, such as Aztec Code, which needs
    /// none, keeps that clear space instead.
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
    /// Adds a Swiss cross to the centre of a built matrix symbol.
    /// </summary>
    /// <param name="size">The side of the cross in modules.</param>
    public void AddSwissCross(float size) => this.SwissCrossSize = size;

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
    /// Sets the range of the printed height of the characters of the text lines, which the emitter measures
    /// as the height of the ink of each line.
    /// </summary>
    /// <param name="minimum">The smallest height in modules.</param>
    /// <param name="maximum">The largest height in modules.</param>
    public void SetTextHeight(float minimum, float maximum)
    {
        this.TextHeightMinimum = minimum;
        this.TextHeightMaximum = maximum;
    }

    /// <summary>
    /// Returns the rented buffers to the shared pool.
    /// </summary>
    public void Dispose()
    {
        this.layoutBuffer.Dispose();
        this.textBuffer.Dispose();
    }

    /// <summary>
    /// Returns a typed view of a range of a buffer. A buffer that was never rented gives an empty view when
    /// the count is zero, so a symbol with no text reads no text.
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
    /// Rents a buffer from the shared <see cref="byte"/> pool and clears it. The pool hands out used arrays
    /// and allocates new ones without clearing, so the symbol clears them itself.
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
    /// Reserves the rows, the runs and the bars of a linear or stacked symbol.
    /// </summary>
    /// <param name="rowCount">The number of rows.</param>
    /// <param name="runCount">The number of runs of all rows.</param>
    /// <param name="barCount">The number of bars of all rows.</param>
    private void Reserve(int rowCount, int runCount, int barCount)
    {
        // The rows, the runs, the bar heights and the bar tops follow each other in one buffer. Each holds
        // four byte fields, so every part starts on a four byte offset.
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

        // A double holds each product of a whole run and the run unit exactly, so the width carries no
        // rounding error from the runs it adds.
        BarcodeRow first = rows[0];
        double width = 0;
        for (int i = first.RunStart; i < first.RunStart + first.RunCount; i++)
        {
            width += runs[i] * (double)this.RunUnit;
        }

        this.WidthInModules = (float)width;

        // Each row is as tall as its lowest bar bottom. Each row top adds every row above it, so the sum is
        // in double and each top converts to float once.
        double top = firstTop;
        for (int r = 0; r < rows.Length; r++)
        {
            BarcodeRow row = rows[r];
            float height = 0;
            for (int i = row.BarStart; i < row.BarStart + row.BarCount; i++)
            {
                height = MathF.Max(height, tops[i] + heights[i]);
            }

            rows[r] = new BarcodeRow(row, (float)top, height);
            top += height;
        }

        return (float)top;
    }
}
