// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the Plessey Code symbology. Every character is a hexadecimal digit sent as four bits, least significant bit first, and every bit is a bar and
/// a space one pitch wide: a 0 bit is a narrow bar and a wide space, and a 1 bit is a wide bar and a narrow space. A symbol is a margin, the start code 1101,
/// the data, eight cyclic redundancy check bits, a full pitch termination bar, the reversed start code 0011 and a margin.
/// <para>
/// The pitch is five modules. The 1 bit is a three module bar and a two module space, and the 0 bit is a one module bar and a four module space, which lie
/// inside the width ranges the symbology gives at its standard pitch of 1.020 mm: 0.533 mm to 0.635 mm and 0.381 mm to 0.483 mm for the 1 bit, and 0.127 mm to
/// 0.229 mm and 0.787 mm to 0.889 mm for the 0 bit.
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
    /// The quiet zone in modules on each side: the margin of "4 bits" the symbology gives, at five modules per bit.
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
    /// The bar height as a fraction of the symbol width, quiet zones excluded, when the caller sets no height: 15 per cent.
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <summary>
    /// The generator polynomial of the check, x^8 + x^7 + x^6 + x^5 + x^3 + 1, as its nine coefficients from the lowest power to the highest, the order the
    /// bits are sent in, in bits 0 to 8: the coefficients 1, 1, 1, 1, 0, 1, 0, 0, 1.
    /// </summary>
    private const int GeneratorMask = 0b1_0010_1111;

    /// <summary>
    /// The width of the bar of a 1 bit in modules.
    /// </summary>
    private const int OneBar = 3;

    /// <summary>
    /// The width of the space of a 1 bit in modules.
    /// </summary>
    private const int OneSpace = 2;

    /// <summary>
    /// The width of the bar of a 0 bit in modules.
    /// </summary>
    private const int ZeroBar = 1;

    /// <summary>
    /// The width of the space of a 0 bit in modules.
    /// </summary>
    private const int ZeroSpace = 4;

    /// <summary>
    /// Gets the run widths of the start code 1101, sent least significant bit first, so the bits 1, 1, 0 and 1.
    /// </summary>
    private static ReadOnlySpan<byte> Start => [3, 2, 3, 2, 1, 4, 3, 2];

    /// <summary>
    /// Gets the run widths of the end of the symbol: the full pitch termination bar and the reversed start code, which is the start code read from the right,
    /// so its bars and spaces are mirrored.
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
    /// Calculates the two check characters over the data. The data bits, least significant bit of each character first, are divided by the generator
    /// polynomial, and the eight bit remainder is sent after the data in the same order, so its low four bits form the first check character and its high four
    /// bits the second.
    /// </summary>
    /// <param name="text">The data the check covers, already validated.</param>
    /// <param name="checks">The buffer that receives the two check characters.</param>
    public static void CheckCharacters(ReadOnlySpan<char> text, Span<char> checks)
    {
        // The bit sequence is the data followed by eight zero bits. Wherever a 1 bit stands, the generator is subtracted from that position, which for bits is
        // an exclusive or, and the eight bits left after the data are the remainder. A subtraction at position i reaches only the nine positions from i, so the
        // division needs only those: the window holds them, bit k for position i + k. The first eight bits fill the window, and every later bit enters at the
        // top, after which the division step for the lowest position runs. The eight zero bits that follow the data enter as nothing, so they only run the last
        // steps.
        int dataBits = text.Length * BitsPerCharacter;
        int window = 0;
        int position = 0;
        foreach (char c in text)
        {
            int value = Value(c);
            for (int bit = 0; bit < BitsPerCharacter; bit++, position++)
            {
                int next = (value >> bit) & 1;
                if (position < CheckBits)
                {
                    window |= next << position;
                }
                else
                {
                    window = DivisionStep(window | (next << CheckBits));
                }
            }
        }

        // One division step runs for every data bit. The data bits after the first eight ran theirs above, so the zero bits run the rest.
        for (int step = Math.Max(dataBits - CheckBits, 0); step < dataBits; step++)
        {
            window = DivisionStep(window);
        }

        checks[0] = Characters[window & 0xF];
        checks[1] = Characters[window >> 4];
    }

    /// <summary>
    /// Encodes the data and its check characters and builds the symbol. The runs start with the first bar of the start code and end on the last bar of the
    /// reversed start code. Plessey Code carries no guard bars, so every bar runs the full height, and the human readable interpretation sits below the symbol:
    /// the data, then the check characters when they print.
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
        int runCount = Start.Length + ((data.Length + checks.Length) * BitsPerCharacter * 2) + Stop.Length;
        double xDimension = options.ResolveXDimension(0, NominalXDimension, double.PositiveInfinity);
        Span<int> runs = symbol.BeginLinear(runCount, xDimension, out Span<float> heights, out _);
        int written = 0;
        foreach (byte width in Start)
        {
            runs[written++] = width;
        }

        AppendCharacters(runs, ref written, data);
        AppendCharacters(runs, ref written, checks);
        foreach (byte width in Stop)
        {
            runs[written++] = width;
        }

        int widthInModules = BarcodeSymbol.RunWidth(runs);

        float barHeight = options.ResolveBarHeight(xDimension, 0, widthInModules * NominalBarHeightFraction, float.PositiveInfinity);
        heights.Fill(barHeight);

        if (options.Font is not null)
        {
            symbol.ReserveText(1, data.Length + printedChecks.Length);
            symbol.AddText(data, printedChecks, [], 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(QuietZone, QuietZone);
    }

    /// <summary>
    /// Writes the bar and space of every bit of every character, least significant bit first.
    /// </summary>
    /// <param name="runs">The buffer the widths are written to.</param>
    /// <param name="written">The write position, advanced by two runs a bit.</param>
    /// <param name="characters">The characters, already validated.</param>
    private static void AppendCharacters(Span<int> runs, ref int written, ReadOnlySpan<char> characters)
    {
        foreach (char c in characters)
        {
            int value = Value(c);
            for (int bit = 0; bit < BitsPerCharacter; bit++)
            {
                bool one = ((value >> bit) & 1) == 1;
                runs[written++] = one ? OneBar : ZeroBar;
                runs[written++] = one ? OneSpace : ZeroSpace;
            }
        }
    }

    /// <summary>
    /// Runs one step of the check division on a window that holds nine sequence positions, bit k for position i + k. Where position i is 1 the generator is
    /// subtracted, which clears it, and the window then moves on one position, so its top bit is free for the next position.
    /// </summary>
    /// <param name="window">The window, positions i to i + 8.</param>
    /// <returns>The window, positions i + 1 to i + 8.</returns>
    private static int DivisionStep(int window) => ((window & 1) != 0 ? window ^ GeneratorMask : window) >> 1;
}
