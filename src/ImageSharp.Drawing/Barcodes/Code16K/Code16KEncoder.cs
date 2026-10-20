// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for Code 16K, AIM USS-16K. Section 2.1: a symbol is 2 to 16 rows, and each row is a start character, a one module guard bar, five symbol
/// characters and a stop character, 70 modules in all. The symbol characters are ordered from the first row down. The first is the starting symbol character
/// and the last two are the check characters. Section 2.2: a symbol character is the eleven module Code 128 pattern of Table 2, three spaces alternating with
/// three bars, starting with a space.
/// </summary>
internal static class Code16KEncoder
{
    /// <summary>
    /// The quiet zone in modules before the start character: the "ten modules adjacent to the start pattern" of section 3.4.
    /// </summary>
    public const int LeadingQuietZone = 10;

    /// <summary>
    /// The quiet zone in modules after the stop character: the "one module adjacent to the stop pattern" of section 3.4.
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
    /// The bar height of a row in modules, nominal and smallest. Section 4.5 d) of EN 12323: "row height (Y dimension): minimum 8X". The section gives no
    /// largest row height.
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
    /// The pad character, section 2.3.5: "When it appears at the end of the symbol characters, just before the check characters, it represents no data."
    /// </summary>
    public const int Pad = 103;

    /// <summary>
    /// The double shift of code sets A and B, Double Shift B in code set A and Double Shift A in code set B, Table 2. Code sets A and B take the values 98 to
    /// 101 of Code 128: Shift, Code C, Code B and Code A.
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
    /// The pattern of the value 106, the one symbol character Table 2 defines beyond the Code 128 set, packed as <see cref="Patterns"/> packs its patterns.
    /// </summary>
    private const uint TripleShiftPattern = 0x211133;

    /// <summary>
    /// The symbol characters a symbol carries beyond its data: the starting symbol character and the two check characters.
    /// </summary>
    private const int Overhead = 3;

    /// <summary>
    /// The longest text a symbol carries: two digits per symbol character in code set C over the data symbol characters of sixteen rows.
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
    /// The starting modes of Table 3 that the encoder uses, with their mode values.
    /// </summary>
    private enum StartingMode
    {
        /// <summary>
        /// The symbol starts in code set A.
        /// </summary>
        CodeSetA = 0,

        /// <summary>
        /// The symbol starts in code set B.
        /// </summary>
        CodeSetB = 1,

        /// <summary>
        /// The symbol starts in code set C.
        /// </summary>
        CodeSetC = 2,

        /// <summary>
        /// The symbol starts in code set C with an implied Shift B, so the first character is in code set B.
        /// </summary>
        CodeSetCShiftB = 5,

        /// <summary>
        /// The symbol starts in code set C with an implied Double Shift B, so the first two characters are in code set B.
        /// </summary>
        CodeSetCDoubleShiftB = 6,
    }

    /// <summary>
    /// The code set that carries the first character, at a position or after it, that only one of code sets A and B carries.
    /// </summary>
    private enum FirstExclusive : byte
    {
        /// <summary>
        /// No character from the position on is carried by only one of code sets A and B.
        /// </summary>
        None,

        /// <summary>
        /// A control character, which only code set A carries, comes first.
        /// </summary>
        SetA,

        /// <summary>
        /// A lower case character, which only code set B carries, comes first.
        /// </summary>
        SetB,
    }

    /// <summary>
    /// Gets the element widths of the symbol characters 0 to 105, the Code 128 patterns of Table 2, packed four bits per element, the first element in the
    /// highest bits: six elements each, three spaces and three bars of one to four modules.
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
    /// Gets the eight start patterns of Table 4, bar, space, bar and space, one nibble each. Table 5 gives the stop patterns the same widths with the bars and
    /// spaces exchanged.
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

        int invalid = text.IndexOfAnyExceptInRange('\0', '\u007F');
        if (invalid >= 0)
        {
            throw new ArgumentException($"Code 16K encodes ASCII 0 to 127; {text.CodePointAt(invalid).ToDisplayString()} is outside that range.", nameof(text));
        }

        // The lookahead takes one entry for every position and one past the end, which the text bound keeps inline. A data character costs at most two symbol
        // characters, and the symbol adds three, so the values take twice the text and three more, and at least the sixteen rows of the largest symbol. A short
        // text keeps the values inline and a longer one rents them once.
        InlineArray256<Lookahead> lookaheadStorage = default;
        Span<Lookahead> lookahead = ((Span<Lookahead>)lookaheadStorage)[..(text.Length + 1)];
        int valueLength = Math.Max((text.Length * 2) + Overhead, MaximumRows * CharactersPerRow);
        InlineArray128<int> valueStorage = default;
        PooledBuffer<int> rented = default;
        Span<int> values = valueLength <= 128
            ? ((Span<int>)valueStorage)[..valueLength]
            : (rented = new PooledBuffer<int>(valueLength)).Span;

        try
        {
            int total = Encode(text, lookahead, values);
            WriteRows(values[..total], readable, options, ref symbol);
        }
        finally
        {
            rented.Dispose();
        }
    }

    /// <summary>
    /// Encodes text into the symbol character values of the whole symbol: the starting symbol character, the data with its code and shift characters, the pad
    /// characters and the two check characters, five per row. Appendix G chooses the starting mode and the code and shift characters.
    /// </summary>
    /// <param name="text">The text to encode, ASCII 0 to 127 and at most <see cref="MaximumText"/> characters.</param>
    /// <param name="lookahead">The scratch for the lookahead, one entry for every position and one past the end.</param>
    /// <param name="values">The buffer that receives the symbol character values.</param>
    /// <returns>The number of symbol character values, five per row.</returns>
    /// <exception cref="ArgumentException">The text holds more data than sixteen rows hold.</exception>
    private static int Encode(ReadOnlySpan<char> text, Span<Lookahead> lookahead, Span<int> values)
    {
        int capacity = (MaximumRows * CharactersPerRow) - Overhead;
        int n = text.Length;

        // The rules of Appendix G depend on the digit run from a position and on which of a control character or a lower case character comes first. The
        // lookahead holds both for every position, read from the end so every entry builds on the one after it. A digit run fits a byte with the text bounded
        // above.
        lookahead[n] = default;
        for (int p = n - 1; p >= 0; p--)
        {
            char c = text[p];
            Lookahead next = lookahead[p + 1];
            lookahead[p] = new Lookahead
            {
                DigitRun = (byte)(char.IsAsciiDigit(c) ? next.DigitRun + 1 : 0),
                First = OnlyIn(CodeSet.A, c) ? FirstExclusive.SetA : OnlyIn(CodeSet.B, c) ? FirstExclusive.SetB : next.First,
            };
        }

        // The data starts at value 1. Value 0 is the starting symbol character, which needs the row count, so it is written after the data.
        int count = 1;
        int i = 0;
        CodeSet set;
        StartingMode mode;

        // Appendix G rule 1: the starting mode, Table 3. The modes with an implied Shift B or Double Shift B start in code set C, which carries the first one
        // or two characters in code set B without spending a symbol character on the shift.
        int firstRun = lookahead[0].DigitRun;
        if (IsEvenRun(firstRun))
        {
            mode = StartingMode.CodeSetC;
            set = CodeSet.C;
        }
        else if (firstRun >= 3 || (n >= 1 && In(CodeSet.B, text[0]) && IsEvenRun(lookahead[1].DigitRun)))
        {
            mode = StartingMode.CodeSetCShiftB;
            set = CodeSet.C;
            values[count++] = ValueIn(CodeSet.B, text[i++]);
        }
        else if ((n >= 1 && In(CodeSet.B, text[0]) && lookahead[1].DigitRun >= 3)
            || (n >= 2 && In(CodeSet.B, text[0]) && In(CodeSet.B, text[1]) && IsEvenRun(lookahead[2].DigitRun)))
        {
            mode = StartingMode.CodeSetCDoubleShiftB;
            set = CodeSet.C;
            values[count++] = ValueIn(CodeSet.B, text[i++]);
            values[count++] = ValueIn(CodeSet.B, text[i++]);
        }
        else if (lookahead[0].First == FirstExclusive.SetA)
        {
            mode = StartingMode.CodeSetA;
            set = CodeSet.A;
        }
        else
        {
            mode = StartingMode.CodeSetB;
            set = CodeSet.B;
        }

        // Rules 3, 4 and 5: the next symbol character from the data that remains, in the current code set.
        while (i < n)
        {
            char c = text[i];
            int run = lookahead[i].DigitRun;
            if (set == CodeSet.C)
            {
                if (run >= 2)
                {
                    count = WritePairs(text, i, 1, values, count);
                    i += 2;
                }
                else if (i + 1 < n && In(CodeSet.B, c) && IsEvenRun(lookahead[i + 1].DigitRun))
                {
                    values[count++] = CodeSetCShiftB;
                    values[count++] = ValueIn(CodeSet.B, c);
                    i++;
                }
                else if ((i + 1 < n && In(CodeSet.B, c) && lookahead[i + 1].DigitRun >= 3)
                    || (i + 2 < n && In(CodeSet.B, c) && In(CodeSet.B, text[i + 1]) && IsEvenRun(lookahead[i + 2].DigitRun)))
                {
                    values[count++] = CodeSetCDoubleShiftB;
                    values[count++] = ValueIn(CodeSet.B, c);
                    values[count++] = ValueIn(CodeSet.B, text[i + 1]);
                    i += 2;
                }
                else if (i + 3 < n
                    && In(CodeSet.B, c)
                    && In(CodeSet.B, text[i + 1])
                    && (lookahead[i + 2].DigitRun >= 3 || (In(CodeSet.B, text[i + 2]) && IsEvenRun(lookahead[i + 3].DigitRun))))
                {
                    values[count++] = CodeSetCTripleShiftB;
                    values[count++] = ValueIn(CodeSet.B, c);
                    values[count++] = ValueIn(CodeSet.B, text[i + 1]);
                    values[count++] = ValueIn(CodeSet.B, text[i + 2]);
                    i += 3;
                }
                else if (lookahead[i].First == FirstExclusive.SetA)
                {
                    values[count++] = LatchA;
                    set = CodeSet.A;
                }
                else
                {
                    values[count++] = LatchB;
                    set = CodeSet.B;
                }

                continue;
            }

            // Code sets A and B mirror each other: each shifts or latches to the other for the characters only the other carries. A shift pays off when the
            // characters after it return to a character only the current set carries first.
            CodeSet other = set == CodeSet.A ? CodeSet.B : CodeSet.A;
            FirstExclusive ownFirst = set == CodeSet.A ? FirstExclusive.SetA : FirstExclusive.SetB;
            if (OnlyIn(other, c) && lookahead[i + 1].First == ownFirst)
            {
                values[count++] = Shift;
                values[count++] = ValueIn(other, c);
                i++;
            }
            else if (i + 1 < n && OnlyIn(other, c) && OnlyIn(other, text[i + 1]) && lookahead[i + 2].First == ownFirst)
            {
                values[count++] = DoubleShift;
                values[count++] = ValueIn(other, c);
                values[count++] = ValueIn(other, text[i + 1]);
                i += 2;
            }
            else if (OnlyIn(other, c))
            {
                values[count++] = other == CodeSet.A ? LatchA : LatchB;
                set = other;
            }
            else if (run == 4 && i + 4 < n && In(set, text[i + 4]))
            {
                values[count++] = DoubleShiftC;
                count = WritePairs(text, i, 2, values, count);
                i += 4;
            }
            else if (run == 6 && i + 6 < n && In(set, text[i + 6]))
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
                values[count++] = ValueIn(set, c);
                i++;
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
        values[count..(total - 2)].Fill(Pad);

        // Section 2.3.1: s = 7(r - 2) + m.
        values[0] = (7 * (rows - MinimumRows)) + (int)mode;

        // Section 2.6: C1 weights the symbol characters from two upward, C2 from one upward and takes C1 as its last term.
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
    /// Writes the symbol from its symbol character values: one row of bars per five values, one module separator bars between the rows, and the top and bottom
    /// separator bars of section 2.1.
    /// </summary>
    /// <param name="values">The symbol character values, five per row.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void WriteRows(ReadOnlySpan<int> values, ReadOnlySpan<char> readable, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        int rowCount = values.Length / CharactersPerRow;

        // Section 4.5 a) of EN 12323: the X dimension "should be defined by the application specification", so the symbology gives no limit.
        double xDimension = options.ResolveXDimension(0, BarcodeSymbology.DefaultXDimension, double.PositiveInfinity);
        float barHeight = options.ResolveBarHeight(xDimension, NominalBarHeight, NominalBarHeight, float.PositiveInfinity);

        // Section 2.1: start, guard bar, five characters and stop, 4 + 1 + 30 + 4 elements, on a bar at both ends. A separator between two rows is one bar
        // across the row width.
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
            foreach (int value in values.Slice(row * CharactersPerRow, CharactersPerRow))
            {
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

        // Appendix D: the human readable interpretation "may accompany the symbol. It should not interfere with the symbol itself nor the quiet zones."
        float heightInModules = (rowCount * barHeight) + ((rowCount + 1) * SeparatorHeight);
        if (options.Font is not null && readable.Length > 0)
        {
            symbol.ReserveText(1, readable.Length);
            symbol.AddText(readable, 0F, RowWidth, BarcodeTextSide.BelowBars, heightInModules + BarcodeTextPlacement.Clearance);
        }

        symbol.EndStacked(SeparatorHeight, LeadingQuietZone, TrailingQuietZone);
    }

    /// <summary>
    /// Returns the symbol character value of a character in code set A or B: code set A runs from the space at value 0 through to the control characters, and
    /// code set B from the space through to the delete character.
    /// </summary>
    /// <param name="set">The code set, A or B.</param>
    /// <param name="character">The character.</param>
    /// <returns>The symbol character value.</returns>
    private static int ValueIn(CodeSet set, char character)
        => set == CodeSet.A && character < 32 ? character + 64 : character - 32;

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
    /// Returns whether only one code set of A and B carries a character: the control characters 0 to 31 for code set A, and the lower case letters and the
    /// characters above them, 96 to 127, for code set B.
    /// </summary>
    /// <param name="set">The code set, A or B.</param>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> when only that code set carries it.</returns>
    private static bool OnlyIn(CodeSet set, char character) => set == CodeSet.A ? character < 32 : character >= 96;

    /// <summary>
    /// Returns whether a code set of A and B carries a character: ASCII 0 to 95 for code set A, and ASCII 32 to 127 for code set B.
    /// </summary>
    /// <param name="set">The code set, A or B.</param>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> when the code set carries it.</returns>
    private static bool In(CodeSet set, char character) => set == CodeSet.A ? character < 96 : character >= 32;

    /// <summary>
    /// Returns whether a digit run is an even run of at least two digits, which code set C carries as whole pairs.
    /// </summary>
    /// <param name="run">The length of the digit run.</param>
    /// <returns><see langword="true"/> when the run is even and at least two long.</returns>
    private static bool IsEvenRun(int run) => run >= 2 && (run & 1) == 0;

    /// <summary>
    /// What the encodation rules read ahead from one position of the text.
    /// </summary>
    private struct Lookahead
    {
        /// <summary>
        /// The length of the run of digits that starts at the position.
        /// </summary>
        public byte DigitRun;

        /// <summary>
        /// The code set of the first character, at the position or after it, that only one of code sets A and B carries.
        /// </summary>
        public FirstExclusive First;
    }
}
