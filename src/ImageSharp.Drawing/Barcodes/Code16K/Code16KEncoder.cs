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
    /// The double shift of code sets A and B, Double Shift B in code set A and Double Shift A in code set B,
    /// Table 2. Code sets A and B take the values 98 to 101 of Code 128: Shift, Code C, Code B and Code A.
    /// </summary>
    private const int DoubleShift = 104;

    /// <summary>
    /// Double Shift C in code sets A and B, Table 2.
    /// </summary>
    private const int DoubleShiftC = 105;

    /// <summary>
    /// Triple Shift C in code sets A and B, Table 2.
    /// </summary>
    private const int TripleShiftC = 106;

    /// <summary>
    /// Shift B in code set C, Table 2: the value of <see cref="DoubleShift"/> in code sets A and B.
    /// </summary>
    private const int CodeSetCShiftB = 104;

    /// <summary>
    /// Double Shift B in code set C, Table 2: the value of <see cref="DoubleShiftC"/> in code sets A and B.
    /// </summary>
    private const int CodeSetCDoubleShiftB = 105;

    /// <summary>
    /// Triple Shift B in code set C, Table 2: the value of <see cref="TripleShiftC"/> in code sets A and B.
    /// </summary>
    private const int CodeSetCTripleShiftB = 106;

    /// <summary>
    /// The pattern of the value 106, the one symbol character Table 2 defines beyond the Code 128 set,
    /// packed as <see cref="Patterns"/> packs its patterns.
    /// </summary>
    private const uint TripleShiftPattern = 0x211133;

    /// <summary>
    /// The symbol characters a symbol carries beyond its data: the starting symbol character and the two
    /// check characters.
    /// </summary>
    private const int Overhead = 3;

    /// <summary>
    /// The longest text a symbol carries: two digits per symbol character in code set C over the data
    /// symbol characters of sixteen rows.
    /// </summary>
    private const int MaximumText = ((MaximumRows * CharactersPerRow) - Overhead) * 2;

    /// <summary>
    /// The symbol character value of Shift, which changes code set A to B, or B to A, for one character.
    /// </summary>
    private const int Shift = 98;

    /// <summary>
    /// The symbol character value of Code C, which changes code set A or B to code set C.
    /// </summary>
    private const int LatchC = 99;

    /// <summary>
    /// The symbol character value of Code B, which changes code set A or C to code set B.
    /// </summary>
    private const int LatchB = 100;

    /// <summary>
    /// The symbol character value of Code A, which changes code set B or C to code set A.
    /// </summary>
    private const int LatchA = 101;

    /// <summary>
    /// The code sets of the Code 128 symbol characters.
    /// </summary>
    private enum CodeSet
    {
        /// <summary>
        /// Code set A: ASCII 32 to 95 as 0 to 63 and the control characters 0 to 31 as 64 to 95.
        /// </summary>
        A,

        /// <summary>
        /// Code set B: ASCII 32 to 127 as 0 to 95.
        /// </summary>
        B,

        /// <summary>
        /// Code set C: the digit pairs 00 to 99 as 0 to 99.
        /// </summary>
        C,
    }

    /// <summary>
    /// Gets the element widths of the symbol characters 0 to 105, the Code 128 patterns of Table 2, packed four
    /// bits per element, the first element in the highest bits: six elements each, three spaces and three
    /// bars of one to four modules.
    /// </summary>
    private static ReadOnlySpan<uint> Patterns =>
    [
        0x212222, 0x222122, 0x222221, 0x121223, 0x121322, 0x131222, 0x122213, 0x122312,
        0x132212, 0x221213, 0x221312, 0x231212, 0x112232, 0x122132, 0x122231, 0x113222,
        0x123122, 0x123221, 0x223211, 0x221132, 0x221231, 0x213212, 0x223112, 0x312131,
        0x311222, 0x321122, 0x321221, 0x312212, 0x322112, 0x322211, 0x212123, 0x212321,
        0x232121, 0x111323, 0x131123, 0x131321, 0x112313, 0x132113, 0x132311, 0x211313,
        0x231113, 0x231311, 0x112133, 0x112331, 0x132131, 0x113123, 0x113321, 0x133121,
        0x313121, 0x211331, 0x231131, 0x213113, 0x213311, 0x213131, 0x311123, 0x311321,
        0x331121, 0x312113, 0x312311, 0x332111, 0x314111, 0x221411, 0x431111, 0x111224,
        0x111422, 0x121124, 0x121421, 0x141122, 0x141221, 0x112214, 0x112412, 0x122114,
        0x122411, 0x142112, 0x142211, 0x241211, 0x221114, 0x413111, 0x241112, 0x134111,
        0x111242, 0x121142, 0x121241, 0x114212, 0x124112, 0x124211, 0x411212, 0x421112,
        0x421211, 0x212141, 0x214121, 0x412121, 0x111143, 0x111341, 0x131141, 0x114113,
        0x114311, 0x411113, 0x411311, 0x113141, 0x114131, 0x311141, 0x411131, 0x211412,
        0x211214, 0x211232,
    ];

    /// <summary>
    /// Gets the eight start patterns of Table 4, bar, space, bar and space, one nibble each. Table 5 gives
    /// the stop patterns the same widths with the bars and spaces exchanged.
    /// </summary>
    private static ReadOnlySpan<ushort> StartPatterns => [0x3211, 0x2221, 0x2122, 0x1411, 0x1132, 0x1231, 0x1114, 0x3112];

    /// <summary>
    /// Gets the stop pattern value of every row, Table 6.
    /// </summary>
    private static ReadOnlySpan<byte> RowStopValues => [0, 1, 2, 3, 4, 5, 6, 7, 4, 5, 6, 7, 0, 1, 2, 3];

    /// <summary>
    /// Encodes text and builds the symbol.
    /// </summary>
    /// <param name="text">The text to encode. Every character must be ASCII 0 to 127.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The text carries a character the symbology cannot encode, or more data than sixteen rows hold.</exception>
    public static void BuildSymbol(ReadOnlySpan<char> text, ReadOnlySpan<char> readable, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (text.Length > MaximumText)
        {
            throw new ArgumentException(
                $"Code 16K carries at most {(MaximumRows * CharactersPerRow) - Overhead} symbol characters in sixteen rows; the text needs more.",
                nameof(text));
        }

        // The lookahead tables hold three bytes for every position and one past the end. A data character
        // costs at most two symbol characters, and the symbol adds three, so the values take twice the
        // text and three more, and at least the sixteen rows of the largest symbol. The text is bounded
        // above, so a short text works inline and a longer one rents each buffer once.
        int tableLength = (text.Length + 1) * 3;
        int valueLength = Math.Max((text.Length * 2) + Overhead, MaximumRows * CharactersPerRow);
        InlineArray256<byte> tableStorage = default;
        InlineArray128<int> valueStorage = default;
        ValueListBuilder<byte> tables = tableLength <= 256 ? new(tableStorage) : new(tableLength);
        ValueListBuilder<int> values = valueLength <= 128 ? new(valueStorage) : new(valueLength);
        try
        {
            int total = Encode(text, tables.AppendSpan(tableLength), values.AppendSpan(valueLength));
            WriteRows(values.AsSpan()[..total], readable, options, ref symbol);
        }
        finally
        {
            values.Dispose();
            tables.Dispose();
        }
    }

    /// <summary>
    /// Encodes text into the symbol character values of the whole symbol: the starting symbol character,
    /// the data with its code and shift characters, the pad characters and the two check characters, five
    /// per row. Appendix G chooses the starting mode and the code and shift characters.
    /// </summary>
    /// <param name="text">The text to encode, at most <see cref="MaximumText"/> characters.</param>
    /// <param name="tables">The scratch for the lookahead tables, three bytes for every position and one past the end.</param>
    /// <param name="values">The buffer that receives the symbol character values.</param>
    /// <returns>The number of symbol character values, five per row.</returns>
    /// <exception cref="ArgumentException">The text carries a character the symbology cannot encode, or more data than sixteen rows hold.</exception>
    private static int Encode(ReadOnlySpan<char> text, Span<byte> tables, Span<int> values)
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

        // The rules of Appendix G depend on the digit run from a position and on which of a control
        // character or a lower case character comes first. Each table holds that for every position, with
        // one entry past the end, and every entry fits a byte with the text bounded above.
        int lookahead = n + 1;
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
            nextControl[p] = OnlyInSetA(c) ? (byte)0 : Saturate(nextControl[p + 1]);
            nextLower[p] = OnlyInSetB(c) ? (byte)0 : Saturate(nextLower[p + 1]);
        }

        // A data character costs at most two symbol characters, a shift and itself, and the symbol adds
        // the starting symbol character and the two check characters; the pad fills the rest of the rows.
        int count = 1;
        int i = 0;
        CodeSet set;
        int mode;

        // Appendix G rule 1: the starting mode, Table 3. Modes 5 and 6 imply a Shift B or a Double Shift B
        // ahead of code set C, which carries the first one or two characters in code set B without
        // spending a symbol character on the shift.
        if (digits[0] >= 2 && (digits[0] & 1) == 0)
        {
            mode = 2;
            set = CodeSet.C;
        }
        else if (digits[0] >= 3)
        {
            mode = 5;
            set = CodeSet.C;
            values[count++] = ValueIn(CodeSet.B, text[i++]);
        }
        else if (n >= 1 && InSetB(text[0]) && digits[1] >= 2 && (digits[1] & 1) == 0)
        {
            mode = 5;
            set = CodeSet.C;
            values[count++] = ValueIn(CodeSet.B, text[i++]);
        }
        else if (n >= 1 && InSetB(text[0]) && digits[1] >= 3)
        {
            mode = 6;
            set = CodeSet.C;
            values[count++] = ValueIn(CodeSet.B, text[i++]);
            values[count++] = ValueIn(CodeSet.B, text[i++]);
        }
        else if (n >= 2 && InSetB(text[0]) && InSetB(text[1]) && digits[2] >= 2 && (digits[2] & 1) == 0)
        {
            mode = 6;
            set = CodeSet.C;
            values[count++] = ValueIn(CodeSet.B, text[i++]);
            values[count++] = ValueIn(CodeSet.B, text[i++]);
        }
        else if (nextControl[0] < nextLower[0])
        {
            mode = 0;
            set = CodeSet.A;
        }
        else
        {
            mode = 1;
            set = CodeSet.B;
        }

        // Rules 3, 4 and 5: the next symbol character from the data that remains, in the current code set.
        while (i < n)
        {
            char c = text[i];
            int run = digits[i];
            if (set == CodeSet.A)
            {
                if (OnlyInSetB(c) && nextControl[i + 1] < nextLower[i + 1])
                {
                    values[count++] = Shift;
                    values[count++] = ValueIn(CodeSet.B, c);
                    i++;
                }
                else if (i + 1 < n && OnlyInSetB(c) && OnlyInSetB(text[i + 1]) && nextControl[i + 2] < nextLower[i + 2])
                {
                    values[count++] = DoubleShift;
                    values[count++] = ValueIn(CodeSet.B, c);
                    values[count++] = ValueIn(CodeSet.B, text[i + 1]);
                    i += 2;
                }
                else if (OnlyInSetB(c))
                {
                    values[count++] = LatchB;
                    set = CodeSet.B;
                }
                else if (run == 4 && i + 4 < n && InSetA(text[i + 4]))
                {
                    values[count++] = DoubleShiftC;
                    count = WritePairs(text, i, 2, values, count);
                    i += 4;
                }
                else if (run == 6 && i + 6 < n && InSetA(text[i + 6]))
                {
                    values[count++] = TripleShiftC;
                    count = WritePairs(text, i, 3, values, count);
                    i += 6;
                }
                else if (run >= 4 && (run & 1) == 0)
                {
                    values[count++] = LatchC;
                    set = CodeSet.C;
                }
                else
                {
                    values[count++] = ValueIn(CodeSet.A, c);
                    i++;
                }
            }
            else if (set == CodeSet.B)
            {
                if (OnlyInSetA(c) && nextLower[i + 1] < nextControl[i + 1])
                {
                    values[count++] = Shift;
                    values[count++] = ValueIn(CodeSet.A, c);
                    i++;
                }
                else if (i + 1 < n && OnlyInSetA(c) && OnlyInSetA(text[i + 1]) && nextLower[i + 2] < nextControl[i + 2])
                {
                    values[count++] = DoubleShift;
                    values[count++] = ValueIn(CodeSet.A, c);
                    values[count++] = ValueIn(CodeSet.A, text[i + 1]);
                    i += 2;
                }
                else if (OnlyInSetA(c))
                {
                    values[count++] = LatchA;
                    set = CodeSet.A;
                }
                else if (run == 4 && i + 4 < n && InSetB(text[i + 4]))
                {
                    values[count++] = DoubleShiftC;
                    count = WritePairs(text, i, 2, values, count);
                    i += 4;
                }
                else if (run == 6 && i + 6 < n && InSetB(text[i + 6]))
                {
                    values[count++] = TripleShiftC;
                    count = WritePairs(text, i, 3, values, count);
                    i += 6;
                }
                else if (run >= 4 && (run & 1) == 0)
                {
                    values[count++] = LatchC;
                    set = CodeSet.C;
                }
                else
                {
                    values[count++] = ValueIn(CodeSet.B, c);
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
                else if (i + 1 < n && InSetB(c) && digits[i + 1] >= 2 && (digits[i + 1] & 1) == 0)
                {
                    values[count++] = CodeSetCShiftB;
                    values[count++] = ValueIn(CodeSet.B, c);
                    i++;
                }
                else if (i + 1 < n && InSetB(c) && digits[i + 1] >= 3)
                {
                    values[count++] = CodeSetCDoubleShiftB;
                    values[count++] = ValueIn(CodeSet.B, c);
                    values[count++] = ValueIn(CodeSet.B, text[i + 1]);
                    i += 2;
                }
                else if (i + 2 < n && InSetB(c) && InSetB(text[i + 1]) && digits[i + 2] >= 2 && (digits[i + 2] & 1) == 0)
                {
                    values[count++] = CodeSetCDoubleShiftB;
                    values[count++] = ValueIn(CodeSet.B, c);
                    values[count++] = ValueIn(CodeSet.B, text[i + 1]);
                    i += 2;
                }
                else if (i + 3 < n && InSetB(c) && InSetB(text[i + 1]) && digits[i + 2] >= 3)
                {
                    values[count++] = CodeSetCTripleShiftB;
                    values[count++] = ValueIn(CodeSet.B, c);
                    values[count++] = ValueIn(CodeSet.B, text[i + 1]);
                    values[count++] = ValueIn(CodeSet.B, text[i + 2]);
                    i += 3;
                }
                else if (i + 3 < n
                    && InSetB(c)
                    && InSetB(text[i + 1])
                    && InSetB(text[i + 2])
                    && digits[i + 3] >= 2
                    && (digits[i + 3] & 1) == 0)
                {
                    values[count++] = CodeSetCTripleShiftB;
                    values[count++] = ValueIn(CodeSet.B, c);
                    values[count++] = ValueIn(CodeSet.B, text[i + 1]);
                    values[count++] = ValueIn(CodeSet.B, text[i + 2]);
                    i += 3;
                }
                else if (nextControl[i] < nextLower[i])
                {
                    values[count++] = LatchA;
                    set = CodeSet.A;
                }
                else
                {
                    values[count++] = LatchB;
                    set = CodeSet.B;
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
        return total;
    }

    /// <summary>
    /// Writes the symbol from its symbol character values: one row of bars per five values, one module
    /// separator bars between the rows, and the top and bottom separator bars of section 2.1.
    /// </summary>
    /// <param name="values">The symbol character values, five per row.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void WriteRows(ReadOnlySpan<int> values, ReadOnlySpan<char> readable, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        int rowCount = values.Length / CharactersPerRow;
        double xDimension = options.XDimension ?? BarcodeSymbology.PointXDimension;
        float barHeight = options.ResolveBarHeight(xDimension, NominalBarHeight);

        // Section 2.1: start, guard bar, five characters and stop, 4 + 1 + 30 + 4 elements, on a bar at both
        // ends. A separator between two rows is one bar across the row width.
        const int rowRuns = 4 + 1 + (CharactersPerRow * 6) + 4;
        int separators = rowCount - 1;
        symbol.BeginStacked(rowCount + separators, (rowCount * rowRuns) + separators, (rowCount * ((rowRuns + 1) >> 1)) + separators, xDimension);
        for (int row = 0; row < rowCount; row++)
        {
            // Table 2 starts every character on a space, which the guard bar leads into.
            Span<int> runs = symbol.AddRow(rowRuns, out Span<float> heights, out _);
            heights.Fill(barHeight);

            // Table 6 starts the rows with the start patterns 0 to 7 in turn.
            int index = 0;
            AppendPattern(StartPatterns[row & 7], 4, runs, ref index);
            runs[index++] = 1;
            for (int k = 0; k < CharactersPerRow; k++)
            {
                int value = values[(row * CharactersPerRow) + k];
                uint pattern = value == TripleShiftC ? TripleShiftPattern : Patterns[value];
                AppendPattern(pattern, 6, runs, ref index);
            }

            AppendPattern(StartPatterns[RowStopValues[row]], 4, runs, ref index);

            if (row < rowCount - 1)
            {
                Span<int> separator = symbol.AddRow(1, out Span<float> separatorHeight, out _);
                separator[0] = RowWidth;
                separatorHeight[0] = SeparatorHeight;
            }
        }

        // Appendix D: the human readable interpretation "may accompany the symbol. It should not
        // interfere with the symbol itself nor the quiet zones."
        float heightInModules = (rowCount * barHeight) + ((rowCount + 1) * SeparatorHeight);
        if (options.Font is not null && readable.Length > 0)
        {
            symbol.ReserveText(1, readable.Length);
            symbol.AddText(readable, 0F, RowWidth, BarcodeTextSide.BelowBars, heightInModules + BarcodeTextPlacement.Clearance);
        }

        symbol.EndStacked(SeparatorHeight, LeadingQuietZone, TrailingQuietZone);
    }

    /// <summary>
    /// Returns the symbol character value of a character in code set A or B: code set A runs from the space
    /// at value 0 through to the control characters, and code set B from the space through to the delete
    /// character.
    /// </summary>
    /// <param name="set">The code set, A or B.</param>
    /// <param name="character">The character.</param>
    /// <returns>The symbol character value.</returns>
    private static int ValueIn(CodeSet set, char character)
        => set == CodeSet.A && character < 32 ? character + 64 : character - 32;

    /// <summary>
    /// Returns whether only code set A carries a character: the control characters 0 to 31.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> when only code set A carries it.</returns>
    private static bool OnlyInSetA(char character) => character < 32;

    /// <summary>
    /// Returns whether only code set B carries a character: the lower case letters and the characters above
    /// them, 96 to 127.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> when only code set B carries it.</returns>
    private static bool OnlyInSetB(char character) => character >= 96;

    /// <summary>
    /// Writes digit pairs as code set C values, each pair the value 0 to 99 it reads as.
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
    /// Writes the element widths of a pattern packed as <see cref="Patterns"/> packs them.
    /// </summary>
    /// <param name="pattern">The pattern, four bits per element, the first element in the highest bits.</param>
    /// <param name="elements">The number of elements.</param>
    /// <param name="runs">The run widths in modules to write.</param>
    /// <param name="index">The position in <paramref name="runs"/>, advanced past the elements.</param>
    private static void AppendPattern(uint pattern, int elements, Span<int> runs, ref int index)
    {
        for (int shift = (elements - 1) * 4; shift >= 0; shift -= 4)
        {
            runs[index++] = (int)((pattern >> shift) & 0xF);
        }
    }

    /// <summary>
    /// Returns whether code set A carries a character: ASCII 0 to 95.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> when code set A carries it.</returns>
    private static bool InSetA(char character) => !OnlyInSetB(character);

    /// <summary>
    /// Returns whether code set B carries a character: ASCII 32 to 127.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> when code set B carries it.</returns>
    private static bool InSetB(char character) => !OnlyInSetA(character);

    /// <summary>
    /// Returns the distance from the position before: one more than the given distance, held at the
    /// maximum, so a run longer than a byte still compares as further away.
    /// </summary>
    /// <param name="distance">The distance from the next position.</param>
    /// <returns>The distance from this position.</returns>
    private static byte Saturate(byte distance) => distance == byte.MaxValue ? distance : (byte)(distance + 1);
}
