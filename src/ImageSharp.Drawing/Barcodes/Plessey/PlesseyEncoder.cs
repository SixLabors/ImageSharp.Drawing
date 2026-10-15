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
    public const double NominalXDimension = 1.020 / 5;

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
    /// The generator polynomial of the check, x^8 + x^7 + x^6 + x^5 + x^3 + 1, as its nine coefficients
    /// from the lowest power to the highest, the order the bits are sent in, in bits 0 to 8: the
    /// coefficients 1, 1, 1, 1, 0, 1, 0, 0, 1.
    /// </summary>
    private const int GeneratorMask = 0b1_0010_1111;

    /// <summary>
    /// The number of coefficients of the generator polynomial.
    /// </summary>
    private const int GeneratorLength = 9;

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
        // bits left after the data are the remainder. A subtraction at position i reaches only the nine
        // positions from i, so the division needs only those: the window holds them, bit k for position
        // i + k. Where position i is 1 the generator is subtracted, which clears it; the window then moves
        // on one position and takes in the bit nine positions on. After the data the window holds the
        // eight remainder bits, the same result as writing the whole sequence out.
        int dataBits = text.Length * BitsPerCharacter;
        int window = 0;
        for (int position = 0; position < GeneratorLength; position++)
        {
            window |= SequenceBit(text, dataBits, position) << position;
        }

        for (int i = 0; i < dataBits; i++)
        {
            if ((window & 1) != 0)
            {
                window ^= GeneratorMask;
            }

            window = (window >> 1) | (SequenceBit(text, dataBits, i + GeneratorLength) << (GeneratorLength - 1));
        }

        int remainder = window & ((1 << CheckBits) - 1);

        checks[0] = Characters[remainder & 0xF];
        checks[1] = Characters[remainder >> 4];
    }

    /// <summary>
    /// Encodes the data and its check characters and builds the symbol. The runs start with the first bar
    /// of the start code and end on the last bar of the reversed start code. Plessey Code carries no guard
    /// bars, so every bar runs the full height, and the human readable interpretation sits below the
    /// symbol: the data, then the check characters when they print.
    /// </summary>
    /// <param name="data">The data, already validated.</param>
    /// <param name="checks">The two check characters.</param>
    /// <param name="printedChecks">The check characters the human readable interpretation prints, or none.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(
        ReadOnlySpan<char> data,
        ReadOnlySpan<char> checks,
        ReadOnlySpan<char> printedChecks,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        int characters = data.Length + checks.Length;
        double xDimension = options.XDimension ?? NominalXDimension;
        Span<int> runs = symbol.BeginLinear(Start.Length + (characters * BitsPerCharacter * 2) + Stop.Length, xDimension, out Span<float> heights, out _);
        int written = 0;
        for (int i = 0; i < Start.Length; i++)
        {
            runs[written++] = Start[i];
        }

        for (int i = 0; i < characters; i++)
        {
            int value = Value(i < data.Length ? data[i] : checks[i - data.Length]);
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

        int widthInModules = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            widthInModules += runs[i];
        }

        float barHeight = options.ResolveBarHeight(xDimension, widthInModules * NominalBarHeightFraction);
        heights.Fill(barHeight);

        if (options.Font is not null)
        {
            symbol.ReserveText(1, data.Length + printedChecks.Length);
            symbol.AddText(data, printedChecks, [], 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(QuietZone, QuietZone);
    }

    /// <summary>
    /// Returns one bit of the sequence the check divides: the bits of the data characters, least
    /// significant bit of each character first, then zero bits.
    /// </summary>
    /// <param name="text">The data, already validated.</param>
    /// <param name="dataBits">The number of data bits.</param>
    /// <param name="position">The position of the bit in the sequence.</param>
    /// <returns>The bit, 0 or 1.</returns>
    private static int SequenceBit(ReadOnlySpan<char> text, int dataBits, int position)
        => position < dataBits ? (Value(text[position / BitsPerCharacter]) >> (position % BitsPerCharacter)) & 1 : 0;
}
