// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the Telepen symbology. Every character is an ASCII value with an even parity bit, sent least significant bit first, and the bits become bars
/// and spaces with a fixed 3:1 ratio: a 1 bit is a narrow bar and a narrow space, the bits 00 are a wide bar and a narrow space, the bits 010 are a wide bar
/// and a wide space, and the bits 01 and, after them, 10 are a narrow bar and a wide space. Every character is sixteen modules. A symbol is the start code, the
/// data, the modulo 127 check character and the stop code, without gaps.
/// </summary>
internal static class TelepenEncoder
{
    /// <summary>
    /// The largest number of characters a symbol carries. The symbology sets no maximum.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The quiet zone in modules on each side.
    /// </summary>
    public const int QuietZone = 10;

    /// <summary>
    /// The X dimension in millimetres a symbol takes when the caller sets none, 0.0125 inch. The Telepen history of SB Electronic Systems: "For normal
    /// applications the recommended value of x should be between 0.010 inch an 0.0125".
    /// </summary>
    private const double NominalXDimension = 0.0125 * 25.4;

    /// <summary>
    /// The number of characters a caller holds inline to build symbol data in. Longer data rents its buffer once.
    /// </summary>
    public const int InlineBufferLength = 64;

    /// <summary>
    /// The start code, "binary 01011111 (ASCII _)".
    /// </summary>
    public const int Start = '_';

    /// <summary>
    /// The stop code, "binary 11111010 (ASCII z)".
    /// </summary>
    public const int Stop = 'z';

    /// <summary>
    /// The modulus of the check character.
    /// </summary>
    public const int CheckModulus = 127;

    /// <summary>
    /// The value that the numeric mode adds to a digit followed by X.
    /// </summary>
    public const int NumericSingleOffset = 17;

    /// <summary>
    /// The value that the numeric mode adds to a pair of digits read as a number.
    /// </summary>
    public const int NumericPairOffset = 27;

    /// <summary>
    /// The number of bits in a character: seven ASCII bits and the parity bit.
    /// </summary>
    private const int BitsPerCharacter = 8;

    /// <summary>
    /// The width of a character in modules: eight bits of two modules.
    /// </summary>
    private const int ModulesPerCharacter = 16;

    /// <summary>
    /// The number of characters a symbol carries beyond its data: the start code, the check character and the stop code.
    /// </summary>
    private const int FrameCharacters = 3;

    /// <summary>
    /// The width of a wide bar or space in modules, from the fixed 3:1 ratio. A narrow bar or space is one module.
    /// </summary>
    private const int WideElement = 3;

    /// <summary>
    /// The bar height as a fraction of the symbol length, the quiet zones included, when the caller sets no height: 15 per cent, and at least
    /// <see cref="MinimumBarHeightMillimetres"/>.
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <summary>
    /// The smallest bar height in millimetres when the caller sets none.
    /// </summary>
    private const float MinimumBarHeightMillimetres = 6.35F;

    /// <summary>
    /// Calculates the check character over the character values the symbol carries: "Add the ASCII values of each character excluding the start and stop
    /// characters. Divide by 127. Unless remainder equals zero subtract from 127. The character whose ASCII value is the result is the check character." For
    /// data of NUL characters alone "the check character is exceptionally ASCII 127".
    /// </summary>
    /// <param name="values">The character values the symbol carries.</param>
    /// <returns>The value of the check character.</returns>
    public static int CheckCharacter(ReadOnlySpan<int> values)
    {
        int sum = 0;
        foreach (int value in values)
        {
            sum += value;
        }

        if (sum == 0)
        {
            return CheckModulus;
        }

        int remainder = sum % CheckModulus;
        return remainder == 0 ? 0 : CheckModulus - remainder;
    }

    /// <summary>
    /// Encodes character values and builds the symbol. The runs start with the first bar of the start code and end on the last bar of the stop code, and carry
    /// the start code, the values, the check character and the stop code. Telepen carries no guard bars, so every bar runs the full height, and the human
    /// readable interpretation sits below the symbol.
    /// </summary>
    /// <param name="values">The character values the symbol carries, already validated.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(ReadOnlySpan<int> values, ReadOnlySpan<char> readable, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        // Every run is at least one module and a character is sixteen modules, so a character is at most sixteen runs. The symbol reserves that many runs and
        // keeps those it writes. The stop code ends on a bar, so the space written after it is dropped.
        double xDimension = options.ResolveXDimension(NominalXDimension);
        Span<int> runs = symbol.BeginLinear((values.Length + FrameCharacters) * ModulesPerCharacter, xDimension, out Span<float> heights, out _);
        int written = 0;
        Append(runs, ref written, Start);
        foreach (int value in values)
        {
            Append(runs, ref written, value);
        }

        Append(runs, ref written, CheckCharacter(values));
        Append(runs, ref written, Stop);
        symbol.TrimLastRow(written - 1);
        runs = runs[..(written - 1)];

        int widthInModules = BarcodeSymbol.RunWidth(runs);

        float nominalBarHeight = MathF.Max((widthInModules + (2 * QuietZone)) * NominalBarHeightFraction, (float)(MinimumBarHeightMillimetres / xDimension));
        float barHeight = options.ResolveBarHeight(xDimension, nominalBarHeight);
        heights[..((runs.Length + 1) >> 1)].Fill(barHeight);

        if (options.Font is not null && readable.Length > 0)
        {
            symbol.ReserveText(1, readable.Length);
            symbol.AddText(readable, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(QuietZone, QuietZone);
    }

    /// <summary>
    /// Writes the bars and spaces of one character. The seven ASCII bits take an even parity bit as their eighth, and the eight bits are read least significant
    /// first. A 1 bit is a narrow bar and a narrow space. The bits 00 are a wide bar and a narrow space, and the bits 010 are a wide bar and a wide space. The
    /// bits 01 followed by a 1 are a narrow bar and a wide space, after which the bits 10 are the same pair, so a lone 0 bit always pairs with a later one.
    /// Even parity gives every character an even number of 0 bits, so the pairing always closes inside the character.
    /// </summary>
    /// <param name="runs">The buffer the widths are written to.</param>
    /// <param name="written">The write position, advanced by the element count.</param>
    /// <param name="value">The ASCII value of the character.</param>
    private static void Append(Span<int> runs, ref int written, int value)
    {
        int bits = value;
        if ((BitOperations.PopCount((uint)value) & 1) == 1)
        {
            bits |= 1 << (BitsPerCharacter - 1);
        }

        // Every step writes one bar and one space. The pair is narrow and narrow unless a 0 bit is involved, and the step consumes one to three bits.
        int i = 0;
        bool open = false;
        while (i < BitsPerCharacter)
        {
            int bar = 1;
            int space = 1;
            int consumed = 1;
            if (open)
            {
                // A pair opened by 01 stays open across 1 bits and closes on the bits 10.
                if (((bits >> (i + 1)) & 1) == 0)
                {
                    space = WideElement;
                    consumed = 2;
                    open = false;
                }
            }
            else if (((bits >> i) & 1) == 0)
            {
                if (((bits >> (i + 1)) & 1) == 0)
                {
                    bar = WideElement;
                    consumed = 2;
                }
                else if (((bits >> (i + 2)) & 1) == 0)
                {
                    bar = WideElement;
                    space = WideElement;
                    consumed = 3;
                }
                else
                {
                    space = WideElement;
                    consumed = 2;
                    open = true;
                }
            }

            runs[written++] = bar;
            runs[written++] = space;
            i += consumed;
        }
    }
}
