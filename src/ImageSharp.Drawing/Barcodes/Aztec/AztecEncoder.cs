// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodes Aztec Code symbols, ISO/IEC 24778. The text becomes a bit stream in the five code sets and the byte shift of Table 2. The bit stream becomes B-bit
/// codewords with no codeword of all zeros or all ones. Reed-Solomon check codewords fill the symbol. The codewords spiral outward from the Core Symbol in
/// reverse order. The mode search finds the shortest sequence of latches, shifts and characters for the whole message.
/// </summary>
internal static class AztecEncoder
{
    /// <summary>
    /// The recommended error correction level of section 11.3: "23% of symbol capacity plus 3 codewords more".
    /// </summary>
    public const int DefaultErrorCorrectionPercentage = 23;

    /// <summary>
    /// The longest input. The largest symbol holds 1664 codewords of 12 bits, 19968 bits, and no character takes fewer than 2.5 bits: the punctuation pairs of
    /// Table 2 put two characters in 5 bits.
    /// </summary>
    public const int MaximumLength = 19968 * 2 / 5;

    /// <summary>
    /// The check codewords added to the percentage, section 11.3: "plus always, for data security, three additional checkwords".
    /// </summary>
    private const int ExtraCheckCodewords = 3;

    /// <summary>
    /// The most bytes one B/S carries, section 7.3.2.2: 31 with a 5-bit length, or 31 more than the 11-bit length, at most 2047.
    /// </summary>
    private const int MaximumByteRun = 31 + 2047;

    /// <summary>
    /// The number of states of the search: the five code sets of Table 2 and the byte state.
    /// </summary>
    private const int StateCount = 6;

    /// <summary>
    /// The number of code sets that hold characters: every state but <see cref="CodeSet.Binary"/>.
    /// </summary>
    private const int CharacterSetCount = 5;

    /// <summary>
    /// The number of compact sizes, 1 to 4 layers, which lead <see cref="SymbolCodewords"/>.
    /// </summary>
    private const int CompactSizeCount = 4;

    /// <summary>
    /// The cost of a state that the search has not reached.
    /// </summary>
    private const int Unreached = 1_000_000;

    /// <summary>
    /// The sequence element of U/L, the latch to the upper code set.
    /// </summary>
    private const int LatchUpper = -2;

    /// <summary>
    /// The sequence element of L/L, the latch to the lower code set.
    /// </summary>
    private const int LatchLower = -3;

    /// <summary>
    /// The sequence element of M/L, the latch to the mixed code set.
    /// </summary>
    private const int LatchMixed = -4;

    /// <summary>
    /// The sequence element of P/L, the latch to the punctuation code set.
    /// </summary>
    private const int LatchPunct = -5;

    /// <summary>
    /// The sequence element of D/L, the latch to the digit code set.
    /// </summary>
    private const int LatchDigit = -6;

    /// <summary>
    /// The sequence element of U/S, the shift to the upper code set for one character.
    /// </summary>
    private const int ShiftUpper = -7;

    /// <summary>
    /// The sequence element of P/S, the shift to the punctuation code set for one character.
    /// </summary>
    private const int ShiftPunct = -8;

    /// <summary>
    /// The sequence element of B/S, the shift to the byte state.
    /// </summary>
    private const int ShiftBinary = -9;

    /// <summary>
    /// The sequence element of the punctuation pair CR LF.
    /// </summary>
    private const int PairCrLf = -11;

    /// <summary>
    /// The sequence element of the punctuation pair period and space.
    /// </summary>
    private const int PairPeriodSpace = -12;

    /// <summary>
    /// The sequence element of the punctuation pair comma and space.
    /// </summary>
    private const int PairCommaSpace = -13;

    /// <summary>
    /// The sequence element of the punctuation pair colon and space.
    /// </summary>
    private const int PairColonSpace = -14;

    /// <summary>
    /// The field of the mode message, section 7.2.4: GF(16) with the prime modulus polynomial x^4 + x + 1.
    /// </summary>
    private static readonly GaloisField ModeField = new(16, 19);

    /// <summary>
    /// The field of the 6-bit data codewords, Table 3: GF(64) with the polynomial 67. Each data field builds its tables on first use, so a symbol builds only
    /// the field of its codeword size. The GF(4096) tables alone hold 12286 ints.
    /// </summary>
    private static readonly Lazy<GaloisField> Field6 = new(() => new GaloisField(64, 67));

    /// <summary>
    /// The field of the 8-bit data codewords, Table 3: GF(256) with the polynomial 301.
    /// </summary>
    private static readonly Lazy<GaloisField> Field8 = new(() => new GaloisField(256, 301));

    /// <summary>
    /// The field of the 10-bit data codewords, Table 3: GF(1024) with the polynomial 1033.
    /// </summary>
    private static readonly Lazy<GaloisField> Field10 = new(() => new GaloisField(1024, 1033));

    /// <summary>
    /// The field of the 12-bit data codewords, Table 3.
    /// </summary>
    private static readonly Lazy<GaloisField> Field12 = new(() => new GaloisField(4096, 4201));

    /// <summary>
    /// The states of the search, in the row and column order of the latch and shift tables: the five code sets of Table 2, then the byte state of a B/S.
    /// </summary>
    private enum CodeSet
    {
        /// <summary>
        /// The upper code set of Table 2.
        /// </summary>
        Upper,

        /// <summary>
        /// The lower code set of Table 2.
        /// </summary>
        Lower,

        /// <summary>
        /// The mixed code set of Table 2.
        /// </summary>
        Mixed,

        /// <summary>
        /// The punctuation code set of Table 2.
        /// </summary>
        Punct,

        /// <summary>
        /// The digit code set of Table 2.
        /// </summary>
        Digit,

        /// <summary>
        /// The byte state of a B/S.
        /// </summary>
        Binary,
    }

    /// <summary>
    /// Gets the bits each code set writes per character, latch or shift: 5, and 4 in the digit code set, and 8 for each byte of a byte shift.
    /// </summary>
    private static ReadOnlySpan<byte> CharacterBits => [5, 5, 5, 5, 4, 8];

    /// <summary>
    /// Gets the bits of the cheapest latch from each state, row, to each state, column. A latch into the byte state is a B/S and its 5-bit length, and the
    /// return from it costs nothing because the length ends it.
    /// </summary>
    private static ReadOnlySpan<byte> LatchBits =>
    [
        0, 5, 5, 10, 5, 10,
        9, 0, 5, 10, 5, 10,
        5, 5, 0, 5, 10, 10,
        5, 10, 10, 0, 10, 15,
        4, 9, 9, 14, 0, 14,
        0, 0, 0, 0, 0, 0,
    ];

    /// <summary>
    /// Gets the latch codes of each latch of <see cref="LatchBits"/>, up to three each, 0 where none.
    /// </summary>
    private static ReadOnlySpan<sbyte> LatchCodes =>
    [
        0, 0, 0, LatchLower, 0, 0, LatchMixed, 0, 0, LatchMixed, LatchPunct, 0, LatchDigit, 0, 0, ShiftBinary, 0, 0,
        LatchDigit, LatchUpper, 0, 0, 0, 0, LatchMixed, 0, 0, LatchMixed, LatchPunct, 0, LatchDigit, 0, 0, ShiftBinary, 0, 0,
        LatchUpper, 0, 0, LatchLower, 0, 0, 0, 0, 0, LatchPunct, 0, 0, LatchUpper, LatchDigit, 0, ShiftBinary, 0, 0,
        LatchUpper, 0, 0, LatchUpper, LatchLower, 0, LatchUpper, LatchMixed, 0, 0, 0, 0, LatchUpper, LatchDigit, 0, LatchUpper, ShiftBinary, 0,
        LatchUpper, 0, 0, LatchUpper, LatchLower, 0, LatchUpper, LatchMixed, 0, LatchUpper, LatchMixed, LatchPunct, 0, 0, 0, LatchUpper, ShiftBinary, 0,
        LatchUpper, 0, 0, LatchLower, 0, 0, LatchMixed, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ];

    /// <summary>
    /// Gets the bits of a shift from each state, row, to each state, column: U/S from the lower and digit code sets and P/S from every code set but
    /// punctuation. Unreached marks no shift.
    /// </summary>
    private static ReadOnlySpan<int> ShiftBits =>
    [
        Unreached, Unreached, Unreached, 5, Unreached,
        5, Unreached, Unreached, 5, Unreached,
        Unreached, Unreached, Unreached, 5, Unreached,
        Unreached, Unreached, Unreached, Unreached, Unreached,
        4, Unreached, Unreached, 4, Unreached,
    ];

    /// <summary>
    /// Gets the codeword count of Table 1 for each symbol in the order of section 11.5: the compact symbols of 1 to 4 layers, then the full-range symbols of 1
    /// to 32 layers.
    /// </summary>
    private static ReadOnlySpan<short> SymbolCodewords =>
    [
        17, 40, 51, 76,
        21, 48, 60, 88, 120, 156, 196, 240, 230, 272, 316, 364, 416, 470, 528, 588, 652, 720, 790, 864, 940, 1020,
        920, 992, 1066, 1144, 1224, 1306, 1392, 1480, 1570, 1664,
    ];

    /// <summary>
    /// Gets the orientation patterns of section 7.1.3 for a finder of half width F, as x and y offsets from F and the module: the six dark modules, then the
    /// six light ones. An offset of 1 is F + 1, 0 is F, and the signs follow.
    /// </summary>
    private static ReadOnlySpan<sbyte> Orientation =>
    [
        -2, 1, 1, -2, 2, 1, -1, 2, 1, 2, 2, 1, 2, 1, 1, 2, -1, 1,
        1, 2, 0, 2, -2, 0, 1, -2, 0, -1, -2, 0, -2, -2, 0, -2, -1, 0,
    ];

    /// <summary>
    /// Gets the value of each ASCII character in each code set of Table 2, at index code set x 128 + character, or 0xFF where the code set has no such
    /// character. The upper, lower, mixed, punctuation and digit code sets follow in turn, 128 characters each. No code set holds a byte above 127.
    /// </summary>
    private static ReadOnlySpan<byte> CharacterValues =>
    [
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        1, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
        17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        1, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
        17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 255, 255, 255, 255, 255,
        255, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 15, 16, 17, 18, 19,
        1, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        20, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 21, 255, 22, 23,
        24, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 25, 255, 26, 27,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 1, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 21, 22, 23, 24, 25, 26,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 27, 255, 28, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 29, 255, 30, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        1, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 12, 255, 13, 255,
        2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
    ];

    /// <summary>
    /// Builds an Aztec Code symbol.
    /// </summary>
    /// <param name="text">The text, characters U+0000 to U+00FF.</param>
    /// <param name="format">The symbol format.</param>
    /// <param name="errorCorrectionPercentage">The minimum error correction percentage, 5 to 95.</param>
    /// <param name="layers">The number of data layers, or 0 for the smallest symbol that holds the data.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The text is empty, has a character above U+00FF, or does not fit.</exception>
    public static void BuildSymbol(
        ReadOnlySpan<char> text,
        AztecFormat format,
        int errorCorrectionPercentage,
        int layers,
        double xDimension,
        ref BarcodeSymbol symbol)
    {
        if (text.IsEmpty)
        {
            throw new ArgumentException("Aztec Code needs at least one character.", nameof(text));
        }

        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        // Section 4.1 a): "All 8-bit values can be encoded", with values 128 to 255 read as ISO/IEC 8859-1.
        int invalid = text.IndexOfAnyExceptInRange('\0', '\u00FF');
        if (invalid >= 0)
        {
            throw new ArgumentException($"Aztec Code encodes U+0000 to U+00FF; got {text.CodePointAt(invalid).ToDisplayString()}.", nameof(text));
        }

        PooledBuffer<byte> bits = default;
        PooledBuffer<int> codewords = default;
        try
        {
            int bitCount = EncodeBits(text, ref bits);
            ReadOnlySpan<byte> stream = bits.Span[..bitCount];

            // The codeword count after stuffing depends only on the codeword size, so the encoder lays out each size once. Every symbol of that size reads the
            // count.
            InlineArray14<int> dataCountsStorage = default;
            Span<int> dataCounts = ((Span<int>)dataCountsStorage)[..13];
            dataCounts.Fill(-1);

            SymbolSize chosen = default;
            bool found = false;
            int start = format == AztecFormat.Full ? CompactSizeCount : 0;
            int end = format == AztecFormat.Compact ? CompactSizeCount : SymbolCodewords.Length;
            for (int index = start; index < end; index++)
            {
                SymbolSize size = SymbolSize.FromIndex(index);

                // Section 11.5: printing programs "print the smallest symbol for a given data set and error correction percentage in the data layer sequence
                // 1*,2*,3*,4*,4,5,6,...,32". Full-range symbols of 1 to 3 layers are "useful only for reader initialization", so the search skips them unless
                // the caller asks for that size.
                if (layers > 0 ? size.Layers != layers : (format == AztecFormat.Auto && !size.IsCompact && size.Layers < 4))
                {
                    continue;
                }

                int total = size.Codewords;
                int codewordBits = size.CodewordBits;
                if (dataCounts[codewordBits] < 0)
                {
                    dataCounts[codewordBits] = Stuff(stream, codewordBits, []);
                }

                // Section 11.3: the percentage of the symbol capacity plus three codewords is the minimum number of check codewords.
                int check = (((total * errorCorrectionPercentage) + 99) / 100) + ExtraCheckCodewords;
                int data = dataCounts[codewordBits];
                if (data + check <= total && data <= size.MaximumDataCodewords)
                {
                    chosen = size;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new ArgumentException("The text does not fit in an Aztec Code symbol of the requested size.", nameof(text));
            }

            codewords = new PooledBuffer<int>(chosen.Codewords);
            Span<int> all = codewords.Span;
            int dataCount = Stuff(stream, chosen.CodewordBits, all);
            AddCheckCodewords(all, dataCount, chosen.CodewordBits);

            InlineArray64<byte> modeStorage = default;
            Span<byte> mode = ((Span<byte>)modeStorage)[..40];
            int modeLength = WriteModeMessage(chosen, dataCount, false, mode);
            Layout(chosen, all, mode[..modeLength], xDimension, ref symbol);
        }
        finally
        {
            bits.Dispose();
            codewords.Dispose();
        }
    }

    /// <summary>
    /// Builds an Aztec Rune: the Core Symbol of a compact symbol whose mode message carries one value of 0 to 255.
    /// </summary>
    /// <param name="value">The value, 0 to 255.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildRune(int value, double xDimension, ref BarcodeSymbol symbol)
    {
        InlineArray32<byte> modeStorage = default;
        Span<byte> mode = ((Span<byte>)modeStorage)[..28];
        SymbolSize core = new(true, 0);
        WriteModeMessage(core, value, true, mode);
        Layout(core, [], mode, xDimension, ref symbol);
    }

    /// <summary>
    /// Translates the text into the bit stream of section 7.3.2.2. The search keeps, for each code set, the cheapest sequence of characters, latches and shifts
    /// that ends in it. The sequences share their common start: each is a node that points back to the element before it, so a copy is one index and an
    /// extension is one new node.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="bits">The buffer the bits are written to, one bit per byte, rented here.</param>
    /// <returns>The number of bits.</returns>
    private static int EncodeBits(ReadOnlySpan<char> text, ref PooledBuffer<byte> bits)
    {
        NodeArena arena = new(text.Length * 8);
        try
        {
            // The costs and sequence heads of each state, indexed by CodeSet, before and after the current character.
            InlineArray6<int> currentCostStorage = default;
            Span<int> currentCost = currentCostStorage;
            InlineArray6<int> nextCostStorage = default;
            Span<int> nextCost = nextCostStorage;
            InlineArray6<int> currentHeadStorage = default;
            Span<int> currentHead = currentHeadStorage;
            InlineArray6<int> nextHeadStorage = default;
            Span<int> nextHead = nextHeadStorage;
            InlineArray16<int> afterStorage = default;
            Span<int> after = afterStorage;
            currentCost.Fill(Unreached);
            currentCost[(int)CodeSet.Upper] = 0;
            currentHead.Fill(-1);

            // Encoding starts in the upper code set. The byte state returns to the code set it shifted from, which the punctuation and digit code sets reach
            // through upper.
            CodeSet backTo = CodeSet.Upper;
            int lastCharacter = -1;
            for (int t = 0; t < text.Length; t++)
            {
                int character = text[t];

                // A latch can make the sequence of another state cheaper. Each improvement can enable another, so the pass repeats until none applies.
                bool improved;
                do
                {
                    improved = false;
                    for (CodeSet x = CodeSet.Upper; x <= CodeSet.Binary; x++)
                    {
                        for (CodeSet y = CodeSet.Upper; y <= CodeSet.Binary; y++)
                        {
                            if (x == CodeSet.Binary && y != backTo)
                            {
                                continue;
                            }

                            int latch = LatchIndex(x, y);
                            int cost = currentCost[(int)x] + LatchBits[latch];
                            if (cost < currentCost[(int)y])
                            {
                                currentCost[(int)y] = cost;
                                int head = currentHead[(int)x];
                                foreach (sbyte code in LatchCodes.Slice(latch * 3, 3))
                                {
                                    if (code == 0)
                                    {
                                        break;
                                    }

                                    head = arena.Append(head, code);
                                }

                                currentHead[(int)y] = head;
                                if (y == CodeSet.Binary)
                                {
                                    backTo = x is CodeSet.Punct or CodeSet.Digit ? CodeSet.Upper : x;
                                }

                                improved = true;
                            }
                        }
                    }
                }
                while (improved);

                // Each state that holds the character extends its own sequence, and the other code sets can reach it with a shift.
                nextCost.Fill(Unreached);
                nextHead.Fill(-1);
                for (CodeSet x = CodeSet.Upper; x <= CodeSet.Binary; x++)
                {
                    if (x != CodeSet.Binary && (character > 127 || CharacterValues[((int)x << 7) | character] == 0xFF))
                    {
                        continue;
                    }

                    int cost = currentCost[(int)x] + CharacterBits[(int)x];
                    int head = currentHead[(int)x];
                    if (x == CodeSet.Binary && head >= 0 && arena[head].Run == MaximumByteRun)
                    {
                        // A full byte run returns to the code set it shifted from and shifts again: the return marker, then a new B/S and its 5-bit length.
                        cost += LatchBits[LatchIndex(backTo, CodeSet.Binary)];
                        head = arena.Append(arena.Append(head, LatchCodes[LatchIndex(CodeSet.Binary, backTo) * 3]), ShiftBinary);
                    }

                    if (cost < nextCost[(int)x])
                    {
                        nextCost[(int)x] = cost;
                        nextHead[(int)x] = arena.Append(head, character);
                    }

                    if (x == CodeSet.Binary)
                    {
                        continue;
                    }

                    for (CodeSet y = CodeSet.Upper; y < CodeSet.Binary; y++)
                    {
                        if (x == y)
                        {
                            continue;
                        }

                        cost = currentCost[(int)y] + ShiftBits[((int)y * CharacterSetCount) + (int)x] + CharacterBits[(int)x];
                        if (cost < nextCost[(int)y])
                        {
                            nextCost[(int)y] = cost;
                            nextHead[(int)y] = arena.Append(arena.Append(currentHead[(int)y], x == CodeSet.Upper ? ShiftUpper : ShiftPunct), character);
                        }
                    }
                }

                // A punctuation pair of Table 2 puts the previous character and this one in one 5-bit value.
                int pair = Pair(lastCharacter, character);
                if (pair != 0)
                {
                    for (CodeSet state = CodeSet.Upper; state < CodeSet.Binary; state++)
                    {
                        int i = (int)state;

                        // The mixed code set holds CR and the digit code set holds the comma and the period themselves, so a sequence in those states did not
                        // reach the first character through punctuation.
                        bool inPunct = !(state == CodeSet.Mixed && lastCharacter == '\r') && !(state == CodeSet.Digit && lastCharacter is ',' or '.');
                        if (!inPunct || currentCost[i] >= nextCost[i])
                        {
                            continue;
                        }

                        // The scan walks back over the latches this step added to the previous character. The marker before it is the latch that set the code
                        // set it was written in, and a byte shift there means it was a byte.
                        bool latchDigit = false;
                        int found = currentHead[i];
                        while (found >= 0 && arena[found].Value != lastCharacter)
                        {
                            found = arena[found].Previous;
                        }

                        if (found >= 0)
                        {
                            int previous = arena[found].Previous;
                            bool shifted = previous >= 0 && arena[previous].Value == ShiftPunct;
                            int marker = previous >= 0 ? arena[previous].Marker : 0;
                            if (marker == ShiftBinary)
                            {
                                found = -1;
                            }
                            else if (marker != 0)
                            {
                                if (state == CodeSet.Punct)
                                {
                                    latchDigit = marker == LatchDigit;
                                }
                                else if (marker != LatchPunct)
                                {
                                    inPunct = shifted;
                                }
                            }
                        }

                        if (!inPunct || found < 0)
                        {
                            continue;
                        }

                        nextCost[i] = currentCost[i];

                        // The elements after the previous character are the latches this step added. The pair replaces the character where it stands, except in
                        // punctuation, where it follows the latches into that code set.
                        int count = 0;
                        for (int n = currentHead[i]; n != found; n = arena[n].Previous)
                        {
                            after[count++] = arena[n].Value;
                        }

                        int rebuilt = arena[found].Previous;
                        if (state != CodeSet.Punct)
                        {
                            rebuilt = arena.Append(rebuilt, pair);
                        }
                        else if (latchDigit && count > 0)
                        {
                            // The character took 4 bits in the digit code set, and the pair takes 5.
                            nextCost[i]++;
                        }

                        for (int c = count - 1; c >= 0; c--)
                        {
                            rebuilt = arena.Append(rebuilt, after[c]);
                        }

                        if (state == CodeSet.Punct)
                        {
                            rebuilt = arena.Append(rebuilt, pair);
                        }

                        nextHead[i] = rebuilt;
                    }
                }

                // Section 7.3.2.2: a byte shift of more than 31 bytes takes an 11-bit length after a zero.
                if (nextHead[(int)CodeSet.Binary] >= 0 && arena[nextHead[(int)CodeSet.Binary]].Run == 32)
                {
                    nextCost[(int)CodeSet.Binary] += 11;
                }

                nextCost.CopyTo(currentCost);
                nextHead.CopyTo(currentHead);
                lastCharacter = character;
            }

            int best = 0;
            for (int i = 1; i < StateCount; i++)
            {
                if (currentCost[i] < currentCost[best])
                {
                    best = i;
                }
            }

            int length = 0;
            for (int n = currentHead[best]; n >= 0; n = arena[n].Previous)
            {
                length++;
            }

            using PooledBuffer<int> sequenceBuffer = new(length);
            Span<int> sequence = sequenceBuffer.Span;
            int position = length;
            for (int n = currentHead[best]; n >= 0; n = arena[n].Previous)
            {
                sequence[--position] = arena[n].Value;
            }

            bits = new PooledBuffer<byte>(currentCost[best]);
            return WriteBits(sequence, bits.Span);
        }
        finally
        {
            arena.Dispose();
        }
    }

    /// <summary>
    /// Writes the bits of a sequence of characters, latches and shifts, most significant bit first.
    /// </summary>
    /// <param name="sequence">The sequence.</param>
    /// <param name="bits">The buffer, one bit per byte.</param>
    /// <returns>The number of bits written.</returns>
    private static int WriteBits(ReadOnlySpan<int> sequence, Span<byte> bits)
    {
        CodeSet state = CodeSet.Upper;
        int written = 0;
        int i = 0;
        while (i < sequence.Length)
        {
            if (state != CodeSet.Binary)
            {
                int element = sequence[i++];
                written = Write(bits, written, Value(state, element), CharacterBits[(int)state]);
                if (element is ShiftUpper or ShiftPunct)
                {
                    CodeSet shiftState = element == ShiftUpper ? CodeSet.Upper : CodeSet.Punct;
                    written = Write(bits, written, Value(shiftState, sequence[i++]), 5);
                }

                state = element switch
                {
                    LatchUpper => CodeSet.Upper,
                    LatchLower => CodeSet.Lower,
                    LatchMixed => CodeSet.Mixed,
                    LatchPunct => CodeSet.Punct,
                    LatchDigit => CodeSet.Digit,
                    ShiftBinary => CodeSet.Binary,
                    _ => state,
                };

                continue;
            }

            // Section 7.3.2.2: the B/S is followed by the byte count, 1 to 31 in 5 bits, or 0 and the count less 31 in 11 bits. The bytes run to the next
            // element that is not a character, a latch or shift code below 0.
            int byteCount = sequence[i..].IndexOfAnyExceptInRange(0, 255);
            if (byteCount < 0)
            {
                byteCount = sequence.Length - i;
            }

            if (byteCount <= 31)
            {
                written = Write(bits, written, byteCount, 5);
            }
            else
            {
                written = Write(bits, written, 0, 5);
                written = Write(bits, written, byteCount - 31, 11);
            }

            for (int b = 0; b < byteCount; b++)
            {
                written = Write(bits, written, sequence[i++], 8);
            }

            // "At the end of the byte string, encoding returns to the mode from which B/S was invoked." The sequence marks that mode with the latch code the
            // search added.
            if (i < sequence.Length)
            {
                state = sequence[i++] switch
                {
                    LatchLower => CodeSet.Lower,
                    LatchMixed => CodeSet.Mixed,
                    _ => CodeSet.Upper,
                };
            }
        }

        return written;
    }

    /// <summary>
    /// Lays the bit stream into codewords, section 7.3.2.3: "whenever the first B-1 bits placed in a codeword are all 0's, then a dummy 1 is inserted into that
    /// codeword's LSB and the following message bit starts off the next codeword", and the same with a dummy 0 after B-1 ones. The final codeword is "padded
    /// out with 1's (and possibly a final dummy 0 if necessary)".
    /// </summary>
    /// <param name="bits">The bit stream.</param>
    /// <param name="size">The codeword size B.</param>
    /// <param name="codewords">The buffer the codewords are written to. Codewords past its end are counted and not written, so an empty buffer only
    /// counts.</param>
    /// <returns>The number of codewords.</returns>
    private static int Stuff(ReadOnlySpan<byte> bits, int size, Span<int> codewords)
    {
        int count = 0;
        int ones = (1 << size) - 1;
        int position = 0;
        while (position < bits.Length)
        {
            int codeword = 0;
            if (bits.Length - position >= size)
            {
                for (int b = 0; b < size - 1; b++)
                {
                    codeword = (codeword << 1) | bits[position + b];
                }

                int head = codeword;
                codeword <<= 1;
                if (head == 0)
                {
                    codeword |= 1;
                    position += size - 1;
                }
                else if (head == ones >> 1)
                {
                    position += size - 1;
                }
                else
                {
                    codeword |= bits[position + size - 1];
                    position += size;
                }
            }
            else
            {
                int remaining = bits.Length - position;
                for (int b = 0; b < size; b++)
                {
                    codeword = (codeword << 1) | (b < remaining ? bits[position + b] : 1);
                }

                if (codeword == ones)
                {
                    codeword ^= 1;
                }

                position = bits.Length;
            }

            if (count < codewords.Length)
            {
                codewords[count] = codeword;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Adds the check codewords of section 7.3.3 after the data codewords: the remainder of the data polynomial times x^K divided by (x - 2^1) ... (x - 2^K)
    /// over the field of Table 3.
    /// </summary>
    /// <param name="codewords">The data codewords followed by room for the check codewords.</param>
    /// <param name="dataCount">The number of data codewords.</param>
    /// <param name="size">The codeword size B.</param>
    private static void AddCheckCodewords(Span<int> codewords, int dataCount, int size)
    {
        GaloisField field = size switch
        {
            6 => Field6.Value,
            8 => Field8.Value,
            10 => Field10.Value,
            _ => Field12.Value,
        };

        int checkCount = codewords.Length - dataCount;
        using PooledBuffer<int> generatorBuffer = new(checkCount);
        Span<int> generator = generatorBuffer.Span;
        field.Generator(1, generator);
        field.Remainder(codewords, 1, dataCount, generator, codewords[dataCount..]);
    }

    /// <summary>
    /// Writes the mode message bits of section 7.2: the layer count less one and the data codeword count less one, as 4-bit datawords, followed by the GF(16)
    /// check words of section 7.2.4. A rune carries its value in place of the two counts, with alternate bits of every 4-bit word inverted.
    /// </summary>
    /// <param name="size">The symbol size.</param>
    /// <param name="dataCount">The number of data codewords, or the rune value.</param>
    /// <param name="rune">Whether the symbol is a rune.</param>
    /// <param name="bits">The buffer, one bit per byte.</param>
    /// <returns>The number of bits: 28 compact, 40 full-range.</returns>
    private static int WriteModeMessage(SymbolSize size, int dataCount, bool rune, Span<byte> bits)
    {
        bool compact = size.IsCompact;
        int dataWords = compact ? 2 : 4;
        int checkWords = compact ? 5 : 6;
        int layers = size.Layers;
        int value = rune ? dataCount : compact ? ((layers - 1) << 6) | (dataCount - 1) : ((layers - 1) << 11) | (dataCount - 1);

        InlineArray10<int> wordsStorage = default;
        Span<int> words = wordsStorage;
        for (int i = 0; i < dataWords; i++)
        {
            words[i] = (value >> ((dataWords - 1 - i) * 4)) & 0xF;
        }

        InlineArray6<int> generatorStorage = default;
        Span<int> generator = ((Span<int>)generatorStorage)[..checkWords];
        ModeField.Generator(1, generator);
        ModeField.Remainder(words, 1, dataWords, generator, words.Slice(dataWords, checkWords));

        int count = dataWords + checkWords;
        for (int i = 0; i < count; i++)
        {
            int word = rune ? words[i] ^ 0b1010 : words[i];
            for (int b = 0; b < 4; b++)
            {
                bits[(i * 4) + b] = (byte)((word >> (3 - b)) & 1);
            }
        }

        return count * 4;
    }

    /// <summary>
    /// Places the modules. The data layers spiral outward clockwise from the upper left of the Core Symbol in dominos of two modules, section 7.3.4, taking the
    /// codewords in reverse order. A full-range symbol is first laid out without its reference grid, then the grid lines of section 7.1.4 are inserted every 16
    /// modules from the centre, which moves every data module outward by the grid lines it crosses.
    /// </summary>
    /// <param name="symbolSize">The symbol size.</param>
    /// <param name="codewords">The data and check codewords.</param>
    /// <param name="mode">The mode message bits.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void Layout(SymbolSize symbolSize, ReadOnlySpan<int> codewords, ReadOnlySpan<byte> mode, double xDimension, ref BarcodeSymbol symbol)
    {
        bool compact = symbolSize.IsCompact;
        int layers = symbolSize.Layers;
        int size = symbolSize.CodewordBits;

        // The core is 11 x 11 modules in a compact symbol and 15 x 15 in a full-range symbol, finder and mode message ring included. Without the grid the
        // full-range core is 14 modules wide, because the grid line through the centre is not there yet. The core width here is the finder alone: 9 modules
        // in a compact symbol, and 13 less the grid line through the centre, 12, in a full-range symbol. The mode message ring is between it and layer 1.
        int coreWidth = compact ? 9 : 12;
        int packedSide = coreWidth + (layers * 4) + 2;
        using PooledBuffer<bool> packedBuffer = new(packedSide * packedSide);
        Span<bool> packed = packedBuffer.Span;
        packed.Clear();
        int packedMiddle = ((packedSide - 1) / 2 * packedSide) + ((packedSide - 1) / 2);

        // The bit stream fills the layers from the inside out with the last bit first. The codewords stand at the end of the layer capacity, so the outermost
        // dominos left over at the end of the spiral are light. Layer l, of width coreWidth + 4l, holds 8 (coreWidth + 4l + 4) bits, so L layers hold
        // 16 L^2 + (8 coreWidth + 16) L bits.
        int capacityBits = (16 * layers * layers) + (((8 * coreWidth) + 16) * layers);
        int offset = capacityBits - (codewords.Length * size);
        int index = 0;
        for (int layer = 1; layer <= layers; layer++)
        {
            int layerWidth = coreWidth + (layer * 4);
            for (int bit = 0; bit < layerWidth * 8; bit++)
            {
                int t = capacityBits - 1 - index++;
                bool dark = false;
                if (t >= offset)
                {
                    int codewordBit = t - offset;
                    dark = ((codewords[codewordBit / size] >> (size - 1 - (codewordBit % size))) & 1) == 1;
                }

                (int x, int y) = LayerPosition(coreWidth, layer, bit, layerWidth);
                packed[packedMiddle + x - (y * packedSide)] = dark;
            }
        }

        int side = packedSide;
        int finder = compact ? 4 : 6;
        Span<bool> modules;
        if (compact)
        {
            // A compact symbol has no reference grid, so the packed layout is the module grid.
            modules = symbol.BeginMatrix(side, side, 0, xDimension);
            packed.CopyTo(modules);
        }
        else
        {
            // Section 7.1.4: the grid occupies "every location where x is a multiple of 16 or y is a multiple of 16", each module (x + y + 1) mod 2. The grid
            // lines a layer crosses add to the side: one line for each 16 modules out from the centre, on both sides. The line count is (layers + 10.5) / 7.5 -
            // 1 rounded down, which integers give exactly as (2 x layers + 21) / 15 - 1.
            side = coreWidth + 1 + (layers * 4) + 2 + (2 * ((((2 * layers) + 21) / 15) - 1));
            modules = symbol.BeginMatrix(side, side, 0, xDimension);
            int half = side / 2;
            int next = 0;
            for (int row = 0; row < side; row++)
            {
                int y = half - row;
                for (int column = 0; column < side; column++)
                {
                    int x = column - half;
                    if (x % 16 == 0 || y % 16 == 0)
                    {
                        modules[(row * side) + column] = ((x + y + 1) & 1) == 1;
                    }
                    else
                    {
                        modules[(row * side) + column] = packed[next++];
                    }
                }
            }
        }

        int middle = side / 2;

        // Section 7.1.2: every module of the finder is ((max(abs(x), abs(y))) + 1) mod 2.
        for (int y = -finder; y <= finder; y++)
        {
            for (int x = -finder; x <= finder; x++)
            {
                modules[((middle - y) * side) + middle + x] = ((Math.Max(Math.Abs(x), Math.Abs(y)) + 1) & 1) == 1;
            }
        }

        ReadOnlySpan<sbyte> orientation = Orientation;
        for (int i = 0; i < orientation.Length; i += 3)
        {
            int x = Corner(orientation[i], finder);
            int y = Corner(orientation[i + 1], finder);
            modules[((middle - y) * side) + middle + x] = orientation[i + 2] == 1;
        }

        // Section 7.2.5: the mode message starts near the upper left corner of the finder and wraps clockwise, in four runs of 7 bits in a compact symbol and
        // eight runs of 5 in a full-range symbol, which skip the grid line through the centre of each side.
        int ring = finder + 1;
        int perSide = mode.Length / 4;
        for (int i = 0; i < mode.Length; i++)
        {
            int side4 = i / perSide;
            int along = i % perSide;
            int step = compact ? along - 3 : along < 5 ? along - 5 : along - 4;
            (int x, int y) = side4 switch
            {
                0 => (step, ring),
                1 => (ring, -step),
                2 => (-step, -ring),
                _ => (-ring, step),
            };

            modules[((middle - y) * side) + middle + x] = mode[i] == 1;
        }
    }

    /// <summary>
    /// Returns the position of a bit of a data layer in the layout without the reference grid: two modules per domino, the more significant bit outward,
    /// running along the top, down the right, back along the bottom and up the left.
    /// </summary>
    /// <param name="coreWidth">The width of the finder without the grid line through the centre. The mode message ring is outside it.</param>
    /// <param name="layer">The layer, from 1 inside.</param>
    /// <param name="bit">The bit within the layer.</param>
    /// <param name="layerWidth">The length of a side of the layer in dominos.</param>
    /// <returns>The x and y of the module, the y axis upward.</returns>
    private static (int X, int Y) LayerPosition(int coreWidth, int layer, int bit, int layerWidth)
    {
        int along = (bit / 2) % layerWidth;
        int depth = (layer * 2) + (bit % 2);
        return ((bit / 2) / layerWidth) switch
        {
            0 => (-((layerWidth - 1) / 2) + 1 + along, ((coreWidth - 1) / 2) + depth),
            1 => ((coreWidth / 2) + depth, ((layerWidth - 1) / 2) - 1 - along),
            2 => ((layerWidth / 2) - 1 - along, -((coreWidth / 2) + depth)),
            _ => (-(((coreWidth - 1) / 2) + depth), -((layerWidth / 2) - 1 - along)),
        };
    }

    /// <summary>
    /// Returns a coordinate of the orientation table: F + 1, F, -F or -F - 1 for 2, 1, -1 and -2.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="finder">The finder half width F.</param>
    /// <returns>The coordinate.</returns>
    private static int Corner(int code, int finder)
        => code switch
        {
            2 => finder + 1,
            1 => finder,
            -1 => -finder,
            _ => -finder - 1,
        };

    /// <summary>
    /// Returns the index of the latch from one state to another in <see cref="LatchBits"/>, the rows by the state latched from and the columns by the state
    /// latched to. Three times the index is the first latch code in <see cref="LatchCodes"/>.
    /// </summary>
    /// <param name="from">The state latched from.</param>
    /// <param name="to">The state latched to.</param>
    /// <returns>The table index.</returns>
    private static int LatchIndex(CodeSet from, CodeSet to) => ((int)from * StateCount) + (int)to;

    /// <summary>
    /// Returns the punctuation pair code of two characters, or 0 when they are no pair of Table 2.
    /// </summary>
    /// <param name="first">The first character, or -1.</param>
    /// <param name="second">The second character.</param>
    /// <returns>The pair code, or 0.</returns>
    private static int Pair(int first, int second)
        => (first, second) switch
        {
            ('\r', '\n') => PairCrLf,
            ('.', ' ') => PairPeriodSpace,
            (',', ' ') => PairCommaSpace,
            (':', ' ') => PairColonSpace,
            _ => 0,
        };

    /// <summary>
    /// Returns the Table 2 value of a sequence element in a code set.
    /// </summary>
    /// <param name="state">The code set.</param>
    /// <param name="element">The character, latch, shift or pair.</param>
    /// <returns>The value.</returns>
    private static int Value(CodeSet state, int element)
    {
        // The search puts a character in a code set only when the code set holds it, so the character is ASCII.
        if (element >= 0)
        {
            return CharacterValues[((int)state << 7) | element];
        }

        return (state, element) switch
        {
            (_, ShiftPunct) => 0,
            (CodeSet.Upper, LatchLower) or (CodeSet.Mixed, LatchLower) => 28,
            (CodeSet.Lower, ShiftUpper) => 28,
            (CodeSet.Upper, LatchMixed) or (CodeSet.Lower, LatchMixed) => 29,
            (CodeSet.Mixed, LatchUpper) => 29,
            (CodeSet.Upper, LatchDigit) or (CodeSet.Lower, LatchDigit) => 30,
            (CodeSet.Mixed, LatchPunct) => 30,
            (_, ShiftBinary) => 31,
            (CodeSet.Punct, LatchUpper) => 31,
            (CodeSet.Digit, LatchUpper) => 14,
            (CodeSet.Digit, ShiftUpper) => 15,
            (_, PairCrLf) => 2,
            (_, PairPeriodSpace) => 3,
            (_, PairCommaSpace) => 4,
            (_, PairColonSpace) => 5,
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// Writes a value most significant bit first.
    /// </summary>
    /// <param name="bits">The buffer, one bit per byte.</param>
    /// <param name="position">The position of the first bit.</param>
    /// <param name="value">The value.</param>
    /// <param name="count">The number of bits.</param>
    /// <returns>The position after the last bit.</returns>
    private static int Write(Span<byte> bits, int position, int value, int count)
    {
        for (int b = count - 1; b >= 0; b--)
        {
            bits[position++] = (byte)((value >> b) & 1);
        }

        return position;
    }

    /// <summary>
    /// The size of a symbol: compact or full-range, and the number of data layers around the core.
    /// </summary>
    private readonly struct SymbolSize
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SymbolSize"/> struct.
        /// </summary>
        /// <param name="isCompact">Whether the symbol is compact.</param>
        /// <param name="layers">The number of data layers, 0 for the core of a rune.</param>
        public SymbolSize(bool isCompact, int layers)
        {
            this.IsCompact = isCompact;
            this.Layers = layers;
        }

        /// <summary>
        /// Gets a value indicating whether the symbol is compact.
        /// </summary>
        public bool IsCompact { get; }

        /// <summary>
        /// Gets the number of data layers.
        /// </summary>
        public int Layers { get; }

        /// <summary>
        /// Gets the codeword size B of Table 3.
        /// </summary>
        public int CodewordBits => this.Layers <= 2 ? 6 : this.Layers <= 8 || this.IsCompact ? 8 : this.Layers <= 22 ? 10 : 12;

        /// <summary>
        /// Gets the number of codewords of Table 1, data and check together.
        /// </summary>
        public int Codewords => SymbolCodewords[this.IsCompact ? this.Layers - 1 : this.Layers + CompactSizeCount - 1];

        /// <summary>
        /// Gets the most data codewords the mode message can count. Section 7.2.3 gives the message length 6 bits in a compact symbol and 11 bits in a
        /// full-range symbol.
        /// </summary>
        public int MaximumDataCodewords => this.IsCompact ? 64 : 2048;

        /// <summary>
        /// Returns the symbol size at an index of <see cref="SymbolCodewords"/>.
        /// </summary>
        /// <param name="index">The index, in the order of section 11.5.</param>
        /// <returns>The symbol size.</returns>
        public static SymbolSize FromIndex(int index)
            => index < CompactSizeCount ? new SymbolSize(true, index + 1) : new SymbolSize(false, index - CompactSizeCount + 1);
    }

    /// <summary>
    /// One element of a search sequence.
    /// </summary>
    private struct Node
    {
        /// <summary>
        /// The element: a character as its byte value, 0 to 255, or the negative code of a pair, a latch or a shift.
        /// </summary>
        public int Value;

        /// <summary>
        /// The node before it, or -1 at the start of the sequence.
        /// </summary>
        public int Previous;

        /// <summary>
        /// The number of elements since the last B/S, which is the length of the byte run when the sequence is in the byte state.
        /// </summary>
        public int Run;

        /// <summary>
        /// The latch code or B/S nearest before or at this node, or 0 when the sequence has none, so the code set an element was written in reads without a
        /// walk back.
        /// </summary>
        public int Marker;
    }

    /// <summary>
    /// The nodes of the search sequences in one pooled buffer that doubles when it fills.
    /// </summary>
    private struct NodeArena : IDisposable
    {
        /// <summary>
        /// The nodes. The arena owns the buffer and returns it in <see cref="Dispose"/>.
        /// </summary>
        private PooledBuffer<Node> buffer;

        /// <summary>
        /// The number of nodes in use.
        /// </summary>
        private int count;

        /// <summary>
        /// Initializes a new instance of the <see cref="NodeArena"/> struct.
        /// </summary>
        /// <param name="capacity">The initial number of nodes.</param>
        public NodeArena(int capacity)
        {
            this.buffer = new PooledBuffer<Node>(Math.Max(capacity, 64));
            this.count = 0;
        }

        /// <summary>
        /// Gets a node.
        /// </summary>
        /// <param name="index">The node index.</param>
        /// <returns>The node.</returns>
        public readonly ref Node this[int index] => ref this.buffer.Span[index];

        /// <summary>
        /// Adds a node after another.
        /// </summary>
        /// <param name="previous">The node before it, or -1 for the start of a sequence.</param>
        /// <param name="value">The element.</param>
        /// <returns>The index of the new node.</returns>
        public int Append(int previous, int value)
        {
            Span<Node> nodes = this.buffer.Span;
            if (this.count == nodes.Length)
            {
                PooledBuffer<Node> larger = new(nodes.Length * 2);
                nodes.CopyTo(larger.Span);
                this.buffer.Dispose();
                this.buffer = larger;
                nodes = larger.Span;
            }

            int run = value == ShiftBinary ? 0 : previous < 0 ? 1 : nodes[previous].Run + 1;
            int marker = value is (>= LatchDigit and <= LatchUpper) or ShiftBinary ? value : previous < 0 ? 0 : nodes[previous].Marker;
            nodes[this.count] = new Node { Value = value, Previous = previous, Run = run, Marker = marker };
            return this.count++;
        }

        /// <inheritdoc/>
        public void Dispose() => this.buffer.Dispose();
    }
}
