// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the Plessey Code symbology. Every character is a hexadecimal digit sent as four bits,
/// least significant bit first, and every bit is a bar and a space one pitch wide: a 0 bit is a narrow
/// bar and a wide space, and a 1 bit is a wide bar and a narrow space. A symbol is a margin, the start
/// code 1101, the data, eight cyclic redundancy check bits, a full pitch termination bar, the reversed
/// start code 0011 and a margin.
/// <para>
/// The pitch is five modules. The 1 bit is a three module bar and a two module space, and the 0 bit is
/// a one module bar and a four module space, which lie inside the width ranges the symbology gives at
/// its standard pitch of 1.020 mm: 0.533 mm to 0.635 mm and 0.381 mm to 0.483 mm for the 1 bit, and
/// 0.127 mm to 0.229 mm and 0.787 mm to 0.889 mm for the 0 bit.
/// </para>
/// </summary>
internal static class PlesseyEncoder
{
    /// <summary>
    /// The characters the symbology encodes, in value order.
    /// </summary>
    public const string Characters = "0123456789ABCDEF";

    /// <summary>
    /// The largest number of characters a symbol carries. The symbology sets no maximum.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The quiet zone in modules on each side: the margin of "4 bits" the symbology gives, at five modules
    /// per bit.
    /// </summary>
    public const int QuietZone = 20;

    /// <summary>
    /// The nominal X dimension in millimetres: one fifth of the 1.020 mm pitch.
    /// </summary>
    public const float NominalXDimension = 1.020F / 5F;

    /// <summary>
    /// The number of check characters a symbol carries, which hold the eight check bits.
    /// </summary>
    public const int CheckCharacterCount = 2;

    /// <summary>
    /// The number of bits in a character.
    /// </summary>
    private const int BitsPerCharacter = 4;

    /// <summary>
    /// The number of check bits, which the generator polynomial of degree eight leaves.
    /// </summary>
    private const int CheckBits = 8;

    /// <summary>
    /// The bar height as a fraction of the symbol width, quiet zones excluded, when the caller sets no
    /// height: the 15 per cent of Code 39 and Codabar, since no document gives one for Plessey Code.
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <summary>
    /// Gets the generator polynomial of the check, x^8 + x^7 + x^6 + x^5 + x^3 + 1, as its nine
    /// coefficients from the lowest power to the highest, the order the bits are sent in.
    /// </summary>
    private static ReadOnlySpan<byte> Generator => [1, 1, 1, 1, 0, 1, 0, 0, 1];

    /// <summary>
    /// Gets the run widths of the start code 1101, sent least significant bit first, so the bits 1, 1,
    /// 0 and 1.
    /// </summary>
    private static ReadOnlySpan<byte> Start => [3, 2, 3, 2, 1, 4, 3, 2];

    /// <summary>
    /// Gets the run widths of the end of the symbol: the full pitch termination bar and the reversed
    /// start code, which is the start code read from the right, so its bars and spaces are mirrored.
    /// </summary>
    private static ReadOnlySpan<byte> Stop => [5, 4, 1, 4, 1, 2, 3, 2, 3];

    /// <summary>
    /// Returns the value of a character, which is its index in <see cref="Characters"/>.
    /// </summary>
    /// <param name="codePoint">The code point to value.</param>
    /// <returns>The value, or -1 when the character is not in the set.</returns>
    public static int Value(int codePoint) => codePoint switch
    {
        >= '0' and <= '9' => codePoint - '0',
        >= 'A' and <= 'F' => codePoint - 'A' + 10,
        _ => -1,
    };

    /// <summary>
    /// Calculates the two check characters over the data. The data bits, least significant bit of each
    /// character first, are divided by the generator polynomial, and the eight bit remainder is sent
    /// after the data in the same order, so its low four bits form the first check character and its
    /// high four bits the second.
    /// </summary>
    /// <param name="text">The data the check covers, already validated.</param>
    /// <param name="checks">The buffer that receives the two check characters.</param>
    public static void CheckCharacters(ReadOnlySpan<char> text, Span<char> checks)
    {
        // The bit sequence is the data followed by eight zero bits. Wherever a 1 bit stands, the
        // generator is subtracted from that position, which for bits is an exclusive or, and the eight
        // bits left after the data are the remainder.
        Span<byte> bits = (text.Length * BitsPerCharacter) + CheckBits <= 256
            ? stackalloc byte[(text.Length * BitsPerCharacter) + CheckBits]
            : new byte[(text.Length * BitsPerCharacter) + CheckBits];
        bits.Clear();
        for (int i = 0; i < text.Length; i++)
        {
            int value = Value(text[i]);
            for (int bit = 0; bit < BitsPerCharacter; bit++)
            {
                bits[(i * BitsPerCharacter) + bit] = (byte)((value >> bit) & 1);
            }
        }

        for (int i = 0; i < text.Length * BitsPerCharacter; i++)
        {
            if (bits[i] == 1)
            {
                for (int j = 0; j < Generator.Length; j++)
                {
                    bits[i + j] ^= Generator[j];
                }
            }
        }

        int remainder = 0;
        for (int bit = 0; bit < CheckBits; bit++)
        {
            remainder |= bits[(text.Length * BitsPerCharacter) + bit] << bit;
        }

        checks[0] = Characters[remainder & 0xF];
        checks[1] = Characters[remainder >> 4];
    }

    /// <summary>
    /// Encodes text into the alternating bar and space run widths the renderer draws, starting with the
    /// first bar of the start code and ending on the last bar of the reversed start code.
    /// </summary>
    /// <param name="text">The data and its two check characters, already validated.</param>
    /// <returns>The run widths in modules.</returns>
    public static int[] Encode(ReadOnlySpan<char> text)
    {
        int[] runs = new int[Start.Length + (text.Length * BitsPerCharacter * 2) + Stop.Length];
        int written = 0;
        for (int i = 0; i < Start.Length; i++)
        {
            runs[written++] = Start[i];
        }

        for (int i = 0; i < text.Length; i++)
        {
            int value = Value(text[i]);
            for (int bit = 0; bit < BitsPerCharacter; bit++)
            {
                bool one = ((value >> bit) & 1) == 1;
                runs[written++] = one ? 3 : 1;
                runs[written++] = one ? 2 : 4;
            }
        }

        for (int i = 0; i < Stop.Length; i++)
        {
            runs[written++] = Stop[i];
        }

        return runs;
    }

    /// <summary>
    /// Builds the symbol from encoded run widths. Plessey Code carries no guard bars, so every bar runs
    /// the full height, and the human readable interpretation sits below the symbol.
    /// </summary>
    /// <param name="runs">The alternating bar and space run widths in modules.</param>
    /// <param name="text">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    public static LinearBarcodeSymbol BuildSymbol(int[] runs, string text, BarcodeOptions options)
    {
        int widthInModules = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            widthInModules += runs[i];
        }

        float barHeight = EanUpcEncoder.ResolveBarHeight(options, NominalXDimension, widthInModules * NominalBarHeightFraction);
        int barCount = (runs.Length + 1) / 2;
        float[] heights = new float[barCount];
        float[] tops = new float[barCount];
        for (int i = 0; i < barCount; i++)
        {
            heights[i] = barHeight;
        }

        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null && text.Length > 0)
        {
            placements = [new BarcodeTextPlacement(text, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance)];
        }

        return new LinearBarcodeSymbol(runs, heights, tops, placements, QuietZone, QuietZone);
    }
}
