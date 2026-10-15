// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the BC412 symbology of SEMI T1-95. Every character is four bars of one module in
/// twelve module positions. A symbol is a quiet zone, the start character, the first data character, the
/// check character, the remaining data characters, the stop character and a quiet zone.
/// </summary>
internal static class Bc412Encoder
{
    /// <summary>
    /// The characters the symbology encodes, in pattern order and in the order the check calculation
    /// values them. The letter O is not in the set, and the digit 0 stands in for it.
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
    /// The quiet zone in modules on each side. The symbol widths, 13.2 mm at 7 data characters and
    /// 29.04 mm at 18, exceed the bars by 0.96 mm at the 0.12 mm module, 4 modules on each side.
    /// </summary>
    public const int QuietZone = 4;

    /// <summary>
    /// The nominal X dimension in millimetres: the 0.12 mm module spacing of Table 1 of SEMI T1-95.
    /// </summary>
    public const double NominalXDimension = 0.12;

    /// <summary>
    /// The position of the check character in the symbol: after the first data character.
    /// </summary>
    public const int CheckPosition = 1;

    /// <summary>
    /// The number of runs in one character: four bars and four spaces, the last of which separates it
    /// from the next character.
    /// </summary>
    private const int RunsPerCharacter = 8;

    /// <summary>
    /// The bar height in modules when the caller sets none: the 2.00 mm of Table 1 of SEMI T1-95 at its
    /// 0.12 mm module spacing.
    /// </summary>
    private const float NominalBarHeight = (float)(2.00 / 0.12);

    /// <summary>
    /// Gets the run widths of every character, eight per character in bar and space order, in the
    /// order of <see cref="Characters"/>. Every bar is one module, and the widths add to twelve.
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
    /// Returns the value of a character, which is its index in <see cref="Characters"/>.
    /// </summary>
    /// <param name="codePoint">The code point to value.</param>
    /// <returns>The value, or -1 when the character is not in the set.</returns>
    public static int Value(int codePoint) => codePoint is >= '0' and <= 'Z' ? Characters.IndexOf((char)codePoint) : -1;

    /// <summary>
    /// Calculates the check character over the data. The check character is counted as the second
    /// character of the symbol with the value 0. The values in the odd positions and the values in the
    /// even positions are added, each sum is reduced modulo 35, the even sum is doubled, and the total is
    /// reduced modulo 35, multiplied by 17 and reduced modulo 35 again.
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

        int check = ((oddSum % 35) + (2 * (evenSum % 35))) % 35;
        return Characters[(check * 17) % 35];
    }

    /// <summary>
    /// Encodes the characters the symbol carries and builds the symbol. The runs start with the bar of the
    /// start character and end on the last bar of the stop character. BC412 carries no guard bars, so
    /// every bar runs the full height, and the human readable interpretation, the carried characters, sits
    /// below the symbol.
    /// </summary>
    /// <param name="carried">The first data character, the check character and the remaining data.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(ReadOnlySpan<char> carried, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        double xDimension = options.XDimension ?? NominalXDimension;
        Span<int> runs = symbol.BeginLinear(2 + (carried.Length * RunsPerCharacter) + 3, xDimension, out Span<float> heights, out _);
        int written = 0;
        runs[written++] = 1;
        runs[written++] = 2;
        for (int i = 0; i < carried.Length; i++)
        {
            int offset = Value(carried[i]) * RunsPerCharacter;
            for (int j = 0; j < RunsPerCharacter; j++)
            {
                runs[written++] = Patterns[offset + j];
            }
        }

        runs[written++] = 1;
        runs[written++] = 1;
        runs[written] = 1;

        int widthInModules = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            widthInModules += runs[i];
        }

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
