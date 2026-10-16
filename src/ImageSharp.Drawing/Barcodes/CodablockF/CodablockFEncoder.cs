// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for Codablock F. A symbol is 2 to 44 rows of Code 128 characters. Every row carries a Start A character, a code set selector, a row indicator,
/// the data columns, a modulo 103 row check character and the stop character. The first row's indicator is the row count less two and every other row's is its
/// index plus 42. The last row gives two of its data columns to the modulo 86 check characters K1 and K2 of the whole message. The row and check values map to
/// code set A and B characters as 64 to 95 for 0 to 31, 0 to 15 for 32 to 47 and 26 to 63 for 48 to 85, and to code set C characters as themselves.
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
    /// The characters a row carries beyond its data columns: the start character, the code set selector, the row indicator, the row check character and the
    /// stop character.
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
    /// The symbol character value of Start A, which starts every row.
    /// </summary>
    private const int StartA = 103;

    /// <summary>
    /// The symbol character value of the stop character, the one character of seven elements.
    /// </summary>
    private const int Stop = 106;

    /// <summary>
    /// The modulus of the Code 128 check character that ends every row.
    /// </summary>
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
    /// The code set that carries the first character, at a position or after it, that only one of code sets A and B carries.
    /// </summary>
    private enum FirstExclusive
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
    /// Gets the element widths of the Code 128 symbol characters the rows carry, packed four bits per element, the first element in the highest bits: six
    /// elements each, three bars and three spaces of one to four modules, except the stop character, <see cref="Stop"/>, which takes seven.
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
        0x211214, 0x211232, 0x2331112,
    ];

    /// <summary>
    /// Returns the nominal row height in modules for a column count: ten, or 0.55 modules per data column plus three when that is more.
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
    /// Encodes text and builds the symbol. The Code 128 character values of the whole symbol run one row after another, each row <paramref name="columns"/>
    /// plus <see cref="Overhead"/> values long. Each row of values becomes one row of Code 128 patterns. A separator between rows keeps the start and stop
    /// patterns and fills the columns between them, a solid bar runs at the top and at the bottom, and the data prints as a text line below the bottom bar when
    /// a font is set. The check characters are not printed.
    /// </summary>
    /// <param name="text">The text to encode. Every character must be ASCII 0 to 127.</param>
    /// <param name="columns">The number of data columns in a row, 4 to 62.</param>
    /// <param name="readablePrefix">The characters of the human readable interpretation before its body.</param>
    /// <param name="readableBody">The body of the human readable interpretation.</param>
    /// <param name="readableSuffix">The characters of the human readable interpretation after its body.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The text carries a character the symbology cannot encode, or more data than 44 rows hold.</exception>
    public static void BuildSymbol(
        ReadOnlySpan<char> text,
        int columns,
        ReadOnlySpan<char> readablePrefix,
        ReadOnlySpan<char> readableBody,
        ReadOnlySpan<char> readableSuffix,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        int n = text.Length;
        if (n > MaximumText)
        {
            throw new ArgumentException($"Codablock F carries at most {MaximumText} characters in 44 rows of 62 columns; the text is longer.", nameof(text));
        }

        int invalid = text.IndexOfAnyExceptInRange('\0', '\u007F');
        if (invalid >= 0)
        {
            throw new ArgumentException(
                $"Codablock F encodes ASCII 0 to 127; {text.CodePointAt(invalid).ToDisplayString()} is outside that range.",
                nameof(text));
        }

        // The value count is known only once the rows are laid out, but the encoder checks the 44 row limit before it starts each row, so the values take room
        // for 44 rows. The lookahead takes one entry, two integers, for every position and one past the end. The text is at most MaximumText characters, so one
        // bounded array, rented once, holds the values and the lookahead.
        int rowSize = columns + Overhead;
        int capacity = MaximumRows * rowSize;
        PooledBuffer<int> buffer = new(capacity + ((n + 1) * 2));
        try
        {
            Span<int> values = buffer.Span[..capacity];
            Span<Lookahead> lookahead = MemoryMarshal.Cast<int, Lookahead>(buffer.Span[capacity..]);
            values = values[..Encode(text, columns, lookahead, values)];

            int rowCount = values.Length / rowSize;
            int width = RowWidth(columns);
            double xDimension = options.XDimension ?? BarcodeSymbology.PointXDimension;
            float barHeight = options.ResolveBarHeight(xDimension, NominalRowHeight(columns));

            // The rows from the top: a solid edge bar, each data row with a separator below it but the last, and a solid edge bar. A separator keeps the start
            // and stop patterns, thirteen runs and seven bars, and an edge bar is one bar across the row width.
            const int separatorRuns = 13;
            int dataRuns = (6 * (rowSize - 1)) + 7;
            int separators = rowCount - 1;
            symbol.BeginStacked(
                (rowCount * 2) + 1,
                2 + (rowCount * dataRuns) + (separators * separatorRuns),
                2 + (rowCount * ((dataRuns + 1) >> 1)) + (separators * ((separatorRuns + 1) >> 1)),
                xDimension);

            // The separator is the Start A pattern and the stop pattern with one bar between them across the columns, which also takes the first bar of the
            // stop pattern: the row width less the 11 modules of Start A and the 11 modules of the stop pattern after its first bar.
            InlineArray16<int> separatorStorage = default;
            Span<int> separatorPattern = ((Span<int>)separatorStorage)[..separatorRuns];
            int separatorIndex = 0;
            AppendPattern(Patterns[StartA], 6, separatorPattern, ref separatorIndex);
            int middle = separatorIndex;
            AppendPattern(Patterns[Stop], 7, separatorPattern, ref separatorIndex);
            separatorPattern[middle] = width - 22;

            AddEdge(width, ref symbol);
            for (int row = 0; row < rowCount; row++)
            {
                Span<int> runs = symbol.AddRow(dataRuns, out Span<float> heights, out _);
                heights.Fill(barHeight);
                WriteRuns(values.Slice(row * rowSize, rowSize), runs);
                if (row < rowCount - 1)
                {
                    Span<int> separator = symbol.AddRow(separatorRuns, out Span<float> separatorHeights, out _);
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
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Encodes text into character values: the rows with their pad, the check characters and the row indicators.
    /// </summary>
    /// <param name="text">The text to encode, ASCII 0 to 127.</param>
    /// <param name="columns">The number of data columns in a row.</param>
    /// <param name="lookahead">The scratch for the lookahead, one entry for every position and one past the end.</param>
    /// <param name="values">The buffer the character values are written to, one row after another, with room for 44 rows.</param>
    /// <returns>The number of character values written.</returns>
    /// <exception cref="ArgumentException">The text needs more than 44 rows.</exception>
    private static int Encode(ReadOnlySpan<char> text, int columns, Span<Lookahead> lookahead, Span<int> values)
    {
        // The row break rules read the digit run from a position and which of a control character or a lower case character comes first. The lookahead holds
        // both for every position, read from the end so every entry builds on the one after it. The entry past the end has no digits and no such character.
        int n = text.Length;
        lookahead[n] = default;
        for (int p = n - 1; p >= 0; p--)
        {
            char c = text[p];
            Lookahead next = lookahead[p + 1];
            lookahead[p] = new Lookahead
            {
                DigitRun = char.IsAsciiDigit(c) ? next.DigitRun + 1 : 0,
                First = OnlyIn(CodeSet.A, c) ? FirstExclusive.SetA : OnlyIn(CodeSet.B, c) ? FirstExclusive.SetB : next.First,
            };
        }

        int rowSize = columns + Overhead;
        int count = 0;
        int i = 0;
        int rows = 1;
        CodeSet set = CodeSet.B;
        bool lastRow = false;
        while (!lastRow)
        {
            if (rows > MaximumRows)
            {
                throw new ArgumentException($"Codablock F carries at most 44 rows of {columns} columns; the text needs more.", nameof(text));
            }

            // Every row starts in the code set of its first characters: C for a digit pair, A when a control character comes before a lower case one, B
            // otherwise. A row past the end of the data starts in B.
            values[count++] = StartA;
            if (lookahead[i].DigitRun >= 2)
            {
                values[count++] = LatchC;
                set = CodeSet.C;
            }
            else if (lookahead[i].First == FirstExclusive.SetA)
            {
                values[count++] = Shift;
                set = CodeSet.A;
            }
            else
            {
                values[count++] = LatchB;
                set = CodeSet.B;
            }

            // The row indicator takes its value once the row count is known.
            values[count++] = 0;

            // Each step writes the next characters while the row has room for them, and the row ends at the first step that does not fit.
            int remaining;
            while (true)
            {
                remaining = columns + 3 - (count % rowSize);
                if (i == n)
                {
                    break;
                }

                char c = text[i];
                int run = Math.Min(lookahead[i].DigitRun, 2 * remaining);
                if (set == CodeSet.C)
                {
                    // Code set C carries the digit pairs, and gives way to code set A or B, whichever carries the first character only one of them carries.
                    if (run >= 2)
                    {
                        count = WritePairs(text, i, 1, values, count);
                        i += 2;
                        continue;
                    }

                    if (remaining < 2)
                    {
                        break;
                    }

                    set = lookahead[i].First == FirstExclusive.SetA ? CodeSet.A : CodeSet.B;
                    values[count++] = set == CodeSet.A ? LatchA : LatchB;
                    values[count++] = ValueIn(set, c);
                    i++;
                    continue;
                }

                // A run of four or more digits moves to code set C: an even run at once, and an odd run after its first digit.
                if (run >= 4)
                {
                    if ((run & 1) == 0 && remaining >= 3)
                    {
                        values[count++] = LatchC;
                        set = CodeSet.C;
                        count = WritePairs(text, i, 2, values, count);
                        i += 4;
                        continue;
                    }

                    if ((run & 1) == 1 && remaining >= 4)
                    {
                        values[count++] = ValueIn(set, c);
                        i++;
                        values[count++] = LatchC;
                        set = CodeSet.C;
                        count = WritePairs(text, i, 2, values, count);
                        i += 4;
                        continue;
                    }
                }

                // Code sets A and B mirror each other: each shifts or latches to the other for a character only the other carries. A shift pays off when the
                // characters after it return to a character only the current set carries first.
                CodeSet other = set == CodeSet.A ? CodeSet.B : CodeSet.A;
                if (OnlyIn(other, c))
                {
                    if (remaining < 2)
                    {
                        break;
                    }

                    FirstExclusive ownFirst = set == CodeSet.A ? FirstExclusive.SetA : FirstExclusive.SetB;
                    if (lookahead[i + 1].First == ownFirst)
                    {
                        values[count++] = Shift;
                    }
                    else
                    {
                        values[count++] = other == CodeSet.A ? LatchA : LatchB;
                        set = other;
                    }

                    values[count++] = ValueIn(other, c);
                    i++;
                    continue;
                }

                if (remaining < 1)
                {
                    break;
                }

                values[count++] = ValueIn(set, c);
                i++;
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

        values[count - 4] = Map(k1, set != CodeSet.C);
        values[count - 3] = Map(k2, set != CodeSet.C);

        // The row indicators: the row count in the first row, the row index from 43 in the rest, each in the code set its row starts in.
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
            values[at + columns + 3] = RowCheckCharacter(values.Slice(at, columns + 3));
        }

        return count;
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
    /// Writes pad characters to the end of a row: Code C in code sets A and B, Code B in code set C, each changing the set for the next.
    /// </summary>
    /// <param name="values">The buffer the character values are written to.</param>
    /// <param name="count">The number of character values written so far.</param>
    /// <param name="pads">The number of pad characters.</param>
    /// <param name="set">The code set in effect, updated as the pads change it.</param>
    /// <returns>The number of character values written after the pad.</returns>
    private static int Pad(Span<int> values, int count, int pads, ref CodeSet set)
    {
        for (int k = 0; k < pads; k++)
        {
            if (set == CodeSet.C)
            {
                values[count++] = LatchB;
                set = CodeSet.B;
            }
            else
            {
                values[count++] = LatchC;
                set = CodeSet.C;
            }
        }

        return count;
    }

    /// <summary>
    /// Maps a row indicator or check value of 0 to 85 to a character value: 64 to 95 for 0 to 31, 0 to 15 for 32 to 47 and 26 to 63 for 48 to 85 in code sets A
    /// and B, and the value itself in code set C.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="alphabetic">Whether the row is in code set A or B rather than C.</param>
    /// <returns>The character value.</returns>
    private static int Map(int value, bool alphabetic)
        => !alphabetic ? value : value switch
        {
            < 32 => value + 64,
            < 48 => value - 32,
            _ => value - 22,
        };

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
    /// Returns whether only one code set of A and B carries a character: the control characters 0 to 31 for code set A, and the lower case letters and the
    /// characters above them, 96 to 127, for code set B.
    /// </summary>
    /// <param name="set">The code set, A or B.</param>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> when only that code set carries it.</returns>
    private static bool OnlyIn(CodeSet set, char character) => set == CodeSet.A ? character < 32 : character >= 96;

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
    /// Writes the run lengths of the symbol character values of one row, starting with a bar.
    /// </summary>
    /// <param name="values">The symbol character values of the row, the start and stop characters included.</param>
    /// <param name="runs">The alternating bar and space run widths in modules to write.</param>
    private static void WriteRuns(ReadOnlySpan<int> values, Span<int> runs)
    {
        int index = 0;
        foreach (int value in values)
        {
            AppendPattern(Patterns[value], value == Stop ? 7 : 6, runs, ref index);
        }
    }

    /// <summary>
    /// Returns the Code 128 check character of a row: the start character weighted by one and each following character by its position, the sum modulo 103.
    /// </summary>
    /// <param name="values">The symbol character values of the row, from the start character.</param>
    /// <returns>The check character value.</returns>
    private static int RowCheckCharacter(ReadOnlySpan<int> values)
    {
        int sum = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            sum += values[i] * i;
        }

        return sum % RowModulus;
    }

    /// <summary>
    /// What the row break rules read ahead from one position of the text. The entry is two integers, so the lookahead shares the rented array of the values.
    /// </summary>
    private struct Lookahead
    {
        /// <summary>
        /// The length of the run of digits that starts at the position.
        /// </summary>
        public int DigitRun;

        /// <summary>
        /// The code set of the first character, at the position or after it, that only one of code sets A and B carries.
        /// </summary>
        public FirstExclusive First;
    }
}
