// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Builds PDF417 and Compact PDF417 symbols, ISO/IEC 15438: the symbol matrix of section 5.9, the row
/// indicators of section 5.11.3 and the rows of section 5.2.2.
/// </summary>
internal static class Pdf417Encoder
{
    /// <summary>
    /// The fewest data columns of a symbol, section 5.2.1.
    /// </summary>
    public const int MinimumColumns = 1;

    /// <summary>
    /// The most data columns of a symbol, section 5.2.1.
    /// </summary>
    public const int MaximumColumns = 30;

    /// <summary>
    /// The highest error correction level, Table 9.
    /// </summary>
    public const int MaximumErrorCorrectionLevel = 8;

    /// <summary>
    /// The fewest rows of a symbol, section 5.2.1.
    /// </summary>
    private const int MinimumRows = 3;

    /// <summary>
    /// The most rows of a symbol, section 5.2.1.
    /// </summary>
    private const int MaximumRows = 90;

    /// <summary>
    /// The most codewords a symbol holds, Annex O parameter 9: c x r &lt; 929.
    /// </summary>
    private const int MaximumCodewords = 928;

    /// <summary>
    /// The nominal row height in modules, the minimum of section 5.8.2: Y &gt;= 3X.
    /// </summary>
    private const float NominalRowHeight = 3F;

    /// <summary>
    /// The quiet zone on each of the four sides in modules, the minimum of section 5.8.3.
    /// </summary>
    private const int QuietZone = 2;

    /// <summary>
    /// The number of rows over which the row indicators repeat their information, section 5.11.3.
    /// </summary>
    private const int IndicatorCycle = 3;

    /// <summary>
    /// The step of the row indicator values from one cycle of three rows to the next, section 5.11.3.
    /// </summary>
    private const int IndicatorStep = 30;

    /// <summary>
    /// Gets the element widths of the start character, Table 1.
    /// </summary>
    private static ReadOnlySpan<int> Start => [8, 1, 1, 1, 1, 1, 1, 3];

    /// <summary>
    /// Gets the element widths of the stop character, Table 1.
    /// </summary>
    private static ReadOnlySpan<int> Stop => [7, 1, 1, 3, 1, 1, 1, 2, 1];

    /// <summary>
    /// Encodes the text into a symbol.
    /// </summary>
    /// <param name="text">The data, the characters U+0000 to U+00FF.</param>
    /// <param name="columns">The number of data columns, 1 to 30, or <see langword="null"/> to choose them from the data.</param>
    /// <param name="errorCorrectionLevel">The error correction level, 0 to 8, or <see langword="null"/> to choose it from the data.</param>
    /// <param name="compact">Whether the symbol is Compact PDF417, Annex G.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    /// <exception cref="ArgumentException">The data is not valid or does not fit the symbol.</exception>
    public static StackedBarcodeSymbol BuildSymbol(ReadOnlySpan<char> text, int? columns, int? errorCorrectionLevel, bool compact, BarcodeOptions options)
    {
        ValueListBuilder<int> data = new(stackalloc int[Pdf417Codewords.StackBufferLength]);
        try
        {
            Pdf417Codewords.Encode(text, ref data);
            return BuildSymbol(data.AsSpan(), columns, errorCorrectionLevel, compact, options);
        }
        finally
        {
            data.Dispose();
        }
    }

    /// <summary>
    /// Builds the symbol from its data codewords.
    /// </summary>
    /// <param name="data">The data codewords, without the Symbol Length Descriptor.</param>
    /// <param name="columns">The number of data columns, 1 to 30, or <see langword="null"/> to choose them from the data.</param>
    /// <param name="errorCorrectionLevel">The error correction level, 0 to 8, or <see langword="null"/> to choose it from the data.</param>
    /// <param name="compact">Whether the symbol is Compact PDF417, Annex G.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    /// <exception cref="ArgumentException">The data does not fit the symbol.</exception>
    private static StackedBarcodeSymbol BuildSymbol(ReadOnlySpan<int> data, int? columns, int? errorCorrectionLevel, bool compact, BarcodeOptions options)
    {
        // The data codewords, the Symbol Length Descriptor and at least the two error correction
        // codewords of level 0 must fit in the largest matrix.
        int m = data.Length;
        if (m + 1 + 2 > MaximumCodewords)
        {
            throw new ArgumentException($"The data takes {m} codewords; a PDF417 symbol holds at most {MaximumCodewords - 3}.");
        }

        // Without a chosen level the symbol takes the recommended minimum of Table E.1, lowered where the
        // data leaves too little room for its error correction codewords: 2^(s + 1) codewords fit when
        // s + 1 <= log2(927 - m).
        int level;
        if (errorCorrectionLevel.HasValue)
        {
            level = errorCorrectionLevel.Value;
        }
        else
        {
            level = m <= 40 ? 2 : m <= 160 ? 3 : m <= 320 ? 4 : 5;
            level = Math.Min(level, BitOperations.Log2((uint)(MaximumCodewords - 1 - m)) - 1);
        }

        // Without chosen columns the matrix is close to three rows per column, the nearest whole number of
        // columns to the square root of a third of the codewords.
        int k = 2 << level;
        int c = columns ?? (int)Math.Floor(Math.Sqrt((m + k) / 3D) + 0.5D);
        int r = Math.Max(MinimumRows, (m + 1 + k + c - 1) / c);
        if (r > MaximumRows || c * r > MaximumCodewords)
        {
            throw new ArgumentException($"The data takes {m} codewords and does not fit in a PDF417 symbol of {c} columns.");
        }

        // Without a chosen level the error correction grows into the room the rows leave, up to level 8,
        // rather than filling it with pad codewords.
        if (!errorCorrectionLevel.HasValue)
        {
            int roomLevel = Math.Min(BitOperations.Log2((uint)((c * r) - 1 - m)) - 1, MaximumErrorCorrectionLevel);
            if (roomLevel > level)
            {
                level = roomLevel;
                k = 2 << level;
            }
        }

        // Section 5.9.2: the Symbol Length Descriptor n = c x r - k counts itself, the data and the pad
        // codewords, which fill the rest of the data region with 900.
        int total = c * r;
        int n = total - k;
        int[] rented = ArrayPool<int>.Shared.Rent(total);
        try
        {
            Span<int> codewords = rented.AsSpan(0, total);
            codewords[0] = n;
            data.CopyTo(codewords[1..]);
            codewords[(m + 1)..n].Fill(Pdf417Codewords.Pad);
            Pdf417Codewords.AppendErrorCorrection(codewords, k);

            return BuildRows(codewords, c, r, level, compact, options);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Builds the rows of the symbol: the start character, the left row indicator, the codewords of the
    /// row, the right row indicator and the stop character. Compact PDF417, Annex G, drops the right row
    /// indicator and ends the row with a single module bar.
    /// </summary>
    /// <param name="codewords">The codewords in row order, error correction codewords included.</param>
    /// <param name="c">The number of data columns.</param>
    /// <param name="r">The number of rows.</param>
    /// <param name="level">The error correction level.</param>
    /// <param name="compact">Whether the symbol is Compact PDF417.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    private static StackedBarcodeSymbol BuildRows(ReadOnlySpan<int> codewords, int c, int r, int level, bool compact, BarcodeOptions options)
    {
        // A row of a full symbol is the start character, c + 2 symbol characters and the stop character;
        // a compact row is the start character, c + 1 symbol characters and one bar.
        int runCount = compact
            ? Start.Length + ((c + 1) * Pdf417Patterns.ElementCount) + 1
            : Start.Length + ((c + 2) * Pdf417Patterns.ElementCount) + Stop.Length;

        // Every row has the same bar count, height and top, so the rows share one heights array and one
        // tops array.
        int barCount = (runCount + 1) / 2;
        float rowHeight = options.ResolveBarHeight(BarcodeSymbology.PointXDimension, NominalRowHeight);
        float[] heights = new float[barCount];
        heights.AsSpan().Fill(rowHeight);
        float[] tops = new float[barCount];

        ReadOnlySpan<int> patterns = Pdf417Patterns.Modules;
        LinearBarcodeSymbol[] rows = new LinearBarcodeSymbol[r];
        for (int row = 0; row < r; row++)
        {
            // Section 5.11.1: row 1 uses cluster 0, row 2 cluster 3 and row 3 cluster 6, repeating. The
            // cluster index here is 0, 1 or 2, and the pattern table holds the clusters in that order.
            int cluster = row % IndicatorCycle;
            ReadOnlySpan<int> clusterPatterns = patterns.Slice(cluster * Pdf417Patterns.CodewordCount, Pdf417Patterns.CodewordCount);

            // Section 5.11.3: each indicator carries the row number F = row + 1 as 30 x ((F - 1) div 3),
            // and one of three values in turn: (r - 1) div 3, the error correction level and (r - 1) mod 3
            // as s x 3 + (r - 1) mod 3, and c - 1.
            int cycle = IndicatorStep * (row / IndicatorCycle);
            int rowValue = cycle + ((r - 1) / IndicatorCycle);
            int levelValue = cycle + (level * 3) + ((r - 1) % IndicatorCycle);
            int columnValue = cycle + c - 1;
            (int left, int right) = cluster switch
            {
                0 => (rowValue, columnValue),
                1 => (levelValue, rowValue),
                _ => (columnValue, levelValue),
            };

            int[] runs = new int[runCount];
            Start.CopyTo(runs);
            int index = Start.Length;
            Pdf417Patterns.AppendCharacter(runs, ref index, clusterPatterns[left]);
            for (int column = 0; column < c; column++)
            {
                Pdf417Patterns.AppendCharacter(runs, ref index, clusterPatterns[codewords[(row * c) + column]]);
            }

            if (compact)
            {
                runs[index] = 1;
            }
            else
            {
                Pdf417Patterns.AppendCharacter(runs, ref index, clusterPatterns[right]);
                Stop.CopyTo(runs.AsSpan(index));
            }

            rows[row] = new LinearBarcodeSymbol(runs, heights, tops, [], 0, 0);
        }

        return new StackedBarcodeSymbol(rows, 0F, [], QuietZone, QuietZone, QuietZone, QuietZone);
    }
}
