// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for GS1 DataBar Expanded and GS1 DataBar Expanded Stacked. A symbol carries GS1 element strings
/// as a bit stream cut into 12 bit symbol characters, preceded by a check character that encodes a modulo
/// 211 check value and the symbol length. A finder pattern follows every second symbol character.
/// <para>
/// The bit stream starts with a linkage bit and an encodation method. Common sequences of element strings
/// that start with Application Identifier (01) compress into a fixed length field, and whatever remains is
/// encoded in the general purpose field in numeric, alphanumeric or ISO/IEC 646 mode. A symbol character is
/// 8 elements over 17 modules, an odd set of 4 and an even set of 4 interleaved, and its group gives the
/// module count and the widest element of each set.
/// </para>
/// </summary>
internal static class DataBarExpandedEncoder
{
    /// <summary>
    /// The nominal X dimension in millimetres, the target X dimension of Table 5-44 of the GS1 General
    /// Specifications.
    /// </summary>
    public const double NominalXDimension = 0.33;

    /// <summary>
    /// The height of a row in modules. Section 5.5.2.3.1 of the GS1 General Specifications gives "34X high",
    /// and Table 5-44 gives 11.23 millimetres, 34X, at the target X dimension.
    /// </summary>
    public const float RowHeight = 34F;

    /// <summary>
    /// The fewest segments in a row of a stacked symbol. Section 5.5.2.3.2: "It can be printed in widths of
    /// 2 to 20 segments".
    /// </summary>
    public const int MinimumSegments = 2;

    /// <summary>
    /// The most segments in a row of a stacked symbol.
    /// </summary>
    public const int MaximumStackedSegments = 20;

    /// <summary>
    /// The most data characters a symbol carries. Section 2.6.10 of the GS1 General Specifications: GS1
    /// DataBar Expanded Versions "SHALL NOT be used when symbols require more than 74 numeric or 41
    /// alphabetic characters of data".
    /// </summary>
    public const int MaximumDataCharacters = 74;

    /// <summary>
    /// The height of each of the three separator rows between two rows of a stacked symbol, in modules.
    /// Section 5.5.2.3.2: "Its structure includes a 3X high separator pattern between rows."
    /// </summary>
    private const float SeparatorHeight = 1F;

    /// <summary>
    /// The most symbol characters a symbol carries, the check character and 21 data characters. Section
    /// 5.5.2.3.1: "from 4 to 22 symbol characters".
    /// </summary>
    private const int MaximumSymbolCharacters = 22;

    /// <summary>
    /// The most bits of the data characters, 21 of 12 bits.
    /// </summary>
    private const int MaximumBits = 252;

    /// <summary>
    /// The width in modules of the widest row: two guard pairs, 20 symbol characters of 17 modules and 10
    /// finder patterns of 15.
    /// </summary>
    private const int MaximumRowWidth = 4 + (MaximumStackedSegments * 17) + (MaximumStackedSegments / 2 * 15);

    /// <summary>
    /// The modulus of the check value.
    /// </summary>
    private const int CheckModulus = 211;

    /// <summary>
    /// The general purpose field mode for digits and the separator, two per seven bits.
    /// </summary>
    private const int NumericMode = 1;

    /// <summary>
    /// The general purpose field mode for digits, capital letters and five punctuation characters.
    /// </summary>
    private const int AlphanumericMode = 2;

    /// <summary>
    /// The general purpose field mode for the ISO/IEC 646 characters GS1 element strings use.
    /// </summary>
    private const int Iso646Mode = 3;

    /// <summary>
    /// Gets the punctuation characters ISO/IEC 646 mode encodes, in the order of their values from 232.
    /// </summary>
    private static ReadOnlySpan<char> Iso646Punctuation => "!\"%&'()*+,-./:;<=>?_ ";

    /// <summary>
    /// Gets the first symbol character value of each group.
    /// </summary>
    private static ReadOnlySpan<int> GroupSum => [0, 348, 1388, 2948, 3988];

    /// <summary>
    /// Gets the number of even set combinations of each group. A value within a group splits into the odd
    /// and the even set value by this number.
    /// </summary>
    private static ReadOnlySpan<byte> EvenCombinations => [4, 20, 52, 104, 204];

    /// <summary>
    /// Gets the module count of the odd set of each group. The even set takes the rest of the 17 modules.
    /// </summary>
    private static ReadOnlySpan<byte> OddModules => [12, 10, 8, 6, 4];

    /// <summary>
    /// Gets the widest element of the odd set of each group. The widest element of the even set is nine
    /// less this.
    /// </summary>
    private static ReadOnlySpan<byte> OddWidest => [7, 5, 4, 3, 1];

    /// <summary>
    /// Gets the element widths of the twelve finder patterns, five each: A1, A2, B1, B2, C1, C2, D1, D2,
    /// E1, E2, F1 and F2. A version 2 finder is its version 1 finder reversed.
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
    /// Gets the finder pattern sequences, one per symbol length. The sequence of a symbol with 2r + 3 or 2r
    /// + 4 symbol characters has r + 2 finders and starts at r (r + 3) / 2.
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
    /// Gets the check weights, 8 for each side of each finder pattern: the character to its left and the
    /// character to its right, in the element order the symbol carries them. The check character to the
    /// left of the first finder carries no weight.
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
    /// Builds GS1 DataBar Expanded, a single row.
    /// </summary>
    /// <param name="text">The prepared element strings, with a separator after every variable length element string but the last.</param>
    /// <param name="caption">The human readable interpretation.</param>
    /// <param name="options">The options that control the bar height and the text.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The data carries a character the symbology cannot encode, or more data than 21 data characters hold.</exception>
    public static void BuildLinear(ReadOnlySpan<char> text, ReadOnlySpan<char> caption, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray256<int> characterStorage = default;
        Span<int> characters = ((Span<int>)characterStorage)[..(MaximumSymbolCharacters * 8)];
        int symbolCharacters = EncodeCharacters(text, MaximumSymbolCharacters, characters);
        ReadOnlySpan<byte> finders = Finders(symbolCharacters);

        // A zero width bar, the guard space and bar, the characters with a finder after every second one, and
        // the guard space and bar. The symbol ends on a bar or a space, so a zero width bar closes the runs
        // when it ends on a space. The zero width bars are the zeros the symbol storage starts with.
        int elements = 3 + (symbolCharacters * 8) + (finders.Length * 5) + 2;
        double xDimension = options.XDimension ?? NominalXDimension;
        Span<int> runs = symbol.BeginLinear(elements + ((elements & 1) == 0 ? 1 : 0), xDimension, out Span<float> heights, out _);
        runs[1] = 1;
        runs[2] = 1;
        int position = 3;
        for (int c = 0; c < symbolCharacters; c++)
        {
            characters.Slice(c * 8, 8).CopyTo(runs[position..]);
            position += 8;
            if ((c & 1) == 0)
            {
                ReadOnlySpan<byte> finder = FinderWidths.Slice(finders[c / 2] * 5, 5);
                for (int k = 0; k < 5; k++)
                {
                    runs[position++] = finder[k];
                }
            }
        }

        runs[position] = 1;
        runs[position + 1] = 1;

        float barHeight = options.ResolveBarHeight(xDimension, RowHeight);
        heights.Fill(barHeight);

        int width = 0;
        foreach (int run in runs)
        {
            width += run;
        }

        if (options.Font is not null)
        {
            symbol.ReserveText(1, caption.Length);
            symbol.AddText(caption, 0F, width, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(0, 0);
    }

    /// <summary>
    /// Builds GS1 DataBar Expanded Stacked: rows of the given number of segments, a segment being a symbol
    /// character, with a separator of three one module rows between two rows. A row of an even number of
    /// segment pairs reads right to left on every second row, and the last row may be partial.
    /// </summary>
    /// <param name="text">The prepared element strings, with a separator after every variable length element string but the last.</param>
    /// <param name="segments">The number of segments in a full row, even and from 2 to 20.</param>
    /// <param name="captionPrefix">The characters of the human readable interpretation before its body.</param>
    /// <param name="captionBody">The body of the human readable interpretation.</param>
    /// <param name="captionSuffix">The characters of the human readable interpretation after its body.</param>
    /// <param name="side">The side of the symbol the human readable interpretation prints on.</param>
    /// <param name="options">The options that control the bar height and the text.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The data carries a character the symbology cannot encode, or more data than 21 data characters hold.</exception>
    public static void BuildStacked(
        ReadOnlySpan<char> text,
        int segments,
        ReadOnlySpan<char> captionPrefix,
        ReadOnlySpan<char> captionBody,
        ReadOnlySpan<char> captionSuffix,
        BarcodeTextSide side,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        InlineArray256<int> characterStorage = default;
        Span<int> characters = ((Span<int>)characterStorage)[..(MaximumSymbolCharacters * 8)];
        int symbolCharacters = EncodeCharacters(text, segments, characters);
        ReadOnlySpan<byte> finders = Finders(symbolCharacters);
        int rowCount = (symbolCharacters + segments - 1) / segments;
        double xDimension = options.XDimension ?? NominalXDimension;
        float rowHeight = options.ResolveBarHeight(xDimension, RowHeight);

        // The buffer holds a row, the separator row that is its inverse, and the middle separator row, each
        // one module wider than the widest row, since a partial last row can move one module right. It is
        // rented once, outside the row loops.
        int stride = MaximumRowWidth + 1;
        PooledBuffer<byte> buffer = new(stride * 3);
        try
        {
            Span<byte> row = buffer.Span[..stride];
            Span<byte> separator = buffer.Span.Slice(stride, stride);
            Span<byte> middle = buffer.Span.Slice(stride * 2, stride);

            // The run count of a row is known only once its modules are composed, so a first pass composes
            // every row to count the runs, and the symbol reserves its storage once. Between two rows stand
            // the separator of the upper row, the middle separator and the separator of the lower row.
            int width = 0;
            int runCount = 0;
            int barCount = 0;
            for (int r = 0; r < rowCount; r++)
            {
                ComposeRow(r, rowCount, segments, symbolCharacters, characters, finders, row, separator, ref width);
                int rowRuns = DataBarRows.RunCount(row[..width]);
                int separatorRuns = DataBarRows.RunCount(separator[..width]);
                int separatorUses = (r > 0 ? 1 : 0) + (r < rowCount - 1 ? 1 : 0);
                runCount += rowRuns + (separatorUses * separatorRuns);
                barCount += ((rowRuns + 1) >> 1) + (separatorUses * ((separatorRuns + 1) >> 1));
            }

            ComposeMiddle(middle[..width]);
            int middleRuns = DataBarRows.RunCount(middle[..width]);
            runCount += (rowCount - 1) * middleRuns;
            barCount += (rowCount - 1) * ((middleRuns + 1) >> 1);

            symbol.BeginStacked((rowCount * 4) - 3, runCount, barCount, xDimension);
            for (int r = 0; r < rowCount; r++)
            {
                ComposeRow(r, rowCount, segments, symbolCharacters, characters, finders, row, separator, ref width);
                if (r > 0)
                {
                    DataBarRows.AddRow(separator[..width], SeparatorHeight, ref symbol);
                }

                DataBarRows.AddRow(row[..width], rowHeight, ref symbol);
                if (r < rowCount - 1)
                {
                    DataBarRows.AddRow(separator[..width], SeparatorHeight, ref symbol);
                    DataBarRows.AddRow(middle[..width], SeparatorHeight, ref symbol);
                }
            }

            if (options.Font is not null)
            {
                float height = (rowCount * rowHeight) + ((rowCount - 1) * 3F * SeparatorHeight);
                float textEdge = side == BarcodeTextSide.AboveBars ? -BarcodeTextPlacement.Clearance : height + BarcodeTextPlacement.Clearance;
                symbol.ReserveText(1, captionPrefix.Length + captionBody.Length + captionSuffix.Length);
                symbol.AddText(captionPrefix, captionBody, captionSuffix, 0F, width, side, textEdge);
            }

            symbol.EndStacked(0F, 0F, 0F);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Composes the modules of one row of a stacked symbol and of the separator row that is its inverse.
    /// </summary>
    /// <param name="r">The row index from the top.</param>
    /// <param name="rowCount">The number of rows.</param>
    /// <param name="segments">The number of segments in a full row.</param>
    /// <param name="symbolCharacters">The number of symbol characters.</param>
    /// <param name="characters">The element widths of the symbol characters, 8 each.</param>
    /// <param name="finders">The finder pattern values.</param>
    /// <param name="row">The buffer that receives the modules of the row, 1 for dark.</param>
    /// <param name="separator">The buffer that receives the modules of the separator row.</param>
    /// <param name="width">The width of the symbol in modules, set by the first row, which is a full row.</param>
    private static void ComposeRow(
        int r,
        int rowCount,
        int segments,
        int symbolCharacters,
        ReadOnlySpan<int> characters,
        ReadOnlySpan<byte> finders,
        Span<byte> row,
        Span<byte> separator,
        ref int width)
    {
        // A row of segments that is not a whole number of segment pairs on both sides starts every second
        // row with its guard bar, so the colours of the row alternate from row to row.
        int length = 0;
        byte colour = (byte)((segments & 3) != 0 && (r & 1) == 1 ? 1 : 0);
        AppendElement(row, ref length, ref colour, 1);
        AppendElement(row, ref length, ref colour, 1);
        int first = r * segments;
        int last = Math.Min(first + segments, symbolCharacters);
        int finderCount = 0;
        for (int c = first; c < last; c++)
        {
            for (int k = 0; k < 8; k++)
            {
                AppendElement(row, ref length, ref colour, characters[(c * 8) + k]);
            }

            if ((c & 1) == 0)
            {
                ReadOnlySpan<byte> finder = FinderWidths.Slice(finders[c / 2] * 5, 5);
                for (int k = 0; k < 5; k++)
                {
                    AppendElement(row, ref length, ref colour, finder[k]);
                }

                finderCount++;
            }
        }

        AppendElement(row, ref length, ref colour, 1);
        AppendElement(row, ref length, ref colour, 1);

        // The separator is the inverse of the row, inside four light modules at each end. Over the 15
        // modules of a finder pattern the light modules break into narrow elements instead, so the
        // separator does not read as part of the finder. A finder follows the 2 guard modules and the 17
        // of the first character, and every 49 modules after that pairs of finders stand 98 apart.
        Span<byte> rowModules = row[..length];
        Span<byte> separatorModules = separator[..length];
        for (int i = 0; i < length; i++)
        {
            separatorModules[i] = (byte)(1 - rowModules[i]);
        }

        for (int start = 19; start <= length - 13; start += 98)
        {
            AlternateOverFinder(rowModules, separatorModules, start);
        }

        for (int start = 68; start <= length - 13; start += 98)
        {
            AlternateOverFinder(rowModules, separatorModules, start);
        }

        separatorModules[..4].Clear();
        separatorModules[^4..].Clear();

        // With a whole number of segment pairs on both sides every second row reads right to left. A
        // partial last row with an odd number of finders keeps its direction and moves one module right.
        if ((segments & 3) == 0 && (r & 1) == 1)
        {
            if (r == rowCount - 1 && last - first < segments && (finderCount & 1) == 1)
            {
                row[..length].CopyTo(row[1..]);
                separator[..length].CopyTo(separator[1..]);
                row[0] = 0;
                separator[0] = 0;
                length++;
            }
            else
            {
                row[..length].Reverse();
                separator[..length].Reverse();
            }
        }

        // The first row is a full row, and a shorter last row fills to its width with light modules.
        if (r == 0)
        {
            width = length;
        }
        else
        {
            row[length..width].Clear();
            separator[length..width].Clear();
        }
    }

    /// <summary>
    /// Encodes the data into the element widths of the symbol characters, 8 each, in the order the symbol
    /// carries them: the check character, then the data characters. A data character that stands to the
    /// right of a finder pattern reads in reverse.
    /// </summary>
    /// <param name="text">The prepared element strings.</param>
    /// <param name="segments">The number of segments in a row, which the symbol length avoids leaving one alone on the last row.</param>
    /// <param name="characters">The buffer of 8 widths per symbol character.</param>
    /// <returns>The number of symbol characters.</returns>
    private static int EncodeCharacters(ReadOnlySpan<char> text, int segments, Span<int> characters)
    {
        InlineArray256<byte> bitStorage = default;
        Span<byte> bits = ((Span<byte>)bitStorage)[..MaximumBits];
        int dataCharacters = EncodeBits(text, segments, bits) / 12;
        int symbolCharacters = dataCharacters + 1;
        ReadOnlySpan<byte> finders = Finders(symbolCharacters);

        // The check value is the weighted sum of the data character widths modulo 211, and the check
        // character adds the symbol length to it.
        InlineArray8<int> setStorage = default;
        Span<int> sets = setStorage;
        int check = 0;
        for (int d = 0; d < dataCharacters; d++)
        {
            int value = 0;
            for (int b = 0; b < 12; b++)
            {
                value = (value << 1) | bits[(d * 12) + b];
            }

            // A data character to the right of a finder, the first of each pair, reads in reverse.
            Span<int> widths = characters.Slice((d + 1) * 8, 8);
            CharacterWidths(value, sets, widths, (d & 1) == 0);

            // Data character d is the right character of finder (d + 1) / 2, or the left one when d is odd.
            int finder = finders[(d + 1) / 2];
            ReadOnlySpan<byte> weights = CheckWeights.Slice((finder * 16) + ((d & 1) == 0 ? 8 : 0), 8);
            for (int k = 0; k < 8; k++)
            {
                check += widths[k] * weights[k];
            }
        }

        CharacterWidths((CheckModulus * (dataCharacters - 3)) + (check % CheckModulus), sets, characters[..8], false);
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

        // Combinations without a one module element are excluded from the odd set only.
        DataBarWidths.Calculate(odd, withinGroup / EvenCombinations[group], OddModules[group], 4, OddWidest[group], true);
        DataBarWidths.Calculate(even, withinGroup % EvenCombinations[group], 17 - OddModules[group], 4, 9 - OddWidest[group], false);
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
    /// Writes the data bits: the linkage bit, the encodation method, the variable length field, the
    /// compressed data field and the general purpose field, padded to a whole number of data characters.
    /// </summary>
    /// <param name="text">The prepared element strings.</param>
    /// <param name="segments">The number of segments in a row.</param>
    /// <param name="bits">The buffer of bits, one per byte.</param>
    /// <returns>The number of bits, a multiple of 12.</returns>
    private static int EncodeBits(ReadOnlySpan<char> text, int segments, Span<byte> bits)
    {
        int length = text.Length;
        int method = length >= 16 && text[0] == '0' && text[1] == '1' ? 1 : 2;

        // A variable measure GTIN, whose indicator digit is 9, followed by a weight or a price takes one of
        // the shorter fixed length methods.
        if (method == 1 && length >= 20 && text[2] == '9' && text[16] == '3')
        {
            if (length >= 26 && text[17] is '1' or '2' && text[18] == '0')
            {
                bool metric = text[17] == '1';
                int weight = Number(text, 20, 6);
                if (weight is >= 0 and <= 99999)
                {
                    if (length == 26)
                    {
                        // Section 5.5.2.3.3 of the GS1 General Specifications: up to 32.767 kilograms in
                        // AI (3103), 99.99 pounds in AI (3202) or 22.767 pounds in AI (3203). Other weights
                        // take the weight and date method with no date.
                        bool limited = metric
                            ? text[19] == '3' && weight <= 32767
                            : (text[19] == '2' && weight <= 9999) || (text[19] == '3' && weight <= 22767);
                        method = limited ? (metric ? 3 : 4) : (metric ? 7 : 8);
                    }
                    else if (length == 34 && text[26] == '1' && text[27] is '1' or '3' or '5' or '7' && Date(text, 28) >= 0)
                    {
                        // A production, packaging, best before or expiration date follows the weight.
                        method = (metric ? 6 : 7) + (text[27] - '0');
                    }
                }
            }
            else if (text[17] == '9' && text[19] is >= '0' and <= '3')
            {
                if (text[18] == '2')
                {
                    method = 5;
                }
                else if (length >= 23 && text[18] == '3' && Number(text, 20, 3) >= 0)
                {
                    method = 6;
                }
            }
        }

        // Every method but the second encodes the GTIN without its check digit, so the check digit must be
        // the one that the 13 digits before it give.
        if (method != 2)
        {
            ReadOnlySpan<char> gtin = text.Slice(2, 14);
            SpanCodePointEnumerator codePoints = gtin.EnumerateCodePoints();
            while (codePoints.MoveNext())
            {
                CodePoint current = codePoints.Current;
                if (!current.IsAsciiDigit())
                {
                    throw new ArgumentException($"The GTIN of Application Identifier (01) carries only digits; got {current.ToDisplayString()}.", nameof(text));
                }
            }

            int expected = Gs1Data.ComputeCheckDigit(gtin[..13]);
            if (gtin[13] - '0' != expected)
            {
                throw new ArgumentException($"Incorrect GTIN check digit: expected {expected}, got {gtin[13]}.", nameof(text));
            }
        }

        int position = 0;
        Append(bits, ref position, 0, 1);
        int variableLength = -1;
        int read;
        switch (method)
        {
            case 1:
                Append(bits, ref position, 1, 1);
                variableLength = position;
                Append(bits, ref position, 0, 2);
                Append(bits, ref position, text[2] - '0', 4);
                AppendGtin(bits, ref position, text);
                read = 16;
                break;

            case 2:
                Append(bits, ref position, 0, 2);
                variableLength = position;
                Append(bits, ref position, 0, 2);
                read = 0;
                break;

            case 3:
            case 4:
                Append(bits, ref position, method == 3 ? 0b0100 : 0b0101, 4);
                AppendGtin(bits, ref position, text);
                Append(bits, ref position, Number(text, 20, 6) + (method == 4 && text[19] == '3' ? 10000 : 0), 15);
                read = 26;
                break;

            case 5:
            case 6:
                Append(bits, ref position, method == 5 ? 0b01100 : 0b01101, 5);
                variableLength = position;
                Append(bits, ref position, 0, 2);
                AppendGtin(bits, ref position, text);
                Append(bits, ref position, text[19] - '0', 2);
                if (method == 6)
                {
                    Append(bits, ref position, Number(text, 20, 3), 10);
                }

                read = method == 5 ? 20 : 23;
                break;

            default:
                // The weight field carries the last digit of the identifier and the five digits after the
                // weight's leading zero. The date field carries 38400 when there is no date.
                Append(bits, ref position, 0b0111000 + (method - 7), 7);
                AppendGtin(bits, ref position, text);
                Append(bits, ref position, ((text[19] - '0') * 100000) + Number(text, 21, 5), 20);
                Append(bits, ref position, length == 34 ? Date(text, 28) : 38400, 16);
                read = length;
                break;
        }

        int mode = EncodeGeneralField(text[read..], bits, ref position, out char lastDigit);

        int symbolCharacters = SymbolCharacters(position, segments);
        int remainder = (12 * (symbolCharacters - 1)) - position;
        if (lastDigit != '\0')
        {
            // A single digit left at the end takes four bits as its value plus one when four to six bits
            // remain to the end of the last character, and otherwise seven bits as a pair with a separator.
            if (remainder is >= 4 and <= 6)
            {
                Append(bits, ref position, lastDigit - '0' + 1, 4);
            }
            else
            {
                Append(bits, ref position, ((lastDigit - '0') * 11) + 10 + 8, 7);
            }

            symbolCharacters = SymbolCharacters(position, segments);
            remainder = (12 * (symbolCharacters - 1)) - position;
        }

        if (symbolCharacters > MaximumSymbolCharacters)
        {
            throw new ArgumentException(
                $"GS1 DataBar Expanded carries at most {MaximumSymbolCharacters - 1} data characters; the data needs more.",
                nameof(text));
        }

        // The pad starts with the numeric to alphanumeric latch when the field ends in numeric mode, and
        // then repeats 00100 to the end of the last data character.
        int end = position + remainder;
        int pad = 0;
        if (mode == NumericMode)
        {
            while (pad < 4 && position < end)
            {
                bits[position++] = 0;
                pad++;
            }
        }

        for (int k = 0; position < end; k = (k + 1) % 5)
        {
            bits[position++] = (byte)(k == 2 ? 1 : 0);
        }

        // The variable length field gives whether the number of symbol characters is odd and whether it
        // exceeds 14.
        if (variableLength >= 0)
        {
            bits[variableLength] = (byte)(symbolCharacters & 1);
            bits[variableLength + 1] = (byte)(symbolCharacters > 14 ? 1 : 0);
        }

        return position;
    }

    /// <summary>
    /// Encodes the general purpose field. Numeric mode carries two digits or a digit and a separator per
    /// seven bits, alphanumeric mode a character per five or six bits, and ISO/IEC 646 mode a character per
    /// five, seven or eight bits. Each mode latches to another when the characters ahead favour it.
    /// </summary>
    /// <param name="field">The characters of the field.</param>
    /// <param name="bits">The buffer of bits.</param>
    /// <param name="position">The bit position.</param>
    /// <param name="lastDigit">A single digit left at the end in numeric mode, or zero.</param>
    /// <returns>The mode at the end of the field.</returns>
    private static int EncodeGeneralField(ReadOnlySpan<char> field, Span<byte> bits, ref int position, out char lastDigit)
    {
        lastDigit = '\0';
        int mode = NumericMode;
        int i = 0;
        while (i < field.Length)
        {
            char c = field[i];
            int type = TypeOf(c);
            if (type == 0)
            {
                throw new ArgumentException($"GS1 DataBar Expanded cannot carry the character U+{(int)c:X4}.", nameof(field));
            }

            switch (mode)
            {
                case NumericMode:
                    if (i < field.Length - 1)
                    {
                        if (type != NumericMode || TypeOf(field[i + 1]) != NumericMode)
                        {
                            Append(bits, ref position, 0b0000, 4);
                            mode = AlphanumericMode;
                        }
                        else
                        {
                            Append(bits, ref position, (11 * NumericValue(c)) + NumericValue(field[i + 1]) + 8, 7);
                            i += 2;
                        }
                    }
                    else if (type != NumericMode)
                    {
                        Append(bits, ref position, 0b0000, 4);
                        mode = AlphanumericMode;
                    }
                    else
                    {
                        lastDigit = c;
                        i++;
                    }

                    break;

                case AlphanumericMode:
                    if (c == Gs1Data.Separator)
                    {
                        Append(bits, ref position, 0b01111, 5);
                        mode = NumericMode;
                        i++;
                    }
                    else if (type == Iso646Mode)
                    {
                        Append(bits, ref position, 0b00100, 5);
                        mode = Iso646Mode;
                    }
                    else if (Next(field, i, 6, NumericMode, 0) || EndsNumeric(field, i))
                    {
                        // Six numeric characters ahead, or four or five that end the field.
                        Append(bits, ref position, 0b000, 3);
                        mode = NumericMode;
                    }
                    else if (char.IsAsciiDigit(c))
                    {
                        Append(bits, ref position, c - 43, 5);
                        i++;
                    }
                    else if (char.IsAsciiLetterUpper(c))
                    {
                        Append(bits, ref position, c - 33, 6);
                        i++;
                    }
                    else
                    {
                        Append(bits, ref position, "*,-./".IndexOf(c) + 58, 6);
                        i++;
                    }

                    break;

                default:
                    if (c == Gs1Data.Separator)
                    {
                        Append(bits, ref position, 0b01111, 5);
                        mode = NumericMode;
                        i++;
                        break;
                    }

                    // A latch out of ISO/IEC 646 mode needs no ISO/IEC 646 only character in the next ten.
                    bool noIso646Ahead = NoneAhead(field, i, 10, Iso646Mode);
                    if (noIso646Ahead && Next(field, i, 4, NumericMode, 0))
                    {
                        Append(bits, ref position, 0b000, 3);
                        mode = NumericMode;
                    }
                    else if (noIso646Ahead && Next(field, i, 5, AlphanumericMode, NumericMode))
                    {
                        Append(bits, ref position, 0b00100, 5);
                        mode = AlphanumericMode;
                    }
                    else if (char.IsAsciiDigit(c))
                    {
                        Append(bits, ref position, c - 43, 5);
                        i++;
                    }
                    else if (char.IsAsciiLetterUpper(c))
                    {
                        Append(bits, ref position, c - 1, 7);
                        i++;
                    }
                    else if (char.IsAsciiLetterLower(c))
                    {
                        Append(bits, ref position, c - 7, 7);
                        i++;
                    }
                    else
                    {
                        Append(bits, ref position, Iso646Punctuation.IndexOf(c) + 232, 8);
                        i++;
                    }

                    break;
            }
        }

        return mode;
    }

    /// <summary>
    /// Returns the number of symbol characters that carry a number of data bits: the data characters rounded
    /// up, and the check character. A symbol carries at least four, and one more when a stacked symbol would
    /// otherwise leave a single symbol character on its last row.
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
    /// Appends the 12 digits of the GTIN after its indicator digit as four groups of three digits in ten
    /// bits each.
    /// </summary>
    /// <param name="bits">The buffer of bits.</param>
    /// <param name="position">The bit position.</param>
    /// <param name="text">The prepared element strings, which start with Application Identifier (01).</param>
    private static void AppendGtin(Span<byte> bits, ref int position, ReadOnlySpan<char> text)
    {
        for (int i = 3; i < 15; i += 3)
        {
            Append(bits, ref position, Number(text, i, 3), 10);
        }
    }

    /// <summary>
    /// Appends a value as a number of bits, most significant first.
    /// </summary>
    /// <param name="bits">The buffer of bits.</param>
    /// <param name="position">The bit position.</param>
    /// <param name="value">The value.</param>
    /// <param name="count">The number of bits.</param>
    /// <exception cref="ArgumentException">The bits exceed the 21 data characters of the largest symbol.</exception>
    private static void Append(Span<byte> bits, ref int position, int value, int count)
    {
        if (position + count > bits.Length)
        {
            throw new ArgumentException($"GS1 DataBar Expanded carries at most {MaximumSymbolCharacters - 1} data characters; the data needs more.");
        }

        for (int bit = count - 1; bit >= 0; bit--)
        {
            bits[position++] = (byte)((value >> bit) & 1);
        }
    }

    /// <summary>
    /// Appends an element of a row and switches the colour for the next one.
    /// </summary>
    /// <param name="row">The modules of the row.</param>
    /// <param name="length">The number of modules written.</param>
    /// <param name="colour">The module value of the element, 1 for dark.</param>
    /// <param name="width">The width of the element in modules.</param>
    private static void AppendElement(Span<byte> row, ref int length, ref byte colour, int width)
    {
        row.Slice(length, width).Fill(colour);
        length += width;
        colour = (byte)(1 - colour);
    }

    /// <summary>
    /// Rewrites the 15 separator modules over a finder pattern. Under a dark module the separator is light.
    /// Under a light module it is dark after a dark row module, and otherwise the opposite of the separator
    /// module to its left.
    /// </summary>
    /// <param name="row">The row the separator touches.</param>
    /// <param name="separator">The separator row.</param>
    /// <param name="start">The first module of the finder pattern.</param>
    private static void AlternateOverFinder(ReadOnlySpan<byte> row, Span<byte> separator, int start)
    {
        for (int i = start; i <= start + 14; i++)
        {
            separator[i] = row[i] == 1
                ? (byte)0
                : row[i - 1] == 1 ? (byte)1 : (byte)(1 - separator[i - 1]);
        }
    }

    /// <summary>
    /// Composes the middle separator row: narrow dark and light modules alternating, inside four light
    /// modules at each end.
    /// </summary>
    /// <param name="middle">The buffer that receives the modules, one for each module of the symbol width.</param>
    private static void ComposeMiddle(Span<byte> middle)
    {
        for (int i = 0; i < middle.Length; i++)
        {
            middle[i] = (byte)(i & 1);
        }

        middle[..4].Clear();
        middle[^4..].Clear();
    }

    /// <summary>
    /// Returns the character class of a general purpose field character: numeric for a digit or the
    /// separator, alphanumeric for a capital letter or one of <c>* , - . /</c>, ISO/IEC 646 for a small
    /// letter, a space or the other punctuation, and zero for a character the field cannot carry.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>The class.</returns>
    private static int TypeOf(char c)
    {
        if (c == Gs1Data.Separator || char.IsAsciiDigit(c))
        {
            return NumericMode;
        }

        if (char.IsAsciiLetterUpper(c) || c is '*' or ',' or '-' or '.' or '/')
        {
            return AlphanumericMode;
        }

        return char.IsAsciiLetterLower(c) || Iso646Punctuation.Contains(c) ? Iso646Mode : 0;
    }

    /// <summary>
    /// Returns the numeric mode value of a digit, or 10 for the separator.
    /// </summary>
    /// <param name="c">The digit or the separator.</param>
    /// <returns>The value.</returns>
    private static int NumericValue(char c)
        => c == Gs1Data.Separator ? 10 : c - '0';

    /// <summary>
    /// Returns whether the next characters, from the given one on, are all of one class or, when given, of a
    /// second class.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="start">The first character.</param>
    /// <param name="count">The number of characters.</param>
    /// <param name="type">The class.</param>
    /// <param name="alternative">A second class, or zero.</param>
    /// <returns>Whether that many characters remain and all are of the class.</returns>
    private static bool Next(ReadOnlySpan<char> field, int start, int count, int type, int alternative)
    {
        if (start + count > field.Length)
        {
            return false;
        }

        for (int i = start; i < start + count; i++)
        {
            int t = TypeOf(field[i]);
            if (t != type && (alternative == 0 || t != alternative))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether four or five numeric characters end the field from the given one on.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="start">The first character.</param>
    /// <returns>Whether the rest of the field is four or five numeric characters.</returns>
    private static bool EndsNumeric(ReadOnlySpan<char> field, int start)
        => field.Length - start is 4 or 5 && Next(field, start, field.Length - start, NumericMode, 0);

    /// <summary>
    /// Returns whether none of the next characters, or the characters to the end, are of a class.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="start">The first character.</param>
    /// <param name="count">The number of characters.</param>
    /// <param name="type">The class.</param>
    /// <returns>Whether none of them is of the class.</returns>
    private static bool NoneAhead(ReadOnlySpan<char> field, int start, int count, int type)
    {
        int end = Math.Min(start + count, field.Length);
        for (int i = start; i < end; i++)
        {
            if (TypeOf(field[i]) == type)
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
    /// Reads a date of six digits, YYMMDD, as the value the weight and date methods carry: the year times
    /// 384, the month less one times 32, and the day. A day of zero stands for the last day of the month.
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
}
