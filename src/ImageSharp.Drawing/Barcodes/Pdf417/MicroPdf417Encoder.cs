// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Builds MicroPDF417 symbols: 1 to 4 data columns between row address patterns, in one of 34 fixed
/// sizes. The symbol characters and the error correction are those of PDF417, and the rows carry no start
/// or stop character and no row indicators.
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
    /// The number of values in one entry of <see cref="Sizes"/>.
    /// </summary>
    private const int SizeFields = 6;

    /// <summary>
    /// Gets the 34 symbol sizes in the order the encoder tries them, six values each: the data columns,
    /// the rows, the error correction codewords, and the first left, centre and right row address
    /// patterns. Only symbols of three or four columns carry a centre pattern.
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
    /// Gets the 52 left and right row address patterns, each the 10 modules of three bars and three spaces
    /// with the first module in bit 9.
    /// </summary>
    private static ReadOnlySpan<short> SideAddressPatterns =>
    [
        802, 930, 946, 818, 882, 890, 826, 954, 922, 986, 970, 906, 778,
        794, 786, 914, 978, 982, 980, 916, 948, 932, 934, 942, 940, 936,
        808, 812, 814, 806, 822, 950, 918, 790, 788, 820, 884, 868, 870,
        878, 876, 872, 840, 856, 860, 862, 846, 844, 836, 838, 834, 866,
    ];

    /// <summary>
    /// Gets the 52 centre row address patterns, each the 10 modules of three bars and three spaces with the
    /// first module in bit 9.
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
        // A character takes at most two data codewords, as in PDF417, and a message format takes one macro
        // codeword more. The text is bounded, so the builder never grows: short data works inline and
        // longer data rents once.
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
        int size = 0;
        while (size < sizes.Length
            && ((columns.HasValue && columns.Value != sizes[size]) || data.Length > (sizes[size] * sizes[size + 1]) - sizes[size + 2]))
        {
            size += SizeFields;
        }

        if (size == sizes.Length)
        {
            throw new ArgumentException(columns.HasValue
                ? $"The data takes {data.Length} codewords and does not fit in a MicroPDF417 symbol of {columns.Value} columns."
                : $"The data takes {data.Length} codewords and does not fit in a MicroPDF417 symbol.");
        }

        // The data codewords, the pad codewords that fill the data region with 900, and the error
        // correction codewords. A MicroPDF417 symbol has no Symbol Length Descriptor.
        int c = sizes[size];
        int r = sizes[size + 1];
        int k = sizes[size + 2];
        InlineArray256<int> codewordStorage = default;
        Span<int> codewords = ((Span<int>)codewordStorage)[..(c * r)];
        data.CopyTo(codewords);
        codewords[data.Length..^k].Fill(Pdf417Codewords.Pad);
        Pdf417Codewords.AppendErrorCorrection(codewords, k);

        BuildRows(codewords, c, r, sizes[size + 3], sizes[size + 4], sizes[size + 5], options, ref symbol);
    }

    /// <summary>
    /// Builds the rows of the symbol: the left row address pattern, the codewords of the row with the centre
    /// row address pattern after the first column of three or the second of four, the right row address
    /// pattern and a single module bar.
    /// </summary>
    /// <param name="codewords">The codewords in row order, error correction codewords included.</param>
    /// <param name="c">The number of data columns.</param>
    /// <param name="r">The number of rows.</param>
    /// <param name="left">The left row address pattern of the first row, counted from 1.</param>
    /// <param name="centre">The centre row address pattern of the first row, counted from 1.</param>
    /// <param name="right">The right row address pattern of the first row, counted from 1.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void BuildRows(
        ReadOnlySpan<int> codewords,
        int c,
        int r,
        int left,
        int centre,
        int right,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        bool hasCentre = c >= 3;
        int centreAfter = c - 2;
        int runCount = (2 * AddressPatternElements) + (c * Pdf417Patterns.ElementCount) + 1 + (hasCentre ? AddressPatternElements : 0);

        float rowHeight = options.ResolveBarHeight(BarcodeSymbology.PointXDimension, NominalRowHeight);

        ReadOnlySpan<int> patterns = Pdf417Patterns.Modules;
        symbol.BeginStacked(r, r * runCount, r * ((runCount + 1) >> 1));
        for (int row = 0; row < r; row++)
        {
            // Each row address pattern steps through its table from the first one of the size, and the
            // cluster of the row follows the left pattern: every third pattern shares one of the three
            // clusters.
            int leftIndex = (row + left - 1) % AddressPatternCount;
            int cluster = (row + left - 1) % 3;
            ReadOnlySpan<int> clusterPatterns = patterns.Slice(cluster * Pdf417Patterns.CodewordCount, Pdf417Patterns.CodewordCount);

            Span<int> runs = symbol.AddRow(runCount, out Span<float> heights, out _);
            heights.Fill(rowHeight);
            int index = 0;
            Pdf417Patterns.AppendElements(runs, ref index, SideAddressPatterns[leftIndex], AddressPatternModules, AddressPatternElements);
            for (int column = 0; column < c; column++)
            {
                if (hasCentre && column == centreAfter)
                {
                    int centreModules = CentreAddressPatterns[(row + centre - 1) % AddressPatternCount];
                    Pdf417Patterns.AppendElements(runs, ref index, centreModules, AddressPatternModules, AddressPatternElements);
                }

                Pdf417Patterns.AppendCharacter(runs, ref index, clusterPatterns[codewords[(row * c) + column]]);
            }

            int rightModules = SideAddressPatterns[(row + right - 1) % AddressPatternCount];
            Pdf417Patterns.AppendElements(runs, ref index, rightModules, AddressPatternModules, AddressPatternElements);
            runs[index] = 1;
        }

        symbol.EndStacked(0F, QuietZone, QuietZone, QuietZone, QuietZone);
    }
}
