// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the PosiCode symbology, which AIM ITS/02-001 defines. Every character is three bars of one module and three spaces, and the data lies in the
/// positions of the bars, whose centres are 2G to 9G apart in version A and 3G to 10G apart in version B, where G is the bar width. A symbol is a quiet zone, a
/// start character, the data, a check character and a stop character. The data characters come from three character sets with latches and shifts between them,
/// characters above 127 are announced by the function 4 character, and the check character maps a cyclic redundancy check value to six bar positions.
/// </summary>
internal static class PosiCodeEncoder
{
    /// <summary>
    /// The largest number of characters a symbol carries. The symbology sets no maximum.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The number of characters a caller holds inline to build symbol data in. Longer data rents its buffer once.
    /// </summary>
    public const int InlineBufferLength = 64;

    /// <summary>
    /// The bar height as a fraction of the symbol width, quiet zones excluded, when the caller sets no height: 15 per cent.
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <summary>
    /// The number of data codewords in the limited character set, the first 38 codewords of set 0.
    /// </summary>
    private const int LimitedCodewords = 38;

    /// <summary>
    /// The number of runs in one data character: three bars and three spaces.
    /// </summary>
    private const int RunsPerCharacter = 6;

    /// <summary>
    /// The number of runs in the check character: six bars and six spaces.
    /// </summary>
    private const int CheckRuns = 12;

    /// <summary>
    /// The number of bar distances the check value maps to.
    /// </summary>
    private const int CheckDistances = 6;

    /// <summary>
    /// The sum of the six check distances before the version B offset.
    /// </summary>
    private const int CheckDistanceSum = 20;

    /// <summary>
    /// The polynomial the check divides by, applied bit by bit to the six bit codewords.
    /// </summary>
    private const int CheckPolynomial = 7682;

    /// <summary>
    /// The number of bits of each codeword the check division reads.
    /// </summary>
    private const int CodewordBits = 6;

    /// <summary>
    /// The mask of the ten bit remainder of the check division.
    /// </summary>
    private const int CheckValueMask = 1023;

    /// <summary>
    /// The offset the standard versions add to the check remainder.
    /// </summary>
    private const int StandardCheckOffset = 45;

    /// <summary>
    /// The first check remainder of the range the limited versions reserve.
    /// </summary>
    private const int LimitedReservedFirst = 825;

    /// <summary>
    /// The last check remainder of the range the limited versions reserve.
    /// </summary>
    private const int LimitedReservedLast = 852;

    /// <summary>
    /// The step that moves a limited check remainder out of the reserved range.
    /// </summary>
    private const int LimitedReservedStep = 292;

    /// <summary>
    /// The number of entries in each row of <see cref="CheckWeights"/>.
    /// </summary>
    private const int CheckWeightColumns = 8;

    /// <summary>
    /// The message value of the function 4 character. It is one past the ASCII values, so <see cref="CharacterCodes"/> holds it beside the characters.
    /// </summary>
    private const byte Function4 = 128;

    /// <summary>
    /// The codeword of the latch to the other one of sets 0 and 1. Both sets hold it at the same codeword.
    /// </summary>
    private const int LatchCodeword = 43;

    /// <summary>
    /// The codeword of the shift to the other one of sets 0 and 1 for one character. Both sets hold it at the same codeword.
    /// </summary>
    private const int ShiftCodeword = 44;

    /// <summary>
    /// The codeword of the shift to set 2 for one character. Both sets 0 and 1 hold it at the same codeword.
    /// </summary>
    private const int ShiftToSet2Codeword = 45;

    /// <summary>
    /// The number of low bits of a <see cref="CharacterCodes"/> entry that hold the codeword. The bits above hold the character set.
    /// </summary>
    private const int CodewordBitCount = 6;

    /// <summary>
    /// The mask of the codeword bits of a <see cref="CharacterCodes"/> entry.
    /// </summary>
    private const int CodewordMask = (1 << CodewordBitCount) - 1;

    /// <summary>
    /// The character sets of the standard versions. Every ASCII character is in exactly one set.
    /// </summary>
    private enum CharacterSet
    {
        /// <summary>
        /// Set 0: the digits, the capital letters and the symbols - . space $ / + %, then the latch to set 1 and the shifts to sets 1 and 2.
        /// </summary>
        Set0,

        /// <summary>
        /// Set 1: the symbols ^ ; &lt; = &gt; ? @ [ \ ], the small letters and the symbols _ ` DEL { | } ~, then the latch to set 0 and the shifts to sets 0
        /// and 2.
        /// </summary>
        Set1,

        /// <summary>
        /// Set 2: the apostrophe, the control characters 27 to 31, the symbols ! " # &amp;, the control characters 1 to 26, the symbols ( ) NUL * , : and the
        /// four function characters.
        /// </summary>
        Set2,
    }

    /// <summary>
    /// Gets the character set and the codeword of each message value: the ASCII characters 0 to 127 and then <see cref="Function4"/>. Each entry is the set
    /// shifted left by <see cref="CodewordBitCount"/> bits, combined with the codeword.
    /// </summary>
    private static ReadOnlySpan<byte> CharacterCodes =>
    [
        0xA6, 0x8A, 0x8B, 0x8C, 0x8D, 0x8E, 0x8F, 0x90, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
        0x99, 0x9A, 0x9B, 0x9C, 0x9D, 0x9E, 0x9F, 0xA0, 0xA1, 0xA2, 0xA3, 0x81, 0x82, 0x83, 0x84, 0x85,
        0x26, 0x86, 0x87, 0x88, 0x27, 0x2A, 0x89, 0x80, 0xA4, 0xA5, 0xA7, 0x29, 0xA8, 0x24, 0x25, 0x28,
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0xA9, 0x41, 0x42, 0x43, 0x44, 0x45,
        0x46, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x47, 0x48, 0x49, 0x40, 0x64,
        0x65, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
        0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F, 0x60, 0x61, 0x62, 0x63, 0x67, 0x68, 0x69, 0x6A, 0x66,
        0xAD,
    ];

    /// <summary>
    /// Gets the run widths of the 46 data codewords of version A, six per codeword in bar and space order.
    /// </summary>
    private static ReadOnlySpan<byte> CharactersA =>
    [
        1, 4, 1, 1, 1, 2, 1, 3, 1, 2, 1, 2, 1, 2, 1, 3, 1, 2, 1, 1, 1, 4, 1, 2, 1, 3, 1, 1, 1, 3,
        1, 2, 1, 2, 1, 3, 1, 1, 1, 3, 1, 3, 1, 2, 1, 1, 1, 4, 1, 1, 1, 2, 1, 4, 1, 1, 1, 1, 1, 5,
        1, 8, 1, 1, 1, 1, 1, 7, 1, 2, 1, 1, 1, 6, 1, 3, 1, 1, 1, 5, 1, 4, 1, 1, 1, 4, 1, 5, 1, 1,
        1, 3, 1, 6, 1, 1, 1, 2, 1, 7, 1, 1, 1, 1, 1, 8, 1, 1, 1, 7, 1, 1, 1, 2, 1, 6, 1, 2, 1, 2,
        1, 5, 1, 3, 1, 2, 1, 4, 1, 4, 1, 2, 1, 3, 1, 5, 1, 2, 1, 2, 1, 6, 1, 2, 1, 1, 1, 7, 1, 2,
        1, 6, 1, 1, 1, 3, 1, 5, 1, 2, 1, 3, 1, 4, 1, 3, 1, 3, 1, 3, 1, 4, 1, 3, 1, 2, 1, 5, 1, 3,
        1, 1, 1, 6, 1, 3, 1, 5, 1, 1, 1, 4, 1, 4, 1, 2, 1, 4, 1, 3, 1, 3, 1, 4, 1, 2, 1, 4, 1, 4,
        1, 1, 1, 5, 1, 4, 1, 4, 1, 1, 1, 5, 1, 3, 1, 2, 1, 5, 1, 2, 1, 3, 1, 5, 1, 1, 1, 4, 1, 5,
        1, 3, 1, 1, 1, 6, 1, 2, 1, 2, 1, 6, 1, 1, 1, 3, 1, 6, 1, 2, 1, 1, 1, 7, 1, 1, 1, 2, 1, 7,
        1, 1, 1, 1, 1, 8,
    ];

    /// <summary>
    /// Gets the run widths of the 46 data codewords of version B, in the layout of <see cref="CharactersA"/>.
    /// </summary>
    private static ReadOnlySpan<byte> CharactersB =>
    [
        1, 5, 1, 2, 1, 3, 1, 4, 1, 3, 1, 3, 1, 3, 1, 4, 1, 3, 1, 2, 1, 5, 1, 3, 1, 4, 1, 2, 1, 4,
        1, 3, 1, 3, 1, 4, 1, 2, 1, 4, 1, 4, 1, 3, 1, 2, 1, 5, 1, 2, 1, 3, 1, 5, 1, 2, 1, 2, 1, 6,
        1, 9, 1, 2, 1, 2, 1, 8, 1, 3, 1, 2, 1, 7, 1, 4, 1, 2, 1, 6, 1, 5, 1, 2, 1, 5, 1, 6, 1, 2,
        1, 4, 1, 7, 1, 2, 1, 3, 1, 8, 1, 2, 1, 2, 1, 9, 1, 2, 1, 8, 1, 2, 1, 3, 1, 7, 1, 3, 1, 3,
        1, 6, 1, 4, 1, 3, 1, 5, 1, 5, 1, 3, 1, 4, 1, 6, 1, 3, 1, 3, 1, 7, 1, 3, 1, 2, 1, 8, 1, 3,
        1, 7, 1, 2, 1, 4, 1, 6, 1, 3, 1, 4, 1, 5, 1, 4, 1, 4, 1, 4, 1, 5, 1, 4, 1, 3, 1, 6, 1, 4,
        1, 2, 1, 7, 1, 4, 1, 6, 1, 2, 1, 5, 1, 5, 1, 3, 1, 5, 1, 4, 1, 4, 1, 5, 1, 3, 1, 5, 1, 5,
        1, 2, 1, 6, 1, 5, 1, 5, 1, 2, 1, 6, 1, 4, 1, 3, 1, 6, 1, 3, 1, 4, 1, 6, 1, 2, 1, 5, 1, 6,
        1, 4, 1, 2, 1, 7, 1, 3, 1, 3, 1, 7, 1, 2, 1, 4, 1, 7, 1, 3, 1, 2, 1, 8, 1, 2, 1, 3, 1, 8,
        1, 2, 1, 2, 1, 9,
    ];

    /// <summary>
    /// Gets the run widths of the 38 data codewords of Limited version A, in the layout of <see cref="CharactersA"/>.
    /// </summary>
    private static ReadOnlySpan<byte> CharactersLimitedA =>
    [
        1, 1, 1, 4, 1, 1, 1, 1, 1, 3, 1, 2, 1, 1, 1, 2, 1, 3, 1, 1, 1, 1, 1, 4, 1, 2, 1, 3, 1, 1,
        1, 2, 1, 2, 1, 2, 1, 2, 1, 1, 1, 3, 1, 4, 1, 1, 1, 1, 1, 3, 1, 2, 1, 1, 1, 3, 1, 1, 1, 2,
        1, 7, 1, 1, 1, 1, 1, 6, 1, 2, 1, 1, 1, 5, 1, 3, 1, 1, 1, 4, 1, 4, 1, 1, 1, 3, 1, 5, 1, 1,
        1, 2, 1, 6, 1, 1, 1, 1, 1, 7, 1, 1, 1, 6, 1, 1, 1, 2, 1, 5, 1, 2, 1, 2, 1, 4, 1, 3, 1, 2,
        1, 3, 1, 4, 1, 2, 1, 2, 1, 5, 1, 2, 1, 1, 1, 6, 1, 2, 1, 5, 1, 1, 1, 3, 1, 4, 1, 2, 1, 3,
        1, 3, 1, 3, 1, 3, 1, 2, 1, 4, 1, 3, 1, 1, 1, 5, 1, 3, 1, 4, 1, 1, 1, 4, 1, 3, 1, 2, 1, 4,
        1, 2, 1, 3, 1, 4, 1, 1, 1, 4, 1, 4, 1, 3, 1, 1, 1, 5, 1, 2, 1, 2, 1, 5, 1, 1, 1, 3, 1, 5,
        1, 2, 1, 1, 1, 6, 1, 1, 1, 2, 1, 6, 1, 1, 1, 1, 1, 7,
    ];

    /// <summary>
    /// Gets the run widths of the 38 data codewords of Limited version B, in the layout of <see cref="CharactersA"/>.
    /// </summary>
    private static ReadOnlySpan<byte> CharactersLimitedB =>
    [
        1, 2, 1, 5, 1, 2, 1, 2, 1, 4, 1, 3, 1, 2, 1, 3, 1, 4, 1, 2, 1, 2, 1, 5, 1, 3, 1, 4, 1, 2,
        1, 3, 1, 3, 1, 3, 1, 3, 1, 2, 1, 4, 1, 5, 1, 2, 1, 2, 1, 4, 1, 3, 1, 2, 1, 4, 1, 2, 1, 3,
        1, 8, 1, 2, 1, 2, 1, 7, 1, 3, 1, 2, 1, 6, 1, 4, 1, 2, 1, 5, 1, 5, 1, 2, 1, 4, 1, 6, 1, 2,
        1, 3, 1, 7, 1, 2, 1, 2, 1, 8, 1, 2, 1, 7, 1, 2, 1, 3, 1, 6, 1, 3, 1, 3, 1, 5, 1, 4, 1, 3,
        1, 4, 1, 5, 1, 3, 1, 3, 1, 6, 1, 3, 1, 2, 1, 7, 1, 3, 1, 6, 1, 2, 1, 4, 1, 5, 1, 3, 1, 4,
        1, 4, 1, 4, 1, 4, 1, 3, 1, 5, 1, 4, 1, 2, 1, 6, 1, 4, 1, 5, 1, 2, 1, 5, 1, 4, 1, 3, 1, 5,
        1, 3, 1, 4, 1, 5, 1, 2, 1, 5, 1, 5, 1, 4, 1, 2, 1, 6, 1, 3, 1, 3, 1, 6, 1, 2, 1, 4, 1, 6,
        1, 3, 1, 2, 1, 7, 1, 2, 1, 3, 1, 7, 1, 2, 1, 2, 1, 8,
    ];

    /// <summary>
    /// Gets the run widths of the start character of version A.
    /// </summary>
    private static ReadOnlySpan<byte> StartA => [1, 12, 1, 1, 1, 1, 1, 2];

    /// <summary>
    /// Gets the run widths of the start character of version B.
    /// </summary>
    private static ReadOnlySpan<byte> StartB => [1, 12, 1, 2, 1, 3, 1, 2];

    /// <summary>
    /// Gets the run widths of the start character of Limited version A.
    /// </summary>
    private static ReadOnlySpan<byte> StartLimitedA => [1, 5, 1, 1, 1, 1];

    /// <summary>
    /// Gets the run widths of the start character of Limited version B.
    /// </summary>
    private static ReadOnlySpan<byte> StartLimitedB => [1, 4, 1, 2, 1, 2];

    /// <summary>
    /// Gets the run widths of the stop character of version A.
    /// </summary>
    private static ReadOnlySpan<byte> StopA => [1, 1, 1, 1, 1, 1, 1, 1, 1, 11, 1];

    /// <summary>
    /// Gets the run widths of the stop character of version B.
    /// </summary>
    private static ReadOnlySpan<byte> StopB => [1, 2, 1, 2, 1, 2, 1, 2, 1, 12, 1];

    /// <summary>
    /// Gets the run widths of the stop character of both limited versions: one bar.
    /// </summary>
    private static ReadOnlySpan<byte> StopLimited => [1];

    /// <summary>
    /// Gets the table that maps a check value to six bar distances. Row r holds the number of ways the remaining distances can be arranged when distance r
    /// grows by one more, and the mapping walks the rows until the running sum reaches the check value.
    /// </summary>
    private static ReadOnlySpan<int> CheckWeights =>
    [
        495, 330, 210, 126, 70, 35, 15, 5,
        165, 120, 84, 56, 35, 20, 10, 4,
        45, 36, 28, 21, 15, 10, 6, 3,
        9, 8, 7, 6, 5, 4, 3, 2,
        1, 1, 1, 1, 1, 1, 1, 1,
    ];

    /// <summary>
    /// Returns the quiet zone in modules on each side of a version: "12G on either side", and 13G for Limited PosiCode B.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>The quiet zone in modules.</returns>
    public static int QuietZone(PosiCodeVersion version) => version == PosiCodeVersion.LimitedB ? 13 : 12;

    /// <summary>
    /// Returns whether a version carries only the limited character set.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>Whether the version is limited.</returns>
    public static bool IsLimited(PosiCodeVersion version) => version is PosiCodeVersion.LimitedA or PosiCodeVersion.LimitedB;

    /// <summary>
    /// Returns the codeword of a character in the limited character set, which is the first 38 codewords of set 0.
    /// </summary>
    /// <param name="codePoint">The code point to value.</param>
    /// <returns>The codeword, or -1 when the character is not in the set.</returns>
    public static int LimitedValue(int codePoint)
    {
        if ((uint)codePoint >= Function4)
        {
            return -1;
        }

        // A set 0 entry has no set bits above the codeword, so an entry below the limited count is a limited codeword of set 0.
        int code = CharacterCodes[codePoint];
        return code < LimitedCodewords ? code : -1;
    }

    /// <summary>
    /// Converts character values to the codewords the symbol carries. Characters at or above 128 drop their high bit and are announced by the function 4
    /// character: once for a run shorter than five, or shorter than three at the end of the data, and twice, which switches the mode, for a longer run. Every
    /// character then encodes in the current set, through a shift to set 2, through a latch when the next character is not in the current set either, or
    /// through a shift to the other set.
    /// </summary>
    /// <param name="values">The character values, 0 to 255.</param>
    /// <param name="codewords">The buffer that receives the codewords, at least four per value.</param>
    /// <returns>The number of codewords written.</returns>
    public static int ToCodewords(ReadOnlySpan<int> values, Span<int> codewords)
    {
        // The latch rule looks at the character after the current one, so the announced sequence is built first. Each value takes at most two function 4
        // characters before it, so the sequence is at most three times as long and the builder never grows: short data works inline and longer data rents once.
        InlineArray128<byte> messageStorage = default;
        int capacity = values.Length * 3;
        ValueListBuilder<byte> message = capacity <= 128 ? new(messageStorage) : new(capacity);
        try
        {
            // Each run of values on one side of 128 is announced as a whole. A run too short to switch the mode takes a function 4 character before each of its
            // values, and a longer run takes two before its first value, which switch the mode for the whole run.
            bool extended = false;
            int position = 0;
            while (position < values.Length)
            {
                ReadOnlySpan<int> rest = values[position..];
                bool high = rest[0] >= 128;
                int run = high ? rest.IndexOfAnyInRange(0, 127) : rest.IndexOfAnyExceptInRange(0, 127);
                if (run < 0)
                {
                    run = rest.Length;
                }

                bool announceEach = false;
                if (extended != high)
                {
                    int limit = run == rest.Length ? 3 : 5;
                    if (run < limit)
                    {
                        announceEach = true;
                    }
                    else
                    {
                        extended = high;
                        message.Append(Function4);
                        message.Append(Function4);
                    }
                }

                foreach (int value in rest[..run])
                {
                    if (announceEach)
                    {
                        message.Append(Function4);
                    }

                    message.Append((byte)(value & 127));
                }

                position += run;
            }

            ReadOnlySpan<byte> codes = CharacterCodes;
            ReadOnlySpan<byte> sequence = message.AsSpan();
            CharacterSet current = CharacterSet.Set0;
            int written = 0;
            for (int i = 0; i < sequence.Length; i++)
            {
                int code = codes[sequence[i]];
                CharacterSet set = (CharacterSet)(code >> CodewordBitCount);
                int codeword = code & CodewordMask;
                if (set == current)
                {
                    codewords[written++] = codeword;
                }
                else if (set == CharacterSet.Set2)
                {
                    codewords[written++] = ShiftToSet2Codeword;
                    codewords[written++] = codeword;
                }
                else if (i + 1 == sequence.Length || (CharacterSet)(codes[sequence[i + 1]] >> CodewordBitCount) != current)
                {
                    // The character is in the other one of sets 0 and 1, and so is the next character or none follows: the latch serves both.
                    codewords[written++] = LatchCodeword;
                    codewords[written++] = codeword;
                    current = set;
                }
                else
                {
                    codewords[written++] = ShiftCodeword;
                    codewords[written++] = codeword;
                }
            }

            return written;
        }
        finally
        {
            message.Dispose();
        }
    }

    /// <summary>
    /// Encodes codewords and builds the symbol. The runs start with the first bar of the start character and end on the last bar of the stop character, and
    /// carry the start character, the codewords, the check character and the stop character. PosiCode carries no guard bars, so every bar runs the full height,
    /// and the human readable interpretation sits below the symbol.
    /// </summary>
    /// <param name="codewords">The codewords the symbol carries.</param>
    /// <param name="version">The version, which sets the patterns and the quiet zone.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(
        ReadOnlySpan<int> codewords,
        PosiCodeVersion version,
        ReadOnlySpan<char> readable,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        VersionPatterns patterns = version switch
        {
            PosiCodeVersion.A => new(CharactersA, StartA, StopA),
            PosiCodeVersion.B => new(CharactersB, StartB, StopB),
            PosiCodeVersion.LimitedA => new(CharactersLimitedA, StartLimitedA, StopLimited),
            _ => new(CharactersLimitedB, StartLimitedB, StopLimited),
        };

        double xDimension = options.ResolveXDimension(BarcodeSymbology.DefaultXDimension);
        int runCount = patterns.Start.Length + (codewords.Length * RunsPerCharacter) + CheckRuns + patterns.Stop.Length;
        Span<int> runs = symbol.BeginLinear(runCount, xDimension, out Span<float> heights, out _);
        int written = 0;
        AppendRuns(patterns.Start, runs, ref written);
        foreach (int codeword in codewords)
        {
            AppendRuns(patterns.Characters.Slice(codeword * RunsPerCharacter, RunsPerCharacter), runs, ref written);
        }

        AppendCheck(codewords, version, runs.Slice(written, CheckRuns));
        written += CheckRuns;
        AppendRuns(patterns.Stop, runs, ref written);

        int widthInModules = BarcodeSymbol.RunWidth(runs);

        float barHeight = options.ResolveBarHeight(xDimension, 0, widthInModules * NominalBarHeightFraction, float.PositiveInfinity);
        heights.Fill(barHeight);

        if (options.Font is not null)
        {
            symbol.ReserveText(1, readable.Length);
            symbol.AddText(readable, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        int quietZone = QuietZone(version);
        symbol.EndLinear(quietZone, quietZone);
    }

    /// <summary>
    /// Writes run widths from a pattern table to the runs of the symbol.
    /// </summary>
    /// <param name="widths">The run widths in modules.</param>
    /// <param name="runs">The runs of the symbol.</param>
    /// <param name="index">The position in <paramref name="runs"/>, advanced past the widths.</param>
    private static void AppendRuns(ReadOnlySpan<byte> widths, Span<int> runs, ref int index)
    {
        foreach (byte width in widths)
        {
            runs[index++] = width;
        }
    }

    /// <summary>
    /// Writes the twelve runs of the check character. The codewords are divided bit by bit by the check polynomial, and the ten bit remainder, offset by 45 in
    /// the standard versions and moved past a reserved range in the limited versions, is mapped to six distances that add to twenty. Version B adds one to
    /// every distance. The character is six bars of one module whose spaces are the distances less one, written from the last distance to the first.
    /// </summary>
    /// <param name="codewords">The codewords the check covers.</param>
    /// <param name="version">The version.</param>
    /// <param name="runs">The twelve runs of the check character.</param>
    private static void AppendCheck(ReadOnlySpan<int> codewords, PosiCodeVersion version, Span<int> runs)
    {
        int value = 0;
        foreach (int codeword in codewords)
        {
            int bits = codeword;
            for (int bit = 0; bit < CodewordBits; bit++)
            {
                if (((bits ^ value) & 1) != 0)
                {
                    value ^= CheckPolynomial;
                }

                value >>= 1;
                bits >>= 1;
            }
        }

        value &= CheckValueMask;
        if (!IsLimited(version))
        {
            value += StandardCheckOffset;
        }
        else if (value is >= LimitedReservedFirst and <= LimitedReservedLast)
        {
            value += LimitedReservedStep;
        }

        // The walk keeps the running sum of the weights taken so far. A weight that reaches the value exactly ends the walk, a weight that passes it closes the
        // distance of the current row, and a smaller weight is taken and widens that distance by one.
        InlineArray6<int> distanceStorage = default;
        Span<int> distances = distanceStorage;
        distances.Fill(2);
        ReadOnlySpan<int> weights = CheckWeights;
        int row = 0;
        int column = 0;
        int width = 0;
        int sum = 0;
        while (sum != value)
        {
            int total = sum + weights[(row * CheckWeightColumns) + column];
            if (total == value)
            {
                width++;
                distances[row] = width + 2;
                sum = total;
            }

            if (total > value)
            {
                distances[row] = width + 2;
                row++;
                width = 0;
            }

            if (total < value)
            {
                column++;
                width++;
                sum = total;
            }
        }

        distances[CheckDistances - 1] = CheckDistanceSum - distances[0] - distances[1] - distances[2] - distances[3] - distances[4];
        int offset = version is PosiCodeVersion.B or PosiCodeVersion.LimitedB ? 1 : 0;
        for (int i = 0; i < CheckDistances; i++)
        {
            runs[i * 2] = 1;
            runs[(i * 2) + 1] = distances[CheckDistances - 1 - i] + offset - 1;
        }
    }

    /// <summary>
    /// The run widths of the characters of one version.
    /// </summary>
    private readonly ref struct VersionPatterns
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VersionPatterns"/> struct.
        /// </summary>
        /// <param name="characters">The run widths of the data codewords, <see cref="RunsPerCharacter"/> per codeword.</param>
        /// <param name="start">The run widths of the start character.</param>
        /// <param name="stop">The run widths of the stop character.</param>
        public VersionPatterns(ReadOnlySpan<byte> characters, ReadOnlySpan<byte> start, ReadOnlySpan<byte> stop)
        {
            this.Characters = characters;
            this.Start = start;
            this.Stop = stop;
        }

        /// <summary>
        /// Gets the run widths of the data codewords, <see cref="RunsPerCharacter"/> per codeword.
        /// </summary>
        public ReadOnlySpan<byte> Characters { get; }

        /// <summary>
        /// Gets the run widths of the start character.
        /// </summary>
        public ReadOnlySpan<byte> Start { get; }

        /// <summary>
        /// Gets the run widths of the stop character.
        /// </summary>
        public ReadOnlySpan<byte> Stop { get; }
    }
}
