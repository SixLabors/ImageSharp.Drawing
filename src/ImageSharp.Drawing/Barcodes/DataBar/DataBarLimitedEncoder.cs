// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for GS1 DataBar Limited. A symbol carries the item identification of GS1 Application Identifier (01), with an indicator digit of 0 or 1, as two
/// symbol characters with a check character between them that encodes a modulo 89 check value. A narrow space and bar lead the symbol, and a narrow space, a
/// narrow bar and a space of five modules close it.
/// <para>
/// The 13 digits before the check digit form a number below 2 x 10^12. It splits into a left and a right character value modulo 2013571. A symbol character is
/// 14 elements, an odd set of 7 and an even set of 7 interleaved, over 26 modules. Its group gives the module count and the widest element of each set, and its
/// value within the group gives the width of every element.
/// </para>
/// </summary>
internal static class DataBarLimitedEncoder
{
    /// <summary>
    /// The nominal X dimension in millimetres, the target X dimension of Table 5-50 of the GS1 General Specifications.
    /// </summary>
    public const double NominalXDimension = 0.33;

    /// <summary>
    /// The height in modules. Section 5.5.2.2 of the GS1 General Specifications: "The minimum height SHALL be 10X", which Table 5-50 gives as the target height
    /// too.
    /// </summary>
    public const float Height = 10F;

    /// <summary>
    /// The width in modules. Section 5.5.2.2: "The total symbol contains 47 elements comprising 79 modules."
    /// </summary>
    public const int Width = 79;

    /// <summary>
    /// The number of elements: the leading guard pair, two symbol characters and the check character of 14 elements each, and the closing guard of three.
    /// </summary>
    private const int ElementCount = 47;

    /// <summary>
    /// The modulus that splits the data value into the left and right character values.
    /// </summary>
    private const int CharacterModulus = 2013571;

    /// <summary>
    /// The value a linked symbol adds to its data value.
    /// </summary>
    private const long LinkedOffset = 2015133531096;

    /// <summary>
    /// The modulus of the check value.
    /// </summary>
    private const int CheckModulus = 89;

    /// <summary>
    /// Gets the first character value of each group.
    /// </summary>
    private static ReadOnlySpan<int> GroupSum => [0, 183064, 820064, 1000776, 1491021, 1979845, 1996939];

    /// <summary>
    /// Gets the number of even set combinations of each group. A value within a group splits into the odd and the even set value by this number.
    /// </summary>
    private static ReadOnlySpan<int> EvenCombinations => [28, 728, 6454, 203, 2408, 1, 16632];

    /// <summary>
    /// Gets the module count of the odd set of each group. The even set takes the rest of the 26 modules.
    /// </summary>
    private static ReadOnlySpan<byte> OddModules => [17, 13, 9, 15, 11, 19, 7];

    /// <summary>
    /// Gets the widest element of the odd set of each group. The widest element of the even set is nine less this.
    /// </summary>
    private static ReadOnlySpan<byte> OddWidest => [6, 5, 3, 5, 4, 8, 1];

    /// <summary>
    /// Gets the check weights, 14 for the left character and 14 for the right.
    /// </summary>
    private static ReadOnlySpan<byte> CheckWeights =>
    [
        1, 3, 9, 27, 81, 65, 17, 51, 64, 14, 42, 37, 22, 66,
        20, 60, 2, 6, 18, 54, 73, 41, 34, 13, 39, 28, 84, 74
    ];

    /// <summary>
    /// Gets the check character pattern of each check value, an index into the 21 x 21 patterns of two sets of six elements over eight modules with no element
    /// wider than three. The quotient by 21 selects the widths of the first set and the remainder those of the second.
    /// </summary>
    private static ReadOnlySpan<ushort> CheckSequence =>
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22,
        23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43,
        45, 52, 57,
        63, 64, 65, 66,
        73, 74, 75, 76, 77, 78, 79,
        82,
        126, 127, 128, 129, 130,
        132,
        141, 142, 143, 144, 145, 146,
        210, 211, 212, 213, 214, 215, 216, 217,
        220,
        316, 317, 318, 319, 320,
        322, 323,
        326, 337
    ];

    /// <summary>
    /// Builds the symbol.
    /// </summary>
    /// <param name="gtin">The 14 digit GTIN, whose first digit is 0 or 1.</param>
    /// <param name="linked">Whether a 2D component of a GS1 Composite symbol is linked to the symbol.</param>
    /// <param name="options">The options that control the bar height and the text.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void Build(ReadOnlySpan<char> gtin, bool linked, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        // The check digit is not encoded: the 13 digits before it are the data value, and a linked symbol adds LinkedOffset to it.
        long value = linked ? LinkedOffset : 0;
        long digits = 0;
        for (int i = 0; i < 13; i++)
        {
            digits = (digits * 10) + (gtin[i] - '0');
        }

        value += digits;

        double xDimension = options.ResolveXDimension(DataBarEncoder.MinimumXDimension, NominalXDimension, DataBarEncoder.MaximumXDimension);

        // The element widths start with the guard space, so a zero width bar leads the runs, and the symbol ends on the five module space, so a zero width bar
        // closes them. Both zero width bars are the zeros the symbol storage starts with.
        Span<int> runs = symbol.BeginLinear(ElementCount + 2, xDimension, out Span<float> heights, out _);
        Span<int> widths = runs.Slice(1, ElementCount);
        widths[0] = 1;
        widths[1] = 1;

        // The buffer holds the odd and the even set of the character being calculated.
        InlineArray14<int> setStorage = default;
        Span<int> sets = setStorage;
        Span<int> odd = sets[..7];
        Span<int> even = sets[7..];
        int check = 0;
        for (int c = 0; c < 2; c++)
        {
            int character = (int)(c == 0 ? value / CharacterModulus : value % CharacterModulus);
            int group = GroupSum.Length - 1;
            while (character < GroupSum[group])
            {
                group--;
            }

            int withinGroup = character - GroupSum[group];

            // Combinations without a one module element are excluded from the even set only.
            DataBarWidths.Calculate(odd, withinGroup / EvenCombinations[group], OddModules[group], OddWidest[group], false);
            DataBarWidths.Calculate(even, withinGroup % EvenCombinations[group], 26 - OddModules[group], 9 - OddWidest[group], true);

            // The left character follows the guard pair and the right character follows the check character.
            Span<int> elements = widths.Slice(c == 0 ? 2 : 30, 14);
            for (int k = 0; k < 7; k++)
            {
                elements[k * 2] = odd[k];
                elements[(k * 2) + 1] = even[k];
                check += (CheckWeights[(c * 14) + (k * 2)] * odd[k]) + (CheckWeights[(c * 14) + (k * 2) + 1] * even[k]);
            }
        }

        // The check character is two sets of six elements over eight modules, interleaved, and a narrow space and bar.
        int sequence = CheckSequence[check % CheckModulus];
        Span<int> first = sets[..6];
        Span<int> second = sets.Slice(6, 6);
        DataBarWidths.Calculate(first, sequence / 21, 8, 3, false);
        DataBarWidths.Calculate(second, sequence % 21, 8, 3, false);
        Span<int> checkElements = widths.Slice(16, 14);
        for (int k = 0; k < 6; k++)
        {
            checkElements[k * 2] = first[k];
            checkElements[(k * 2) + 1] = second[k];
        }

        checkElements[12] = 1;
        checkElements[13] = 1;

        widths[44] = 1;
        widths[45] = 1;
        widths[46] = 5;

        // Section 5.5.2.2 gives a minimum height and no maximum.
        float barHeight = options.ResolveBarHeight(xDimension, Height, Height, float.PositiveInfinity);
        heights.Fill(barHeight);

        // Section 4.14 of the GS1 General Specifications prints an element string with its Application Identifier in parentheses.
        DataBarEncoder.AddCaption(gtin, Width, barHeight, options, ref symbol);
        symbol.EndLinear(0, 0);
    }
}
