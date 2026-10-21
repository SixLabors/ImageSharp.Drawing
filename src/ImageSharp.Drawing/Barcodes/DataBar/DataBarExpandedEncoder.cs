// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for GS1 DataBar Expanded and GS1 DataBar Expanded Stacked. A symbol carries GS1 element strings as a bit stream cut into 12 bit symbol
/// characters, preceded by a check character that encodes a modulo 211 check value and the symbol length. A finder pattern follows every second symbol
/// character.
/// <para>
/// The bit stream starts with a linkage bit and an encodation method. Common sequences of element strings that start with Application Identifier (01) compress
/// into a fixed length field. The general purpose field encodes whatever remains in numeric, alphanumeric or ISO/IEC 646 mode. A symbol character is 8 elements
/// over 17 modules: an odd set of 4 and an even set of 4, interleaved. The group of a character gives the module count and the widest element of each set.
/// </para>
/// </summary>
internal static class DataBarExpandedEncoder
{
    /// <summary>
    /// The nominal X dimension in millimetres, the target X dimension of Table 5-44 of the GS1 General Specifications.
    /// </summary>
    public const double NominalXDimension = 0.33;

    /// <summary>
    /// The height of a row in modules. Section 5.5.2.3.1 of the GS1 General Specifications gives "34X high", and Table 5-44 gives 11.23 millimetres, 34X, at
    /// the target X dimension. It is also the minimum. Table 5-56 gives GS1 DataBar Expanded 8.98 millimetres, 34X, at the X dimension of 0.264 millimetre. It
    /// gives GS1 DataBar Expanded Stacked 18.74 millimetres, 71X, for two rows of 34X and the 3X separator. Its note (*): "the symbol height SHALL NOT be
    /// truncated below the minimum".
    /// </summary>
    public const float RowHeight = 34F;

    /// <summary>
    /// The fewest segments in a row of a stacked symbol. Section 5.5.2.3.2: "It can be printed in widths of 2 to 20 segments".
    /// </summary>
    public const int MinimumSegments = 2;

    /// <summary>
    /// The most segments in a row of a stacked symbol.
    /// </summary>
    public const int MaximumStackedSegments = 20;

    /// <summary>
    /// The most data characters a symbol carries. Section 5.5.1.1 of the GS1 General Specifications gives the maximum data capacity of the GS1 DataBar
    /// Expanded variations as "74 numeric or 41 alphabetic characters".
    /// </summary>
    public const int MaximumDataCharacters = 74;

    /// <summary>
    /// The height of each of the three separator rows between two rows of a stacked symbol, in modules. Section 5.5.2.3.2: "Its structure includes a 3X high
    /// separator pattern between rows."
    /// </summary>
    private const float SeparatorHeight = 1F;

    /// <summary>
    /// The most symbol characters a symbol carries, the check character and 21 data characters. Section 5.5.2.3.1: "from 4 to 22 symbol characters".
    /// </summary>
    private const int MaximumSymbolCharacters = 22;

    /// <summary>
    /// The most data characters a symbol carries, the symbol characters after the check character.
    /// </summary>
    private const int MaximumDataSymbolCharacters = MaximumSymbolCharacters - 1;

    /// <summary>
    /// The number of data bits one data character carries.
    /// </summary>
    private const int BitsPerSymbolCharacter = 12;

    /// <summary>
    /// The number of modules of a symbol character.
    /// </summary>
    private const int CharacterModules = 17;

    /// <summary>
    /// The number of modules of a finder pattern.
    /// </summary>
    private const int FinderModules = 15;

    /// <summary>
    /// The number of elements of a symbol character, an odd set of 4 and an even set of 4.
    /// </summary>
    private const int CharacterElements = 8;

    /// <summary>
    /// The number of elements of a finder pattern.
    /// </summary>
    private const int FinderElements = 5;

    /// <summary>
    /// The number of elements of the guard pattern at each end, a narrow space and a narrow bar. Each element is one module, so the guard pattern is also
    /// this many modules wide.
    /// </summary>
    private const int GuardElements = 2;

    /// <summary>
    /// The fewest symbol characters a symbol carries. Section 5.5.2.3.1: "from 4 to 22 symbol characters".
    /// </summary>
    private const int MinimumSymbolCharacters = 4;

    /// <summary>
    /// The first module of the first finder pattern of a row, after the guard pattern and the first symbol character.
    /// </summary>
    private const int FirstFinderStart = GuardElements + CharacterModules;

    /// <summary>
    /// The distance in modules from one finder pattern of a row to the next: the finder pattern and two symbol characters.
    /// </summary>
    private const int FinderPitch = FinderModules + (2 * CharacterModules);

    /// <summary>
    /// The number of light modules at each end of a separator row.
    /// </summary>
    private const int SeparatorLightModules = 4;

    /// <summary>
    /// The width in modules of the widest row: two guard patterns, 20 symbol characters and 10 finder patterns.
    /// </summary>
    private const int MaximumRowWidth = (2 * GuardElements) + (MaximumStackedSegments * CharacterModules) + (MaximumStackedSegments / 2 * FinderModules);

    /// <summary>
    /// The modulus of the check value.
    /// </summary>
    private const int CheckModulus = 211;

    /// <summary>
    /// The encodation methods, which the encodation method field after the linkage bit selects. Each member names the bits of its field.
    /// </summary>
    private enum EncodationMethod
    {
        /// <summary>
        /// Method 1: Application Identifier (01) in a compressed field, then the general purpose field.
        /// </summary>
        Gtin,

        /// <summary>
        /// Method 00: the general purpose field alone.
        /// </summary>
        GeneralPurpose,

        /// <summary>
        /// Method 0100: Application Identifiers (01) and (3103).
        /// </summary>
        MetricWeight,

        /// <summary>
        /// Method 0101: Application Identifiers (01) and (3202) or (3203).
        /// </summary>
        PoundWeight,

        /// <summary>
        /// Method 01100: Application Identifiers (01) and (392x), then the general purpose field.
        /// </summary>
        Price,

        /// <summary>
        /// Method 01101: Application Identifiers (01) and (393x), then the general purpose field.
        /// </summary>
        PriceWithCurrency,

        /// <summary>
        /// Methods 0111000 to 0111111: Application Identifiers (01), (310x) or (320x), and (11), (13), (15) or (17) or no date.
        /// </summary>
        WeightAndDate,
    }

    /// <summary>
    /// The modes of the general purpose field. The class of a character is the mode with the fewest bits that carries it.
    /// </summary>
    private enum GeneralFieldMode : byte
    {
        /// <summary>
        /// The class of a character the field cannot carry.
        /// </summary>
        None,

        /// <summary>
        /// Digits and the separator, two per seven bits.
        /// </summary>
        Numeric,

        /// <summary>
        /// Digits, capital letters and five punctuation characters.
        /// </summary>
        Alphanumeric,

        /// <summary>
        /// The ISO/IEC 646 characters GS1 element strings use.
        /// </summary>
        Iso646,
    }

    /// <summary>
    /// Gets the punctuation characters ISO/IEC 646 mode encodes, in the order of their values from 232.
    /// </summary>
    private static ReadOnlySpan<char> Iso646Punctuation => "!\"%&'()*+,-./:;<=>?_ ";

    /// <summary>
    /// Gets the first symbol character value of each group.
    /// </summary>
    private static ReadOnlySpan<int> GroupSum => [0, 348, 1388, 2948, 3988];

    /// <summary>
    /// Gets the number of even set combinations of each group. A value within a group splits into the odd and the even set value by this number.
    /// </summary>
    private static ReadOnlySpan<byte> EvenCombinations => [4, 20, 52, 104, 204];

    /// <summary>
    /// Gets the module count of the odd set of each group. The even set takes the rest of the 17 modules.
    /// </summary>
    private static ReadOnlySpan<byte> OddModules => [12, 10, 8, 6, 4];

    /// <summary>
    /// Gets the widest element of the odd set of each group. The widest element of the even set is nine less this.
    /// </summary>
    private static ReadOnlySpan<byte> OddWidest => [7, 5, 4, 3, 1];

    /// <summary>
    /// Gets the element widths of the twelve finder patterns, five each: A1, A2, B1, B2, C1, C2, D1, D2, E1, E2, F1 and F2. A version 2 finder is its version 1
    /// finder reversed.
    /// </summary>
    private static ReadOnlySpan<byte> FinderWidths =>
    [
        1, 8, 4, 1, 1,
        1, 1, 4, 8, 1,
        3, 6, 4, 1, 1,
        1, 1, 4, 6, 3,
        3, 4, 6, 1, 1,
        1, 1, 6, 4, 3,
        3, 2, 8, 1, 1,
        1, 1, 8, 2, 3,
        2, 6, 5, 1, 1,
        1, 1, 5, 6, 2,
        2, 2, 9, 1, 1,
        1, 1, 9, 2, 2
    ];

    /// <summary>
    /// Gets the finder pattern sequences, one per symbol length. The sequence of a symbol with 2r + 3 or 2r + 4 symbol characters has r + 2 finders and starts
    /// at r (r + 3) / 2.
    /// </summary>
    private static ReadOnlySpan<byte> FinderSequences =>
    [
        0, 1,
        0, 3, 2,
        0, 5, 2, 7,
        0, 9, 2, 7, 4,
        0, 9, 2, 7, 6, 11,
        0, 9, 2, 7, 8, 11, 10,
        0, 1, 2, 3, 4, 5, 6, 7,
        0, 1, 2, 3, 4, 5, 6, 9, 8,
        0, 1, 2, 3, 4, 5, 6, 9, 10, 11,
        0, 1, 2, 3, 4, 7, 6, 9, 8, 11, 10
    ];

    /// <summary>
    /// Gets the check weights, 8 for each side of each finder pattern: the character to its left and the character to its right, in the element order the
    /// symbol carries them. The check character to the left of the first finder carries no weight.
    /// </summary>
    private static ReadOnlySpan<byte> CheckWeights =>
    [
        0, 0, 0, 0, 0, 0, 0, 0,
        77, 96, 32, 81, 27, 9, 3, 1,
        20, 60, 180, 118, 143, 7, 21, 63,
        205, 209, 140, 117, 39, 13, 145, 189,
        193, 157, 49, 147, 19, 57, 171, 91,
        132, 44, 85, 169, 197, 136, 186, 62,
        185, 133, 188, 142, 4, 12, 36, 108,
        50, 87, 29, 80, 97, 173, 128, 113,
        150, 28, 84, 41, 123, 158, 52, 156,
        166, 196, 206, 139, 187, 203, 138, 46,
        76, 17, 51, 153, 37, 111, 122, 155,
        146, 119, 110, 107, 106, 176, 129, 43,
        16, 48, 144, 10, 30, 90, 59, 177,
        164, 125, 112, 178, 200, 137, 116, 109,
        70, 210, 208, 202, 184, 130, 179, 115,
        190, 204, 68, 93, 31, 151, 191, 134,
        148, 22, 66, 198, 172, 94, 71, 2,
        40, 154, 192, 64, 162, 54, 18, 6,
        120, 149, 25, 75, 14, 42, 126, 167,
        175, 199, 207, 69, 23, 78, 26, 79,
        103, 98, 83, 38, 114, 131, 182, 124,
        159, 53, 88, 170, 127, 183, 61, 161,
        55, 165, 73, 8, 24, 72, 5, 15,
        89, 100, 174, 58, 160, 194, 135, 45
    ];

    /// <summary>
    /// Checks that Application Identifier (01) is the first element string when the data holds it. Section 5.5.2.3.3 of the GS1 General Specifications: "when
    /// the data includes AI (01), it SHALL be the first element string encoded."
    /// </summary>
    /// <param name="text">The element strings that <see cref="Gs1Data.Prepare"/> accepted. Data ends at the next opening parenthesis, so each opening
    /// parenthesis starts an Application Identifier.</param>
    /// <exception cref="ArgumentException">Application Identifier (01) follows another element string.</exception>
    public static void ValidateGtinFirst(ReadOnlySpan<char> text)
    {
        if (text.IndexOf("(01)", StringComparison.Ordinal) > 0)
        {
            throw new ArgumentException("GS1 DataBar Expanded carries Application Identifier (01) as the first element string.", nameof(text));
        }
    }

    /// <summary>
    /// Validates the number of segments in a full row of a stacked symbol: an even number from <see cref="MinimumSegments"/> to
    /// <see cref="MaximumStackedSegments"/>.
    /// </summary>
    /// <param name="segments">The number of segments.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="segments"/> is outside the range or odd.</exception>
    public static void ValidateSegments(int segments)
    {
        Guard.MustBeBetweenOrEqualTo(segments, MinimumSegments, MaximumStackedSegments, nameof(segments));
        if ((segments & 1) == 1)
        {
            throw new ArgumentOutOfRangeException(nameof(segments), segments, "The number of segments in a row is even.");
        }
    }

    /// <summary>
    /// Builds GS1 DataBar Expanded, a single row.
    /// </summary>
    /// <param name="text">The prepared element strings, with a separator after every variable length element string but the last.</param>
    /// <param name="caption">The human readable interpretation.</param>
    /// <param name="linked">Whether a 2D component of a GS1 Composite symbol is linked to the symbol: section 7.3 of ISO/IEC 24723 sets the linkage bit.</param>
    /// <param name="options">The options that control the bar height and the text.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The data carries a character the symbology cannot encode, or more data than 21 data characters hold.</exception>
    public static void BuildLinear(ReadOnlySpan<char> text, ReadOnlySpan<char> caption, bool linked, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray256<int> characterStorage = default;
        Span<int> characters = ((Span<int>)characterStorage)[..(MaximumSymbolCharacters * CharacterElements)];
        int symbolCharacters = EncodeCharacters(text, MaximumSymbolCharacters, linked, characters);
        ReadOnlySpan<byte> finders = Finders(symbolCharacters);

        // A zero width bar, the guard space and bar, the characters with a finder after every second one, and the guard space and bar. The symbol ends on a bar
        // or a space, so a zero width bar closes the runs when it ends on a space. The zero width bars are the zeros the symbol storage starts with.
        int elements = 1 + GuardElements + (symbolCharacters * CharacterElements) + (finders.Length * FinderElements) + GuardElements;
        double xDimension = options.ResolveXDimension(DataBarEncoder.MinimumXDimension, NominalXDimension, DataBarEncoder.MaximumXDimension);
        Span<int> runs = symbol.BeginLinear(elements + ((elements & 1) == 0 ? 1 : 0), xDimension, out Span<float> heights, out _);
        runs[1] = 1;
        runs[2] = 1;
        int position = 3;
        for (int c = 0; c < symbolCharacters; c++)
        {
            characters.Slice(c * CharacterElements, CharacterElements).CopyTo(runs[position..]);
            position += CharacterElements;
            if ((c & 1) == 0)
            {
                foreach (byte width in FinderWidths.Slice(finders[c / 2] * FinderElements, FinderElements))
                {
                    runs[position++] = width;
                }
            }
        }

        runs[position] = 1;
        runs[position + 1] = 1;

        // Section 5.5.2.3.1 gives a minimum height and no maximum.
        float barHeight = options.ResolveBarHeight(xDimension, RowHeight, RowHeight, float.PositiveInfinity);
        heights.Fill(barHeight);

        if (options.Font is not null)
        {
            // The two guard patterns and the fixed module counts of the characters and the finders give the width.
            int width = (2 * GuardElements) + (symbolCharacters * CharacterModules) + (finders.Length * FinderModules);
            symbol.ReserveText(1, caption.Length);
            symbol.AddText(caption, 0F, width, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);

            // Section 4.14 rule 2.a.iii of the GS1 General Specifications: "The font size SHOULD be at least 2 mm (0.08 in) in height".
            symbol.SetTextSize(Gs1Data.MinimumTextSize, 0F);
        }

        symbol.EndLinear(0, 0);
    }

    /// <summary>
    /// Builds GS1 DataBar Expanded Stacked: rows of the given number of segments, a segment being a symbol character, with a separator of three one module rows
    /// between two rows. A row of an even number of segment pairs reads right to left on every second row, and the last row can be partial.
    /// </summary>
    /// <param name="text">The prepared element strings, with a separator after every variable length element string but the last.</param>
    /// <param name="segments">The number of segments in a full row, even and from 2 to 20.</param>
    /// <param name="linked">Whether a 2D component of a GS1 Composite symbol is linked to the symbol: section 7.3 of ISO/IEC 24723 sets the linkage bit.</param>
    /// <param name="captionPrefix">The characters of the human readable interpretation before its body.</param>
    /// <param name="captionBody">The body of the human readable interpretation.</param>
    /// <param name="captionSuffix">The characters of the human readable interpretation after its body.</param>
    /// <param name="side">The side of the symbol the human readable interpretation prints on.</param>
    /// <param name="minimumXDimension">The smallest X dimension the application standard permits, in millimetres.</param>
    /// <param name="maximumXDimension">The largest X dimension the application standard permits, in millimetres.</param>
    /// <param name="maximumRowHeight">The largest row height the application standard permits, in modules, or <see cref="float.PositiveInfinity"/> where it
    /// gives none. The smallest row height is <see cref="RowHeight"/>.</param>
    /// <param name="options">The options that control the bar height and the text.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The data carries a character the symbology cannot encode, or more data than 21 data characters hold.</exception>
    public static void BuildStacked(
        ReadOnlySpan<char> text,
        int segments,
        bool linked,
        ReadOnlySpan<char> captionPrefix,
        ReadOnlySpan<char> captionBody,
        ReadOnlySpan<char> captionSuffix,
        BarcodeTextSide side,
        double minimumXDimension,
        double maximumXDimension,
        float maximumRowHeight,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        InlineArray256<int> characterStorage = default;
        Span<int> characters = ((Span<int>)characterStorage)[..(MaximumSymbolCharacters * CharacterElements)];
        int symbolCharacters = EncodeCharacters(text, segments, linked, characters);
        double xDimension = options.ResolveXDimension(minimumXDimension, NominalXDimension, maximumXDimension);
        float rowHeight = options.ResolveBarHeight(xDimension, RowHeight, RowHeight, maximumRowHeight);

        // The buffer holds a row, the separator row that is its inverse, and the middle separator row, each one module wider than the widest row, since a
        // partial last row can move one module right. It is rented once, outside the row loops.
        int stride = MaximumRowWidth + 1;
        PooledBuffer<byte> buffer = new(stride * 3);
        try
        {
            StackedRowComposer rows = new(characters, symbolCharacters, segments, buffer.Span[..stride], buffer.Span.Slice(stride, stride));
            Span<byte> middle = buffer.Span.Slice(stride * 2, stride);
            int rowCount = rows.RowCount;

            // The run count of a row is known only once its modules are composed, so a first pass composes every row to count the runs, and the symbol reserves
            // its storage once. Between two rows stand the separator of the upper row, the middle separator and the separator of the lower row.
            int runCount = 0;
            int barCount = 0;
            for (int r = 0; r < rowCount; r++)
            {
                rows.Compose(r);
                int rowRuns = DataBarRows.RunCount(rows.Row);
                int separatorRuns = DataBarRows.RunCount(rows.Separator);
                int separatorUses = (r > 0 ? 1 : 0) + (r < rowCount - 1 ? 1 : 0);
                runCount += rowRuns + (separatorUses * separatorRuns);
                barCount += ((rowRuns + 1) >> 1) + (separatorUses * ((separatorRuns + 1) >> 1));
            }

            int width = rows.Width;
            ComposeMiddle(middle[..width]);
            int middleRuns = DataBarRows.RunCount(middle[..width]);
            runCount += (rowCount - 1) * middleRuns;
            barCount += (rowCount - 1) * ((middleRuns + 1) >> 1);

            symbol.BeginStacked((rowCount * 4) - 3, runCount, barCount, xDimension);
            for (int r = 0; r < rowCount; r++)
            {
                rows.Compose(r);
                if (r > 0)
                {
                    DataBarRows.AddRow(rows.Separator, SeparatorHeight, ref symbol);
                }

                DataBarRows.AddRow(rows.Row, rowHeight, ref symbol);
                if (r < rowCount - 1)
                {
                    DataBarRows.AddRow(rows.Separator, SeparatorHeight, ref symbol);
                    DataBarRows.AddRow(middle[..width], SeparatorHeight, ref symbol);
                }
            }

            if (options.Font is not null)
            {
                float height = (rowCount * rowHeight) + ((rowCount - 1) * 3F * SeparatorHeight);
                float textEdge = side == BarcodeTextSide.AboveBars ? -BarcodeTextPlacement.Clearance : height + BarcodeTextPlacement.Clearance;
                symbol.ReserveText(1, captionPrefix.Length + captionBody.Length + captionSuffix.Length);
                symbol.AddText(captionPrefix, captionBody, captionSuffix, 0F, width, side, textEdge);

                // Section 4.14 rule 2.a.iii of the GS1 General Specifications: "The font size SHOULD be at least 2 mm (0.08 in) in height".
                symbol.SetTextSize(Gs1Data.MinimumTextSize, 0F);
            }

            symbol.EndStacked(0F, 0F, 0F);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Encodes the data into the element widths of the symbol characters, 8 each, in the order the symbol carries them: the check character, then the data
    /// characters. A data character that stands to the right of a finder pattern reads in reverse.
    /// </summary>
    /// <param name="text">The prepared element strings.</param>
    /// <param name="segments">The number of segments in a row, which the symbol length avoids leaving one alone on the last row.</param>
    /// <param name="linked">Whether a 2D component of a GS1 Composite symbol is linked to the symbol.</param>
    /// <param name="characters">The buffer of 8 widths per symbol character.</param>
    /// <returns>The number of symbol characters.</returns>
    private static int EncodeCharacters(ReadOnlySpan<char> text, int segments, bool linked, Span<int> characters)
    {
        InlineArray24<int> valueStorage = default;
        Span<int> values = ((Span<int>)valueStorage)[..MaximumDataSymbolCharacters];
        int dataCharacters = EncodeBits(text, segments, linked, values) / BitsPerSymbolCharacter;
        int symbolCharacters = dataCharacters + 1;
        ReadOnlySpan<byte> finders = Finders(symbolCharacters);

        // The check value is the weighted sum of the data character widths modulo 211, and the check character adds the symbol length to it.
        InlineArray8<int> setStorage = default;
        Span<int> sets = setStorage;
        int check = 0;
        for (int d = 0; d < dataCharacters; d++)
        {
            // A data character to the right of a finder, the first of each pair, reads in reverse.
            Span<int> widths = characters.Slice((d + 1) * CharacterElements, CharacterElements);
            CharacterWidths(values[d], sets, widths, (d & 1) == 0);

            // Data character d is the right character of finder (d + 1) / 2, or the left one when d is odd. Each finder has the weights of its left character
            // and then the weights of its right character.
            int finder = finders[(d + 1) / 2];
            int side = (d & 1) == 0 ? CharacterElements : 0;
            ReadOnlySpan<byte> weights = CheckWeights.Slice((finder * 2 * CharacterElements) + side, CharacterElements);
            for (int k = 0; k < CharacterElements; k++)
            {
                check += widths[k] * weights[k];
            }
        }

        // The value of the check character is 211 times the number of symbol characters over the minimum, plus the check value.
        CharacterWidths((CheckModulus * (symbolCharacters - MinimumSymbolCharacters)) + (check % CheckModulus), sets, characters[..CharacterElements], false);
        return symbolCharacters;
    }

    /// <summary>
    /// Writes the 8 element widths of a symbol character.
    /// </summary>
    /// <param name="value">The character value.</param>
    /// <param name="sets">Scratch for the odd and the even set.</param>
    /// <param name="widths">The 8 widths of the character.</param>
    /// <param name="reverse">Whether the character reads in reverse.</param>
    private static void CharacterWidths(int value, Span<int> sets, Span<int> widths, bool reverse)
    {
        int group = GroupSum.Length - 1;
        while (value < GroupSum[group])
        {
            group--;
        }

        int withinGroup = value - GroupSum[group];
        Span<int> odd = sets[..4];
        Span<int> even = sets[4..];

        // Only the odd set excludes the combinations without a one module element.
        DataBarWidths.Calculate(odd, withinGroup / EvenCombinations[group], OddModules[group], OddWidest[group], true);
        DataBarWidths.Calculate(even, withinGroup % EvenCombinations[group], CharacterModules - OddModules[group], 9 - OddWidest[group], false);
        for (int k = 0; k < 4; k++)
        {
            if (reverse)
            {
                widths[7 - (k * 2)] = odd[k];
                widths[6 - (k * 2)] = even[k];
            }
            else
            {
                widths[k * 2] = odd[k];
                widths[(k * 2) + 1] = even[k];
            }
        }
    }

    /// <summary>
    /// Returns the finder pattern sequence of a symbol length.
    /// </summary>
    /// <param name="symbolCharacters">The number of symbol characters.</param>
    /// <returns>The finder patterns, an index into <see cref="FinderWidths"/> each.</returns>
    private static ReadOnlySpan<byte> Finders(int symbolCharacters)
    {
        int r = (symbolCharacters - 3) / 2;
        return FinderSequences.Slice(r * (r + 3) / 2, r + 2);
    }

    /// <summary>
    /// Writes the data bits: the linkage bit, the encodation method, the variable length field, the compressed data field and the general purpose field, padded
    /// to a whole number of data characters.
    /// </summary>
    /// <param name="text">The prepared element strings.</param>
    /// <param name="segments">The number of segments in a row.</param>
    /// <param name="linked">Whether a 2D component of a GS1 Composite symbol is linked to the symbol, which sets the linkage bit.</param>
    /// <param name="values">The buffer that receives the 12 bit value of each data character.</param>
    /// <returns>The number of bits, a multiple of 12.</returns>
    private static int EncodeBits(ReadOnlySpan<char> text, int segments, bool linked, Span<int> values)
    {
        int length = text.Length;
        EncodationMethod method = length >= 16 && text[0] == '0' && text[1] == '1' ? EncodationMethod.Gtin : EncodationMethod.GeneralPurpose;

        // The last three bits of a weight and date method: the date Application Identifier 11, 13, 15 or 17 as 0, 2, 4 or 6, plus 1 for a weight in pounds. A
        // symbol with no date takes the bits of AI (11).
        int weightAndDate = 0;

        // A variable measure GTIN, whose indicator digit is 9, followed by a weight or a price takes one of the shorter fixed length methods.
        if (method == EncodationMethod.Gtin && length >= 20 && text[2] == '9' && text[16] == '3')
        {
            if (length >= 26 && text[17] is '1' or '2' && text[18] == '0')
            {
                bool metric = text[17] == '1';
                int weight = Number(text, 20, 6);
                if (weight is >= 0 and <= 99999)
                {
                    int poundBit = metric ? 0 : 1;
                    if (length == 26)
                    {
                        // Section 5.5.2.3.3 of the GS1 General Specifications: up to 32.767 kilograms in AI (3103), 99.99 pounds in AI (3202) or 22.767 pounds
                        // in AI (3203). Other weights take the weight and date method with no date.
                        bool limited = metric
                            ? text[19] == '3' && weight <= 32767
                            : (text[19] == '2' && weight <= 9999) || (text[19] == '3' && weight <= 22767);
                        method = limited ? (metric ? EncodationMethod.MetricWeight : EncodationMethod.PoundWeight) : EncodationMethod.WeightAndDate;
                        weightAndDate = poundBit;
                    }
                    else if (length == 34 && text[26] == '1' && text[27] is '1' or '3' or '5' or '7' && Date(text, 28) >= 0)
                    {
                        // A production, packaging, best before or expiration date follows the weight.
                        method = EncodationMethod.WeightAndDate;
                        weightAndDate = text[27] - '1' + poundBit;
                    }
                }
            }
            else if (text[17] == '9' && text[19] is >= '0' and <= '3')
            {
                if (text[18] == '2')
                {
                    method = EncodationMethod.Price;
                }
                else if (length >= 23 && text[18] == '3' && Number(text, 20, 3) >= 0)
                {
                    method = EncodationMethod.PriceWithCurrency;
                }
            }
        }

        // Every method but the general purpose method encodes the GTIN without its check digit, so the check digit must be the one that the 13 digits before it
        // give.
        if (method != EncodationMethod.GeneralPurpose)
        {
            ReadOnlySpan<char> gtin = text.Slice(2, 14);
            BarcodeSymbology.ValidateDigits(gtin);

            int expected = Gs1Data.ComputeCheckDigit(gtin[..13]);
            if (gtin[13] - '0' != expected)
            {
                throw new ArgumentException($"Incorrect GTIN check digit: expected {expected}, got {gtin[13]}.", nameof(text));
            }
        }

        BitWriter bits = new(values);
        bits.Append(linked ? 1 : 0, 1);
        int variableLength = -1;
        int read;
        switch (method)
        {
            case EncodationMethod.Gtin:
                bits.Append(1, 1);
                variableLength = bits.Position;
                bits.Append(0, 2);
                bits.Append(text[2] - '0', 4);
                AppendGtin(ref bits, text);
                read = 16;
                break;

            case EncodationMethod.GeneralPurpose:
                bits.Append(0, 2);
                variableLength = bits.Position;
                bits.Append(0, 2);
                read = 0;
                break;

            case EncodationMethod.MetricWeight:
            case EncodationMethod.PoundWeight:
                bool poundWeight = method == EncodationMethod.PoundWeight;
                bits.Append(poundWeight ? 0b0101 : 0b0100, 4);
                AppendGtin(ref bits, text);
                bits.Append(Number(text, 20, 6) + (poundWeight && text[19] == '3' ? 10000 : 0), 15);
                read = 26;
                break;

            case EncodationMethod.Price:
            case EncodationMethod.PriceWithCurrency:
                bool currency = method == EncodationMethod.PriceWithCurrency;
                bits.Append(currency ? 0b01101 : 0b01100, 5);
                variableLength = bits.Position;
                bits.Append(0, 2);
                AppendGtin(ref bits, text);
                bits.Append(text[19] - '0', 2);
                if (currency)
                {
                    bits.Append(Number(text, 20, 3), 10);
                }

                read = currency ? 23 : 20;
                break;

            default:
                // The weight and date methods. The weight field carries the last digit of the identifier and the five digits after the weight's leading zero.
                // The date field carries 38400 when there is no date.
                bits.Append(0b0111000 + weightAndDate, 7);
                AppendGtin(ref bits, text);
                bits.Append(((text[19] - '0') * 100000) + Number(text, 21, 5), 20);
                bits.Append(length == 34 ? Date(text, 28) : 38400, 16);
                read = length;
                break;
        }

        GeneralFieldMode mode = EncodeGeneralField(text[read..], ref bits, out char lastDigit);

        int symbolCharacters = SymbolCharacters(bits.Position, segments);
        int remainder = (BitsPerSymbolCharacter * (symbolCharacters - 1)) - bits.Position;
        if (lastDigit != '\0')
        {
            // A single digit left at the end takes four bits as its value plus one when four to six bits remain to the end of the last character, and otherwise
            // seven bits as a pair with a separator.
            if (remainder is >= 4 and <= 6)
            {
                bits.Append(lastDigit - '0' + 1, 4);
            }
            else
            {
                bits.Append(((lastDigit - '0') * 11) + 10 + 8, 7);
            }

            symbolCharacters = SymbolCharacters(bits.Position, segments);
            remainder = (BitsPerSymbolCharacter * (symbolCharacters - 1)) - bits.Position;
        }

        if (symbolCharacters > MaximumSymbolCharacters)
        {
            throw new ArgumentException(
                $"GS1 DataBar Expanded carries at most {MaximumDataSymbolCharacters} data characters; the data needs more.",
                nameof(text));
        }

        // The pad starts with the numeric to alphanumeric latch, or as much of it as fits, when the field ends in numeric mode, and then repeats 00100 to the
        // end of the last data character.
        int end = bits.Position + remainder;
        if (mode == GeneralFieldMode.Numeric)
        {
            bits.Append(0, Math.Min(4, remainder));
        }

        for (int k = 0; bits.Position < end; k = (k + 1) % 5)
        {
            bits.Append(k == 2 ? 1 : 0, 1);
        }

        // The variable length field gives whether the number of symbol characters is odd and whether it exceeds 14.
        if (variableLength >= 0)
        {
            bits.Set(variableLength, symbolCharacters & 1);
            bits.Set(variableLength + 1, symbolCharacters > 14 ? 1 : 0);
        }

        return bits.Position;
    }

    /// <summary>
    /// Encodes the general purpose text. Numeric mode carries two digits or a digit and a separator per seven bits, alphanumeric mode a character per five or
    /// six bits, and ISO/IEC 646 mode a character per five, seven or eight bits. Each mode latches to another when the characters ahead favour it.
    /// </summary>
    /// <param name="text">The characters of the general purpose text, the end of the prepared text.</param>
    /// <param name="bits">The bit writer.</param>
    /// <param name="lastDigit">A single digit left at the end in numeric mode, or zero.</param>
    /// <returns>The mode at the end of the text.</returns>
    private static GeneralFieldMode EncodeGeneralField(ReadOnlySpan<char> text, ref BitWriter bits, out char lastDigit)
    {
        // The latch decisions look up to ten characters ahead, so every character is classified once. The text is a tail of the prepared text, which
        // Gs1Data.PreparedCapacity bounds to 122 characters for MaximumDataCharacters, so the classes fit inline.
        InlineArray128<GeneralFieldMode> classStorage = default;
        Span<GeneralFieldMode> classes = ((Span<GeneralFieldMode>)classStorage)[..text.Length];
        for (int k = 0; k < text.Length; k++)
        {
            classes[k] = ClassOf(text[k]);
            if (classes[k] == GeneralFieldMode.None)
            {
                throw new ArgumentException($"GS1 DataBar Expanded cannot carry the character U+{(int)text[k]:X4}.", nameof(text));
            }
        }

        lastDigit = '\0';
        GeneralFieldMode mode = GeneralFieldMode.Numeric;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            GeneralFieldMode type = classes[i];
            switch (mode)
            {
                case GeneralFieldMode.Numeric:
                    if (i < text.Length - 1)
                    {
                        if (type != GeneralFieldMode.Numeric || classes[i + 1] != GeneralFieldMode.Numeric)
                        {
                            bits.Append(0b0000, 4);
                            mode = GeneralFieldMode.Alphanumeric;
                        }
                        else
                        {
                            bits.Append((11 * NumericValue(c)) + NumericValue(text[i + 1]) + 8, 7);
                            i += 2;
                        }
                    }
                    else if (type != GeneralFieldMode.Numeric)
                    {
                        bits.Append(0b0000, 4);
                        mode = GeneralFieldMode.Alphanumeric;
                    }
                    else
                    {
                        lastDigit = c;
                        i++;
                    }

                    break;

                case GeneralFieldMode.Alphanumeric:
                    if (c == Gs1Data.Separator)
                    {
                        bits.Append(0b01111, 5);
                        mode = GeneralFieldMode.Numeric;
                        i++;
                    }
                    else if (type == GeneralFieldMode.Iso646)
                    {
                        bits.Append(0b00100, 5);
                        mode = GeneralFieldMode.Iso646;
                    }
                    else if (Next(classes, i, 6, GeneralFieldMode.Numeric) || EndsNumeric(classes, i))
                    {
                        // Six numeric characters ahead, or four or five that end the text.
                        bits.Append(0b000, 3);
                        mode = GeneralFieldMode.Numeric;
                    }
                    else if (char.IsAsciiDigit(c))
                    {
                        bits.Append(c - 43, 5);
                        i++;
                    }
                    else if (char.IsAsciiLetterUpper(c))
                    {
                        bits.Append(c - 33, 6);
                        i++;
                    }
                    else
                    {
                        bits.Append("*,-./".IndexOf(c) + 58, 6);
                        i++;
                    }

                    break;

                default:
                    if (c == Gs1Data.Separator)
                    {
                        bits.Append(0b01111, 5);
                        mode = GeneralFieldMode.Numeric;
                        i++;
                        break;
                    }

                    // A latch out of ISO/IEC 646 mode needs no ISO/IEC 646 only character in the next ten.
                    bool noIso646Ahead = NoneAhead(classes, i, 10, GeneralFieldMode.Iso646);
                    if (noIso646Ahead && Next(classes, i, 4, GeneralFieldMode.Numeric))
                    {
                        bits.Append(0b000, 3);
                        mode = GeneralFieldMode.Numeric;
                    }
                    else if (noIso646Ahead && Next(classes, i, 5, GeneralFieldMode.Alphanumeric, GeneralFieldMode.Numeric))
                    {
                        bits.Append(0b00100, 5);
                        mode = GeneralFieldMode.Alphanumeric;
                    }
                    else if (char.IsAsciiDigit(c))
                    {
                        bits.Append(c - 43, 5);
                        i++;
                    }
                    else if (char.IsAsciiLetterUpper(c))
                    {
                        bits.Append(c - 1, 7);
                        i++;
                    }
                    else if (char.IsAsciiLetterLower(c))
                    {
                        bits.Append(c - 7, 7);
                        i++;
                    }
                    else
                    {
                        bits.Append(Iso646Punctuation.IndexOf(c) + 232, 8);
                        i++;
                    }

                    break;
            }
        }

        return mode;
    }

    /// <summary>
    /// Returns the number of symbol characters that carry a number of data bits: the data characters rounded up, and the check character. A symbol carries at
    /// least four. When the count leaves a single symbol character on the last row, the symbol carries one more.
    /// </summary>
    /// <param name="bitCount">The number of data bits.</param>
    /// <param name="segments">The number of segments in a row.</param>
    /// <returns>The number of symbol characters.</returns>
    private static int SymbolCharacters(int bitCount, int segments)
    {
        int symbolCharacters = ((bitCount + 11) / 12) + 1;
        if (symbolCharacters % segments == 1)
        {
            symbolCharacters++;
        }

        return Math.Max(symbolCharacters, 4);
    }

    /// <summary>
    /// Appends the 12 digits of the GTIN after its indicator digit as four groups of three digits in ten bits each.
    /// </summary>
    /// <param name="bits">The bit writer.</param>
    /// <param name="text">The prepared element strings, which start with Application Identifier (01).</param>
    private static void AppendGtin(ref BitWriter bits, ReadOnlySpan<char> text)
    {
        for (int i = 3; i < 15; i += 3)
        {
            bits.Append(Number(text, i, 3), 10);
        }
    }

    /// <summary>
    /// Rewrites the 15 separator modules over a finder pattern. Under a dark module the separator is light. Under a light module it is dark after a dark row
    /// module, and otherwise the opposite of the separator module to its left.
    /// </summary>
    /// <param name="row">The row the separator touches.</param>
    /// <param name="separator">The separator row.</param>
    /// <param name="start">The first module of the finder pattern.</param>
    private static void AlternateOverFinder(ReadOnlySpan<byte> row, Span<byte> separator, int start)
    {
        for (int i = start; i < start + FinderModules; i++)
        {
            separator[i] = row[i] == 1
                ? (byte)0
                : row[i - 1] == 1 ? (byte)1 : (byte)(1 - separator[i - 1]);
        }
    }

    /// <summary>
    /// Composes the middle separator row: narrow dark and light modules alternating, inside four light modules at each end.
    /// </summary>
    /// <param name="middle">The buffer that receives the modules, one for each module of the symbol width.</param>
    private static void ComposeMiddle(Span<byte> middle)
    {
        for (int i = 0; i < middle.Length; i++)
        {
            middle[i] = (byte)(i & 1);
        }

        middle[..SeparatorLightModules].Clear();
        middle[^SeparatorLightModules..].Clear();
    }

    /// <summary>
    /// Returns the character class of a general purpose field character: numeric for a digit or the separator, alphanumeric for a capital letter or one of <c>*
    /// , - . /</c>, ISO/IEC 646 for a small letter, a space or the other punctuation, and none for a character the field cannot carry.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>The class.</returns>
    private static GeneralFieldMode ClassOf(char c)
    {
        if (c == Gs1Data.Separator || char.IsAsciiDigit(c))
        {
            return GeneralFieldMode.Numeric;
        }

        if (char.IsAsciiLetterUpper(c) || c is '*' or ',' or '-' or '.' or '/')
        {
            return GeneralFieldMode.Alphanumeric;
        }

        return char.IsAsciiLetterLower(c) || Iso646Punctuation.Contains(c) ? GeneralFieldMode.Iso646 : GeneralFieldMode.None;
    }

    /// <summary>
    /// Returns the numeric mode value of a digit, or 10 for the separator.
    /// </summary>
    /// <param name="c">The digit or the separator.</param>
    /// <returns>The value.</returns>
    private static int NumericValue(char c)
        => c == Gs1Data.Separator ? 10 : c - '0';

    /// <summary>
    /// Returns whether the next characters, from the given one on, are all of one class.
    /// </summary>
    /// <param name="classes">The class of every character of the field.</param>
    /// <param name="start">The first character.</param>
    /// <param name="count">The number of characters.</param>
    /// <param name="type">The class.</param>
    /// <returns>Whether that many characters remain and all are of the class.</returns>
    private static bool Next(ReadOnlySpan<GeneralFieldMode> classes, int start, int count, GeneralFieldMode type)
        => Next(classes, start, count, type, type);

    /// <summary>
    /// Returns whether the next characters, from the given one on, are all of one of two classes.
    /// </summary>
    /// <param name="classes">The class of every character of the field.</param>
    /// <param name="start">The first character.</param>
    /// <param name="count">The number of characters.</param>
    /// <param name="type">The class.</param>
    /// <param name="alternative">The second class.</param>
    /// <returns>Whether that many characters remain and each is of one of the classes.</returns>
    private static bool Next(ReadOnlySpan<GeneralFieldMode> classes, int start, int count, GeneralFieldMode type, GeneralFieldMode alternative)
    {
        if (start + count > classes.Length)
        {
            return false;
        }

        foreach (GeneralFieldMode c in classes.Slice(start, count))
        {
            if (c != type && c != alternative)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether four or five numeric characters end the field from the given one on.
    /// </summary>
    /// <param name="classes">The class of every character of the field.</param>
    /// <param name="start">The first character.</param>
    /// <returns>Whether the rest of the field is four or five numeric characters.</returns>
    private static bool EndsNumeric(ReadOnlySpan<GeneralFieldMode> classes, int start)
        => classes.Length - start is 4 or 5 && Next(classes, start, classes.Length - start, GeneralFieldMode.Numeric);

    /// <summary>
    /// Returns whether none of the next characters, or the characters to the end, are of a class.
    /// </summary>
    /// <param name="classes">The class of every character of the field.</param>
    /// <param name="start">The first character.</param>
    /// <param name="count">The number of characters.</param>
    /// <param name="type">The class.</param>
    /// <returns>Whether none of them is of the class.</returns>
    private static bool NoneAhead(ReadOnlySpan<GeneralFieldMode> classes, int start, int count, GeneralFieldMode type)
    {
        foreach (GeneralFieldMode c in classes[start..Math.Min(start + count, classes.Length)])
        {
            if (c == type)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads a number of digits as a value.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The first digit.</param>
    /// <param name="count">The number of digits.</param>
    /// <returns>The value, or -1 when a character is not a digit.</returns>
    private static int Number(ReadOnlySpan<char> text, int start, int count)
    {
        int value = 0;
        for (int i = start; i < start + count; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                return -1;
            }

            value = (value * 10) + (text[i] - '0');
        }

        return value;
    }

    /// <summary>
    /// Reads a date of six digits, YYMMDD, as the value the weight and date methods carry: the year times 384, the month less one times 32, and the day. A day
    /// of zero stands for the last day of the month.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The first digit.</param>
    /// <returns>The value, or -1 when the date is not valid.</returns>
    private static int Date(ReadOnlySpan<char> text, int start)
    {
        int year = Number(text, start, 2);
        int month = Number(text, start + 2, 2);
        int day = Number(text, start + 4, 2);
        if (year < 0 || month is < 1 or > 12 || day is < 0 or > 31)
        {
            return -1;
        }

        return (year * 384) + ((month - 1) * 32) + day;
    }

    /// <summary>
    /// Writes the data bits, most significant first, straight into the 12 bit values of the data characters. Bit 0 of the stream is bit 11 of the first value.
    /// </summary>
    private ref struct BitWriter
    {
        /// <summary>
        /// The values of the data characters.
        /// </summary>
        private readonly Span<int> values;

        /// <summary>
        /// Initializes a new instance of the <see cref="BitWriter"/> struct.
        /// </summary>
        /// <param name="values">The values of the data characters.</param>
        public BitWriter(Span<int> values) => this.values = values;

        /// <summary>
        /// Gets the number of bits appended, which counts the bits past the buffer too.
        /// </summary>
        public int Position { get; private set; }

        /// <summary>
        /// Appends a value as a number of bits, most significant first.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="count">The number of bits.</param>
        public void Append(int value, int count)
        {
            // Bits past the buffer cannot fit the largest symbol. The position still counts them, so the symbol character count after the general purpose field
            // rejects the data.
            int end = this.Position + count;
            if (end <= this.values.Length * BitsPerSymbolCharacter)
            {
                for (int bit = count - 1; bit >= 0; bit--)
                {
                    this.Set(this.Position++, (value >> bit) & 1);
                }
            }

            this.Position = end;
        }

        /// <summary>
        /// Sets one bit of the stream.
        /// </summary>
        /// <param name="index">The index of the bit in the stream.</param>
        /// <param name="bit">The bit, 0 or 1.</param>
        public readonly void Set(int index, int bit)
        {
            int shift = BitsPerSymbolCharacter - 1 - (index % BitsPerSymbolCharacter);
            ref int value = ref this.values[index / BitsPerSymbolCharacter];
            value = (value & ~(1 << shift)) | (bit << shift);
        }
    }

    /// <summary>
    /// Composes the rows of a stacked symbol one at a time, each with the separator row that is its inverse, into a row buffer and a separator buffer the
    /// caller owns. Both buffers hold the widest row and one module more, since a partial last row can move one module right.
    /// </summary>
    private ref struct StackedRowComposer
    {
        /// <summary>
        /// The element widths of the symbol characters, 8 each.
        /// </summary>
        private readonly ReadOnlySpan<int> characters;

        /// <summary>
        /// The finder pattern values, an index into <see cref="FinderWidths"/> each.
        /// </summary>
        private readonly ReadOnlySpan<byte> finders;

        /// <summary>
        /// The modules of the row, 1 for dark.
        /// </summary>
        private readonly Span<byte> row;

        /// <summary>
        /// The modules of the separator row.
        /// </summary>
        private readonly Span<byte> separator;

        /// <summary>
        /// The number of segments in a full row.
        /// </summary>
        private readonly int segments;

        /// <summary>
        /// The number of symbol characters.
        /// </summary>
        private readonly int symbolCharacters;

        /// <summary>
        /// The number of modules of the row written so far.
        /// </summary>
        private int length;

        /// <summary>
        /// The module value of the next element, 1 for dark.
        /// </summary>
        private byte colour;

        /// <summary>
        /// Initializes a new instance of the <see cref="StackedRowComposer"/> struct.
        /// </summary>
        /// <param name="characters">The element widths of the symbol characters, 8 each.</param>
        /// <param name="symbolCharacters">The number of symbol characters.</param>
        /// <param name="segments">The number of segments in a full row.</param>
        /// <param name="row">The buffer that receives the modules of a row.</param>
        /// <param name="separator">The buffer that receives the modules of its separator row.</param>
        public StackedRowComposer(ReadOnlySpan<int> characters, int symbolCharacters, int segments, Span<byte> row, Span<byte> separator)
        {
            this.characters = characters;
            this.finders = Finders(symbolCharacters);
            this.row = row;
            this.separator = separator;
            this.segments = segments;
            this.symbolCharacters = symbolCharacters;
            this.RowCount = (symbolCharacters + segments - 1) / segments;
        }

        /// <summary>
        /// Gets the number of rows.
        /// </summary>
        public int RowCount { get; }

        /// <summary>
        /// Gets the width of the symbol in modules, which the first row sets because it is a full row.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Gets the modules of the last composed row, 1 for dark.
        /// </summary>
        public readonly ReadOnlySpan<byte> Row => this.row[..this.Width];

        /// <summary>
        /// Gets the modules of the separator row of the last composed row.
        /// </summary>
        public readonly ReadOnlySpan<byte> Separator => this.separator[..this.Width];

        /// <summary>
        /// Composes the modules of one row and of its separator row. Row 0 must be composed first, since it sets the width.
        /// </summary>
        /// <param name="r">The row index from the top.</param>
        public void Compose(int r)
        {
            // A row of segments that is not a whole number of segment pairs on both sides starts every second row with its guard bar, so the colours of the row
            // alternate from row to row.
            this.length = 0;
            this.colour = (byte)((this.segments & 3) != 0 && (r & 1) == 1 ? 1 : 0);
            this.AppendElement(1);
            this.AppendElement(1);
            int first = r * this.segments;
            int last = Math.Min(first + this.segments, this.symbolCharacters);
            int finderCount = 0;
            for (int c = first; c < last; c++)
            {
                foreach (int width in this.characters.Slice(c * CharacterElements, CharacterElements))
                {
                    this.AppendElement(width);
                }

                if ((c & 1) == 0)
                {
                    foreach (byte width in FinderWidths.Slice(this.finders[c / 2] * FinderElements, FinderElements))
                    {
                        this.AppendElement(width);
                    }

                    finderCount++;
                }
            }

            this.AppendElement(1);
            this.AppendElement(1);

            // The separator is the inverse of the row, inside four light modules at each end. Over the 15 modules of a finder pattern the light modules break
            // into narrow elements instead, so the separator does not read as part of the finder. The first finder starts after the guard pattern and the
            // first character. Each next finder starts one finder and two characters later.
            int rowLength = this.length;
            Span<byte> rowModules = this.row[..rowLength];
            Span<byte> separatorModules = this.separator[..rowLength];
            for (int i = 0; i < rowLength; i++)
            {
                separatorModules[i] = (byte)(1 - rowModules[i]);
            }

            for (int start = FirstFinderStart; start + FinderModules <= rowLength; start += FinderPitch)
            {
                AlternateOverFinder(rowModules, separatorModules, start);
            }

            separatorModules[..SeparatorLightModules].Clear();
            separatorModules[^SeparatorLightModules..].Clear();

            // With a whole number of segment pairs on both sides every second row reads right to left. A partial last row with an odd number of finders keeps
            // its direction and moves one module right.
            if ((this.segments & 3) == 0 && (r & 1) == 1)
            {
                if (r == this.RowCount - 1 && last - first < this.segments && (finderCount & 1) == 1)
                {
                    rowModules.CopyTo(this.row[1..]);
                    separatorModules.CopyTo(this.separator[1..]);
                    this.row[0] = 0;
                    this.separator[0] = 0;
                    rowLength++;
                }
                else
                {
                    rowModules.Reverse();
                    separatorModules.Reverse();
                }
            }

            // The first row is a full row, and a shorter last row fills to its width with light modules.
            if (r == 0)
            {
                this.Width = rowLength;
            }
            else
            {
                this.row[rowLength..this.Width].Clear();
                this.separator[rowLength..this.Width].Clear();
            }
        }

        /// <summary>
        /// Appends an element to the row and switches the colour for the next one.
        /// </summary>
        /// <param name="width">The width of the element in modules.</param>
        private void AppendElement(int width)
        {
            this.row.Slice(this.length, width).Fill(this.colour);
            this.length += width;
            this.colour = (byte)(1 - this.colour);
        }
    }
}
