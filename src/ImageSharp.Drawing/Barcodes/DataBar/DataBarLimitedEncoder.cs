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
    private const double NominalXDimension = 0.33;

    /// <summary>
    /// The height in modules. Section 5.5.2.2 of the GS1 General Specifications: "The minimum height SHALL be 10X", which Table 5-50 gives as the target height
    /// too.
    /// </summary>
    private const float Height = 10F;

    /// <summary>
    /// The width in modules. Section 5.5.2.2: "The total symbol contains 47 elements comprising 79 modules."
    /// </summary>
    private const int Width = 79;

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
    /// The number of digits that the symbol encodes: the GTIN without its check digit.
    /// </summary>
    private const int DataDigits = 13;

    /// <summary>
    /// The number of elements of a symbol character or the check character.
    /// </summary>
    private const int CharacterElements = 14;

    /// <summary>
    /// The number of modules of a symbol character.
    /// </summary>
    private const int CharacterModules = 26;

    /// <summary>
    /// The number of elements of the leading guard pattern, a narrow space and a narrow bar.
    /// </summary>
    private const int GuardElements = 2;

    /// <summary>
    /// The number of elements of each of the two sets of the check character before its narrow space and bar.
    /// </summary>
    private const int CheckSetElements = 6;

    /// <summary>
    /// The number of modules of each of the two sets of the check character.
    /// </summary>
    private const int CheckSetModules = 8;

    /// <summary>
    /// The widest element of a set of the check character, in modules.
    /// </summary>
    private const int CheckSetWidest = 3;

    /// <summary>
    /// The number of patterns of one set of the check character, <see cref="CheckSetElements"/> elements over <see cref="CheckSetModules"/> modules with no
    /// element wider than <see cref="CheckSetWidest"/>.
    /// </summary>
    private const int CheckSetPatterns = 21;

    /// <summary>
    /// The width in modules of the space that closes the symbol. Section 5.5.2.2: "ending with a 5X space".
    /// </summary>
    private const int TrailingSpaceModules = 5;

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
        for (int i = 0; i < DataDigits; i++)
        {
            digits = (digits * 10) + (gtin[i] - '0');
        }

        value += digits;

        double xDimension = options.ResolveXDimension(DataBarEncoder.MinimumXDimension, NominalXDimension, DataBarEncoder.MaximumXDimension);

        // The element widths start with the guard space, so a zero width bar leads the runs, and the symbol ends on the five module space, so a zero width bar
        // closes them. Both zero width bars are the zeros the symbol storage starts with. The left character follows the guard pattern, then the check
        // character and the right character follow.
        const int checkStart = GuardElements + CharacterElements;
        const int rightStart = checkStart + CharacterElements;
        Span<int> runs = symbol.BeginLinear(ElementCount + 2, xDimension, out Span<float> heights, out _);
        Span<int> widths = runs.Slice(1, ElementCount);
        widths[..GuardElements].Fill(1);

        // The buffer holds the odd and the even set of the character being calculated.
        const int setElements = CharacterElements / 2;
        InlineArray14<int> setStorage = default;
        Span<int> sets = setStorage;
        Span<int> odd = sets[..setElements];
        Span<int> even = sets[setElements..];
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

            // Only the even set excludes the combinations without a one module element.
            DataBarWidths.Calculate(odd, withinGroup / EvenCombinations[group], OddModules[group], OddWidest[group], false);
            DataBarWidths.Calculate(even, withinGroup % EvenCombinations[group], CharacterModules - OddModules[group], 9 - OddWidest[group], true);

            Span<int> elements = widths.Slice(c == 0 ? GuardElements : rightStart, CharacterElements);
            ReadOnlySpan<byte> weights = CheckWeights.Slice(c * CharacterElements, CharacterElements);
            for (int k = 0; k < setElements; k++)
            {
                elements[k * 2] = odd[k];
                elements[(k * 2) + 1] = even[k];
                check += (weights[k * 2] * odd[k]) + (weights[(k * 2) + 1] * even[k]);
            }
        }

        // The check character is two sets of six elements over eight modules, interleaved, and a narrow space and bar.
        int sequence = CheckSequence[check % CheckModulus];
        Span<int> first = sets[..CheckSetElements];
        Span<int> second = sets.Slice(CheckSetElements, CheckSetElements);
        DataBarWidths.Calculate(first, sequence / CheckSetPatterns, CheckSetModules, CheckSetWidest, false);
        DataBarWidths.Calculate(second, sequence % CheckSetPatterns, CheckSetModules, CheckSetWidest, false);
        Span<int> checkElements = widths.Slice(checkStart, CharacterElements);
        for (int k = 0; k < CheckSetElements; k++)
        {
            checkElements[k * 2] = first[k];
            checkElements[(k * 2) + 1] = second[k];
        }

        checkElements[^2..].Fill(1);

        // A narrow space, a narrow bar and the five module space close the symbol.
        widths[^3] = 1;
        widths[^2] = 1;
        widths[^1] = TrailingSpaceModules;

        // Section 5.5.2.2 gives a minimum height and no maximum.
        float barHeight = options.ResolveBarHeight(xDimension, Height, Height, float.PositiveInfinity);
        heights.Fill(barHeight);

        // Section 4.14 of the GS1 General Specifications prints an element string with its Application Identifier in parentheses.
        DataBarEncoder.AddCaption(gtin, Width, barHeight, options, ref symbol);
        symbol.EndLinear(0, 0);
    }
}
