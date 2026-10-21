// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the Codabar symbology, which ANSI/AIM BC3-1995 and BS EN 798:1995 define. Every symbol character has seven elements, four bars and three
/// spaces, of which two or three are wide, and a narrow inter-character gap follows it. Section 4.2 of BS EN 798 gives the structure: a quiet zone, a start
/// character, one or more data characters, a stop character and a quiet zone.
/// </summary>
internal static class CodabarEncoder
{
    /// <summary>
    /// The largest number of characters a symbol carries. The symbology sets no maximum.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The value of the first start and stop character. Values below it are data characters.
    /// </summary>
    public const int FirstStartStop = 16;

    /// <summary>
    /// The characters the symbology encodes, in pattern order: the sixteen data characters, then the four start and stop characters.
    /// </summary>
    private const string Characters = "0123456789-$:/.+ABCD";

    /// <summary>
    /// The sixteen data characters of <see cref="Characters"/>, for one vectorized search over the data.
    /// </summary>
    public static readonly SearchValues<char> DataCharacterSet = SearchValues.Create(Characters.AsSpan(0, FirstStartStop));

    /// <summary>
    /// The quiet zone in modules on each side when 10X is wider than <see cref="MinimumQuietZoneMillimetres"/>. Section 4.4.1 (d) of BS EN 798: "minimum width
    /// of quiet zone: 10X or 2,54 mm, whichever is greater".
    /// </summary>
    private const int QuietZone = 10;

    /// <summary>
    /// The smallest quiet zone in millimetres, section 4.4.1 (d) of BS EN 798.
    /// </summary>
    private const double MinimumQuietZoneMillimetres = 2.54;

    /// <summary>
    /// The smallest X dimension in millimetres. Section 4.4.1 (a) of BS EN 798: "minimum width of narrow element (X): 0,191 mm". The section gives no largest X
    /// dimension.
    /// </summary>
    private const double MinimumXDimension = 0.191;

    /// <summary>
    /// The number of elements in a symbol character, its inter-character gap excluded.
    /// </summary>
    private const int ElementsPerCharacter = 7;

    /// <summary>
    /// The width of a wide element in modules. Section 6.1.2 of the TEC-IT Barcode Reference gives a print ratio "in the following range: 1:2 -1:3 (Pr &gt;=
    /// 2.25:1)", and 3 is the only whole number in that range.
    /// </summary>
    private const int WideElement = 3;

    /// <summary>
    /// The smallest bar height as a fraction of the symbol width, quiet zones excluded. Section 4.4.1 (e) of BS EN 798: "minimum bar code height: 5,0 mm or
    /// 15 % of symbol width, whichever is greater".
    /// </summary>
    private const float MinimumBarHeightFraction = 0.15F;

    /// <summary>
    /// The smallest bar height in millimetres, section 4.4.1 (e) of BS EN 798.
    /// </summary>
    private const float MinimumBarHeightMillimetres = 5F;

    /// <summary>
    /// Gets the element widths of every character, one bit per element, most significant element first, in bar and space order: a set bit is a wide element and
    /// a clear bit a narrow one. The gap that follows the character is not in the pattern. The entries are in the order of <see cref="Characters"/>.
    /// </summary>
    private static ReadOnlySpan<byte> Patterns =>
    [
        0x03, 0x06, 0x09, 0x60, 0x12, 0x42, 0x21, 0x24, 0x30, 0x48,
        0x0C, 0x18, 0x45, 0x51, 0x54, 0x15, 0x1A, 0x29, 0x0B, 0x0E,
    ];

    /// <summary>
    /// Returns the value of a character, which is its index in <see cref="Characters"/>. The letters T, N, * and E are the alternative names of the start and
    /// stop characters A, B, C and D, and take the same values.
    /// </summary>
    /// <param name="codePoint">The code point to value.</param>
    /// <returns>The value, or -1 when the character is not in the set.</returns>
    public static int Value(int codePoint) => codePoint switch
    {
        >= '0' and <= '9' => codePoint - '0',
        '-' => 10,
        '$' => 11,
        ':' => 12,
        '/' => 13,
        '.' => 14,
        '+' => 15,
        'A' or 'T' => 16,
        'B' or 'N' => 17,
        'C' or '*' => 18,
        'D' or 'E' => 19,
        _ => -1,
    };

    /// <summary>
    /// Calculates the modulo 16 check character over a symbol. Annex A.3 of BS EN 798 leaves the check character to the application. The values of the start
    /// character, the data characters and the stop character are added, and the check character is the one whose value lifts the sum to the next multiple of
    /// sixteen.
    /// </summary>
    /// <param name="head">The start character and the data.</param>
    /// <param name="stop">The stop character.</param>
    /// <returns>The check character.</returns>
    public static char CheckCharacter(ReadOnlySpan<char> head, char stop)
    {
        int sum = Value(stop);
        foreach (char c in head)
        {
            sum += Value(c);
        }

        return Characters[(FirstStartStop - (sum % FirstStartStop)) % FirstStartStop];
    }

    /// <summary>
    /// Encodes the characters and builds the symbol. The runs carry the start character, the data, an optional check character and the stop character, each
    /// followed by its inter-character gap except the stop character, so the runs end on a bar. The check character sits before the stop character. Codabar
    /// carries no guard bars, so every bar runs the full height, and the human readable interpretation sits below the symbol: the start character and the data,
    /// the check character when it prints, and the stop character.
    /// </summary>
    /// <param name="head">The start character and the data, already validated.</param>
    /// <param name="stop">The stop character, already validated.</param>
    /// <param name="check">The check character to carry before the stop character, or <see langword="null"/>.</param>
    /// <param name="printCheck">Whether the check character is part of the human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(ReadOnlySpan<char> head, char stop, char? check, bool printCheck, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        int characters = head.Length + 1 + (check is null ? 0 : 1);
        double xDimension = options.ResolveXDimension(MinimumXDimension, BarcodeSymbology.DefaultXDimension, double.PositiveInfinity);
        Span<int> runs = symbol.BeginLinear((characters * (ElementsPerCharacter + 1)) - 1, xDimension, out Span<float> heights, out _);
        int written = 0;

        foreach (char c in head)
        {
            Append(runs, ref written, Value(c), true);
        }

        if (check is not null)
        {
            Append(runs, ref written, Value(check.Value), true);
        }

        Append(runs, ref written, Value(stop), false);

        int widthInModules = BarcodeSymbol.RunWidth(runs);

        // The minimum of section 4.4.1 (e) is also the nominal bar height. The section gives no maximum.
        float minimumBarHeight = MathF.Max(widthInModules * MinimumBarHeightFraction, (float)(MinimumBarHeightMillimetres / xDimension));
        float barHeight = options.ResolveBarHeight(xDimension, minimumBarHeight, minimumBarHeight, float.PositiveInfinity);
        heights.Fill(barHeight);

        if (options.Font is not null)
        {
            ReadOnlySpan<char> tail = check is not null && printCheck ? [check.Value, stop] : [stop];
            symbol.ReserveText(1, head.Length + tail.Length);
            symbol.AddText(head, tail, [], 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        float quietZone = MathF.Max(QuietZone, (float)(MinimumQuietZoneMillimetres / xDimension));
        symbol.EndLinear(quietZone, quietZone);
    }

    /// <summary>
    /// Writes the seven element widths of one character and, when one follows, its inter-character gap.
    /// </summary>
    /// <param name="runs">The buffer the widths are written to.</param>
    /// <param name="written">The write position, advanced by the element count.</param>
    /// <param name="value">The character value, which is the pattern index.</param>
    /// <param name="gap">Whether a narrow inter-character gap follows the character.</param>
    private static void Append(Span<int> runs, ref int written, int value, bool gap)
    {
        byte pattern = Patterns[value];
        for (int element = ElementsPerCharacter - 1; element >= 0; element--)
        {
            runs[written++] = ((pattern >> element) & 1) == 1 ? WideElement : 1;
        }

        if (gap)
        {
            runs[written++] = 1;
        }
    }
}
