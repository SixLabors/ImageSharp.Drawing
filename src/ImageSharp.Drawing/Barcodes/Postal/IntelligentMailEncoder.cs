// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the United States Postal Service Intelligent Mail barcode, from USPS-B-3200 revision H.
/// Section 2.2 converts the routing code and the tracking code to a binary value, generates an 11-bit
/// frame check sequence on it, divides it into ten codewords, converts the codewords to 13-bit characters
/// and maps the character bits to the 65 bars.
/// </summary>
internal static class IntelligentMailEncoder
{
    /// <summary>
    /// The number of bars in a symbol.
    /// </summary>
    public const int BarCount = 65;

    /// <summary>
    /// The number of digits in the tracking code.
    /// </summary>
    public const int TrackingLength = 20;

    /// <summary>
    /// The number of codewords, and of characters, in a symbol.
    /// </summary>
    public const int CharacterCount = 10;

    /// <summary>
    /// The number of entries in the table of characters with five bits set, Table 19.
    /// </summary>
    private const int FiveOf13Length = 1287;

    /// <summary>
    /// The number of entries in the table of characters with two bits set, Table 20.
    /// </summary>
    private const int TwoOf13Length = 78;

    /// <summary>
    /// The generator polynomial of the frame check sequence, section 2.2.2.
    /// </summary>
    private const int GeneratorPolynomial = 0xF35;

    /// <summary>
    /// The dimensions of Figure 6, which gives every vertical dimension as a minimum and a maximum from
    /// the centerline: a full bar of 0.125 to 0.165 inch, a tracker of 0.039 to 0.057 inch and an ascender
    /// or descender bar of 0.083 to 0.111 inch. The metrics are the middle of each range: a full bar of
    /// 0.145 inch, a tracker of 0.048 inch and an extender of 0.0485 inch above or below it. The bar width
    /// of 0.020 inch, the pitch of 22 bars per inch and the clear zone of 0.125 inch on each end are the
    /// POSTNET dimensions. Section 2.4.1 keeps the human readable information at least 0.028 inch below
    /// the bars, and section 2.4.2 aligns "the left edge of the leftmost digit" with the leftmost bar.
    /// </summary>
    public static readonly FourStateMetrics Metrics = new(
        UspsPostalEncoder.NominalXDimension,
        UspsPostalEncoder.RunUnit,
        UspsPostalEncoder.BarUnits,
        UspsPostalEncoder.SpaceUnits,
        (0.145F - 0.048F) / 2F / 0.020F,
        0.048F / 0.020F,
        (0.145F - 0.048F) / 2F / 0.020F,
        UspsPostalEncoder.ClearZone,
        BarcodeTextSide.BelowBars,
        0.028F / 0.020F,
        BarcodeTextAlignment.Left);

    private static readonly int[] FiveOf13 = InitializeNof13Table(5, FiveOf13Length);

    private static readonly int[] TwoOf13 = InitializeNof13Table(2, TwoOf13Length);

    /// <summary>
    /// Gets Table 22, the bar to character mapping, as four values per bar from the left: the character
    /// and the bit of the descender, then the character and the bit of the ascender. Characters A to J
    /// are 0 to 9.
    /// </summary>
    private static ReadOnlySpan<byte> BarMap =>
    [
        7, 2, 4, 3, 1, 10, 0, 0, 9, 12, 2, 8, 5, 5, 6, 11, 8, 9, 3, 1, 0, 1, 5, 12, 2, 5, 1, 8, 4, 4, 9, 11, 6, 3, 8, 10, 3, 9, 7, 6,
        5, 11, 1, 4, 8, 5, 2, 12, 9, 10, 0, 2, 7, 1, 6, 7, 3, 6, 4, 9, 0, 3, 8, 6, 6, 4, 2, 7, 1, 1, 9, 9, 7, 10, 5, 2, 4, 0, 3, 8,
        6, 2, 0, 4, 8, 11, 1, 0, 9, 8, 3, 12, 2, 6, 7, 7, 5, 1, 4, 10, 1, 12, 6, 9, 7, 3, 8, 0, 5, 8, 9, 7, 4, 6, 2, 10, 3, 4, 0, 5,
        8, 4, 5, 7, 7, 11, 1, 9, 6, 0, 9, 6, 0, 6, 4, 8, 2, 1, 3, 2, 5, 9, 8, 12, 4, 11, 6, 1, 9, 5, 7, 4, 3, 3, 1, 2, 0, 7, 2, 0,
        1, 3, 4, 1, 6, 10, 3, 5, 8, 7, 9, 4, 2, 11, 5, 6, 0, 8, 7, 12, 4, 2, 8, 1, 5, 10, 3, 0, 9, 3, 0, 9, 6, 5, 2, 4, 7, 8, 1, 7,
        5, 0, 4, 5, 2, 3, 0, 10, 6, 12, 9, 2, 3, 11, 1, 6, 8, 8, 7, 9, 5, 4, 0, 11, 1, 5, 2, 2, 9, 1, 4, 12, 8, 3, 6, 6, 7, 0, 3, 7,
        4, 7, 7, 5, 0, 12, 1, 11, 2, 9, 9, 0, 6, 8, 5, 3, 3, 10, 8, 2,
    ];

    /// <summary>
    /// Encodes the digits into bar states.
    /// </summary>
    /// <param name="text">The tracking code followed by the routing code, already validated.</param>
    /// <param name="states">The buffer that receives the 65 bar states.</param>
    public static void Encode(ReadOnlySpan<char> text, Span<FourState> states)
    {
        UInt128 binary = Binary(text[..TrackingLength], text[TrackingLength..]);
        int fcs = FrameCheckSequence(binary);
        Span<int> codewords = stackalloc int[CharacterCount];
        Codewords(binary, fcs, codewords);
        Span<int> characters = stackalloc int[CharacterCount];
        Characters(codewords, fcs, characters);
        Bars(characters, states);
    }

    /// <summary>
    /// Step 1, section 2.2.1: converts the routing code to a value by Table 4, then multiplies the value
    /// by 10 and adds the first tracking digit, multiplies by 5 and adds the second, and multiplies by 10
    /// and adds each of the remaining 18.
    /// </summary>
    /// <param name="tracking">The 20 tracking digits.</param>
    /// <param name="routing">The 0, 5, 9 or 11 routing digits.</param>
    /// <returns>The binary data, which fills the rightmost 102 bits.</returns>
    public static UInt128 Binary(ReadOnlySpan<char> tracking, ReadOnlySpan<char> routing)
    {
        UInt128 value = 0;
        for (int i = 0; i < routing.Length; i++)
        {
            value = (value * 10U) + (uint)(routing[i] - '0');
        }

        value += routing.Length switch
        {
            5 => 1UL,
            9 => 100001UL,
            11 => 1000100001UL,
            _ => 0UL,
        };

        value = (value * 10U) + (uint)(tracking[0] - '0');
        value = (value * 5U) + (uint)(tracking[1] - '0');
        for (int i = 2; i < tracking.Length; i++)
        {
            value = (value * 10U) + (uint)(tracking[i] - '0');
        }

        return value;
    }

    /// <summary>
    /// Step 2, section 2.2.2: the 11-bit frame check sequence of the rightmost 102 bits, by the code of
    /// Table 17. The data is 13 bytes of which the first byte carries 6 bits.
    /// </summary>
    /// <param name="data">The binary data.</param>
    /// <returns>The frame check sequence.</returns>
    public static int FrameCheckSequence(UInt128 data)
    {
        int fcs = 0x7FF;
        int word = ((int)(data >> 96) & 0xFF) << 5;
        for (int bit = 2; bit < 8; bit++)
        {
            fcs = Shift(fcs, word);
            word <<= 1;
        }

        for (int byteIndex = 1; byteIndex < 13; byteIndex++)
        {
            word = ((int)(data >> (8 * (12 - byteIndex))) & 0xFF) << 3;
            for (int bit = 0; bit < 8; bit++)
            {
                fcs = Shift(fcs, word);
                word <<= 1;
            }
        }

        return fcs;

        static int Shift(int fcs, int word)
            => (((fcs ^ word) & 0x400) != 0 ? (fcs << 1) ^ GeneratorPolynomial : fcs << 1) & 0x7FF;
    }

    /// <summary>
    /// Steps 3 and 4, sections 2.2.3 and 2.2.4: the rightmost codeword J is the value modulo 636, codewords
    /// I to B are successive remainders modulo 1365 and codeword A is what remains. J is then doubled, and
    /// A gains 659 when the most significant bit of the frame check sequence is set.
    /// </summary>
    /// <param name="data">The binary data.</param>
    /// <param name="fcs">The frame check sequence.</param>
    /// <param name="codewords">The buffer that receives the ten codewords, A first.</param>
    public static void Codewords(UInt128 data, int fcs, Span<int> codewords)
    {
        codewords[9] = (int)(data % 636U) * 2;
        data /= 636U;
        for (int i = 8; i >= 1; i--)
        {
            codewords[i] = (int)(data % 1365U);
            data /= 1365U;
        }

        codewords[0] = (int)data;
        if ((fcs & 0x400) != 0)
        {
            codewords[0] += 659;
        }
    }

    /// <summary>
    /// Step 5, section 2.2.5: a codeword below 1287 selects its character from Table 19 and any other
    /// codeword selects entry codeword minus 1287 from Table 20. Then, by Table 21, each of the frame check
    /// sequence bits 0 to 9 that is set negates the 13 bits of characters A to J in turn.
    /// </summary>
    /// <param name="codewords">The ten codewords.</param>
    /// <param name="fcs">The frame check sequence.</param>
    /// <param name="characters">The buffer that receives the ten characters, A first.</param>
    public static void Characters(ReadOnlySpan<int> codewords, int fcs, Span<int> characters)
    {
        for (int i = 0; i < CharacterCount; i++)
        {
            int codeword = codewords[i];
            characters[i] = codeword < FiveOf13Length ? FiveOf13[codeword] : TwoOf13[codeword - FiveOf13Length];
            if (((fcs >> i) & 1) != 0)
            {
                characters[i] ^= 0x1FFF;
            }
        }
    }

    /// <summary>
    /// Step 6, section 2.2.6: every bar takes its descender from one character bit and its ascender from
    /// another, by Table 22.
    /// </summary>
    /// <param name="characters">The ten characters.</param>
    /// <param name="states">The buffer that receives the 65 bar states.</param>
    public static void Bars(ReadOnlySpan<int> characters, Span<FourState> states)
    {
        for (int bar = 0; bar < BarCount; bar++)
        {
            int entry = bar * 4;
            bool descender = ((characters[BarMap[entry]] >> BarMap[entry + 1]) & 1) != 0;
            bool ascender = ((characters[BarMap[entry + 2]] >> BarMap[entry + 3]) & 1) != 0;
            states[bar] = ascender && descender
                ? FourState.Full
                : ascender
                    ? FourState.Ascender
                    : descender
                        ? FourState.Descender
                        : FourState.Tracker;
        }
    }

    /// <summary>
    /// Generates Table 19 or Table 20 by the code of Table 18: every 13-bit value with the given number of
    /// bits set is visited in order, a value and its bit reversal are placed at the next free entries from
    /// the start of the table, and a value that is its own reversal is placed at the next free entry from
    /// the end.
    /// </summary>
    /// <param name="bitsSet">The number of bits set in every character.</param>
    /// <param name="tableLength">The number of entries.</param>
    /// <returns>The table.</returns>
    private static int[] InitializeNof13Table(int bitsSet, int tableLength)
    {
        int[] table = new int[tableLength];
        int lowerIndex = 0;
        int upperIndex = tableLength - 1;
        for (int count = 0; count < 8192; count++)
        {
            if (BitOperations.PopCount((uint)count) != bitsSet)
            {
                continue;
            }

            int reverse = 0;
            int remaining = count;
            for (int bit = 0; bit < 13; bit++)
            {
                reverse = (reverse << 1) | (remaining & 1);
                remaining >>= 1;
            }

            if (reverse < count)
            {
                continue;
            }

            if (count == reverse)
            {
                table[upperIndex--] = count;
            }
            else
            {
                table[lowerIndex++] = count;
                table[lowerIndex++] = reverse;
            }
        }

        return table;
    }
}
