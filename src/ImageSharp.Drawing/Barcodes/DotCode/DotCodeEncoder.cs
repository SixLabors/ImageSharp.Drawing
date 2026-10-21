// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodes DotCode symbols: a grid of dots with no finder pattern, in which only the cells whose column and row sum to an even number can be dark. The data is
/// codewords 0 to 112 in code sets A, B and C, a binary mode and upper shifts. The codewords take a mask and Reed-Solomon error correction over the prime field
/// of 113, and each is placed as a pattern of five dark dots in nine. The mask that leaves the most legible edges and the fewest voids wins.
/// </summary>
internal static class DotCodeEncoder
{
    /// <summary>
    /// The longest message, in characters and FNC1 characters.
    /// </summary>
    public const int MaximumLength = 2000;

    /// <summary>
    /// The fewest columns or rows of a symbol.
    /// </summary>
    public const int MinimumSide = 5;

    /// <summary>
    /// The most columns or rows of a symbol whose size is given.
    /// </summary>
    public const int MaximumSide = 200;

    /// <summary>
    /// The quiet zone on each of the four sides in modules.
    /// </summary>
    private const int QuietZone = 3;

    /// <summary>
    /// The message value of an FNC1 character. Characters are 0 to 255, so a negative value cannot collide.
    /// </summary>
    private const int Fnc1 = -1;

    /// <summary>
    /// The prime modulus of the Reed-Solomon field and the number of codeword values.
    /// </summary>
    private const int Modulus = 113;

    /// <summary>
    /// The width to height ratio of a symbol whose size is not given.
    /// </summary>
    private const double DefaultRatio = 3D / 2D;

    /// <summary>
    /// The score that disqualifies a symbol with an edge that has no dark dot.
    /// </summary>
    private const double UnlitEdgeScore = -99999;

    /// <summary>
    /// The score a symbol with lit corners must exceed to replace the best symbol of the first pass.
    /// </summary>
    private const double RetryFloor = -99999999;

    /// <summary>
    /// The FNC1 codeword. It has this value in code sets A, B and C.
    /// </summary>
    private const int Fnc1Codeword = 107;

    /// <summary>
    /// The upper shift A codeword. It has this value in code sets A, B and C.
    /// </summary>
    private const int UpperShiftA = 110;

    /// <summary>
    /// The upper shift B codeword. It has this value in code sets A, B and C.
    /// </summary>
    private const int UpperShiftB = 111;

    /// <summary>
    /// The codeword that latches to binary mode. It has this value in code sets A, B and C.
    /// </summary>
    private const int BinaryLatch = 112;

    /// <summary>
    /// The codeword of code sets A and B that shifts to code set C for two digit pairs. The shifts for three and four digit pairs follow it.
    /// </summary>
    private const int ShiftC2 = 103;

    /// <summary>
    /// The codeword of code sets A and B that latches to code set C.
    /// </summary>
    private const int LatchC = 106;

    /// <summary>
    /// The codeword of code set A that shifts to code set B for one character. The shifts for two to six characters follow it.
    /// </summary>
    private const int AShiftB1 = 96;

    /// <summary>
    /// The codeword of code set A that latches to code set B.
    /// </summary>
    private const int ALatchB = 102;

    /// <summary>
    /// The codeword of code set B that shifts to code set A.
    /// </summary>
    private const int BShiftA = 101;

    /// <summary>
    /// The codeword of code set B that latches to code set A.
    /// </summary>
    private const int BLatchA = 102;

    /// <summary>
    /// The codeword of code set B for the CR LF pair.
    /// </summary>
    private const int BCrLf = 96;

    /// <summary>
    /// The codeword of code set C for the 17xxxxxx10 macro.
    /// </summary>
    private const int CSeventeenTen = 100;

    /// <summary>
    /// The codeword of code set C that latches to code set A.
    /// </summary>
    private const int CLatchA = 101;

    /// <summary>
    /// The codeword of code set C that shifts to code set B for one character. The shifts for two to four characters follow it.
    /// </summary>
    private const int CShiftB1 = 102;

    /// <summary>
    /// The codeword of code set C that latches to code set B.
    /// </summary>
    private const int CLatchB = 106;

    /// <summary>
    /// The codeword of binary mode that shifts to code set C for two digit pairs. The shifts for three to seven digit pairs follow it.
    /// </summary>
    private const int BinaryShiftC2 = 103;

    /// <summary>
    /// The codeword of binary mode that terminates to code set A.
    /// </summary>
    private const int BinaryTerminateA = 109;

    /// <summary>
    /// The codeword of binary mode that terminates to code set B.
    /// </summary>
    private const int BinaryTerminateB = 110;

    /// <summary>
    /// The codeword of binary mode that terminates to code set C.
    /// </summary>
    private const int BinaryTerminateC = 111;

    /// <summary>
    /// The first codeword of code set B of a message that starts with the macro header [)&gt;RS05GS.
    /// </summary>
    private const int Macro05 = 97;

    /// <summary>
    /// The first codeword of code set B of a message that starts with the macro header [)&gt;RS06GS.
    /// </summary>
    private const int Macro06 = 98;

    /// <summary>
    /// The first codeword of code set B of a message that starts with the macro header [)&gt;RS12GS.
    /// </summary>
    private const int Macro12 = 99;

    /// <summary>
    /// The first codeword of code set B of a message that starts with the macro header [)&gt;RS and any two other digits.
    /// </summary>
    private const int MacroAny = 100;

    /// <summary>
    /// The pad codeword. In code sets A and B it latches to code set C, and in code set C it latches to code set B, so a run of them carries nothing.
    /// </summary>
    private const int Pad = 106;

    /// <summary>
    /// The code sets of the encodation.
    /// </summary>
    private enum CodeSet
    {
        /// <summary>
        /// Code set A: space to underscore and the control characters.
        /// </summary>
        A,

        /// <summary>
        /// Code set B: space to DEL, HT, FS, GS, RS and the CR LF pair.
        /// </summary>
        B,

        /// <summary>
        /// Code set C: digit pairs.
        /// </summary>
        C,

        /// <summary>
        /// Binary mode: five bytes in six codewords.
        /// </summary>
        Binary,
    }

    /// <summary>
    /// What the analysis records about the message at a position.
    /// </summary>
    [Flags]
    private enum Traits : byte
    {
        /// <summary>
        /// Nothing.
        /// </summary>
        None = 0,

        /// <summary>
        /// Code set A holds the character.
        /// </summary>
        SetA = 1,

        /// <summary>
        /// Code set B holds the character, or the CR LF pair it starts.
        /// </summary>
        SetB = 2,

        /// <summary>
        /// Code set C holds the position: two digits start there, or an FNC1 stands there.
        /// </summary>
        SetC = 4,

        /// <summary>
        /// The character is a byte 128 to 255.
        /// </summary>
        Binary = 8,

        /// <summary>
        /// A CR LF pair starts at the position.
        /// </summary>
        CrLf = 16,

        /// <summary>
        /// The 17xxxxxx10 macro starts at the position.
        /// </summary>
        SeventeenTen = 32,
    }

    /// <summary>
    /// Gets the amounts the four masks add to successive data codewords, modulo 113.
    /// </summary>
    private static ReadOnlySpan<byte> MaskFactors => [0, 3, 7, 17];

    /// <summary>
    /// Gets the dot pattern of each codeword value: nine dots, five of them dark, the first dot as the most significant of nine bits.
    /// </summary>
    private static ReadOnlySpan<ushort> Patterns =>
    [
        0b101010101, 0b010101011, 0b010101101, 0b010110101, 0b011010101, 0b101010110, 0b101011010, 0b101101010, 0b110101010, 0b010101110,
        0b010110110, 0b010111010, 0b011010110, 0b011011010, 0b011101010, 0b100101011, 0b100101101, 0b100110101, 0b101001011, 0b101001101,
        0b101010011, 0b101011001, 0b101100101, 0b101101001, 0b110010101, 0b110100101, 0b110101001, 0b001010111, 0b001011011, 0b001011101,
        0b001101011, 0b001101101, 0b001110101, 0b010010111, 0b010011011, 0b010011101, 0b010100111, 0b010110011, 0b010111001, 0b011001011,
        0b011001101, 0b011010011, 0b011011001, 0b011100101, 0b011101001, 0b100101110, 0b100110110, 0b100111010, 0b101001110, 0b101011100,
        0b101100110, 0b101101100, 0b101110010, 0b101110100, 0b110010110, 0b110011010, 0b110100110, 0b110101100, 0b110110010, 0b110110100,
        0b111001010, 0b111010010, 0b111010100, 0b001011110, 0b001101110, 0b001110110, 0b001111010, 0b010011110, 0b010111100, 0b011001110,
        0b011011100, 0b011100110, 0b011101100, 0b011110010, 0b011110100, 0b100010111, 0b100011011, 0b100011101, 0b100100111, 0b100110011,
        0b100111001, 0b101000111, 0b101100011, 0b101110001, 0b110001011, 0b110001101, 0b110010011, 0b110011001, 0b110100011, 0b110110001,
        0b111000101, 0b111001001, 0b111010001, 0b000101111, 0b000110111, 0b000111011, 0b000111101, 0b001001111, 0b001100111, 0b001110011,
        0b001111001, 0b010001111, 0b011000111, 0b011100011, 0b011110001, 0b100011110, 0b100111100, 0b101111000, 0b110001110, 0b110011100,
        0b110111000, 0b111000110, 0b111001100,
    ];

    /// <summary>
    /// Validates the rows of a fixed symbol size. The rows are 5 to 200, and the columns and the rows sum to an odd number.
    /// </summary>
    /// <param name="columns">The number of columns, already validated.</param>
    /// <param name="rows">The number of rows.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rows"/> is not 5 to 200.</exception>
    /// <exception cref="ArgumentException">The columns and the rows sum to an even number.</exception>
    public static void ValidateRows(int columns, int rows)
    {
        Guard.MustBeBetweenOrEqualTo(rows, MinimumSide, MaximumSide, nameof(rows));
        if (((columns + rows) & 1) == 0)
        {
            throw new ArgumentException("The columns and the rows of a DotCode symbol must sum to an odd number.", nameof(rows));
        }
    }

    /// <summary>
    /// Builds a DotCode symbol.
    /// </summary>
    /// <param name="text">The text, characters U+0000 to U+00FF. With <paramref name="gs1"/>, the prepared GS1 data.</param>
    /// <param name="gs1">Whether the text is GS1 data: an FNC1 starts the message and every GS1 separator becomes an FNC1.</param>
    /// <param name="columns">The number of columns, or 0 to follow from the data.</param>
    /// <param name="rows">The number of rows, or 0 to follow from the data and the columns.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The text is empty, too long, has a character above U+00FF, or does not fit.</exception>
    public static void BuildSymbol(ReadOnlySpan<char> text, bool gs1, int columns, int rows, double xDimension, ref BarcodeSymbol symbol)
    {
        int length = text.Length + (gs1 ? 1 : 0);
        Guard.MustBeBetweenOrEqualTo(length, 1, MaximumLength, nameof(text));
        int invalid = text.IndexOfAnyExceptInRange('\0', '\u00FF');
        if (invalid >= 0)
        {
            throw new ArgumentException($"DotCode encodes U+0000 to U+00FF; got {text.CodePointAt(invalid).ToDisplayString()}.", nameof(text));
        }

        // One rent holds the message, the five per-position counts of the analysis, the codewords and the per-position traits. Each count array has two entries
        // past the message, which end its look-ahead. A character takes at most two codewords, and the message one more. The traits look up to eight places
        // ahead, so they have eight entries past the message, read as bytes.
        int stride = length + 2;
        int codewordCapacity = (2 * length) + 1;
        int traitInts = (length + 8 + sizeof(int) - 1) / sizeof(int);
        using PooledBuffer<int> analysis = new(length + (5 * stride) + codewordCapacity + traitInts);
        Span<int> buffer = analysis.Span;
        Span<int> message = buffer[..length];
        Span<int> codewords = buffer.Slice(length + (5 * stride), codewordCapacity);
        Span<Traits> traits = MemoryMarshal.Cast<int, Traits>(buffer.Slice(length + (5 * stride) + codewordCapacity, traitInts))[..(length + 8)];

        int m = 0;
        if (gs1)
        {
            message[m++] = Fnc1;
        }

        foreach (char c in text)
        {
            message[m++] = gs1 && c == Gs1Data.Separator ? Fnc1 : c;
        }

        // A message that starts with a macro header and ends with its trailer carries the macro codeword in place of the header. The macro implies the trailer,
        // so the writer reads the message without it: RS EOT after the 05, 06 and 12 headers, and EOT alone after the header with any two digits.
        int macro = CodewordWriter.MacroAtStart(message);
        int trailerLength = macro == 0 ? 0 : macro == MacroAny ? 1 : 2;
        CodewordWriter writer = new(message[..(length - trailerLength)], buffer.Slice(length, 5 * stride), stride, traits, codewords, macro);
        writer.Analyze();
        int dataCount = writer.Encode();

        // The symbol size: the smallest area that holds the codewords at the ratio, or the rows for the given columns, or the given size.
        int minimumArea = (((dataCount + 3 + (dataCount / 2)) * 9) + 2) * 2;
        if (columns == 0)
        {
            (columns, rows) = SizeForRatio(minimumArea, DefaultRatio);
        }
        else if (rows == 0)
        {
            // The rows round up to hold the area. When the sides then sum to an even number, the rows grow by one.
            rows = (minimumArea + columns - 1) / columns;
            if (((rows + columns) & 1) == 0)
            {
                rows++;
            }

            if (rows < MinimumSide)
            {
                rows = MinimumSide + (columns & 1);
            }
        }

        // Half the cells can hold a dot. Two dots carry the mask, and each codeword takes nine. The data codewords grow while one more, with its share of check
        // codewords, still fits; the check codewords are three plus half the data codewords.
        int dotCount = rows * columns / 2;
        int nd = dataCount;
        while (((nd + 1 + ((nd + 1) / 2) + 3) * 9) + 2 <= dotCount)
        {
            nd++;
        }

        int nw = nd + (nd / 2) + 3;
        if ((nw * 9) > dotCount - 2)
        {
            throw new ArgumentException("The text does not fit in a DotCode symbol of the requested size.", nameof(text));
        }

        // The second rent holds the mask value and the masked codewords, and two grids of cells: the symbol under the current mask and the best so far.
        int cellCount = rows * columns;
        int cellInts = (cellCount + sizeof(int) - 1) / sizeof(int);
        using PooledBuffer<int> layoutBuffer = new(nw + 1 + (2 * cellInts));
        Span<int> masked = layoutBuffer.Span[..(nw + 1)];
        Span<byte> cells = MemoryMarshal.Cast<int, byte>(layoutBuffer.Span.Slice(nw + 1, cellInts))[..cellCount];
        Span<byte> best = MemoryMarshal.Cast<int, byte>(layoutBuffer.Span.Slice(nw + 1 + cellInts, cellInts))[..cellCount];

        DotLayout layout = new(codewords[..dataCount], nd, writer.EndsInBinary, columns, rows, masked);
        double bestScore = double.MinValue;
        for (int mask = 0; mask < MaskFactors.Length; mask++)
        {
            layout.Place(mask, false, cells);
            double score = Score(cells, columns, rows);
            if (score > bestScore)
            {
                bestScore = score;
                cells.CopyTo(best);
            }
        }

        // When no mask scores above half the cells, the six corner dots that carry the last bits are lit in every mask, which gives the edges more dark dots,
        // and the best of those wins instead. A lit symbol must score above RetryFloor to replace the best symbol so far.
        if (bestScore <= cellCount / 2)
        {
            bestScore = RetryFloor;
            for (int mask = 0; mask < MaskFactors.Length; mask++)
            {
                layout.Place(mask, true, cells);
                double score = Score(cells, columns, rows);
                if (score > bestScore)
                {
                    bestScore = score;
                    cells.CopyTo(best);
                }
            }
        }

        Span<bool> modules = symbol.BeginDotted(columns, rows, QuietZone, xDimension);
        MemoryMarshal.Cast<byte, bool>(best).CopyTo(modules);
    }

    /// <summary>
    /// Returns the size of the smallest symbol near a width to height ratio that holds an area. The sides start from the exact sides of the area at the ratio,
    /// rounded down. When their sum is odd and the area does not fit, both sides grow by one. When their sum is even, the side further below its exact value
    /// grows by one. If the area does not fit, that side goes back and the other side grows by one. If the area still does not fit, the first side grows by
    /// two.
    /// </summary>
    /// <param name="minimumArea">The area to hold, in cells.</param>
    /// <param name="ratio">The width to height ratio.</param>
    /// <returns>The columns and the rows.</returns>
    private static (int Columns, int Rows) SizeForRatio(int minimumArea, double ratio)
    {
        double exactHeight = Math.Sqrt(minimumArea / ratio);
        double exactWidth = Math.Sqrt(minimumArea * ratio);
        int h = (int)exactHeight;
        int w = (int)exactWidth;
        if (((h + w) & 1) == 1)
        {
            if (h * w < minimumArea)
            {
                h++;
                w++;
            }
        }
        else if (exactHeight * w < exactWidth * h)
        {
            // The width is further below its exact value in proportion, so it grows first.
            w++;
            if (h * w < minimumArea)
            {
                w--;
                h++;
                if (h * w < minimumArea)
                {
                    w += 2;
                }
            }
        }
        else
        {
            h++;
            if (h * w < minimumArea)
            {
                h--;
                w++;
                if (h * w < minimumArea)
                {
                    h += 2;
                }
            }
        }

        return (w, h);
    }

    /// <summary>
    /// Adds the Reed-Solomon check codewords over the prime field of 113. The codewords split into blocks of at most 112: block s of the step takes every
    /// step-th codeword from s, its data first and then its check codewords, so the blocks interleave in the codeword order.
    /// </summary>
    /// <param name="codewords">The mask value and the data codewords, followed by room for the check codewords.</param>
    /// <param name="dataCount">The number of codewords protected, the mask value and the data.</param>
    private static void AddErrorCorrection(Span<int> codewords, int dataCount)
    {
        int total = codewords.Length;
        int step = ((total - 1) / 112) + 1;

        // A block holds at most 112 codewords, and its check codewords are three plus half its data, so a block has at most 39 check codewords.
        InlineArray64<int> generatorStorage = default;
        InlineArray64<int> registerStorage = default;
        for (int start = 0; start < step; start++)
        {
            // Block start holds codewords start, start + step, and so on: ceil((count - start) / step) of them.
            int blockData = (dataCount - start + step - 1) / step;
            int checkCount = ((total - start + step - 1) / step) - blockData;
            Span<int> generator = ((Span<int>)generatorStorage)[..checkCount];
            Span<int> register = ((Span<int>)registerStorage)[..checkCount];
            Generator(generator);

            // The division of the data polynomial times x^k by the generator, the first data codeword the highest term, as a shift register: each codeword
            // feeds back its difference from the first register value, and the register shifts down by one place with the generator terms added in. The
            // register ends holding the check codewords, the first one first.
            register.Clear();
            for (int k = 0; k < blockData; k++)
            {
                int feedback = (codewords[start + (k * step)] - register[0] + Modulus) % Modulus;
                for (int j = 0; j < checkCount - 1; j++)
                {
                    register[j] = (register[j + 1] + (generator[checkCount - 1 - j] * feedback)) % Modulus;
                }

                register[checkCount - 1] = (generator[0] * feedback) % Modulus;
            }

            for (int j = 0; j < checkCount; j++)
            {
                codewords[start + ((blockData + j) * step)] = register[j];
            }
        }
    }

    /// <summary>
    /// Writes the generator polynomial of a block, the product of (x - 3^i) for i = 1 to k over the field of 113, without its leading 1: the coefficient of x^0
    /// first.
    /// </summary>
    /// <param name="generator">The buffer of k coefficients to write.</param>
    private static void Generator(Span<int> generator)
    {
        // The product builds in k + 1 terms, the coefficient of x^k first. Each factor (x - r) updates every term to itself less r times the term above it,
        // from the bottom up, so each update reads the term above before that term changes.
        int k = generator.Length;
        InlineArray64<int> productStorage = default;
        Span<int> product = ((Span<int>)productStorage)[..(k + 1)];
        product.Clear();
        product[0] = 1;
        int root = 1;
        for (int i = 1; i <= k; i++)
        {
            root = (root * 3) % Modulus;
            for (int j = k; j >= 1; j--)
            {
                product[j] = (product[j] + Modulus - ((root * product[j - 1]) % Modulus)) % Modulus;
            }
        }

        // The coefficient of x^j is product[k - j].
        for (int j = 0; j < k; j++)
        {
            generator[j] = product[k - j];
        }
    }

    /// <summary>
    /// Scores a symbol: the weakest edge less the square of the voids and isolated dots, less a penalty for runs of blank columns or rows. A symbol with an
    /// edge that has no dark dot scores <see cref="UnlitEdgeScore"/>. The penalty grows as a power of the side, past the range of an integer, so the score is a
    /// double.
    /// </summary>
    /// <param name="cells">The cells, row-major, 1 for a dark dot.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="rows">The number of rows.</param>
    /// <returns>The score; higher is better.</returns>
    private static double Score(ReadOnlySpan<byte> cells, int columns, int rows)
    {
        // An edge scores its dark dots plus the span from its first dark dot to its last, times the length of the other side. The weakest edge counts.
        double worst = Math.Min(
            Math.Min(EdgeScore(cells, 0, 1, columns), EdgeScore(cells, (rows - 1) * columns, 1, columns)) * (double)rows,
            Math.Min(EdgeScore(cells, 0, columns, rows), EdgeScore(cells, columns - 1, columns, rows)) * (double)columns);

        // With odd rows or 12 rows or fewer, the score takes a penalty for runs of blank inner columns. Otherwise, the codewords run down the columns of a
        // symbol tall enough to break up runs of light dots. With even rows or 12 columns or fewer, the score takes the same penalty for runs of blank inner
        // rows.
        double penalty = 0;
        if ((rows & 1) == 1 || rows <= 12)
        {
            penalty += BlankRunPenalty(cells, columns, rows, true);
        }

        if ((rows & 1) == 0 || columns <= 12)
        {
            penalty += BlankRunPenalty(cells, columns, rows, false);
        }

        // A void is a light dot cell with no dark diagonal neighbour. An isolated dot is a dark dot with no dark diagonal neighbour and no dark dot two cells
        // away along its row or column. Cells beyond the symbol are light.
        int count = 0;
        for (int y = 0; y < rows; y++)
        {
            for (int x = y & 1; x < columns; x += 2)
            {
                if (Dark(cells, columns, rows, x - 1, y - 1)
                    || Dark(cells, columns, rows, x + 1, y - 1)
                    || Dark(cells, columns, rows, x - 1, y + 1)
                    || Dark(cells, columns, rows, x + 1, y + 1))
                {
                    continue;
                }

                if (cells[(y * columns) + x] == 0
                    || !(Dark(cells, columns, rows, x - 2, y)
                        || Dark(cells, columns, rows, x, y - 2)
                        || Dark(cells, columns, rows, x + 2, y)
                        || Dark(cells, columns, rows, x, y + 2)))
                {
                    count++;
                }
            }
        }

        return worst == 0 ? UnlitEdgeScore : worst - ((double)count * count) - penalty;
    }

    /// <summary>
    /// Returns the dark dots of an edge plus the span from its first dark dot to its last, or 0 for an edge with no dark dot.
    /// </summary>
    /// <param name="cells">The cells.</param>
    /// <param name="start">The index of the first cell of the edge.</param>
    /// <param name="step">The distance between two cells of the edge.</param>
    /// <param name="length">The number of cells of the edge.</param>
    /// <returns>The edge score before scaling.</returns>
    private static int EdgeScore(ReadOnlySpan<byte> cells, int start, int step, int length)
    {
        int sum = 0;
        int first = -1;
        int last = -1;
        for (int k = 0; k < length; k++)
        {
            if (cells[start + (k * step)] == 1)
            {
                if (first < 0)
                {
                    first = k;
                }

                last = k;
                sum++;
            }
        }

        return sum + last - first;
    }

    /// <summary>
    /// Returns the penalty of the runs of blank inner columns or rows: for each run of n, the length of the lines to the power n.
    /// </summary>
    /// <param name="cells">The cells.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="rows">The number of rows.</param>
    /// <param name="byColumn">Whether the lines are columns.</param>
    /// <returns>The penalty.</returns>
    private static double BlankRunPenalty(ReadOnlySpan<byte> cells, int columns, int rows, bool byColumn)
    {
        int lines = byColumn ? columns : rows;
        int lineLength = byColumn ? rows : columns;
        int start = byColumn ? 1 : columns;
        int step = byColumn ? columns : 1;
        int lineStep = byColumn ? 1 : columns;
        double penalty = 0;
        double run = 0;
        for (int line = 1; line < lines - 1; line++, start += lineStep)
        {
            bool blank = true;
            for (int k = 0; k < lineLength && blank; k++)
            {
                blank = cells[start + (k * step)] == 0;
            }

            if (blank)
            {
                run = run == 0 ? lineLength : run * lineLength;
            }
            else
            {
                penalty += run;
                run = 0;
            }
        }

        return penalty + run;
    }

    /// <summary>
    /// Returns whether a cell is a dark dot; cells beyond the symbol are light.
    /// </summary>
    /// <param name="cells">The cells.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="rows">The number of rows.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns><see langword="true"/> for a dark dot.</returns>
    private static bool Dark(ReadOnlySpan<byte> cells, int columns, int rows, int x, int y)
        => (uint)x < (uint)columns && (uint)y < (uint)rows && cells[(y * columns) + x] == 1;

    /// <summary>
    /// Places the codewords of a symbol under each mask: masks the data, adds the error correction and writes the dots.
    /// </summary>
    private readonly ref struct DotLayout
    {
        /// <summary>
        /// The marker of the six corner cells while the stream fills the other dot cells.
        /// </summary>
        private const byte Reserved = 2;

        /// <summary>
        /// The data codewords before padding.
        /// </summary>
        private readonly ReadOnlySpan<int> data;

        /// <summary>
        /// The number of data codewords after padding.
        /// </summary>
        private readonly int dataCount;

        /// <summary>
        /// Whether the data ends in binary mode, so the first pad terminates to code set A.
        /// </summary>
        private readonly bool endsInBinary;

        /// <summary>
        /// The number of columns.
        /// </summary>
        private readonly int columns;

        /// <summary>
        /// The number of rows.
        /// </summary>
        private readonly int rows;

        /// <summary>
        /// The mask value followed by the masked data and check codewords.
        /// </summary>
        private readonly Span<int> masked;

        /// <summary>
        /// Initializes a new instance of the <see cref="DotLayout"/> struct.
        /// </summary>
        /// <param name="data">The data codewords before padding.</param>
        /// <param name="dataCount">The number of data codewords after padding.</param>
        /// <param name="endsInBinary">Whether the data ends in binary mode.</param>
        /// <param name="columns">The number of columns.</param>
        /// <param name="rows">The number of rows.</param>
        /// <param name="masked">The buffer of the mask value and the masked codewords, one longer than the data and check codewords.</param>
        public DotLayout(ReadOnlySpan<int> data, int dataCount, bool endsInBinary, int columns, int rows, Span<int> masked)
        {
            this.data = data;
            this.dataCount = dataCount;
            this.endsInBinary = endsInBinary;
            this.columns = columns;
            this.rows = rows;
            this.masked = masked;
        }

        /// <summary>
        /// Writes the dots of the symbol under one mask.
        /// </summary>
        /// <param name="mask">The mask, 0 to 3.</param>
        /// <param name="litCorners">Whether the six corner dots are dark whatever bits they carry.</param>
        /// <param name="cells">The cells, row-major, to write: 1 for a dark dot.</param>
        public void Place(int mask, bool litCorners, Span<byte> cells)
        {
            // The mask value leads the codewords and is covered by the error correction. Mask m adds successive multiples of its factor to the data codewords,
            // the first plus 0, the next plus the factor, and so on, modulo 113. The data pads to its full count: a pad in binary mode first terminates to code
            // set A.
            Span<int> codewords = this.masked;
            int factor = MaskFactors[mask];
            int weight = 0;
            codewords[0] = mask;
            for (int k = 0; k < this.dataCount; k++)
            {
                int value = k < this.data.Length ? this.data[k] : k == this.data.Length && this.endsInBinary ? BinaryTerminateA : Pad;
                codewords[k + 1] = (value + weight) % Modulus;
                weight += factor;
            }

            AddErrorCorrection(codewords, this.dataCount + 1);

            // The six corner cells carry the last six bits, so they are reserved while the stream fills the other dot cells. Each lies where the column and the
            // row sum to an even number, because the sides sum to an odd number.
            InlineArray6<int> cornerStorage = default;
            Span<int> corners = cornerStorage;
            this.CornerCells(corners);
            cells.Clear();
            foreach (int corner in corners)
            {
                cells[corner] = Reserved;
            }

            // A dot cell is one whose column and row sum to an even number. With even rows the stream fills them column by column from the top left;
            // with odd rows it fills them row by row from the bottom left upward.
            int columns = this.columns;
            int rows = this.rows;
            int bitCount = (rows * columns / 2) - corners.Length;
            int bit = 0;
            bool evenRows = (rows & 1) == 0;
            int outer = evenRows ? columns : rows;
            int inner = evenRows ? rows : columns;
            for (int a = 0; a < outer && bit < bitCount; a++)
            {
                for (int b = 0; b < inner && bit < bitCount; b++)
                {
                    int x = evenRows ? a : b;
                    int y = evenRows ? b : rows - 1 - a;
                    int index = (y * columns) + x;
                    if (((x + y) & 1) == 0 && cells[index] != Reserved)
                    {
                        cells[index] = (byte)this.Bit(bit++);
                    }
                }
            }

            for (int c = 0; c < corners.Length; c++)
            {
                cells[corners[c]] = litCorners ? (byte)1 : (byte)this.Bit(bitCount + c);
            }
        }

        /// <summary>
        /// Returns one bit of the dot stream: the two bits of the mask value, then nine for each codeword after it, the first dot of its pattern first, then
        /// dark padding bits to the end.
        /// </summary>
        /// <param name="index">The bit.</param>
        /// <returns>The bit, 1 for a dark dot.</returns>
        private readonly int Bit(int index)
        {
            if (index < 2)
            {
                return (this.masked[0] >> (1 - index)) & 1;
            }

            int offset = index - 2;
            int codeword = 1 + (offset / 9);
            return codeword < this.masked.Length ? (Patterns[this.masked[codeword]] >> (8 - (offset % 9))) & 1 : 1;
        }

        /// <summary>
        /// Writes the cells of the six corner dots in the order of the last six bits.
        /// </summary>
        /// <param name="corners">The buffer of six cell indices to write.</param>
        private readonly void CornerCells(Span<int> corners)
        {
            int last = this.columns - 1;
            int bottom = (this.rows - 1) * this.columns;
            if ((this.rows & 1) == 0)
            {
                corners[0] = bottom - this.columns + last;
                corners[1] = bottom - this.columns;
                corners[2] = bottom + last - 1;
                corners[3] = bottom + 1;
                corners[4] = last;
                corners[5] = 0;
            }
            else
            {
                corners[0] = last - 1;
                corners[1] = bottom + last - 1;
                corners[2] = this.columns + last;
                corners[3] = bottom - this.columns + last;
                corners[4] = 0;
                corners[5] = bottom;
            }
        }
    }

    /// <summary>
    /// Writes the codewords of a message. The analysis pass reads the message from its end. At each position, it records the runs ahead that each code set
    /// can take. The encoding pass then walks the message from code set C, the starting set. At each position, it writes the codewords that its current set
    /// chooses.
    /// </summary>
    private ref struct CodewordWriter
    {
        /// <summary>
        /// The message: characters 0 to 255 and <see cref="Fnc1"/>.
        /// </summary>
        private readonly ReadOnlySpan<int> message;

        /// <summary>
        /// The number of consecutive digits from each position.
        /// </summary>
        private readonly Span<int> digits;

        /// <summary>
        /// The number of code set C units, digit pairs or FNC1 characters, from each position.
        /// </summary>
        private readonly Span<int> aheadC;

        /// <summary>
        /// The code set C units from a position that starts with a digit, or 0. A shift or latch to code set C at the position takes this run.
        /// </summary>
        private readonly Span<int> tryC;

        /// <summary>
        /// The number of consecutive characters from each position that code set A holds and that do not start a run of two or more code set C units.
        /// </summary>
        private readonly Span<int> aheadA;

        /// <summary>
        /// The number of consecutive characters from each position that code set B holds, a CR LF pair as one, and that do not start a run of two or more code
        /// set C units.
        /// </summary>
        private readonly Span<int> aheadB;

        /// <summary>
        /// The traits of each position, eight past the end of the message.
        /// </summary>
        private readonly Span<Traits> traits;

        /// <summary>
        /// The codewords written.
        /// </summary>
        private readonly Span<int> codewords;

        /// <summary>
        /// The bytes waiting for a binary mode group: five bytes encode as six codewords.
        /// </summary>
        private InlineArray5<int> pending;

        /// <summary>
        /// The number of bytes waiting.
        /// </summary>
        private int pendingCount;

        /// <summary>
        /// The number of codewords written.
        /// </summary>
        private int count;

        /// <summary>
        /// The position in the message.
        /// </summary>
        private int position;

        /// <summary>
        /// The current code set.
        /// </summary>
        private CodeSet codeSet;

        /// <summary>
        /// The macro codeword the message starts with, 0 for none. The message ends before the trailer, which the macro implies.
        /// </summary>
        private readonly int macro;

        /// <summary>
        /// Initializes a new instance of the <see cref="CodewordWriter"/> struct.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="counts">The five count arrays of the analysis, each <paramref name="stride"/> long, end to end.</param>
        /// <param name="stride">The length of each count array, the message length plus two.</param>
        /// <param name="traits">The traits, the message length plus eight.</param>
        /// <param name="codewords">The codewords to write, twice the message length plus one.</param>
        /// <param name="macro">The macro codeword the message starts with, 0 for none. The message ends before the trailer of the macro.</param>
        public CodewordWriter(ReadOnlySpan<int> message, Span<int> counts, int stride, Span<Traits> traits, Span<int> codewords, int macro)
        {
            this.message = message;
            this.digits = counts[..stride];
            this.aheadC = counts.Slice(stride, stride);
            this.tryC = counts.Slice(2 * stride, stride);
            this.aheadA = counts.Slice(3 * stride, stride);
            this.aheadB = counts.Slice(4 * stride, stride);
            this.traits = traits;
            this.codewords = codewords;
            this.pending = default;
            this.pendingCount = 0;
            this.count = 0;
            this.position = 0;
            this.codeSet = CodeSet.C;
            this.macro = macro;
        }

        /// <summary>
        /// Gets a value indicating whether the encoding ends in binary mode.
        /// </summary>
        public readonly bool EndsInBinary => this.codeSet == CodeSet.Binary;

        /// <summary>
        /// Records the analysis of every position, from the end of the message back to its start, so each position reads the counts of the positions after it.
        /// </summary>
        public readonly void Analyze()
        {
            ReadOnlySpan<int> msg = this.message;
            int length = msg.Length;
            this.digits[length..].Clear();
            this.aheadC[length..].Clear();
            this.tryC[length..].Clear();
            this.aheadA[length..].Clear();
            this.aheadB[length..].Clear();
            this.traits.Clear();
            for (int i = length - 1; i >= 0; i--)
            {
                int c = msg[i];
                this.digits[i] = c is >= '0' and <= '9' ? this.digits[i + 1] + 1 : 0;

                bool crLf = c == '\r' && i < length - 1 && msg[i + 1] == '\n';
                Traits trait = Traits.None;
                if (c is >= 0 and <= 95 or Fnc1)
                {
                    trait |= Traits.SetA;
                }

                if (c is >= 32 and <= 127 or '\t' or 28 or 29 or 30 or Fnc1 || crLf)
                {
                    trait |= Traits.SetB;
                }

                if (this.digits[i] >= 2 || c == Fnc1)
                {
                    trait |= Traits.SetC;
                }

                if (c >= 128)
                {
                    trait |= Traits.Binary;
                }

                if (crLf)
                {
                    trait |= Traits.CrLf;
                }

                // The 17xxxxxx10 macro: the GS1 expiry date element string (17) followed by the start of a batch number (10).
                if (this.digits[i] >= 10 && msg[i] == '1' && msg[i + 1] == '7' && msg[i + 8] == '1' && msg[i + 9] == '0')
                {
                    trait |= Traits.SeventeenTen;
                }

                this.traits[i] = trait;

                // An FNC1 is one unit of code set C, and so is each pair of digits. A run of code set C starts only on a digit whose pair begins more units
                // than the next position does.
                this.aheadC[i] = c == Fnc1 ? this.aheadC[i + 1] + 1 : this.digits[i] <= 1 ? 0 : this.aheadC[i + 2] + 1;
                this.tryC[i] = this.digits[i] > 0 && this.aheadC[i] > this.aheadC[i + 1] ? this.aheadC[i] : 0;
                this.aheadA[i] = (trait & Traits.SetA) != 0 && this.tryC[i] < 2 ? this.aheadA[i + 1] + 1 : 0;
                this.aheadB[i] = (trait & Traits.SetB) != 0 && this.tryC[i] < 2 ? this.aheadB[i + (crLf ? 2 : 1)] + 1 : 0;
            }
        }

        /// <summary>
        /// Writes the codewords of the whole message.
        /// </summary>
        /// <returns>The number of codewords.</returns>
        public int Encode()
        {
            int length = this.message.Length;
            while (this.position < length)
            {
                switch (this.codeSet)
                {
                    case CodeSet.A:
                        this.EncodeA();
                        break;
                    case CodeSet.B:
                        this.EncodeB();
                        break;
                    case CodeSet.C:
                        this.EncodeC();
                        break;
                    default:
                        this.EncodeBinary();
                        break;
                }
            }

            return this.count;
        }

        /// <summary>
        /// Encodes from the current position in code set C.
        /// </summary>
        private void EncodeC()
        {
            ReadOnlySpan<int> msg = this.message;
            int i = this.position;
            if (i == 0)
            {
                // A message that starts with a macro header and ends with its trailer latches to code set B and carries the macro codeword in place of the
                // header.
                if (this.macro != 0)
                {
                    this.Add(CLatchB);
                    this.codeSet = CodeSet.B;
                    this.Add(this.macro);
                    if (this.macro == MacroAny)
                    {
                        this.Add(BValue(msg[4]));
                        this.Add(BValue(msg[5]));
                        this.position = 6;
                    }
                    else
                    {
                        this.position = 7;
                    }

                    return;
                }

                // Two digits in the first codeword mark GS1 data. Other data that starts with two digits leads with an FNC1, and GS1 data drops the FNC1 it
                // starts with.
                if (this.digits[i] >= 2)
                {
                    this.Add(Fnc1Codeword);
                }

                if (msg[i] == Fnc1 && this.digits[i + 1] >= 2)
                {
                    this.position = ++i;
                }
            }

            Traits trait = this.traits[i];
            if ((trait & Traits.SeventeenTen) != 0)
            {
                // The macro codeword, then the six digits of the date as three pairs; the (10) is implied.
                this.Add(CSeventeenTen);
                this.Add(Pair(msg, i + 2));
                this.Add(Pair(msg, i + 4));
                this.Add(Pair(msg, i + 6));
                this.position = i + 10;
                return;
            }

            if ((trait & Traits.SetC) != 0)
            {
                this.AddCUnits(1);
                return;
            }

            if ((trait & Traits.Binary) != 0)
            {
                // A lone byte before a digit takes an upper shift and stays in code set C.
                if (this.digits[i + 1] > 0)
                {
                    this.AddUpperShift(msg[i]);
                    this.position = i + 1;
                    return;
                }

                this.Latch(BinaryLatch, CodeSet.Binary);
                return;
            }

            int runA = this.aheadA[i];
            int runB = this.aheadB[i];

            // HT, FS, GS and RS are macros in the first codeword of code set B, so a message that starts with one latches to code set A.
            if (runA > runB || (i == 0 && msg[i] is '\t' or 28 or 29 or 30))
            {
                this.Latch(CLatchA, CodeSet.A);
                return;
            }

            if (runB > 4)
            {
                this.Latch(CLatchB, CodeSet.B);
                return;
            }

            // A shift for one to four characters of code set B.
            this.Add(CShiftB1 + runB - 1);
            this.AddBRun(runB);
        }

        /// <summary>
        /// Encodes from the current position in code set B.
        /// </summary>
        private void EncodeB()
        {
            ReadOnlySpan<int> msg = this.message;
            int i = this.position;
            if (this.SwitchToC())
            {
                return;
            }

            Traits trait = this.traits[i];
            if ((trait & Traits.SetB) != 0)
            {
                if ((trait & Traits.CrLf) != 0)
                {
                    this.Add(BCrLf);
                    this.position = i + 2;
                    return;
                }

                this.Add(msg[i] == Fnc1 ? Fnc1Codeword : BValue(msg[i]));
                this.position = i + 1;
                return;
            }

            if ((trait & Traits.Binary) != 0)
            {
                this.UpperShiftOrBinary(Traits.SetB);
                return;
            }

            // One code set A character takes a shift; more latch.
            if (this.aheadA[i] == 1)
            {
                this.Add(BShiftA);
                this.Add(AValue(msg[i]));
                this.position = i + 1;
                return;
            }

            this.Latch(BLatchA, CodeSet.A);
        }

        /// <summary>
        /// Encodes from the current position in code set A.
        /// </summary>
        private void EncodeA()
        {
            ReadOnlySpan<int> msg = this.message;
            int i = this.position;
            if (this.SwitchToC())
            {
                return;
            }

            Traits trait = this.traits[i];
            if ((trait & Traits.SetA) != 0)
            {
                this.Add(msg[i] == Fnc1 ? Fnc1Codeword : AValue(msg[i]));
                this.position = i + 1;
                return;
            }

            if ((trait & Traits.Binary) != 0)
            {
                this.UpperShiftOrBinary(Traits.SetA);
                return;
            }

            // One to six code set B characters take a shift; more latch.
            int run = this.aheadB[i];
            if (run > 6)
            {
                this.Latch(ALatchB, CodeSet.B);
                return;
            }

            this.Add(AShiftB1 + run - 1);
            this.AddBRun(run);
        }

        /// <summary>
        /// Encodes from the current position in binary mode.
        /// </summary>
        private void EncodeBinary()
        {
            ReadOnlySpan<int> msg = this.message;
            int i = this.position;
            int units = this.tryC[i];
            if (units >= 2)
            {
                // Two to seven code set C units interrupt binary mode with a shift; more terminate to code set C.
                this.FlushBinary();
                if (units > 7)
                {
                    this.Latch(BinaryTerminateC, CodeSet.C);
                    return;
                }

                this.Add(BinaryShiftC2 + units - 2);
                this.AddCUnits(units);
                return;
            }

            // A character stays in binary mode while a byte 128 to 255 follows within the next four positions.
            if (msg[i] != Fnc1 && ((this.traits[i] | this.traits[i + 1] | this.traits[i + 2] | this.traits[i + 3]) & Traits.Binary) != 0)
            {
                this.AddBinary(msg[i]);
                this.position = i + 1;
                if (this.position == msg.Length)
                {
                    this.FlushBinary();
                }

                return;
            }

            this.FlushBinary();
            if (this.aheadA[i] > this.aheadB[i])
            {
                this.Latch(BinaryTerminateA, CodeSet.A);
            }
            else
            {
                this.Latch(BinaryTerminateB, CodeSet.B);
            }
        }

        /// <summary>
        /// Switches from code set A or B to code set C when two or more code set C units start at the position: a latch for five or more, and a shift for two
        /// to four, which also writes the units.
        /// </summary>
        /// <returns><see langword="true"/> when the position switched.</returns>
        private bool SwitchToC()
        {
            int units = this.tryC[this.position];
            if (units < 2)
            {
                return false;
            }

            if (units > 4)
            {
                this.Latch(LatchC, CodeSet.C);
                return true;
            }

            this.Add(ShiftC2 + units - 2);
            this.AddCUnits(units);
            return true;
        }

        /// <summary>
        /// Writes a byte 128 to 255 in code set A or B: an upper shift when the next character belongs to the current set, otherwise a latch to binary mode.
        /// </summary>
        /// <param name="currentSet">The trait of the current code set.</param>
        private void UpperShiftOrBinary(Traits currentSet)
        {
            int i = this.position;
            if ((this.traits[i + 1] & currentSet) != 0)
            {
                this.AddUpperShift(this.message[i]);
                this.position = i + 1;
                return;
            }

            this.Latch(BinaryLatch, CodeSet.Binary);
        }

        /// <summary>
        /// Returns the macro codeword of a message that starts with a macro header and ends with its trailer, or 0. The header is [)&gt;RS and two digits; 05,
        /// 06 and 12 also need GS after the digits and RS before the final EOT, and any other digits need only the final EOT.
        /// </summary>
        /// <param name="msg">The whole message, the trailer included.</param>
        /// <returns>The macro codeword, or 0.</returns>
        public static int MacroAtStart(ReadOnlySpan<int> msg)
        {
            int end = msg.Length;
            if (end < 7
                || msg[0] != '['
                || msg[1] != ')'
                || msg[2] != '>'
                || msg[3] != 30
                || msg[4] is < '0' or > '9'
                || msg[5] is < '0' or > '9'
                || msg[end - 1] != 4)
            {
                return 0;
            }

            int format = Pair(msg, 4);
            if (format is not (5 or 6 or 12))
            {
                return MacroAny;
            }

            if (msg[6] != 29 || msg[end - 2] != 30)
            {
                return 0;
            }

            return format switch
            {
                5 => Macro05,
                6 => Macro06,
                _ => Macro12,
            };
        }

        /// <summary>
        /// Writes a latch and changes the code set.
        /// </summary>
        /// <param name="codeword">The latch codeword.</param>
        /// <param name="codeSet">The code set it latches to.</param>
        private void Latch(int codeword, CodeSet codeSet)
        {
            this.Add(codeword);
            this.codeSet = codeSet;
        }

        /// <summary>
        /// Writes a run of code set B characters after a shift, a CR LF pair as one.
        /// </summary>
        /// <param name="run">The number of characters.</param>
        private void AddBRun(int run)
        {
            for (int k = 0; k < run; k++)
            {
                int c = this.message[this.position];
                this.Add(c == '\r' ? BCrLf : BValue(c));
                this.position += c == '\r' ? 2 : 1;
            }
        }

        /// <summary>
        /// Writes code set C units: digit pairs and FNC1 characters.
        /// </summary>
        /// <param name="units">The number of units.</param>
        private void AddCUnits(int units)
        {
            for (int k = 0; k < units; k++)
            {
                int i = this.position;
                bool fnc1 = this.message[i] == Fnc1;
                this.Add(fnc1 ? Fnc1Codeword : Pair(this.message, i));
                this.position = i + (fnc1 ? 1 : 2);
            }
        }

        /// <summary>
        /// Writes a byte 128 to 255 as an upper shift and a character: upper shift A for 128 to 159, which carries the code set A value of the byte less 128,
        /// and upper shift B for 160 to 255, which carries the code set B value.
        /// </summary>
        /// <param name="value">The byte.</param>
        private void AddUpperShift(int value)
        {
            bool shiftA = value < 160;
            this.Add(shiftA ? UpperShiftA : UpperShiftB);
            this.Add(shiftA ? AValue(value - 128) : BValue(value - 128));
        }

        /// <summary>
        /// Adds a byte to the binary group, and writes the group once it holds five.
        /// </summary>
        /// <param name="value">The byte.</param>
        private void AddBinary(int value)
        {
            this.pending[this.pendingCount++] = value;
            if (this.pendingCount == 5)
            {
                this.FlushBinary();
            }
        }

        /// <summary>
        /// Writes the waiting bytes of binary mode: n bytes, read as a base 259 number, become n + 1 base 103 codewords, the most significant first.
        /// </summary>
        private void FlushBinary()
        {
            int n = this.pendingCount;
            if (n == 0)
            {
                return;
            }

            // The bytes align right in five base 259 digits, so the value is high x 259^3 + low, where high is the top two digits and low the bottom three.
            // Both convert to base 103 on their own, h0 to h2 and l0 to l3, least significant first. 259^3 is 15 x 103^3 + 92 x 103^2 + 68 x 103 + 42, so each
            // base 103 digit of the value is the low digit of its column plus the products of the high digits with those four constants, plus the carry from
            // the column below. Each column fits an int.
            Span<int> bytes = this.pending;
            int b0 = n >= 5 ? bytes[n - 5] : 0;
            int b1 = n >= 4 ? bytes[n - 4] : 0;
            int b2 = n >= 3 ? bytes[n - 3] : 0;
            int b3 = n >= 2 ? bytes[n - 2] : 0;
            int b4 = bytes[n - 1];

            int high = (b0 * 259) + b1;
            int h0 = high % 103;
            int h1 = (high / 103) % 103;
            int h2 = high / (103 * 103);

            int low = (b2 * 67081) + (b3 * 259) + b4;
            int l0 = low % 103;
            int l1 = (low / 103) % 103;
            int l2 = (low / (103 * 103)) % 103;
            int l3 = low / (103 * 103 * 103);

            InlineArray6<int> outputStorage = default;
            Span<int> output = outputStorage;
            int t = l0 + (h0 * 42);
            output[5] = t % 103;
            t = (t / 103) + l1 + (h0 * 68) + (h1 * 42);
            output[4] = t % 103;
            t = (t / 103) + l2 + (h0 * 92) + (h1 * 68) + (h2 * 42);
            output[3] = t % 103;
            t = (t / 103) + l3 + (h0 * 15) + (h1 * 92) + (h2 * 68);
            output[2] = t % 103;
            t = (t / 103) + (h1 * 15) + (h2 * 92);
            output[1] = t % 103;
            t = (t / 103) + (h2 * 15);
            output[0] = t % 103;

            foreach (int codeword in output[(5 - n)..])
            {
                this.Add(codeword);
            }

            this.pendingCount = 0;
        }

        /// <summary>
        /// Writes one codeword.
        /// </summary>
        /// <param name="value">The codeword, 0 to 112.</param>
        private void Add(int value) => this.codewords[this.count++] = value;

        /// <summary>
        /// Returns the code set C value of the two digits at a position.
        /// </summary>
        /// <param name="msg">The message.</param>
        /// <param name="i">The position of the first digit.</param>
        /// <returns>The value, 0 to 99.</returns>
        private static int Pair(ReadOnlySpan<int> msg, int i) => ((msg[i] - '0') * 10) + (msg[i + 1] - '0');

        /// <summary>
        /// Returns the code set A value of a character 0 to 95: space to underscore are 0 to 63, and the control characters NUL to US are 64 to 95.
        /// </summary>
        /// <param name="c">The character.</param>
        /// <returns>The value.</returns>
        private static int AValue(int c) => c >= 32 ? c - 32 : c + 64;

        /// <summary>
        /// Returns the code set B value of a character: space to DEL are 0 to 95, and HT, FS, GS and RS are 97 to 100.
        /// </summary>
        /// <param name="c">The character.</param>
        /// <returns>The value.</returns>
        private static int BValue(int c) => c switch
        {
            >= 32 => c - 32,
            '\t' => 97,
            _ => c - 28 + 98,
        };
    }
}
