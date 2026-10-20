// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Builds MicroPDF417 symbols: 1 to 4 data columns between row address patterns, in one of 34 fixed sizes. The symbol characters and the error correction are
/// those of PDF417, and the rows carry no start or stop character and no row indicators.
/// </summary>
internal static class MicroPdf417Encoder
{
    /// <summary>
    /// The fewest data columns of a symbol.
    /// </summary>
    public const int MinimumColumns = 1;

    /// <summary>
    /// The most data columns of a symbol.
    /// </summary>
    public const int MaximumColumns = 4;

    /// <summary>
    /// The nominal row height in modules.
    /// </summary>
    private const float NominalRowHeight = 2F;

    /// <summary>
    /// The quiet zone on each of the four sides in modules.
    /// </summary>
    private const int QuietZone = 1;

    /// <summary>
    /// The most codewords a symbol holds, four columns of 44 rows.
    /// </summary>
    private const int MaximumCodewords = 176;

    /// <summary>
    /// The number of entries in each of the two row address pattern tables.
    /// </summary>
    private const int AddressPatternCount = 52;

    /// <summary>
    /// The number of modules in a row address pattern.
    /// </summary>
    private const int AddressPatternModules = 10;

    /// <summary>
    /// The number of elements in a row address pattern, three bars and three spaces.
    /// </summary>
    private const int AddressPatternElements = 6;

    /// <summary>
    /// Gets the 34 symbol sizes in the order the encoder tries them, one <see cref="SymbolSize"/> of six values each: the data columns, the rows, the error
    /// correction codewords, and the first left, centre and right row address patterns. Only symbols of three or four columns carry a centre pattern.
    /// </summary>
    private static ReadOnlySpan<byte> Sizes =>
    [
        1, 11, 7, 1, 0, 9,
        1, 14, 7, 8, 0, 8,
        1, 17, 7, 36, 0, 36,
        1, 20, 8, 19, 0, 19,
        1, 24, 8, 9, 0, 17,
        1, 28, 8, 25, 0, 33,
        2, 8, 8, 1, 0, 1,
        2, 11, 9, 1, 0, 9,
        2, 14, 9, 8, 0, 8,
        2, 17, 10, 36, 0, 36,
        2, 20, 11, 19, 0, 19,
        2, 23, 13, 9, 0, 17,
        2, 26, 15, 27, 0, 35,
        3, 6, 12, 1, 1, 1,
        3, 8, 14, 7, 7, 7,
        3, 10, 16, 15, 15, 15,
        3, 12, 18, 25, 25, 25,
        3, 15, 21, 37, 37, 37,
        3, 20, 26, 1, 17, 33,
        3, 26, 32, 1, 9, 17,
        3, 32, 38, 21, 29, 37,
        3, 38, 44, 15, 31, 47,
        3, 44, 50, 1, 25, 49,
        4, 4, 8, 47, 19, 43,
        4, 6, 12, 1, 1, 1,
        4, 8, 14, 7, 7, 7,
        4, 10, 16, 15, 15, 15,
        4, 12, 18, 25, 25, 25,
        4, 15, 21, 37, 37, 37,
        4, 20, 26, 1, 17, 33,
        4, 26, 32, 1, 9, 17,
        4, 32, 38, 21, 29, 37,
        4, 38, 44, 15, 31, 47,
        4, 44, 50, 1, 25, 49,
    ];

    /// <summary>
    /// Gets the 52 left and right row address patterns, each the 10 modules of three bars and three spaces with the first module in bit 9.
    /// </summary>
    private static ReadOnlySpan<short> SideAddressPatterns =>
    [
        802, 930, 946, 818, 882, 890, 826, 954, 922, 986, 970, 906, 778,
        794, 786, 914, 978, 982, 980, 916, 948, 932, 934, 942, 940, 936,
        808, 812, 814, 806, 822, 950, 918, 790, 788, 820, 884, 868, 870,
        878, 876, 872, 840, 856, 860, 862, 846, 844, 836, 838, 834, 866,
    ];

    /// <summary>
    /// Gets the 52 centre row address patterns, each the 10 modules of three bars and three spaces with the first module in bit 9.
    /// </summary>
    private static ReadOnlySpan<short> CentreAddressPatterns =>
    [
        718, 590, 622, 558, 550, 566, 534, 530, 538, 570, 562, 546, 610,
        626, 634, 762, 754, 758, 630, 628, 612, 614, 582, 578, 706, 738,
        742, 740, 748, 620, 556, 552, 616, 744, 712, 716, 708, 710, 646,
        654, 652, 668, 664, 696, 688, 656, 720, 592, 600, 604, 732, 734,
    ];

    /// <summary>
    /// Encodes the text into a symbol.
    /// </summary>
    /// <param name="text">The data, the characters U+0000 to U+00FF.</param>
    /// <param name="columns">The number of data columns, 1 to 4, or <see langword="null"/> to choose them from the data.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The data is not valid or does not fit the symbol.</exception>
    public static void BuildSymbol(ReadOnlySpan<char> text, int? columns, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        // A character takes at most two data codewords, as in PDF417, and a message format takes one macro codeword more. The text is bounded, so the builder
        // never grows: short data works inline and longer data rents once.
        Guard.MustBeLessThanOrEqualTo(text.Length, Pdf417Codewords.MicroMaximumLength, nameof(text));
        InlineArray64<int> dataStorage = default;
        int capacity = (2 * text.Length) + 2;
        ValueListBuilder<int> data = capacity <= Pdf417Codewords.InlineBufferLength ? new(dataStorage) : new(capacity);
        try
        {
            Pdf417Codewords.EncodeMicro(text, ref data);
            BuildSymbol(data.AsSpan(), columns, options, ref symbol);
        }
        finally
        {
            data.Dispose();
        }
    }

    /// <summary>
    /// Returns the number of data codewords of a symbol size: its matrix less its error correction codewords.
    /// </summary>
    /// <param name="columns">The number of data columns of the size.</param>
    /// <param name="rows">The number of rows of the size.</param>
    /// <returns>The number of data codewords.</returns>
    public static int DataCodewords(int columns, int rows)
    {
        SymbolSize size = FindSize(columns, rows);
        return (size.Columns * size.Rows) - size.ErrorCorrection;
    }

    /// <summary>
    /// Builds a symbol of one size from its data codewords. Section 9 of ISO/IEC 24723 builds the CC-B 2D component of a GS1 Composite symbol this way, at the
    /// size its bit stream fills.
    /// </summary>
    /// <param name="data">The data codewords, at most <see cref="DataCodewords"/> of the size.</param>
    /// <param name="columns">The number of data columns of the size.</param>
    /// <param name="rows">The number of rows of the size.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="rowHeight">The height of a row in modules.</param>
    /// <param name="verticalQuietZone">The quiet zone above and below the symbol in modules.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(
        ReadOnlySpan<int> data,
        int columns,
        int rows,
        double xDimension,
        float rowHeight,
        float verticalQuietZone,
        ref BarcodeSymbol symbol)
        => BuildMatrix(data, FindSize(columns, rows), xDimension, rowHeight, verticalQuietZone, ref symbol);

    /// <summary>
    /// Returns the symbol size of a number of columns and rows. The caller gives a size of <see cref="Sizes"/>.
    /// </summary>
    /// <param name="columns">The number of data columns.</param>
    /// <param name="rows">The number of rows.</param>
    /// <returns>The size.</returns>
    private static SymbolSize FindSize(int columns, int rows)
    {
        ReadOnlySpan<byte> sizes = Sizes;
        int offset = 0;
        while (sizes[offset] != columns || sizes[offset + 1] != rows)
        {
            offset += SymbolSize.Length;
        }

        return new SymbolSize(sizes.Slice(offset, SymbolSize.Length));
    }

    /// <summary>
    /// Builds the symbol from its data codewords in the first size that holds them.
    /// </summary>
    /// <param name="data">The data codewords.</param>
    /// <param name="columns">The number of data columns, 1 to 4, or <see langword="null"/> to choose them from the data.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The data does not fit the symbol.</exception>
    private static void BuildSymbol(ReadOnlySpan<int> data, int? columns, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        ReadOnlySpan<byte> sizes = Sizes;
        int offset = 0;
        while (offset < sizes.Length && !new SymbolSize(sizes.Slice(offset, SymbolSize.Length)).Holds(data.Length, columns))
        {
            offset += SymbolSize.Length;
        }

        if (offset == sizes.Length)
        {
            throw new ArgumentException(columns.HasValue
                ? $"The data takes {data.Length} codewords and does not fit in a MicroPDF417 symbol of {columns.Value} columns."
                : $"The data takes {data.Length} codewords and does not fit in a MicroPDF417 symbol.");
        }

        double xDimension = options.ResolveXDimension(0, BarcodeSymbology.DefaultXDimension, double.PositiveInfinity);
        float rowHeight = options.ResolveBarHeight(xDimension, 0, NominalRowHeight, float.PositiveInfinity);
        BuildMatrix(data, new SymbolSize(sizes.Slice(offset, SymbolSize.Length)), xDimension, rowHeight, QuietZone, ref symbol);
    }

    /// <summary>
    /// Builds a symbol of one size from its data codewords: the data, the pad codewords, the error correction codewords and then the rows.
    /// </summary>
    /// <param name="data">The data codewords, at most the data codewords of the size.</param>
    /// <param name="size">The symbol size.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="rowHeight">The height of a row in modules.</param>
    /// <param name="verticalQuietZone">The quiet zone above and below the symbol in modules.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void BuildMatrix(
        ReadOnlySpan<int> data,
        SymbolSize size,
        double xDimension,
        float rowHeight,
        float verticalQuietZone,
        ref BarcodeSymbol symbol)
    {
        // The data codewords, the pad codewords that fill the data region with 900, and the error correction codewords. A MicroPDF417 symbol has no Symbol
        // Length Descriptor. A symbol holds at most MaximumCodewords codewords, so they fit inline.
        int k = size.ErrorCorrection;
        InlineArray256<int> codewordStorage = default;
        Span<int> codewords = ((Span<int>)codewordStorage)[..(size.Columns * size.Rows)];
        data.CopyTo(codewords);
        codewords[data.Length..^k].Fill(Pdf417Codewords.Pad);

        // The generator coefficients take one more than the error correction codewords. A size of Sizes has at most 50 error correction codewords, so the
        // coefficients fit inline.
        InlineArray64<int> alphaStorage = default;
        Pdf417Codewords.AppendErrorCorrection(codewords, k, alphaStorage);

        AddressPatterns patterns = new(size.LeftPattern, size.CentrePattern, size.RightPattern, true);
        BuildRows(codewords, size.Columns, size.Rows, patterns, xDimension, rowHeight, verticalQuietZone, ref symbol);
    }

    /// <summary>
    /// Builds the rows of a MicroPDF417 symbol or of the 2D component of a GS1 Composite symbol, whose rows section 8.2 of ISO/IEC 24723 takes from
    /// MicroPDF417: the left row address pattern, if the symbol has one, the codewords of the row with the centre row address pattern after the first column of
    /// three or the second of four, the right row address pattern and a single module bar.
    /// </summary>
    /// <param name="codewords">The codewords in row order, error correction codewords included.</param>
    /// <param name="columns">The number of data columns, 1 to 4.</param>
    /// <param name="rows">The number of rows.</param>
    /// <param name="patterns">The row address patterns of the first row.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="rowHeight">The height of a row in modules.</param>
    /// <param name="verticalQuietZone">The quiet zone above and below the symbol in modules.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildRows(
        ReadOnlySpan<int> codewords,
        int columns,
        int rows,
        AddressPatterns patterns,
        double xDimension,
        float rowHeight,
        float verticalQuietZone,
        ref BarcodeSymbol symbol)
    {
        int c = columns;
        bool hasCentre = c >= 3;
        int centreAfter = c - 2;
        int runCount = ((patterns.HasLeft ? 2 : 1) * AddressPatternElements) + (c * Pdf417Patterns.ElementCount) + 1 + (hasCentre ? AddressPatternElements : 0);

        ReadOnlySpan<int> characters = Pdf417Patterns.Modules;
        symbol.BeginStacked(rows, rows * runCount, rows * ((runCount + 1) >> 1), xDimension);
        for (int row = 0; row < rows; row++)
        {
            // Each row address pattern steps through its table from the first one of the size, and the cluster of the row follows the left pattern: every third
            // pattern shares one of the three clusters. A symbol without a left pattern takes the cluster of the left pattern its row would have.
            int leftIndex = (row + patterns.Left - 1) % AddressPatternCount;
            int cluster = (row + patterns.Left - 1) % 3;
            ReadOnlySpan<int> clusterPatterns = characters.Slice(cluster * Pdf417Patterns.CodewordCount, Pdf417Patterns.CodewordCount);

            Span<int> runs = symbol.AddRow(runCount, out Span<float> heights, out _);
            heights.Fill(rowHeight);
            int index = 0;
            if (patterns.HasLeft)
            {
                Pdf417Patterns.AppendElements(runs, ref index, SideAddressPatterns[leftIndex], AddressPatternModules, AddressPatternElements);
            }

            for (int column = 0; column < c; column++)
            {
                if (hasCentre && column == centreAfter)
                {
                    int centreModules = CentreAddressPatterns[(row + patterns.Centre - 1) % AddressPatternCount];
                    Pdf417Patterns.AppendElements(runs, ref index, centreModules, AddressPatternModules, AddressPatternElements);
                }

                Pdf417Patterns.AppendCharacter(runs, ref index, clusterPatterns[codewords[(row * c) + column]]);
            }

            int rightModules = SideAddressPatterns[(row + patterns.Right - 1) % AddressPatternCount];
            Pdf417Patterns.AppendElements(runs, ref index, rightModules, AddressPatternModules, AddressPatternElements);
            runs[index] = 1;
        }

        symbol.EndStacked(0F, QuietZone, QuietZone, verticalQuietZone, verticalQuietZone);
    }

    /// <summary>
    /// The row address patterns of the first row of a symbol, each counted from 1. Every row after it takes the next pattern of each table.
    /// </summary>
    internal readonly struct AddressPatterns
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AddressPatterns"/> struct.
        /// </summary>
        /// <param name="left">The left row address pattern of the first row. A symbol without a left pattern gives the one its first row would have, which
        /// sets the cluster of the row.</param>
        /// <param name="centre">The centre row address pattern of the first row, or 0 for a symbol of one or two columns.</param>
        /// <param name="right">The right row address pattern of the first row.</param>
        /// <param name="hasLeft">Whether the rows draw the left row address pattern.</param>
        public AddressPatterns(int left, int centre, int right, bool hasLeft)
        {
            this.Left = left;
            this.Centre = centre;
            this.Right = right;
            this.HasLeft = hasLeft;
        }

        /// <summary>
        /// Gets the left row address pattern of the first row.
        /// </summary>
        public int Left { get; }

        /// <summary>
        /// Gets the centre row address pattern of the first row, or 0 for a symbol of one or two columns.
        /// </summary>
        public int Centre { get; }

        /// <summary>
        /// Gets the right row address pattern of the first row.
        /// </summary>
        public int Right { get; }

        /// <summary>
        /// Gets a value indicating whether the rows draw the left row address pattern. The 3-column 2D component of a GS1 Composite symbol omits it.
        /// </summary>
        public bool HasLeft { get; }
    }

    /// <summary>
    /// One MicroPDF417 symbol size: a view of the six bytes of an entry of <see cref="Sizes"/>.
    /// </summary>
    private readonly ref struct SymbolSize
    {
        /// <summary>
        /// The number of bytes of an entry of <see cref="Sizes"/>.
        /// </summary>
        public const int Length = 6;

        /// <summary>
        /// The six bytes of the entry.
        /// </summary>
        private readonly ReadOnlySpan<byte> entry;

        /// <summary>
        /// Initializes a new instance of the <see cref="SymbolSize"/> struct.
        /// </summary>
        /// <param name="entry">The six bytes of the entry.</param>
        public SymbolSize(ReadOnlySpan<byte> entry) => this.entry = entry;

        /// <summary>
        /// Gets the number of data columns.
        /// </summary>
        public int Columns => this.entry[0];

        /// <summary>
        /// Gets the number of rows.
        /// </summary>
        public int Rows => this.entry[1];

        /// <summary>
        /// Gets the number of error correction codewords.
        /// </summary>
        public int ErrorCorrection => this.entry[2];

        /// <summary>
        /// Gets the left row address pattern of the first row, counted from 1.
        /// </summary>
        public int LeftPattern => this.entry[3];

        /// <summary>
        /// Gets the centre row address pattern of the first row, counted from 1, or 0 for a symbol of one or two columns.
        /// </summary>
        public int CentrePattern => this.entry[4];

        /// <summary>
        /// Gets the right row address pattern of the first row, counted from 1.
        /// </summary>
        public int RightPattern => this.entry[5];

        /// <summary>
        /// Returns whether the size holds the data codewords: it has the requested columns, if any, and its matrix less the error correction codewords holds
        /// the data.
        /// </summary>
        /// <param name="dataLength">The number of data codewords.</param>
        /// <param name="columns">The requested number of data columns, or <see langword="null"/> for any.</param>
        /// <returns><see langword="true"/> when the size holds the data.</returns>
        public bool Holds(int dataLength, int? columns)
            => (!columns.HasValue || columns.Value == this.Columns) && dataLength <= (this.Columns * this.Rows) - this.ErrorCorrection;
    }
}
