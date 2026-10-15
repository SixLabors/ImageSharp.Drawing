// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodes Aztec Code symbols, ISO/IEC 24778. The text becomes a bit stream in the five code sets and the
/// byte shift of Table 2, the bit stream becomes B-bit codewords with no codeword of all zeros or all ones,
/// Reed-Solomon check codewords fill the symbol, and the codewords spiral outward from the Core Symbol in
/// reverse order. The mode search follows the reference encoder BWIPP, which finds the shortest sequence of
/// latches, shifts and characters for the whole message.
/// </summary>
internal static class AztecEncoder
{
    /// <summary>
    /// The recommended error correction level of section 11.3: "23% of symbol capacity plus 3 codewords more".
    /// </summary>
    public const int DefaultErrorCorrectionPercentage = 23;

    /// <summary>
    /// The longest input. The largest symbol holds 1664 codewords of 12 bits, 19968 bits, and no character
    /// takes fewer than 2.5 bits: the punctuation pairs of Table 2 put two characters in 5 bits.
    /// </summary>
    public const int MaximumLength = 19968 * 2 / 5;

    /// <summary>
    /// The check codewords added to the percentage, section 11.3: "plus always, for data security, three
    /// additional checkwords".
    /// </summary>
    private const int ExtraCheckCodewords = 3;

    private const int Upper = 0;
    private const int Lower = 1;
    private const int Mixed = 2;
    private const int Punct = 3;
    private const int Digit = 4;
    private const int Binary = 5;
    private const int StateCount = 6;

    /// <summary>
    /// The cost of a state that the search has not reached.
    /// </summary>
    private const int Unreached = 1_000_000;

    // The sequence elements that are not characters. A character is its byte value, 0 to 255.
    private const int LatchUpper = -2;
    private const int LatchLower = -3;
    private const int LatchMixed = -4;
    private const int LatchPunct = -5;
    private const int LatchDigit = -6;
    private const int ShiftUpper = -7;
    private const int ShiftPunct = -8;
    private const int ShiftBinary = -9;
    private const int PairCrLf = -11;
    private const int PairPeriodSpace = -12;
    private const int PairCommaSpace = -13;
    private const int PairColonSpace = -14;

    /// <summary>
    /// The field of the mode message, section 7.2.4: GF(16) with the prime modulus polynomial x^4 + x + 1.
    /// </summary>
    private static readonly GaloisField ModeField = new(16, 19);

    /// <summary>
    /// The fields of the data codewords of 6, 10 and 12 bits, Table 3. The 8-bit field, x^8 + x^5 + x^3 +
    /// x^2 + 1, is the field of Data Matrix.
    /// </summary>
    private static readonly GaloisField Field6 = new(64, 67);
    private static readonly GaloisField Field10 = new(1024, 1033);
    private static readonly GaloisField Field12 = new(4096, 4201);

    /// <summary>
    /// The value of each byte in each code set, Table 2, at index state x 256 + byte, or 0xFF where the
    /// code set has no such character.
    /// </summary>
    private static readonly byte[] CharacterValues = BuildCharacterValues();

    /// <summary>
    /// Gets the bits each code set writes per character, latch or shift: 5, and 4 in the digit code set, and
    /// 8 for each byte of a byte shift.
    /// </summary>
    private static ReadOnlySpan<byte> CharacterBits => [5, 5, 5, 5, 4, 8];

    /// <summary>
    /// Gets the bits of the cheapest latch from each state, row, to each state, column. A latch into the byte
    /// state is a B/S and its 5-bit length, and the return from it costs nothing because the length ends it.
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
    /// Gets the bits of a shift from each state, row, to each state, column: U/S from the lower and digit
    /// code sets and P/S from every code set but punctuation. Unreached marks no shift.
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
    /// Gets the codeword count of Table 1 for each symbol in the order of section 11.5: the compact symbols of
    /// 1 to 4 layers, then the full-range symbols of 1 to 32 layers.
    /// </summary>
    private static ReadOnlySpan<short> SymbolCodewords =>
    [
        17, 40, 51, 76,
        21, 48, 60, 88, 120, 156, 196, 240, 230, 272, 316, 364, 416, 470, 528, 588, 652, 720, 790, 864, 940, 1020,
        920, 992, 1066, 1144, 1224, 1306, 1392, 1480, 1570, 1664,
    ];

    /// <summary>
    /// Gets the orientation patterns of section 7.1.3 for a finder of half width F, as x and y offsets from F
    /// and the module: the six dark modules, then the six light ones. An offset of 1 is F + 1, 0 is F, and the
    /// signs follow.
    /// </summary>
    private static ReadOnlySpan<sbyte> Orientation =>
    [
        -2, 1, 1, -2, 2, 1, -1, 2, 1, 2, 2, 1, 2, 1, 1, 2, -1, 1,
        1, 2, 0, 2, -2, 0, 1, -2, 0, -1, -2, 0, -2, -2, 0, -2, -1, 0,
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
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] > 0xFF)
            {
                throw new ArgumentException($"Aztec Code encodes U+0000 to U+00FF; got U+{(int)text[i]:X4}.", nameof(text));
            }
        }

        PooledBuffer<byte> bits = default;
        PooledBuffer<int> codewords = default;
        try
        {
            int bitCount = EncodeBits(text, ref bits);
            ReadOnlySpan<byte> stream = bits.Span[..bitCount];

            // The codeword count after stuffing depends only on the codeword size, so each size is laid out
            // once, and every symbol of that size reads the count.
            Span<int> dataCounts = stackalloc int[13];
            dataCounts.Fill(-1);

            int chosen = -1;
            int start = format == AztecFormat.Full ? 4 : 0;
            int end = format == AztecFormat.Compact ? 4 : SymbolCodewords.Length;
            for (int index = start; index < end; index++)
            {
                bool compact = index < 4;
                int symbolLayers = compact ? index + 1 : index - 3;

                // Section 11.5: printing programs "print the smallest symbol for a given data set and error
                // correction percentage in the data layer sequence 1*,2*,3*,4*,4,5,6,...,32". Full-range
                // symbols of 1 to 3 layers are "useful only for reader initialization", so the search skips
                // them unless the caller asks for that size.
                if (layers > 0 ? symbolLayers != layers : (format == AztecFormat.Auto && !compact && symbolLayers < 4))
                {
                    continue;
                }

                int total = SymbolCodewords[index];
                int size = CodewordBits(compact, symbolLayers);
                if (dataCounts[size] < 0)
                {
                    dataCounts[size] = CountCodewords(stream, size);
                }

                // Section 11.3: the percentage of the symbol capacity plus three codewords is the minimum
                // number of check codewords. Section 7.2.3 gives the message length 6 bits in a compact symbol
                // and 11 bits in a full-range symbol, 64 and 2048 codewords at most.
                int check = (((total * errorCorrectionPercentage) + 99) / 100) + ExtraCheckCodewords;
                int data = dataCounts[size];
                if (data + check <= total && data <= (compact ? 64 : 2048))
                {
                    chosen = index;
                    break;
                }
            }

            if (chosen < 0)
            {
                throw new ArgumentException("The text does not fit in an Aztec Code symbol of the requested size.", nameof(text));
            }

            bool isCompact = chosen < 4;
            int layerCount = isCompact ? chosen + 1 : chosen - 3;
            int codewordBits = CodewordBits(isCompact, layerCount);
            int capacity = SymbolCodewords[chosen];
            codewords = new PooledBuffer<int>(capacity);
            Span<int> all = codewords.Span;
            int dataCount = Stuff(stream, codewordBits, all);
            AddCheckCodewords(all, dataCount, codewordBits);

            Span<byte> mode = stackalloc byte[40];
            int modeLength = WriteModeMessage(isCompact, layerCount, dataCount, false, mode);
            Layout(isCompact, layerCount, all, codewordBits, mode[..modeLength], xDimension, ref symbol);
        }
        finally
        {
            bits.Dispose();
            codewords.Dispose();
        }
    }

    /// <summary>
    /// Builds an Aztec Rune, Annex A: the Core Symbol of a compact symbol whose mode message carries one
    /// value of 0 to 255.
    /// </summary>
    /// <param name="value">The value, 0 to 255.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildRune(int value, double xDimension, ref BarcodeSymbol symbol)
    {
        Span<byte> mode = stackalloc byte[28];
        WriteModeMessage(true, 0, value, true, mode);
        Layout(true, 0, [], 0, mode, xDimension, ref symbol);
    }

    /// <summary>
    /// Translates the text into the bit stream of section 7.3.2.2. The search keeps, for each code set, the
    /// cheapest sequence of characters, latches and shifts that ends in it, as BWIPP does. The sequences
    /// share their common start: each is a node that points back to the element before it, so a copy is one
    /// index and an extension is one new node.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="bits">The buffer the bits are written to, one bit per byte, rented here.</param>
    /// <returns>The number of bits.</returns>
    private static int EncodeBits(ReadOnlySpan<char> text, ref PooledBuffer<byte> bits)
    {
        NodeArena arena = new(text.Length * 8);
        try
        {
            Span<int> currentCost = stackalloc int[StateCount];
            Span<int> nextCost = stackalloc int[StateCount];
            Span<int> currentHead = stackalloc int[StateCount];
            Span<int> nextHead = stackalloc int[StateCount];
            Span<int> after = stackalloc int[16];
            currentCost.Fill(Unreached);
            currentCost[Upper] = 0;
            currentHead.Fill(-1);

            // Encoding starts in the upper code set. The byte state returns to the code set it shifted from,
            // which the punctuation and digit code sets reach through upper.
            int backTo = Upper;
            int lastCharacter = -1;
            for (int t = 0; t < text.Length; t++)
            {
                int character = text[t];

                // A latch can make the sequence of another state cheaper. Each improvement can enable another,
                // so the pass repeats until none applies.
                bool improved;
                do
                {
                    improved = false;
                    for (int x = 0; x < StateCount; x++)
                    {
                        for (int y = 0; y < StateCount; y++)
                        {
                            if (x == Binary && y != backTo)
                            {
                                continue;
                            }

                            int cost = currentCost[x] + LatchBits[(x * StateCount) + y];
                            if (cost < currentCost[y])
                            {
                                currentCost[y] = cost;
                                int head = currentHead[x];
                                ReadOnlySpan<sbyte> codes = LatchCodes.Slice(((x * StateCount) + y) * 3, 3);
                                for (int c = 0; c < codes.Length && codes[c] != 0; c++)
                                {
                                    head = arena.Append(head, codes[c]);
                                }

                                currentHead[y] = head;
                                if (y == Binary)
                                {
                                    backTo = x is Punct or Digit ? Upper : x;
                                }

                                improved = true;
                            }
                        }
                    }
                }
                while (improved);

                // Each state that holds the character extends its own sequence, and the other code sets can
                // reach it with a shift.
                nextCost.Fill(Unreached);
                nextHead.Fill(-1);
                for (int x = 0; x < StateCount; x++)
                {
                    if (x != Binary && CharacterValues[(x << 8) | character] == 0xFF)
                    {
                        continue;
                    }

                    int cost = currentCost[x] + CharacterBits[x];
                    if (cost < nextCost[x])
                    {
                        nextCost[x] = cost;
                        nextHead[x] = arena.Append(currentHead[x], character);
                    }

                    if (x == Binary)
                    {
                        continue;
                    }

                    for (int y = 0; y < Binary; y++)
                    {
                        if (x == y)
                        {
                            continue;
                        }

                        cost = currentCost[y] + ShiftBits[(y * 5) + x] + CharacterBits[x];
                        if (cost < nextCost[y])
                        {
                            nextCost[y] = cost;
                            nextHead[y] = arena.Append(arena.Append(currentHead[y], x == Upper ? ShiftUpper : ShiftPunct), character);
                        }
                    }
                }

                // A punctuation pair of Table 2 puts the previous character and this one in one 5-bit value.
                int pair = Pair(lastCharacter, character);
                if (pair != 0)
                {
                    for (int i = 0; i < Binary; i++)
                    {
                        // The mixed code set holds CR and the digit code set holds the comma and the period
                        // themselves, so a sequence in those states did not reach the first character
                        // through punctuation.
                        bool inPunct = !(i == Mixed && lastCharacter == 13) && !(i == Digit && lastCharacter is 44 or 46);
                        if (!inPunct || currentCost[i] >= nextCost[i])
                        {
                            continue;
                        }

                        // The scan walks back to the previous character, then on to the latch that set the
                        // code set it was written in. A byte shift on the way means it was a byte.
                        bool latchDigit = false;
                        bool shifted = false;
                        int found = -1;
                        int node = currentHead[i];
                        while (node >= 0)
                        {
                            int value = arena[node].Value;
                            if (found < 0)
                            {
                                if (value == lastCharacter)
                                {
                                    found = node;
                                    int previous = arena[node].Previous;
                                    shifted = previous >= 0 && arena[previous].Value == ShiftPunct;
                                }
                            }
                            else
                            {
                                if (value == ShiftBinary)
                                {
                                    found = -1;
                                    break;
                                }

                                if (value is >= LatchDigit and <= LatchUpper)
                                {
                                    if (i == Punct)
                                    {
                                        latchDigit = value == LatchDigit;
                                    }
                                    else if (value != LatchPunct)
                                    {
                                        inPunct = shifted;
                                    }

                                    break;
                                }
                            }

                            node = arena[node].Previous;
                        }

                        if (!inPunct || found < 0)
                        {
                            continue;
                        }

                        nextCost[i] = currentCost[i];

                        // The elements after the previous character are the latches this step added. The pair
                        // replaces the character where it stands, except in punctuation, where it follows
                        // the latches into that code set.
                        int count = 0;
                        for (int n = currentHead[i]; n != found; n = arena[n].Previous)
                        {
                            after[count++] = arena[n].Value;
                        }

                        int rebuilt = arena[found].Previous;
                        if (i != Punct)
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

                        if (i == Punct)
                        {
                            rebuilt = arena.Append(rebuilt, pair);
                        }

                        nextHead[i] = rebuilt;
                    }
                }

                // Section 7.3.2.2: a byte shift of more than 31 bytes takes an 11-bit length after a zero.
                if (nextHead[Binary] >= 0 && arena[nextHead[Binary]].Run == 32)
                {
                    nextCost[Binary] += 11;
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
        int state = Upper;
        int written = 0;
        int i = 0;
        while (i < sequence.Length)
        {
            if (state != Binary)
            {
                int element = sequence[i++];
                written = Write(bits, written, Value(state, element), CharacterBits[state]);
                if (element is ShiftUpper or ShiftPunct)
                {
                    int shiftState = element == ShiftUpper ? Upper : Punct;
                    written = Write(bits, written, Value(shiftState, sequence[i++]), 5);
                }

                state = element switch
                {
                    LatchUpper => Upper,
                    LatchLower => Lower,
                    LatchMixed => Mixed,
                    LatchPunct => Punct,
                    LatchDigit => Digit,
                    ShiftBinary => Binary,
                    _ => state,
                };

                continue;
            }

            // Section 7.3.2.2: the B/S is followed by the byte count, 1 to 31 in 5 bits, or 0 and the count
            // less 31 in 11 bits.
            int byteCount = 0;
            while (i + byteCount < sequence.Length && sequence[i + byteCount] >= 0 && byteCount < 2078)
            {
                byteCount++;
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

            // "At the end of the byte string, encoding returns to the mode from which B/S was invoked." The
            // sequence marks that mode with the latch code the search added.
            if (i < sequence.Length)
            {
                state = sequence[i++] switch
                {
                    LatchLower => Lower,
                    LatchMixed => Mixed,
                    _ => Upper,
                };
            }
        }

        return written;
    }

    /// <summary>
    /// Lays the bit stream into codewords, section 7.3.2.3: "whenever the first B-1 bits placed in a codeword
    /// are all 0's, then a dummy 1 is inserted into that codeword's LSB and the following message bit starts
    /// off the next codeword", and the same with a dummy 0 after B-1 ones. The final codeword is "padded out
    /// with 1's (and possibly a final dummy 0 if necessary)".
    /// </summary>
    /// <param name="bits">The bit stream.</param>
    /// <param name="size">The codeword size B.</param>
    /// <param name="codewords">The buffer the codewords are written to.</param>
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
    /// Counts the codewords of the bit stream at a codeword size without keeping them.
    /// </summary>
    /// <param name="bits">The bit stream.</param>
    /// <param name="size">The codeword size B.</param>
    /// <returns>The number of codewords.</returns>
    private static int CountCodewords(ReadOnlySpan<byte> bits, int size) => Stuff(bits, size, []);

    /// <summary>
    /// Adds the check codewords of section 7.3.3 after the data codewords: the remainder of the data
    /// polynomial times x^K divided by (x - 2^1) ... (x - 2^K) over the field of Table 3.
    /// </summary>
    /// <param name="codewords">The data codewords followed by room for the check codewords.</param>
    /// <param name="dataCount">The number of data codewords.</param>
    /// <param name="size">The codeword size B.</param>
    private static void AddCheckCodewords(Span<int> codewords, int dataCount, int size)
    {
        GaloisField field = size switch
        {
            6 => Field6,
            8 => GaloisField.DataMatrix,
            10 => Field10,
            _ => Field12,
        };

        int checkCount = codewords.Length - dataCount;
        using PooledBuffer<int> generatorBuffer = new(checkCount);
        Span<int> generator = generatorBuffer.Span;
        field.Generator(1, generator);
        field.Remainder(codewords, 1, dataCount, generator, codewords[dataCount..]);
    }

    /// <summary>
    /// Writes the mode message bits of section 7.2: the layer count less one and the data codeword count less
    /// one, as 4-bit datawords, followed by the GF(16) check words of section 7.2.4. A rune carries its value
    /// in place of the two counts, and Annex A inverts alternate bits of every 4-bit word.
    /// </summary>
    /// <param name="compact">Whether the symbol is compact.</param>
    /// <param name="layers">The number of data layers.</param>
    /// <param name="dataCount">The number of data codewords, or the rune value.</param>
    /// <param name="rune">Whether the symbol is a rune.</param>
    /// <param name="bits">The buffer, one bit per byte.</param>
    /// <returns>The number of bits: 28 compact, 40 full-range.</returns>
    private static int WriteModeMessage(bool compact, int layers, int dataCount, bool rune, Span<byte> bits)
    {
        int dataWords = compact ? 2 : 4;
        int checkWords = compact ? 5 : 6;
        int value = rune ? dataCount : compact ? ((layers - 1) << 6) | (dataCount - 1) : ((layers - 1) << 11) | (dataCount - 1);

        Span<int> words = stackalloc int[10];
        for (int i = 0; i < dataWords; i++)
        {
            words[i] = (value >> ((dataWords - 1 - i) * 4)) & 0xF;
        }

        Span<int> generator = stackalloc int[checkWords];
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
    /// Places the modules. The data layers spiral outward clockwise from the upper left of the Core Symbol
    /// in dominos of two modules, section 7.3.4, taking the codewords in reverse order. A full-range symbol
    /// is first laid out without its reference grid, then the grid lines of section 7.1.4 are inserted every
    /// 16 modules from the centre, which moves every data module outward by the grid lines it crosses.
    /// </summary>
    /// <param name="compact">Whether the symbol is compact.</param>
    /// <param name="layers">The number of data layers.</param>
    /// <param name="codewords">The data and check codewords.</param>
    /// <param name="size">The codeword size B.</param>
    /// <param name="mode">The mode message bits.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void Layout(
        bool compact,
        int layers,
        ReadOnlySpan<int> codewords,
        int size,
        ReadOnlySpan<byte> mode,
        double xDimension,
        ref BarcodeSymbol symbol)
    {
        // The core is 11 x 11 modules in a compact symbol and 15 x 15 in a full-range symbol, finder and mode
        // message ring included. Without the grid the full-range core is 14 modules wide, because the grid
        // line through the centre is not there yet.
        int coreWidth = compact ? 9 : 12;
        int packedSide = coreWidth + (layers * 4) + 2;
        using PooledBuffer<sbyte> packedBuffer = new(packedSide * packedSide);
        Span<sbyte> packed = packedBuffer.Span;
        packed.Clear();
        int packedMiddle = ((packedSide - 1) / 2 * packedSide) + ((packedSide - 1) / 2);

        // The bit stream fills the layers from the inside out with the last bit first. The codewords stand at
        // the end of the layer capacity, so the outermost dominos left over at the end of the spiral are light.
        int capacityBits = (16 * layers * layers) + ((compact ? 88 : 112) * layers);
        int offset = capacityBits - (codewords.Length * size);
        int index = 0;
        for (int layer = 1; layer <= layers; layer++)
        {
            int layerWidth = coreWidth + (layer * 4);
            for (int bit = 0; bit < layerWidth * 8; bit++)
            {
                int t = capacityBits - 1 - index++;
                int value = 0;
                if (t >= offset)
                {
                    int codewordBit = t - offset;
                    value = (codewords[codewordBit / size] >> (size - 1 - (codewordBit % size))) & 1;
                }

                (int x, int y) = LayerPosition(coreWidth, layer, bit, layerWidth);
                packed[packedMiddle + x - (y * packedSide)] = (sbyte)value;
            }
        }

        int side = packedSide;
        int finder = compact ? 4 : 6;
        Span<bool> modules;
        if (compact)
        {
            modules = symbol.BeginMatrix(side, side, 0, xDimension);
            for (int i = 0; i < packed.Length; i++)
            {
                modules[i] = packed[i] == 1;
            }
        }
        else
        {
            // Section 7.1.4: the grid occupies "every location where x is a multiple of 16 or y is a multiple
            // of 16", each module (x + y + 1) mod 2. The grid lines a layer crosses add to the side: one line
            // for each 16 modules out from the centre, on both sides.
            side = coreWidth + 1 + (layers * 4) + 2 + (2 * (int)(((layers + 10.5) / 7.5) - 1));
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
                        modules[(row * side) + column] = packed[next++] == 1;
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

        // Section 7.2.5: the mode message starts near the upper left corner of the finder and wraps
        // clockwise, in four runs of 7 bits in a compact symbol and eight runs of 5 in a full-range symbol,
        // which skip the grid line through the centre of each side.
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
    /// Returns the position of a bit of a data layer in the layout without the reference grid: two modules
    /// per domino, the more significant bit outward, running along the top, down the right, back along the
    /// bottom and up the left.
    /// </summary>
    /// <param name="coreWidth">The width of the core region the layers surround.</param>
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
    /// Returns the codeword size of Table 3 for a symbol.
    /// </summary>
    /// <param name="compact">Whether the symbol is compact.</param>
    /// <param name="layers">The number of data layers.</param>
    /// <returns>The codeword size B.</returns>
    private static int CodewordBits(bool compact, int layers)
        => layers <= 2 ? 6 : layers <= 8 || compact ? 8 : layers <= 22 ? 10 : 12;

    /// <summary>
    /// Returns the punctuation pair code of two characters, or 0 when they are no pair of Table 2.
    /// </summary>
    /// <param name="first">The first character, or -1.</param>
    /// <param name="second">The second character.</param>
    /// <returns>The pair code, or 0.</returns>
    private static int Pair(int first, int second)
        => (first, second) switch
        {
            (13, 10) => PairCrLf,
            (46, 32) => PairPeriodSpace,
            (44, 32) => PairCommaSpace,
            (58, 32) => PairColonSpace,
            _ => 0,
        };

    /// <summary>
    /// Returns the Table 2 value of a sequence element in a code set.
    /// </summary>
    /// <param name="state">The code set.</param>
    /// <param name="element">The character, latch, shift or pair.</param>
    /// <returns>The value.</returns>
    private static int Value(int state, int element)
    {
        if (element >= 0)
        {
            return CharacterValues[(state << 8) | element];
        }

        return (state, element) switch
        {
            (_, ShiftPunct) => 0,
            (Upper, LatchLower) or (Mixed, LatchLower) => 28,
            (Lower, ShiftUpper) => 28,
            (Upper, LatchMixed) or (Lower, LatchMixed) => 29,
            (Mixed, LatchUpper) => 29,
            (Upper, LatchDigit) or (Lower, LatchDigit) => 30,
            (Mixed, LatchPunct) => 30,
            (_, ShiftBinary) => 31,
            (Punct, LatchUpper) => 31,
            (Digit, LatchUpper) => 14,
            (Digit, ShiftUpper) => 15,
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
    /// Builds the character values of Table 2 from the character each value of each code set stands for.
    /// </summary>
    /// <returns>The values, state x 256 + byte, 0xFF where the code set has no such character.</returns>
    private static byte[] BuildCharacterValues()
    {
        // Each code set lists the byte of each value, 0 where the value is a latch, a shift or a pair.
        ReadOnlySpan<byte> upper = [0, 32, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90];
        ReadOnlySpan<byte> lower =
            [0, 32, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122];
        ReadOnlySpan<byte> mixed = [0, 32, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 27, 28, 29, 30, 31, 64, 92, 94, 95, 96, 124, 126, 127];
        ReadOnlySpan<byte> punct =
            [0, 13, 0, 0, 0, 0, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 58, 59, 60, 61, 62, 63, 91, 93, 123, 125];
        ReadOnlySpan<byte> digit = [0, 32, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 44, 46];

        byte[] values = new byte[5 << 8];
        values.AsSpan().Fill(0xFF);
        Fill(values, Upper, upper);
        Fill(values, Lower, lower);
        Fill(values, Mixed, mixed);
        Fill(values, Punct, punct);
        Fill(values, Digit, digit);
        return values;

        static void Fill(byte[] values, int state, ReadOnlySpan<byte> characters)
        {
            for (int value = 1; value < characters.Length; value++)
            {
                if (characters[value] != 0)
                {
                    values[(state << 8) | characters[value]] = (byte)value;
                }
            }
        }
    }

    /// <summary>
    /// One element of a search sequence: the element, the node before it, and the number of elements since
    /// the last B/S, which is the length of the byte run when the sequence is in the byte state.
    /// </summary>
    private struct Node
    {
        public int Value;
        public int Previous;
        public int Run;
    }

    /// <summary>
    /// The nodes of the search sequences in one pooled buffer that doubles when it fills.
    /// </summary>
    private struct NodeArena : IDisposable
    {
        private PooledBuffer<Node> buffer;
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
            nodes[this.count] = new Node { Value = value, Previous = previous, Run = run };
            return this.count++;
        }

        /// <inheritdoc/>
        public void Dispose() => this.buffer.Dispose();
    }
}
