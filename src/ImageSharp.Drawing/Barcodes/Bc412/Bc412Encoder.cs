// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the BC412 symbology of SEMI T1-95. Every character is four bars of one module in twelve module positions. A symbol is a quiet zone, the start
/// character, the first data character, the check character, the remaining data characters, the stop character and a quiet zone.
/// </summary>
internal static class Bc412Encoder
{
    /// <summary>
    /// The characters the symbology encodes, in pattern order and in the order the check calculation values them. The letter O is not in the set, and the digit
    /// 0 stands in for it.
    /// </summary>
    public const string Characters = "0R9GLVHA8EZ4NTS1J2Q6C7DYKBUIX3FWP5M";

    /// <summary>
    /// The smallest number of data characters a symbol carries.
    /// </summary>
    public const int MinimumLength = 7;

    /// <summary>
    /// The largest number of data characters a symbol carries.
    /// </summary>
    public const int MaximumLength = 18;

    /// <summary>
    /// The quiet zone in modules on each side. The symbol widths, 13.2 mm at 7 data characters and 29.04 mm at 18, exceed the bars by 0.96 mm at the 0.12 mm
    /// module, 4 modules on each side.
    /// </summary>
    public const int QuietZone = 4;

    /// <summary>
    /// The nominal X dimension in millimetres: a module spacing of 0.12 mm.
    /// </summary>
    public const double NominalXDimension = 0.12;

    /// <summary>
    /// The position of the check character in the symbol: after the first data character.
    /// </summary>
    public const int CheckPosition = 1;

    /// <summary>
    /// The number of runs in one character: four bars and four spaces, the last of which separates it from the next character.
    /// </summary>
    private const int RunsPerCharacter = 8;

    /// <summary>
    /// The number of runs of the start character: a bar and a space.
    /// </summary>
    private const int StartElements = 2;

    /// <summary>
    /// The number of runs of the stop character: two bars and the space between them.
    /// </summary>
    private const int StopElements = 3;

    /// <summary>
    /// The modulus of the check character, the size of the character set.
    /// </summary>
    private const int CheckModulus = 35;

    /// <summary>
    /// The multiplier the check calculation applies to the reduced total.
    /// </summary>
    private const int CheckMultiplier = 17;

    /// <summary>
    /// The bar height in modules when the caller sets none: a module height of 2.00 mm at the module spacing of 0.12 mm.
    /// </summary>
    private const float NominalBarHeight = (float)(2.00 / 0.12);

    /// <summary>
    /// Gets the run widths of every character, eight per character in bar and space order, in the order of <see cref="Characters"/>. Every bar is one module,
    /// and the widths add to twelve.
    /// </summary>
    private static ReadOnlySpan<byte> Patterns =>
    [
        1, 1, 1, 1, 1, 1, 1, 5, 1, 3, 1, 1, 1, 2, 1, 2, 1, 1, 1, 3, 1, 1, 1, 3, 1, 2, 1, 1, 1, 2, 1, 3,
        1, 2, 1, 2, 1, 3, 1, 1, 1, 3, 1, 3, 1, 1, 1, 1, 1, 2, 1, 1, 1, 3, 1, 2, 1, 1, 1, 3, 1, 2, 1, 2,
        1, 1, 1, 2, 1, 4, 1, 1, 1, 1, 1, 5, 1, 1, 1, 1, 1, 5, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 5, 1, 1,
        1, 2, 1, 3, 1, 2, 1, 1, 1, 3, 1, 2, 1, 1, 1, 2, 1, 3, 1, 1, 1, 3, 1, 1, 1, 1, 1, 1, 1, 2, 1, 4,
        1, 2, 1, 2, 1, 1, 1, 3, 1, 1, 1, 1, 1, 3, 1, 3, 1, 3, 1, 1, 1, 1, 1, 3, 1, 1, 1, 2, 1, 2, 1, 3,
        1, 1, 1, 4, 1, 1, 1, 2, 1, 1, 1, 2, 1, 3, 1, 2, 1, 1, 1, 4, 1, 2, 1, 1, 1, 4, 1, 2, 1, 1, 1, 1,
        1, 2, 1, 2, 1, 2, 1, 2, 1, 1, 1, 3, 1, 3, 1, 1, 1, 3, 1, 2, 1, 2, 1, 1, 1, 2, 1, 1, 1, 4, 1, 1,
        1, 4, 1, 1, 1, 2, 1, 1, 1, 1, 1, 1, 1, 4, 1, 2, 1, 2, 1, 1, 1, 1, 1, 4, 1, 4, 1, 1, 1, 1, 1, 2,
        1, 2, 1, 4, 1, 1, 1, 1, 1, 1, 1, 2, 1, 1, 1, 4, 1, 2, 1, 3, 1, 1, 1, 2,
    ];

    /// <summary>
    /// Gets the value of every code point from 0 to Z, which is its index in <see cref="Characters"/>, or -1 for a code point outside the set: the seven
    /// symbols between the digits and the letters, and the letter O.
    /// </summary>
    private static ReadOnlySpan<sbyte> Values =>
    [
        0, 15, 17, 29, 11, 33, 19, 21, 8, 2,
        -1, -1, -1, -1, -1, -1, -1,
        7, 25, 20, 22, 9, 30, 3, 6, 27, 16, 24, 4, 34, 12, -1, 32, 18, 1, 14, 13, 26, 5, 31, 28, 23, 10,
    ];

    /// <summary>
    /// Returns the value of a character, which is its index in <see cref="Characters"/>.
    /// </summary>
    /// <param name="codePoint">The code point to value.</param>
    /// <returns>The value, or -1 when the character is not in the set.</returns>
    public static int Value(int codePoint) => codePoint is >= '0' and <= 'Z' ? Values[codePoint - '0'] : -1;

    /// <summary>
    /// Calculates the check character over the data. The check character is counted as the second character of the symbol with the value 0. The values in the
    /// odd positions and the values in the even positions are added, each sum is reduced modulo 35, the even sum is doubled, and the total is reduced modulo
    /// 35, multiplied by 17 and reduced modulo 35 again.
    /// </summary>
    /// <param name="data">The data characters, already validated.</param>
    /// <returns>The check character.</returns>
    public static char CheckCharacter(ReadOnlySpan<char> data)
    {
        int oddSum = Value(data[0]);
        int evenSum = 0;
        for (int i = 1; i < data.Length; i++)
        {
            int value = Value(data[i]);
            if (((i + CheckPosition) & 1) == 0)
            {
                oddSum += value;
            }
            else
            {
                evenSum += value;
            }
        }

        int check = ((oddSum % CheckModulus) + (2 * (evenSum % CheckModulus))) % CheckModulus;
        return Characters[(check * CheckMultiplier) % CheckModulus];
    }

    /// <summary>
    /// Encodes the characters the symbol carries and builds the symbol. The runs start with the bar of the start character and end on the last bar of the stop
    /// character. BC412 carries no guard bars, so every bar runs the full height, and the human readable interpretation, the carried characters, sits below the
    /// symbol.
    /// </summary>
    /// <param name="carried">The first data character, the check character and the remaining data.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(ReadOnlySpan<char> carried, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        double xDimension = options.XDimension ?? NominalXDimension;
        Span<int> runs = symbol.BeginLinear(StartElements + (carried.Length * RunsPerCharacter) + StopElements, xDimension, out Span<float> heights, out _);

        // The start character is a bar and a two module space, and the stop character is a bar, a space and a bar of one module each.
        int written = 0;
        runs[written++] = 1;
        runs[written++] = 2;
        foreach (char c in carried)
        {
            foreach (byte width in Patterns.Slice(Value(c) * RunsPerCharacter, RunsPerCharacter))
            {
                runs[written++] = width;
            }
        }

        runs[written++] = 1;
        runs[written++] = 1;
        runs[written] = 1;

        int widthInModules = BarcodeSymbol.RunWidth(runs);

        float barHeight = options.ResolveBarHeight(xDimension, NominalBarHeight);
        heights.Fill(barHeight);

        if (options.Font is not null)
        {
            symbol.ReserveText(1, carried.Length);
            symbol.AddText(carried, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(QuietZone, QuietZone);
    }
}
