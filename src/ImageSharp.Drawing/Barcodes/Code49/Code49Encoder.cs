// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for Code 49, AIM USS-49. Section 2.1: a symbol is 2 to 8 rows, and each row is a start pattern, four symbol characters of 16 modules and a stop
/// pattern, 70 modules in all. A symbol character carries two code characters of value 0 to 48, W = 49 C1 + C2, in the parity Table 2 gives its row and column.
/// Every row ends in a row check character, and the last row carries the row count, the mode and the symbol check characters of section 2.1.4.
/// </summary>
internal static class Code49Encoder
{
    /// <summary>
    /// The quiet zone in modules before the start pattern: the "ten modules adjacent to the start pattern" of section 3.4.
    /// </summary>
    public const int LeadingQuietZone = 10;

    /// <summary>
    /// The quiet zone in modules after the stop pattern: the "one module adjacent to the stop pattern" of section 3.4.
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
    public const int MaximumRows = 8;

    /// <summary>
    /// The number of code characters in a row, Table 3.
    /// </summary>
    public const int CodeCharactersPerRow = 8;

    /// <summary>
    /// The nominal bar height in modules: the "minimum bar height of 8X" of section 3.3.
    /// </summary>
    public const float NominalBarHeight = 8F;

    /// <summary>
    /// The height in modules of the separator bars: the "1-module high separator bar" of section 2.1.
    /// </summary>
    public const float SeparatorHeight = 1F;

    /// <summary>
    /// The modulus of the row check character, section 2.1.2.
    /// </summary>
    public const int RowModulus = 49;

    /// <summary>
    /// The modulus of the symbol check characters, Formula 2.
    /// </summary>
    public const int SymbolModulus = 2401;

    /// <summary>
    /// The code character value of Shift 1, Table 6.
    /// </summary>
    public const int ShiftOne = 43;

    /// <summary>
    /// The code character value of Shift 2, Table 6.
    /// </summary>
    public const int ShiftTwo = 44;

    /// <summary>
    /// The code character value of the numeric shift, Table 6, which section 2.2.1 also makes the pad character: "When one or more numeric shifts appear
    /// immediately preceding the symbol check characters, they are pad characters and do not represent data."
    /// </summary>
    public const int NumericShift = 48;

    /// <summary>
    /// The shortest digit run the numeric encodation method of section 2.2.2 compresses: five, the length of one block.
    /// </summary>
    public const int NumericRun = 5;

    /// <summary>
    /// The number of symbol characters in a row, section 2.1.1.
    /// </summary>
    private const int SymbolCharactersPerRow = 4;

    /// <summary>
    /// The number of digits one block of three numeric code characters carries, section 2.2.2.
    /// </summary>
    private const int NumericBlock = 5;

    /// <summary>
    /// The number a four digit remainder is added to, section 2.2.2: "48²C1 + 48C2 + C3 = 100,000 plus the four digits to be represented".
    /// </summary>
    private const int FourDigitOffset = 100000;

    /// <summary>
    /// The longest text a symbol carries: the 81 numeric characters of Table 1, which exceed its 49 alphanumeric ones.
    /// </summary>
    private const int MaximumText = 81;

    /// <summary>
    /// The number of runs in a row: the start bar and space, four bars and four spaces per symbol character, and the stop bar. Every symbol character begins
    /// with a bar and ends with a space, so no two runs merge.
    /// </summary>
    private const int RowRunCount = 2 + (SymbolCharactersPerRow * 8) + 1;

    /// <summary>
    /// The base of the numeric encodation method of section 2.2.2, in which a block of digits converts to code characters of value 0 to 47.
    /// </summary>
    private const int NumericBase = 48;

    /// <summary>
    /// The starting modes of Table 8 that the encoder uses, with their mode values.
    /// </summary>
    private enum StartingMode
    {
        /// <summary>
        /// The data starts in the alphanumeric set with no shift.
        /// </summary>
        Alphanumeric = 0,

        /// <summary>
        /// The data starts with the numeric shift, in the numeric encodation method.
        /// </summary>
        Numeric = 2,

        /// <summary>
        /// The data starts with Shift 1.
        /// </summary>
        ShiftOne = 4,

        /// <summary>
        /// The data starts with Shift 2.
        /// </summary>
        ShiftTwo = 5,
    }

    /// <summary>
    /// Gets the check character weights of Table 5 in the order of the symbol characters. The X weights start at the first entry, the Y weights at the second
    /// and the Z weights at the third; the row count character weighs 20, 16 and 38.
    /// </summary>
    private static ReadOnlySpan<byte> Weights =>
    [
        1, 9, 31, 26, 2, 12, 17, 23, 37, 18, 22, 6, 27, 44, 15, 43, 39, 11, 13, 5, 41, 33, 36, 8, 4, 32, 3, 19, 40, 25, 29, 10, 24, 30
    ];

    /// <summary>
    /// Gets the parity of the four symbol characters of rows 1 to 7, Table 2, one bit per symbol character from the left, a set bit for odd parity. The last
    /// row is always even.
    /// </summary>
    private static ReadOnlySpan<byte> RowParity => [0b1001, 0b0101, 0b1100, 0b0011, 0b1010, 0b0110, 0b1111];

    /// <summary>
    /// Encodes text and builds the symbol.
    /// </summary>
    /// <param name="text">The text to encode. Every character must be ASCII 0 to 127.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The text is empty, carries a character the symbology cannot encode, or holds more data than eight rows
    /// carry.</exception>
    public static void BuildSymbol(ReadOnlySpan<char> text, ReadOnlySpan<char> readable, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        // Eight code characters for each of at most eight rows.
        InlineArray64<int> codeStorage = default;
        int count = Encode(text, codeStorage);
        WriteRows(((ReadOnlySpan<int>)codeStorage)[..count], readable, options, ref symbol);
    }

    /// <summary>
    /// Encodes text into the code characters of the whole symbol, eight per row: the data with its shift characters, the pad, the row check characters, the row
    /// count character and the symbol check characters, laid out as Table 3 does.
    /// </summary>
    /// <param name="text">The text to encode. Every character must be ASCII 0 to 127.</param>
    /// <param name="codes">The buffer that receives the code character values, eight per row for eight rows.</param>
    /// <returns>The number of code character values, eight per row.</returns>
    /// <exception cref="ArgumentException">The text is empty, carries a character the symbology cannot encode, or holds more data than eight rows
    /// carry.</exception>
    public static int Encode(ReadOnlySpan<char> text, Span<int> codes)
    {
        if (text.Length == 0)
        {
            throw new ArgumentException("Code 49 needs at least one character.", nameof(text));
        }

        int invalid = text.IndexOfAnyExceptInRange('\0', '\u007F');
        if (invalid >= 0)
        {
            throw new ArgumentException($"Code 49 encodes ASCII 0 to 127; {text.CodePointAt(invalid).ToDisplayString()} is outside that range.", nameof(text));
        }

        int n = text.Length;
        if (n > MaximumText)
        {
            throw new ArgumentException(
                $"Code 49 carries at most {MaximumText} digits or {Capacity(MaximumRows)} code characters in eight rows; the text is longer.",
                nameof(text));
        }

        // The text is at most MaximumText characters, so the digit runs and the values fit inline.
        InlineArray128<byte> digitStorage = default;
        Span<byte> digits = ((Span<byte>)digitStorage)[..(MaximumText + 1)];
        digits[n] = 0;
        for (int p = n - 1; p >= 0; p--)
        {
            digits[p] = (byte)(char.IsAsciiDigit(text[p]) ? digits[p + 1] + 1 : 0);
        }

        // A character costs at most a shift and itself, and a numeric shift opens and closes each run.
        InlineArray256<int> valueStorage = default;
        Span<int> values = ((Span<int>)valueStorage)[..((MaximumText * 2) + 2)];
        int count = 0;
        int i = 0;
        bool numeric = false;
        while (i < n)
        {
            int run = digits[i];
            if (!numeric)
            {
                if (run >= NumericRun)
                {
                    values[count++] = NumericShift;
                    numeric = true;
                    continue;
                }

                int value = CodeCharacter(text[i], out int shift);
                if (shift != 0)
                {
                    values[count++] = shift;
                }

                values[count++] = value;
                i++;
            }
            else
            {
                if (run < NumericRun)
                {
                    values[count++] = NumericShift;
                    numeric = false;
                    continue;
                }

                count = WriteNumeric(text.Slice(i, run), values, count);
                i += run;
            }
        }

        // Table 8: a leading numeric shift or shift is the starting mode rather than a code character.
        StartingMode mode = values[0] switch
        {
            NumericShift => StartingMode.Numeric,
            ShiftOne => StartingMode.ShiftOne,
            ShiftTwo => StartingMode.ShiftTwo,
            _ => StartingMode.Alphanumeric,
        };

        ReadOnlySpan<int> data = values[(mode == StartingMode.Alphanumeric ? 0 : 1)..count];
        int dataCount = data.Length;

        // Section 2.1.4 and Table 4: a symbol of up to six rows has two symbol check characters and one of seven or eight rows has three, so the last row holds
        // two data code characters in the first case and none in the second. Every other row holds seven and its row check character.
        int rows = MinimumRows;
        while (rows <= MaximumRows && Capacity(rows) < dataCount)
        {
            rows++;
        }

        if (rows > MaximumRows)
        {
            throw new ArgumentException(
                $"Code 49 carries at most {Capacity(MaximumRows)} code characters in eight rows; the text needs {dataCount}.",
                nameof(text));
        }

        // The data fills the data positions of the rows in order, and the numeric shift pads the positions it leaves.
        codes = codes[..(rows * CodeCharactersPerRow)];
        for (int row = 0; row < rows; row++)
        {
            int perRow = row < rows - 1 ? 7 : (rows <= 6 ? 2 : 0);
            Span<int> slots = codes.Slice(row * CodeCharactersPerRow, perRow);
            int taken = Math.Min(perRow, data.Length);
            data[..taken].CopyTo(slots);
            slots[taken..].Fill(NumericShift);
            data = data[taken..];
        }

        // Section 2.1.2: the last code character of every row is the modulo 49 sum of the other seven.
        for (int row = 0; row < rows - 1; row++)
        {
            Span<int> rowCodes = codes.Slice(row * CodeCharactersPerRow, CodeCharactersPerRow);
            rowCodes[7] = RowCheckCharacter(rowCodes);
        }

        // Section 2.1.3: Cr7 = 7(r - 2) + M.
        int last = (rows - 1) * CodeCharactersPerRow;
        int rowCount = (7 * (rows - MinimumRows)) + (int)mode;
        codes[last + 6] = rowCount;

        // Formula 2 with the weights of Table 5: the symbol characters of every row but the last, in order, then Wr1 for the Y and X sums and Wr2 for the X
        // sum.
        ReadOnlySpan<byte> weights = Weights;
        long x = 20L * rowCount;
        long y = 16L * rowCount;
        long z = 38L * rowCount;
        int position = 0;
        for (int row = 0; row < rows - 1; row++)
        {
            for (int j = 0; j < SymbolCharactersPerRow; j++)
            {
                int w = SymbolCharacter(codes, (row * CodeCharactersPerRow) + (j * 2));
                x += weights[position] * w;
                y += weights[position + 1] * w;
                z += weights[position + 2] * w;
                position++;
            }
        }

        if (rows > 6)
        {
            int wr1 = (int)(z % SymbolModulus);
            codes[last] = wr1 / RowModulus;
            codes[last + 1] = wr1 % RowModulus;
        }

        int first1 = SymbolCharacter(codes, last);
        x += weights[position] * first1;
        y += weights[position + 1] * first1;
        position++;

        int wr2 = (int)(y % SymbolModulus);
        codes[last + 2] = wr2 / RowModulus;
        codes[last + 3] = wr2 % RowModulus;
        x += weights[position] * wr2;

        int wr3 = (int)(x % SymbolModulus);
        codes[last + 4] = wr3 / RowModulus;
        codes[last + 5] = wr3 % RowModulus;

        codes[last + 7] = RowCheckCharacter(codes[last..]);
        return codes.Length;
    }

    /// <summary>
    /// Writes the symbol from its code characters: one row of bars per eight, one module separator bars between the rows, and the top and bottom separator bars
    /// of section 2.1.
    /// </summary>
    /// <param name="codes">The code character values, eight per row.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void WriteRows(ReadOnlySpan<int> codes, ReadOnlySpan<char> readable, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        int rowCount = codes.Length / CodeCharactersPerRow;
        double xDimension = options.XDimension ?? BarcodeSymbology.PointXDimension;
        float barHeight = options.ResolveBarHeight(xDimension, NominalBarHeight);
        ReadOnlySpan<byte> parities = RowParity;
        ReadOnlySpan<ushort> even = Code49Patterns.Even;
        ReadOnlySpan<ushort> odd = Code49Patterns.Odd;

        // A separator between two rows is one bar across the row width.
        int separators = rowCount - 1;
        symbol.BeginStacked(rowCount + separators, (rowCount * RowRunCount) + separators, (rowCount * ((RowRunCount + 1) >> 1)) + separators, xDimension);
        for (int row = 0; row < rowCount; row++)
        {
            // Section 2.1.5: the start pattern is a one module bar and a one module space, and the stop pattern a four module bar. Every symbol character
            // begins with a bar and ends with a space, so the row is one run of bits from the start bar to the stop bar.
            int parity = row < rowCount - 1 ? parities[row] : 0;
            Span<int> runs = symbol.AddRow(RowRunCount, out Span<float> heights, out _);
            heights.Fill(barHeight);
            runs[0] = 1;
            runs[1] = 1;
            int length = 2;
            bool bar = false;
            for (int j = 0; j < SymbolCharactersPerRow; j++)
            {
                int w = SymbolCharacter(codes, (row * CodeCharactersPerRow) + (j * 2));
                bool isOdd = ((parity >> (SymbolCharactersPerRow - 1 - j)) & 1) == 1;
                ushort bits = isOdd ? odd[w] : even[w];
                for (int shift = 15; shift >= 0; shift--)
                {
                    bool set = ((bits >> shift) & 1) == 1;
                    if (set == bar)
                    {
                        runs[length - 1]++;
                    }
                    else
                    {
                        runs[length++] = 1;
                        bar = set;
                    }
                }
            }

            runs[length] = 4;
            if (row < rowCount - 1)
            {
                Span<int> separator = symbol.AddRow(1, out Span<float> separatorHeight, out _);
                separator[0] = RowWidth;
                separatorHeight[0] = SeparatorHeight;
            }
        }

        // Appendix C: the human readable interpretation "may accompany the symbol. It should not interfere with the symbol itself nor the quiet zones."
        float heightInModules = (rowCount * barHeight) + ((rowCount + 1) * SeparatorHeight);
        if (options.Font is not null && readable.Length > 0)
        {
            symbol.ReserveText(1, readable.Length);
            symbol.AddText(readable, 0F, RowWidth, BarcodeTextSide.BelowBars, heightInModules + BarcodeTextPlacement.Clearance);
        }

        symbol.EndStacked(SeparatorHeight, LeadingQuietZone, TrailingQuietZone);
    }

    /// <summary>
    /// Returns the number of data code characters a symbol of the given row count carries: seven per row before the last, and two in the last row of a symbol
    /// with up to six rows, Table 4.
    /// </summary>
    /// <param name="rows">The number of rows.</param>
    /// <returns>The number of data code characters.</returns>
    private static int Capacity(int rows) => (7 * (rows - 1)) + (rows <= 6 ? 2 : 0);

    /// <summary>
    /// Returns the row check character of a row, section 2.1.2: the modulo 49 sum of its first seven code characters.
    /// </summary>
    /// <param name="row">The code characters of the row, from its first.</param>
    /// <returns>The row check character.</returns>
    private static int RowCheckCharacter(ReadOnlySpan<int> row)
    {
        int sum = 0;
        foreach (int code in row[..7])
        {
            sum += code;
        }

        return sum % RowModulus;
    }

    /// <summary>
    /// Returns the value of the symbol character at a code character position, Formula 1: W = 49 C1 + C2.
    /// </summary>
    /// <param name="codes">The code characters.</param>
    /// <param name="index">The index of the first code character of the pair.</param>
    /// <returns>The symbol character value.</returns>
    private static int SymbolCharacter(ReadOnlySpan<int> codes, int index)
        => (codes[index] * RowModulus) + codes[index + 1];

    /// <summary>
    /// Writes a run of digits by the numeric encodation method of section 2.2.2: three code characters per five digits, then one for a remainder of one, two
    /// for three, three for four with 100,000 added, and a run whose remainder is two ends in four digits then three.
    /// </summary>
    /// <param name="digits">The digit run.</param>
    /// <param name="values">The buffer the code characters are written to.</param>
    /// <param name="count">The number of values written so far.</param>
    /// <returns>The number of values written after the run.</returns>
    private static int WriteNumeric(ReadOnlySpan<char> digits, Span<int> values, int count)
    {
        int remainder = digits.Length % NumericBlock;
        int blocks = remainder == 2 ? digits.Length - 7 : digits.Length - remainder;
        int at = 0;
        for (; at < blocks; at += NumericBlock)
        {
            count = WriteBase48(Number(digits.Slice(at, NumericBlock)), 3, values, count);
        }

        switch (digits.Length - at)
        {
            case 1:
                values[count++] = digits[at] - '0';
                break;
            case 3:
                count = WriteBase48(Number(digits.Slice(at, 3)), 2, values, count);
                break;
            case 4:
                count = WriteBase48(FourDigitOffset + Number(digits.Slice(at, 4)), 3, values, count);
                break;
            case 7:
                count = WriteBase48(FourDigitOffset + Number(digits.Slice(at, 4)), 3, values, count);
                count = WriteBase48(Number(digits.Slice(at + 4, 3)), 2, values, count);
                break;
        }

        return count;
    }

    /// <summary>
    /// Writes a number as code characters in base 48, most significant first.
    /// </summary>
    /// <param name="number">The number, less than 48 to the power of <paramref name="characters"/>.</param>
    /// <param name="characters">The number of code characters.</param>
    /// <param name="values">The buffer the code characters are written to.</param>
    /// <param name="count">The number of values written so far.</param>
    /// <returns>The number of values written after the number.</returns>
    private static int WriteBase48(int number, int characters, Span<int> values, int count)
    {
        int divisor = 1;
        for (int k = 1; k < characters; k++)
        {
            divisor *= NumericBase;
        }

        for (; divisor > 0; divisor /= NumericBase)
        {
            values[count++] = number / divisor;
            number %= divisor;
        }

        return count;
    }

    /// <summary>
    /// Returns the value of a digit string.
    /// </summary>
    /// <param name="digits">The digits.</param>
    /// <returns>The value.</returns>
    private static int Number(ReadOnlySpan<char> digits)
    {
        int value = 0;
        foreach (char digit in digits)
        {
            value = (value * 10) + (digit - '0');
        }

        return value;
    }

    /// <summary>
    /// Returns the code character of an ASCII character by Table 7, with the Shift 1 or Shift 2 that precedes it when the character is outside the alphanumeric
    /// set of Table 6.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <param name="shift">The shift code character that precedes the value, or zero for none.</param>
    /// <returns>The code character value from Table 6.</returns>
    private static int CodeCharacter(char c, out int shift)
    {
        shift = 0;
        if (c < 32)
        {
            shift = ShiftOne;
            if (c == 0)
            {
                return 38;
            }

            return c <= 26 ? 9 + c : c - 26;
        }

        if (char.IsAsciiDigit(c))
        {
            return c - '0';
        }

        if (char.IsAsciiLetterUpper(c))
        {
            return 10 + (c - 'A');
        }

        if (char.IsAsciiLetterLower(c))
        {
            shift = ShiftTwo;
            return 10 + (c - 'a');
        }

        switch (c)
        {
            case ' ':
                return 38;
            case '$':
                return 39;
            case '%':
                return 42;
            case '+':
                return 41;
            case '-':
                return 36;
            case '.':
                return 37;
            case '/':
                return 40;
        }

        shift = c < 59 ? ShiftOne : ShiftTwo;
        return c switch
        {
            '!' => 6,
            '"' => 7,
            '#' => 8,
            '&' => 9,
            '\'' => 0,
            '(' => 36,
            ')' => 37,
            '*' => 39,
            ',' => 40,
            ':' => 41,
            ';' => 1,
            '<' => 2,
            '=' => 3,
            '>' => 4,
            '?' => 5,
            '@' => 6,
            '[' => 7,
            '\\' => 8,
            ']' => 9,
            '^' => 0,
            '_' => 36,
            '`' => 37,
            '{' => 39,
            '|' => 40,
            '}' => 41,
            '~' => 42,
            _ => 38,
        };
    }
}
