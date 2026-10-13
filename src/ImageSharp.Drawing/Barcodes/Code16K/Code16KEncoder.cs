// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for Code 16K, AIM USS-16K. Section 2.1: a symbol is 2 to 16 rows, and each row is a start
/// character, a one module guard bar, five symbol characters and a stop character, 70 modules in all.
/// The symbol characters are ordered from the first row down. The first is the starting symbol character
/// and the last two are the check characters. Section 2.2: a symbol character is the eleven module
/// Code 128 pattern of Table 2, three spaces alternating with three bars, starting with a space.
/// </summary>
internal static class Code16KEncoder
{
    /// <summary>
    /// The quiet zone in modules before the start character: the "ten modules adjacent to the start
    /// pattern" of section 3.4.
    /// </summary>
    public const int LeadingQuietZone = 10;

    /// <summary>
    /// The quiet zone in modules after the stop character: the "one module adjacent to the stop pattern"
    /// of section 3.4.
    /// </summary>
    public const int TrailingQuietZone = 1;

    /// <summary>
    /// The width of a row in modules: "the 70X bars and spaces" of section 3.6.1.
    /// </summary>
    public const int RowWidth = 70;

    /// <summary>
    /// The smallest number of rows, section 2.1.
    /// </summary>
    public const int MinimumRows = 2;

    /// <summary>
    /// The largest number of rows, section 2.1.
    /// </summary>
    public const int MaximumRows = 16;

    /// <summary>
    /// The number of symbol characters in a row, section 2.1.
    /// </summary>
    public const int CharactersPerRow = 5;

    /// <summary>
    /// The nominal bar height in modules: the "minimum bar height of 8X" of section 3.3.
    /// </summary>
    public const float NominalBarHeight = 8F;

    /// <summary>
    /// The height in modules of the separator bars: the "1-module high separator bar" of section 2.1.
    /// </summary>
    public const float SeparatorHeight = 1F;

    /// <summary>
    /// The modulus of the check characters, section 2.6.
    /// </summary>
    public const int Modulus = 107;

    /// <summary>
    /// The pad character, section 2.3.5: "When it appears at the end of the symbol characters, just
    /// before the check characters, it represents no data."
    /// </summary>
    public const int Pad = 103;

    /// <summary>
    /// The shift character of code sets A and B: Shift B in code set A and Shift A in code set B, Table 2.
    /// </summary>
    private const int Shift = 98;

    /// <summary>
    /// The Code C character of code sets A and B, Table 2.
    /// </summary>
    private const int LatchC = 99;

    /// <summary>
    /// The Code B character of code sets A and C, Table 2.
    /// </summary>
    private const int LatchB = 100;

    /// <summary>
    /// The Code A character of code sets B and C, Table 2.
    /// </summary>
    private const int LatchA = 101;

    /// <summary>
    /// The double shift of code sets A and B, Double Shift B and Double Shift A, and the single Shift B
    /// of code set C, Table 2.
    /// </summary>
    private const int DoubleShift = 104;

    /// <summary>
    /// Double Shift C in code sets A and B, and Double Shift B in code set C, Table 2.
    /// </summary>
    private const int DoubleShiftC = 105;

    /// <summary>
    /// Triple Shift C in code sets A and B, and Triple Shift B in code set C, Table 2.
    /// </summary>
    private const int TripleShiftC = 106;

    /// <summary>
    /// The pattern of the value 106, the one symbol character Table 2 defines beyond the Code 128 set,
    /// packed as <see cref="Code128Encoder.Patterns"/> packs its patterns.
    /// </summary>
    private const uint TripleShiftPattern = 0x6211133;

    /// <summary>
    /// The number of data symbol characters a symbol of a given row count carries: five per row, less
    /// the starting symbol character and the two check characters.
    /// </summary>
    private const int Overhead = 3;

    /// <summary>
    /// The longest text a symbol carries: two digits per symbol character in code set C over the data
    /// symbol characters of sixteen rows.
    /// </summary>
    private const int MaximumText = ((MaximumRows * CharactersPerRow) - Overhead) * 2;

    /// <summary>
    /// Gets the eight start patterns of Table 4, bar, space, bar and space, one nibble each. Table 5 gives
    /// the stop patterns the same widths with the bars and spaces exchanged.
    /// </summary>
    private static ReadOnlySpan<ushort> StartPatterns => [0x3211, 0x2221, 0x2122, 0x1411, 0x1132, 0x1231, 0x1114, 0x3112];

    /// <summary>
    /// Gets the start pattern value of every row, Table 6.
    /// </summary>
    private static ReadOnlySpan<byte> RowStartValues => [0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2, 3, 4, 5, 6, 7];

    /// <summary>
    /// Gets the stop pattern value of every row, Table 6.
    /// </summary>
    private static ReadOnlySpan<byte> RowStopValues => [0, 1, 2, 3, 4, 5, 6, 7, 4, 5, 6, 7, 0, 1, 2, 3];

    /// <summary>
    /// Encodes text into the symbol character values of the whole symbol: the starting symbol character,
    /// the data with its code and shift characters, the pad characters and the two check characters, five
    /// per row. Appendix G chooses the starting mode and the code and shift characters.
    /// </summary>
    /// <param name="text">The text to encode. Every character must be ASCII 0 to 127.</param>
    /// <returns>The symbol character values, five per row.</returns>
    /// <exception cref="ArgumentException">The text carries a character the symbology cannot encode, or more data than sixteen rows hold.</exception>
    public static int[] Encode(ReadOnlySpan<char> text)
    {
        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAscii)
            {
                throw new ArgumentException(
                    $"Code 16K encodes ASCII 0 to 127; {current.ToDisplayString()} is outside that range.",
                    nameof(text));
            }
        }

        int capacity = (MaximumRows * CharactersPerRow) - Overhead;
        int n = text.Length;
        if (n > MaximumText)
        {
            throw new ArgumentException($"Code 16K carries at most {capacity} symbol characters in sixteen rows; the text needs more.", nameof(text));
        }

        // The rules of Appendix G depend on the digit run from a position and on which of a control
        // character or a lower case character comes first. Each table holds that for every position, with
        // one entry past the end, and every entry fits a byte with the text bounded above.
        int lookahead = n + 1;
        Span<byte> tables = stackalloc byte[(MaximumText + 1) * 3];
        Span<byte> digits = tables[..lookahead];
        Span<byte> nextControl = tables.Slice(lookahead, lookahead);
        Span<byte> nextLower = tables.Slice(lookahead * 2, lookahead);
        digits[n] = 0;
        nextControl[n] = byte.MaxValue;
        nextLower[n] = byte.MaxValue;
        for (int p = n - 1; p >= 0; p--)
        {
            char c = text[p];
            digits[p] = (byte)(char.IsAsciiDigit(c) ? digits[p + 1] + 1 : 0);
            nextControl[p] = IsControl(c) ? (byte)0 : Saturate(nextControl[p + 1]);
            nextLower[p] = IsLower(c) ? (byte)0 : Saturate(nextLower[p + 1]);
        }

        // A data character costs at most two symbol characters, a shift and itself, and the symbol adds
        // the starting symbol character and the two check characters; the pad fills the rest of the rows.
        Span<int> values = stackalloc int[(MaximumText * 2) + Overhead];
        int count = 1;
        int i = 0;
        int set;
        int mode;

        // Appendix G rule 1: the starting mode, Table 3. Modes 5 and 6 imply a Shift B or a Double Shift B
        // ahead of code set C, which carries the first one or two characters in code set B without
        // spending a symbol character on the shift.
        if (digits[0] >= 2 && (digits[0] & 1) == 0)
        {
            mode = 2;
            set = 'C';
        }
        else if (digits[0] >= 3)
        {
            mode = 5;
            set = 'C';
            values[count++] = ValueB(text[i++]);
        }
        else if (n >= 1 && IsInB(text[0]) && digits[1] >= 2 && (digits[1] & 1) == 0)
        {
            mode = 5;
            set = 'C';
            values[count++] = ValueB(text[i++]);
        }
        else if (n >= 1 && IsInB(text[0]) && digits[1] >= 3)
        {
            mode = 6;
            set = 'C';
            values[count++] = ValueB(text[i++]);
            values[count++] = ValueB(text[i++]);
        }
        else if (n >= 2 && IsInB(text[0]) && IsInB(text[1]) && digits[2] >= 2 && (digits[2] & 1) == 0)
        {
            mode = 6;
            set = 'C';
            values[count++] = ValueB(text[i++]);
            values[count++] = ValueB(text[i++]);
        }
        else if (nextControl[0] < nextLower[0])
        {
            mode = 0;
            set = 'A';
        }
        else
        {
            mode = 1;
            set = 'B';
        }

        // Rules 3, 4 and 5: the next symbol character from the data that remains, in the current code set.
        while (i < n)
        {
            char c = text[i];
            int run = digits[i];
            if (set == 'A')
            {
                if (IsLower(c) && nextControl[i + 1] < nextLower[i + 1])
                {
                    values[count++] = Shift;
                    values[count++] = ValueB(c);
                    i++;
                }
                else if (i + 1 < n && IsLower(c) && IsLower(text[i + 1]) && nextControl[i + 2] < nextLower[i + 2])
                {
                    values[count++] = DoubleShift;
                    values[count++] = ValueB(c);
                    values[count++] = ValueB(text[i + 1]);
                    i += 2;
                }
                else if (IsLower(c))
                {
                    values[count++] = LatchB;
                    set = 'B';
                }
                else if (run == 4 && i + 4 < n && IsInA(text[i + 4]))
                {
                    values[count++] = DoubleShiftC;
                    count = WritePairs(text, i, 2, values, count);
                    i += 4;
                }
                else if (run == 6 && i + 6 < n && IsInA(text[i + 6]))
                {
                    values[count++] = TripleShiftC;
                    count = WritePairs(text, i, 3, values, count);
                    i += 6;
                }
                else if (run >= 4 && (run & 1) == 0)
                {
                    values[count++] = LatchC;
                    set = 'C';
                }
                else
                {
                    values[count++] = ValueA(c);
                    i++;
                }
            }
            else if (set == 'B')
            {
                if (IsControl(c) && nextLower[i + 1] < nextControl[i + 1])
                {
                    values[count++] = Shift;
                    values[count++] = ValueA(c);
                    i++;
                }
                else if (i + 1 < n && IsControl(c) && IsControl(text[i + 1]) && nextLower[i + 2] < nextControl[i + 2])
                {
                    values[count++] = DoubleShift;
                    values[count++] = ValueA(c);
                    values[count++] = ValueA(text[i + 1]);
                    i += 2;
                }
                else if (IsControl(c))
                {
                    values[count++] = LatchA;
                    set = 'A';
                }
                else if (run == 4 && i + 4 < n && IsInB(text[i + 4]))
                {
                    values[count++] = DoubleShiftC;
                    count = WritePairs(text, i, 2, values, count);
                    i += 4;
                }
                else if (run == 6 && i + 6 < n && IsInB(text[i + 6]))
                {
                    values[count++] = TripleShiftC;
                    count = WritePairs(text, i, 3, values, count);
                    i += 6;
                }
                else if (run >= 4 && (run & 1) == 0)
                {
                    values[count++] = LatchC;
                    set = 'C';
                }
                else
                {
                    values[count++] = ValueB(c);
                    i++;
                }
            }
            else
            {
                if (run >= 2)
                {
                    count = WritePairs(text, i, 1, values, count);
                    i += 2;
                }
                else if (i + 1 < n && IsInB(c) && digits[i + 1] >= 2 && (digits[i + 1] & 1) == 0)
                {
                    values[count++] = DoubleShift;
                    values[count++] = ValueB(c);
                    i++;
                }
                else if (i + 1 < n && IsInB(c) && digits[i + 1] >= 3)
                {
                    values[count++] = DoubleShiftC;
                    values[count++] = ValueB(c);
                    values[count++] = ValueB(text[i + 1]);
                    i += 2;
                }
                else if (i + 2 < n && IsInB(c) && IsInB(text[i + 1]) && digits[i + 2] >= 2 && (digits[i + 2] & 1) == 0)
                {
                    values[count++] = DoubleShiftC;
                    values[count++] = ValueB(c);
                    values[count++] = ValueB(text[i + 1]);
                    i += 2;
                }
                else if (i + 3 < n && IsInB(c) && IsInB(text[i + 1]) && digits[i + 2] >= 3)
                {
                    values[count++] = TripleShiftC;
                    values[count++] = ValueB(c);
                    values[count++] = ValueB(text[i + 1]);
                    values[count++] = ValueB(text[i + 2]);
                    i += 3;
                }
                else if (i + 3 < n && IsInB(c) && IsInB(text[i + 1]) && IsInB(text[i + 2]) && digits[i + 3] >= 2 && (digits[i + 3] & 1) == 0)
                {
                    values[count++] = TripleShiftC;
                    values[count++] = ValueB(c);
                    values[count++] = ValueB(text[i + 1]);
                    values[count++] = ValueB(text[i + 2]);
                    i += 3;
                }
                else if (nextControl[i] < nextLower[i])
                {
                    values[count++] = LatchA;
                    set = 'A';
                }
                else
                {
                    values[count++] = LatchB;
                    set = 'B';
                }
            }
        }

        int dataCount = count - 1;
        if (dataCount > capacity)
        {
            throw new ArgumentException($"Code 16K carries at most {capacity} symbol characters in sixteen rows; the text needs {dataCount}.", nameof(text));
        }

        // Section 2.1 gives the smallest symbol that holds the data, and section 2.3.5 pads the last row.
        int rows = MinimumRows;
        while ((rows * CharactersPerRow) - Overhead < dataCount)
        {
            rows++;
        }

        int total = rows * CharactersPerRow;
        for (int j = count; j < total - 2; j++)
        {
            values[j] = Pad;
        }

        // Section 2.3.1: s = 7(r - 2) + m.
        values[0] = (7 * (rows - MinimumRows)) + mode;

        // Section 2.6: C1 weights the symbol characters from two upward, C2 from one upward and takes
        // C1 as its last term.
        int c1 = 0;
        int c2 = 0;
        for (int j = 0; j < total - 2; j++)
        {
            c1 += (j + 2) * values[j];
            c2 += (j + 1) * values[j];
        }

        c1 %= Modulus;
        c2 = (c2 + ((total - 1) * c1)) % Modulus;
        values[total - 2] = c1;
        values[total - 1] = c2;
        return values[..total].ToArray();
    }

    /// <summary>
    /// Builds the symbol from its symbol character values: one row of bars per five values, one module
    /// separator bars between the rows, and the top and bottom separator bars of section 2.1.
    /// </summary>
    /// <param name="values">The symbol character values, five per row.</param>
    /// <param name="text">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    public static StackedBarcodeSymbol BuildSymbol(int[] values, string text, BarcodeOptions options)
    {
        int rowCount = values.Length / CharactersPerRow;
        float barHeight = options.ResolveBarHeight(BarcodeSymbology.PointXDimension, NominalBarHeight);
        LinearBarcodeSymbol separator = new([RowWidth], [SeparatorHeight], [0F], [], 0, 0);
        LinearBarcodeSymbol[] rows = new LinearBarcodeSymbol[(rowCount * 2) - 1];
        ReadOnlySpan<uint> patterns = Code128Encoder.Patterns;

        // Every data row has the same bar count, height and top, so the rows share one heights array and
        // one tops array.
        int barCount = (4 + 1 + (CharactersPerRow * 6) + 4 + 1) / 2;
        float[] heights = new float[barCount];
        heights.AsSpan().Fill(barHeight);
        float[] tops = new float[barCount];
        for (int row = 0; row < rowCount; row++)
        {
            // Section 2.1: start, guard bar, five characters and stop, 4 + 1 + 30 + 4 elements, on a bar
            // at both ends. Table 2 starts every character on a space, which the guard bar leads into.
            int[] runs = new int[4 + 1 + (CharactersPerRow * 6) + 4];
            int index = 0;
            ushort start = StartPatterns[RowStartValues[row]];
            for (int shift = 12; shift >= 0; shift -= 4)
            {
                runs[index++] = (start >> shift) & 0xF;
            }

            runs[index++] = 1;
            for (int k = 0; k < CharactersPerRow; k++)
            {
                int value = values[(row * CharactersPerRow) + k];
                uint pattern = value == TripleShiftC ? TripleShiftPattern : patterns[value];
                for (int shift = 20; shift >= 0; shift -= 4)
                {
                    runs[index++] = (int)((pattern >> shift) & 0xF);
                }
            }

            ushort stop = StartPatterns[RowStopValues[row]];
            for (int shift = 12; shift >= 0; shift -= 4)
            {
                runs[index++] = (stop >> shift) & 0xF;
            }

            rows[row * 2] = new LinearBarcodeSymbol(runs, heights, tops, [], 0, 0);
            if (row < rowCount - 1)
            {
                rows[(row * 2) + 1] = separator;
            }
        }

        // Appendix D: the human readable interpretation "may accompany the symbol. It should not
        // interfere with the symbol itself nor the quiet zones."
        float heightInModules = (rowCount * barHeight) + ((rowCount + 1) * SeparatorHeight);
        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null && text.Length > 0)
        {
            placements = [new BarcodeTextPlacement(text, 0F, RowWidth, BarcodeTextSide.BelowBars, heightInModules + BarcodeTextPlacement.Clearance)];
        }

        return new StackedBarcodeSymbol(rows, SeparatorHeight, placements, LeadingQuietZone, TrailingQuietZone);
    }

    /// <summary>
    /// Writes digit pairs as code set C values.
    /// </summary>
    /// <param name="text">The text being encoded.</param>
    /// <param name="start">The index of the first digit.</param>
    /// <param name="pairs">The number of pairs to write.</param>
    /// <param name="values">The buffer the symbol character values are written to.</param>
    /// <param name="count">The number of values written so far.</param>
    /// <returns>The number of values written after the pairs.</returns>
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
    /// 95, section 2.2.1.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>The value.</returns>
    private static int ValueA(char c)
        => c < 32 ? c + 64 : c - 32;

    /// <summary>
    /// Returns the code set B value of a character: ASCII 32 to 127 as 0 to 95, section 2.2.2.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>The value.</returns>
    private static int ValueB(char c)
        => c - 32;

    /// <summary>
    /// Returns whether a character is a control character, the Appendix G class of ASCII 0 to 31, which
    /// code set A alone carries.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>Whether the character is a control character.</returns>
    private static bool IsControl(char c)
        => c < 32;

    /// <summary>
    /// Returns whether a character is lower case, the Appendix G class of ASCII 96 to 127, which code set
    /// B alone carries.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>Whether the character is lower case.</returns>
    private static bool IsLower(char c)
        => c >= 96;

    /// <summary>
    /// Returns whether code set A carries a character.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>Whether code set A carries the character.</returns>
    private static bool IsInA(char c)
        => c <= 95;

    /// <summary>
    /// Returns whether code set B carries a character.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>Whether code set B carries the character.</returns>
    private static bool IsInB(char c)
        => c >= 32;

    /// <summary>
    /// Returns the distance from the position before: one more than the given distance, or none when
    /// the given distance is none.
    /// </summary>
    /// <param name="distance">The distance from the next position.</param>
    /// <returns>The distance from this position.</returns>
    private static byte Saturate(byte distance)
        => distance == byte.MaxValue ? byte.MaxValue : (byte)(distance + 1);
}
