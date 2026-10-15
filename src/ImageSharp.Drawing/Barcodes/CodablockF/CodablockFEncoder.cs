// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for Codablock F. A symbol is 2 to 44 rows of Code 128 characters. Every row carries a
/// Start A character, a code set selector, a row indicator, the data columns, a modulo 103 row check
/// character and the stop character. The first row's indicator is the row count less two and every other
/// row's is its index plus 42. The last row gives two of its data columns to the modulo 86 check
/// characters K1 and K2 of the whole message. The row and check values map to code set A and B
/// characters as 64 to 95 for 0 to 31, 0 to 15 for 32 to 47 and 26 to 63 for 48 to 85, and to code set
/// C characters as themselves.
/// </summary>
internal static class CodablockFEncoder
{
    /// <summary>
    /// The quiet zone in modules on each side.
    /// </summary>
    public const int QuietZone = 10;

    /// <summary>
    /// The smallest number of data columns in a row.
    /// </summary>
    public const int MinimumColumns = 4;

    /// <summary>
    /// The largest number of data columns in a row.
    /// </summary>
    public const int MaximumColumns = 62;

    /// <summary>
    /// The number of data columns in a row when none is given.
    /// </summary>
    public const int DefaultColumns = 8;

    /// <summary>
    /// The smallest number of rows.
    /// </summary>
    public const int MinimumRows = 2;

    /// <summary>
    /// The largest number of rows.
    /// </summary>
    public const int MaximumRows = 44;

    /// <summary>
    /// The characters a row carries beyond its data columns: the start character, the code set selector,
    /// the row indicator, the row check character and the stop character.
    /// </summary>
    public const int Overhead = 5;

    /// <summary>
    /// The height in modules of the separator bars between rows and at the top and bottom of the symbol.
    /// </summary>
    public const float SeparatorHeight = 1F;

    /// <summary>
    /// The modulus of the check characters K1 and K2.
    /// </summary>
    public const int CheckModulus = 86;

    private const int StartA = 103;
    private const int Shift = 98;
    private const int LatchC = 99;
    private const int LatchB = 100;
    private const int LatchA = 101;
    private const int Stop = 106;
    private const int RowModulus = 103;

    /// <summary>
    /// The row indicator of the second row; each row that follows adds one.
    /// </summary>
    private const int SecondRowIndicator = 43;

    /// <summary>
    /// The longest text a symbol carries: two digits per character over 44 rows of 62 columns.
    /// </summary>
    private const int MaximumText = MaximumRows * MaximumColumns * 2;

    /// <summary>
    /// Returns the nominal row height in modules for a column count: ten, or 0.55 modules per data
    /// column plus three when that is more.
    /// </summary>
    /// <param name="columns">The number of data columns.</param>
    /// <returns>The nominal row height in modules.</returns>
    public static float NominalRowHeight(int columns)
        => MathF.Max(10F, (0.55F * columns) + 3F);

    /// <summary>
    /// Returns the width of a row in modules: eleven per character and thirteen for the stop character.
    /// </summary>
    /// <param name="columns">The number of data columns.</param>
    /// <returns>The row width in modules.</returns>
    public static int RowWidth(int columns)
        => (11 * (columns + Overhead - 1)) + 13;

    /// <summary>
    /// Encodes text and builds the symbol. The Code 128 character values of the whole symbol run one row
    /// after another, each row <paramref name="columns"/> plus <see cref="Overhead"/> values long.
    /// </summary>
    /// <param name="text">The text to encode. Every character must be ASCII 0 to 127.</param>
    /// <param name="columns">The number of data columns in a row, 4 to 62.</param>
    /// <param name="readablePrefix">The characters of the human readable interpretation before its body.</param>
    /// <param name="readableBody">The body of the human readable interpretation.</param>
    /// <param name="readableSuffix">The characters of the human readable interpretation after its body.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The text carries a character the symbology cannot encode, or more data than 44 rows hold.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The column count is outside 4 to 62.</exception>
    public static void BuildSymbol(
        ReadOnlySpan<char> text,
        int columns,
        ReadOnlySpan<char> readablePrefix,
        ReadOnlySpan<char> readableBody,
        ReadOnlySpan<char> readableSuffix,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        Guard.MustBeBetweenOrEqualTo(columns, MinimumColumns, MaximumColumns, nameof(columns));

        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAscii)
            {
                throw new ArgumentException(
                    $"Codablock F encodes ASCII 0 to 127; {current.ToDisplayString()} is outside that range.",
                    nameof(text));
            }
        }

        int n = text.Length;
        if (n > MaximumText)
        {
            throw new ArgumentException($"Codablock F carries at most {MaximumText} characters in 44 rows of 62 columns; the text is longer.", nameof(text));
        }

        // The row break rules read the digit run from a position and which of a control character or a
        // lower case character comes first. Each table holds that for every position, with one entry past
        // the end. The value count is known only once the rows are laid out, but the encoder checks the
        // 44 row limit before it starts each row of rowSize values, so the values take room for 44 rows.
        // The text is at most MaximumText characters, so one bounded array, rented once, holds the
        // values and the three tables.
        int lookahead = n + 1;
        int capacity = MaximumRows * (columns + Overhead);
        PooledBuffer<int> buffer = new(capacity + (lookahead * 3));
        try
        {
            Span<int> values = buffer.Span[..capacity];
            Span<int> digits = buffer.Span.Slice(capacity, lookahead);
            Span<int> nextControl = buffer.Span.Slice(capacity + lookahead, lookahead);
            Span<int> nextLower = buffer.Span.Slice(capacity + (lookahead * 2), lookahead);
            int count = Encode(text, columns, digits, nextControl, nextLower, values);
            WriteRows(values[..count], columns, readablePrefix, readableBody, readableSuffix, options, ref symbol);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Encodes text into character values with the buffers supplied: the rows with their pad, the check
    /// characters and the row indicators.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="columns">The number of data columns in a row.</param>
    /// <param name="digits">Scratch for the digit run from each position.</param>
    /// <param name="nextControl">Scratch for the distance to the next control character from each position.</param>
    /// <param name="nextLower">Scratch for the distance to the next lower case character from each position.</param>
    /// <param name="values">The buffer the character values are written to, one row after another, with room for 44 rows.</param>
    /// <returns>The number of character values written.</returns>
    private static int Encode(
        ReadOnlySpan<char> text,
        int columns,
        Span<int> digits,
        Span<int> nextControl,
        Span<int> nextLower,
        Span<int> values)
    {
        int n = text.Length;
        digits[n] = 0;
        nextControl[n] = int.MaxValue;
        nextLower[n] = int.MaxValue;
        for (int p = n - 1; p >= 0; p--)
        {
            char c = text[p];
            digits[p] = char.IsAsciiDigit(c) ? digits[p + 1] + 1 : 0;
            nextControl[p] = c < 32 ? 0 : Saturate(nextControl[p + 1]);
            nextLower[p] = c >= 96 ? 0 : Saturate(nextLower[p + 1]);
        }

        int rowSize = columns + Overhead;
        int count = 0;
        int i = 0;
        int rows = 1;
        int set = 'B';
        bool lastRow = false;
        while (!lastRow)
        {
            if (rows > MaximumRows)
            {
                throw new ArgumentException($"Codablock F carries at most 44 rows of {columns} columns; the text needs more.", nameof(text));
            }

            // Every row starts in the code set of its first characters: C for a digit pair, A when a
            // control character comes before a lower case one, B otherwise.
            values[count++] = StartA;
            if (n == 0)
            {
                values[count++] = LatchB;
                set = 'B';
            }
            else if (i < n && digits[i] >= 2)
            {
                values[count++] = LatchC;
                set = 'C';
            }
            else if (i < n && nextControl[i] < nextLower[i])
            {
                values[count++] = Shift;
                set = 'A';
            }
            else
            {
                values[count++] = LatchB;
                set = 'B';
            }

            // The row indicator takes its value once the row count is known.
            values[count++] = 0;

            int remaining;
            bool endOfRow = false;
            while (true)
            {
                remaining = columns + 3 - (count % rowSize);
                if (i == n || endOfRow)
                {
                    break;
                }

                char c = text[i];
                int run = Math.Min(digits[i], 2 * remaining);
                if (set != 'C' && run >= 4)
                {
                    if ((run & 1) == 0 && remaining >= 3)
                    {
                        values[count++] = LatchC;
                        set = 'C';
                        count = WritePairs(text, i, 2, values, count);
                        i += 4;
                        continue;
                    }

                    if ((run & 1) == 1 && remaining >= 4)
                    {
                        values[count++] = set == 'A' ? ValueA(c) : ValueB(c);
                        i++;
                        values[count++] = LatchC;
                        set = 'C';
                        count = WritePairs(text, i, 2, values, count);
                        i += 4;
                        continue;
                    }
                }

                if (set == 'B' && c < 32 && remaining >= 2)
                {
                    if (i < n - 1 && nextLower[i + 1] < nextControl[i + 1])
                    {
                        values[count++] = Shift;
                    }
                    else
                    {
                        values[count++] = LatchA;
                        set = 'A';
                    }

                    values[count++] = ValueA(c);
                    i++;
                    continue;
                }

                if (set == 'A' && c >= 96 && remaining >= 2)
                {
                    if (i < n - 1 && nextControl[i + 1] < nextLower[i + 1])
                    {
                        values[count++] = Shift;
                    }
                    else
                    {
                        values[count++] = LatchB;
                        set = 'B';
                    }

                    values[count++] = ValueB(c);
                    i++;
                    continue;
                }

                if (set == 'C' && run < 2 && remaining >= 2)
                {
                    if (nextControl[i] < nextLower[i])
                    {
                        values[count++] = LatchA;
                        set = 'A';
                        values[count++] = ValueA(c);
                    }
                    else
                    {
                        values[count++] = LatchB;
                        set = 'B';
                        values[count++] = ValueB(c);
                    }

                    i++;
                    continue;
                }

                if (set == 'A' && c <= 95 && remaining >= 1)
                {
                    values[count++] = ValueA(c);
                    i++;
                    continue;
                }

                if (set == 'B' && c >= 32 && remaining >= 1)
                {
                    values[count++] = ValueB(c);
                    i++;
                    continue;
                }

                if (set == 'C' && run >= 2 && remaining >= 1)
                {
                    count = WritePairs(text, i, 1, values, count);
                    i += 2;
                    continue;
                }

                endOfRow = true;
            }

            // The last row needs two columns for K1 and K2 and a symbol needs two rows.
            if (rows > 1 && i == n && remaining >= 2)
            {
                count = Pad(values, count, remaining - 2, ref set);

                // K1, K2 and the row check character take their values below.
                values.Slice(count, 3).Clear();
                count += 3;
                values[count++] = Stop;
                lastRow = true;
            }
            else
            {
                count = Pad(values, count, remaining, ref set);

                // The row check character takes its value below.
                values[count++] = 0;
                values[count++] = Stop;
                rows++;
            }
        }

        // The check characters K1 and K2 of the message take the code set the last row ends in.
        int k1 = 0;
        int k2 = 0;
        for (int p = 0; p < n; p++)
        {
            k1 = (k1 + ((p + 1) * text[p])) % CheckModulus;
            k2 = (k2 + (p * text[p])) % CheckModulus;
        }

        values[count - 4] = Map(k1, set != 'C');
        values[count - 3] = Map(k2, set != 'C');

        // The row indicators: the row count in the first row, the row index from 43 in the rest, each in
        // the code set its row starts in.
        values[2] = Map(rows - 2, values[1] != LatchC);
        for (int row = 1; row < rows; row++)
        {
            int at = row * rowSize;
            values[at + 2] = Map(row + SecondRowIndicator - 1, values[at + 1] != LatchC);
        }

        // Every row ends in the Code 128 check character of its own characters.
        for (int row = 0; row < rows; row++)
        {
            int at = row * rowSize;
            int sum = values[at];
            for (int k = 1; k <= columns + 2; k++)
            {
                sum += values[at + k] * k;
            }

            values[at + columns + 3] = sum % RowModulus;
        }

        return count;
    }

    /// <summary>
    /// Writes the symbol from its character values: one row of Code 128 patterns per row of values, a
    /// separator between rows that keeps the start and stop patterns and fills the columns between them,
    /// a solid bar at the top and at the bottom, and the data as a text line below the bottom bar when
    /// a font is set. The check characters are not printed.
    /// </summary>
    /// <param name="values">The character values, one row after another.</param>
    /// <param name="columns">The number of data columns in a row.</param>
    /// <param name="readablePrefix">The characters of the human readable interpretation before its body.</param>
    /// <param name="readableBody">The body of the human readable interpretation.</param>
    /// <param name="readableSuffix">The characters of the human readable interpretation after its body.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void WriteRows(
        ReadOnlySpan<int> values,
        int columns,
        ReadOnlySpan<char> readablePrefix,
        ReadOnlySpan<char> readableBody,
        ReadOnlySpan<char> readableSuffix,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        int rowSize = columns + Overhead;
        int rowCount = values.Length / rowSize;
        int width = RowWidth(columns);
        float barHeight = options.ResolveBarHeight(BarcodeSymbology.PointXDimension, NominalRowHeight(columns));
        ReadOnlySpan<uint> patterns = Code128Encoder.Patterns;

        // The rows from the top: a solid edge bar, each data row with a separator below it but the last,
        // and a solid edge bar. A separator keeps the start and stop patterns, thirteen runs and seven
        // bars, and an edge bar is one bar across the row width.
        const int separatorRuns = 13;
        int dataRuns = (6 * (rowSize - 1)) + 7;
        int separators = rowCount - 1;
        symbol.BeginStacked(
            (rowCount * 2) + 1,
            2 + (rowCount * dataRuns) + (separators * separatorRuns),
            2 + (rowCount * ((dataRuns + 1) >> 1)) + (separators * ((separatorRuns + 1) >> 1)));

        AddEdge(width, ref symbol);
        for (int row = 0; row < rowCount; row++)
        {
            Span<int> runs = symbol.AddRow(dataRuns, out Span<float> heights, out _);
            heights.Fill(barHeight);
            int index = 0;
            for (int k = 0; k < rowSize; k++)
            {
                int value = values[(row * rowSize) + k];
                uint pattern = patterns[value];
                int elements = value == Stop ? 7 : 6;
                for (int shift = (elements - 1) * 4; shift >= 0; shift -= 4)
                {
                    runs[index++] = (int)((pattern >> shift) & 0xF);
                }
            }

            if (row < rowCount - 1)
            {
                Span<int> separator = symbol.AddRow(separatorRuns, out Span<float> separatorHeights, out _);
                ReadOnlySpan<int> separatorPattern = [2, 1, 1, 4, 1, 2, width - 22, 3, 3, 1, 1, 1, 2];
                separatorPattern.CopyTo(separator);
                separatorHeights.Fill(SeparatorHeight);
            }
        }

        AddEdge(width, ref symbol);

        float heightInModules = (rowCount * barHeight) + ((rowCount + 1) * SeparatorHeight);
        int readableLength = readablePrefix.Length + readableBody.Length + readableSuffix.Length;
        if (options.Font is not null && readableLength > 0)
        {
            symbol.ReserveText(1, readableLength);
            symbol.AddText(
                readablePrefix,
                readableBody,
                readableSuffix,
                0F,
                width,
                BarcodeTextSide.BelowBars,
                heightInModules + BarcodeTextPlacement.Clearance);
        }

        symbol.EndStacked(0F, QuietZone, QuietZone);
    }

    /// <summary>
    /// Adds a solid edge bar across the row width, at the separator height.
    /// </summary>
    /// <param name="width">The row width in modules.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void AddEdge(int width, ref BarcodeSymbol symbol)
    {
        Span<int> runs = symbol.AddRow(1, out Span<float> heights, out _);
        runs[0] = width;
        heights[0] = SeparatorHeight;
    }

    /// <summary>
    /// Writes pad characters to the end of a row: Code C in code sets A and B, Code B in code set C, each
    /// changing the set for the next.
    /// </summary>
    /// <param name="values">The buffer the character values are written to.</param>
    /// <param name="count">The number of character values written so far.</param>
    /// <param name="pads">The number of pad characters.</param>
    /// <param name="set">The code set in effect, updated as the pads change it.</param>
    /// <returns>The number of character values written after the pad.</returns>
    private static int Pad(Span<int> values, int count, int pads, ref int set)
    {
        for (int k = 0; k < pads; k++)
        {
            if (set == 'C')
            {
                values[count++] = LatchB;
                set = 'B';
            }
            else
            {
                values[count++] = LatchC;
                set = 'C';
            }
        }

        return count;
    }

    /// <summary>
    /// Maps a row indicator or check value of 0 to 85 to a character value: 64 to 95 for 0 to 31, 0 to
    /// 15 for 32 to 47 and 26 to 63 for 48 to 85 in code sets A and B, and the value itself in code set C.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="alphabetic">Whether the row is in code set A or B rather than C.</param>
    /// <returns>The character value.</returns>
    private static int Map(int value, bool alphabetic)
    {
        if (!alphabetic)
        {
            return value;
        }

        if (value < 32)
        {
            return value + 64;
        }

        return value < 48 ? value - 32 : value - 22;
    }

    /// <summary>
    /// Writes digit pairs as code set C values.
    /// </summary>
    /// <param name="text">The text being encoded.</param>
    /// <param name="start">The index of the first digit.</param>
    /// <param name="pairs">The number of pairs to write.</param>
    /// <param name="values">The buffer the character values are written to.</param>
    /// <param name="count">The number of character values written so far.</param>
    /// <returns>The number of character values written after the pairs.</returns>
    private static int WritePairs(ReadOnlySpan<char> text, int start, int pairs, Span<int> values, int count)
    {
        for (int p = 0; p < pairs; p++)
        {
            int at = start + (p * 2);
            values[count++] = ((text[at] - '0') * 10) + (text[at + 1] - '0');
        }

        return count;
    }

    /// <summary>
    /// Returns the code set A value of a character: ASCII 32 to 95 as 0 to 63 and ASCII 0 to 31 as 64 to
    /// 95.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>The value.</returns>
    private static int ValueA(char c)
        => c < 32 ? c + 64 : c - 32;

    /// <summary>
    /// Returns the code set B value of a character: ASCII 32 to 127 as 0 to 95.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>The value.</returns>
    private static int ValueB(char c)
        => c - 32;

    /// <summary>
    /// Returns the distance from the position before: one more than the given distance, or none when
    /// the given distance is none.
    /// </summary>
    /// <param name="distance">The distance from the next position.</param>
    /// <returns>The distance from this position.</returns>
    private static int Saturate(int distance)
        => distance == int.MaxValue ? int.MaxValue : distance + 1;
}
