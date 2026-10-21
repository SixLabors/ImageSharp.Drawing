// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the first group of GS1 DataBar symbols: GS1 DataBar Omnidirectional, Truncated, Stacked and Stacked Omnidirectional. A symbol carries the
/// item identification of GS1 Application Identifier (01) as four symbol characters and two finder patterns, between a guard pattern of a narrow space and a
/// narrow bar at each end. The finder patterns encode a modulo 79 check value. The four variations encode the data in the same way and differ in height and in
/// how the two halves of the symbol stack.
/// <para>
/// The 13 digits before the check digit form a number below 10^13. It splits into a left and a right pair value modulo 4537077, and each pair splits into an
/// outside and an inside symbol character modulo 1597. A symbol character is 8 elements, an odd set of 4 and an even set of 4 interleaved. Its group gives the
/// module count and the widest element of each set, and its value within the group gives the width of every element.
/// </para>
/// </summary>
internal static class DataBarEncoder
{
    /// <summary>
    /// The nominal X dimension in millimetres, the target X dimension of Tables 5-44, 5-50 and 5-56 of the GS1 General Specifications. GS1 DataBar Truncated
    /// and GS1 DataBar Stacked are in Tables 5-50 and 5-56 only.
    /// </summary>
    public const double NominalXDimension = 0.33;

    /// <summary>
    /// The smallest X dimension in millimetres of every GS1 DataBar symbol, the smallest minimum of the symbol specification tables of section 5.12.3 that list
    /// them: 0.170 in Table 5-52.
    /// </summary>
    public const double MinimumXDimension = 0.170;

    /// <summary>
    /// The largest X dimension in millimetres of every GS1 DataBar symbol, the maximum of every symbol specification table that lists them.
    /// </summary>
    public const double MaximumXDimension = 0.660;

    /// <summary>
    /// The nominal height of GS1 DataBar Omnidirectional in modules. Table 5-44 gives 15.19 millimetres at the target X dimension, and its note (****) requires
    /// an over square symbol of 46X in an omnidirectional scanning environment.
    /// </summary>
    public const float OmnidirectionalHeight = 46F;

    /// <summary>
    /// The minimum height of GS1 DataBar Omnidirectional in modules. Section 5.5.2.1.1 of the GS1 General Specifications: "33X is the minimum height of the
    /// symbol".
    /// </summary>
    public const float OmnidirectionalMinimumHeight = 33F;

    /// <summary>
    /// The height of GS1 DataBar Truncated in modules. Section 5.5.2.1.2 of the GS1 General Specifications gives "96X wide by 13X high". It is also the
    /// minimum: Table 5-56 gives 3.43 millimetres, 13X, at the X dimension of 0.264 millimetre, and "the symbol height SHALL NOT be truncated below the
    /// minimum".
    /// </summary>
    public const float TruncatedHeight = 13F;

    /// <summary>
    /// The height of the upper row of GS1 DataBar Stacked in modules. With the lower row and the 1X separator it makes the 13X height of the "50X wide by 13X
    /// high" of section 5.5.2.1.3, which gives no height for each row.
    /// </summary>
    private const float StackedUpperRowHeight = 5F;

    /// <summary>
    /// The height of the lower row of GS1 DataBar Stacked in modules.
    /// </summary>
    private const float StackedLowerRowHeight = 7F;

    /// <summary>
    /// The nominal height of each row of GS1 DataBar Stacked Omnidirectional in modules. Table 5-44 gives 31.37 millimetres, 95X, at the target X dimension,
    /// and section 5.5.2.1.4 places a 3X separator between the two rows.
    /// </summary>
    private const float StackedOmnidirectionalRowHeight = 46F;

    /// <summary>
    /// The minimum height of each row of GS1 DataBar Stacked Omnidirectional in modules. Section 5.5.2.1.4: "69X is the minimum height of the symbol", and
    /// "The height of 69X includes a 3X high separator pattern between two rows of 33X each".
    /// </summary>
    private const float StackedOmnidirectionalMinimumRowHeight = 33F;

    /// <summary>
    /// The width of a single row symbol in modules, section 5.5.2.1.1: "Its dimensions are 96X wide".
    /// </summary>
    private const int Width = 96;

    /// <summary>
    /// The width of a stacked symbol in modules, section 5.5.2.1.3: "Its dimensions are 50X wide".
    /// </summary>
    private const int StackedWidth = 50;

    /// <summary>
    /// The number of elements in a symbol: a guard pair at each end, four symbol characters of 8 elements and two finder patterns of 5, starting with a space.
    /// </summary>
    private const int ElementCount = 46;

    /// <summary>
    /// The modulus that splits the data value into the left and right pair values.
    /// </summary>
    private const int PairModulus = 4537077;

    /// <summary>
    /// The modulus that splits a pair value into its outside and inside symbol characters.
    /// </summary>
    private const int CharacterModulus = 1597;

    /// <summary>
    /// The modulus of the check value.
    /// </summary>
    private const int CheckModulus = 79;

    /// <summary>
    /// The number of digits that the symbol encodes: the GTIN without its check digit.
    /// </summary>
    private const int DataDigits = 13;

    /// <summary>
    /// The number of symbol characters of a symbol.
    /// </summary>
    private const int CharacterCount = 4;

    /// <summary>
    /// The number of elements of a symbol character, an odd set of 4 and an even set of 4.
    /// </summary>
    private const int CharacterElements = 8;

    /// <summary>
    /// The number of modules of an outside symbol character, the first or the third.
    /// </summary>
    private const int OutsideModules = 16;

    /// <summary>
    /// The number of modules of an inside symbol character, the second or the fourth.
    /// </summary>
    private const int InsideModules = 15;

    /// <summary>
    /// The first group of the inside symbol characters in <see cref="GroupSum"/>. The groups before it are the groups of the outside symbol characters.
    /// </summary>
    private const int FirstInsideGroup = 5;

    /// <summary>
    /// The number of elements of a finder pattern.
    /// </summary>
    private const int FinderElements = 5;

    /// <summary>
    /// The number of finder patterns in <see cref="FinderPatterns"/>.
    /// </summary>
    private const int FinderValues = 9;

    /// <summary>
    /// The number of modules of the first three elements of a finder pattern, as the left finder reads them. The last two elements are one module each.
    /// </summary>
    private const int FinderLeadModules = 13;

    /// <summary>
    /// The number of elements of the guard pattern at each end, a narrow space and a narrow bar. Each element is one module, so the guard pattern is also
    /// this many modules wide.
    /// </summary>
    private const int GuardElements = 2;

    /// <summary>
    /// The number of light modules at each end of a separator row.
    /// </summary>
    private const int SeparatorLightModules = 4;

    /// <summary>
    /// Gets the first symbol character value of each group: groups 0 to 4 are the outside characters and groups 5 to 8 the inside characters.
    /// </summary>
    private static ReadOnlySpan<int> GroupSum => [0, 161, 961, 2015, 2715, 0, 336, 1036, 1516];

    /// <summary>
    /// Gets the number of even set combinations of an outside group, and of odd set combinations of an inside group. A value within a group splits into the two
    /// set values by this number.
    /// </summary>
    private static ReadOnlySpan<int> Combinations => [1, 10, 34, 70, 126, 4, 20, 48, 81];

    /// <summary>
    /// Gets the module count of the odd set of each group. The even set takes the rest of the 16 modules of an outside character or the 15 modules of an inside
    /// character.
    /// </summary>
    private static ReadOnlySpan<int> OddModules => [12, 10, 8, 6, 4, 5, 7, 9, 11];

    /// <summary>
    /// Gets the widest element of the odd set of each group. The widest element of the even set is nine less this.
    /// </summary>
    private static ReadOnlySpan<int> OddWidest => [8, 6, 4, 3, 1, 2, 4, 6, 8];

    /// <summary>
    /// Gets the element widths of the nine finder patterns, five elements each, as the left finder reads them. The right finder reads the same widths in
    /// reverse.
    /// </summary>
    private static ReadOnlySpan<byte> FinderPatterns =>
    [
        3, 8, 2, 1, 1,
        3, 5, 5, 1, 1,
        3, 3, 7, 1, 1,
        3, 1, 9, 1, 1,
        2, 7, 4, 1, 1,
        2, 5, 6, 1, 1,
        2, 3, 8, 1, 1,
        1, 5, 7, 1, 1,
        1, 3, 9, 1, 1
    ];

    /// <summary>
    /// Gets the check weights, eight per symbol character in the order the characters are calculated.
    /// </summary>
    private static ReadOnlySpan<byte> CheckWeights =>
    [
        1, 3, 9, 27, 2, 6, 18, 54,
        4, 12, 36, 29, 8, 24, 72, 58,
        16, 48, 65, 37, 32, 17, 51, 74,
        64, 34, 23, 69, 49, 68, 46, 59
    ];

    /// <summary>
    /// Builds a single row symbol, GS1 DataBar Omnidirectional or GS1 DataBar Truncated.
    /// </summary>
    /// <param name="gtin">The 14 digit GTIN.</param>
    /// <param name="linked">Whether a 2D component of a GS1 Composite symbol is linked to the symbol.</param>
    /// <param name="nominalHeight">The nominal height of the variation in modules.</param>
    /// <param name="minimumHeight">The minimum height that the variation requires in modules. A smaller bar height draws at this height.</param>
    /// <param name="options">The options that control the bar height and the text.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildLinear(
        ReadOnlySpan<char> gtin,
        bool linked,
        float nominalHeight,
        float minimumHeight,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        double xDimension = options.ResolveXDimension(MinimumXDimension, NominalXDimension, MaximumXDimension);

        // The element widths start with the guard space, so a zero width bar leads the runs and the widths follow it.
        Span<int> runs = symbol.BeginLinear(ElementCount + 1, xDimension, out Span<float> heights, out _);
        _ = EncodeWidths(gtin, linked, runs[1..]);

        // Section 5.5.2 of the GS1 General Specifications gives a minimum height and no maximum: "the actual height of the symbol used depends on the specific
        // application requirements".
        float barHeight = options.ResolveBarHeight(xDimension, minimumHeight, nominalHeight, float.PositiveInfinity);
        heights.Fill(barHeight);
        AddCaption(gtin, Width, barHeight, options, ref symbol);
        symbol.EndLinear(0, 0);
    }

    /// <summary>
    /// Builds GS1 DataBar Stacked: the left half of the symbol over the right half, with a one module separator between them. The bar height of the options
    /// sets the lower row, and the upper row keeps its ratio of five to seven.
    /// </summary>
    /// <param name="gtin">The 14 digit GTIN.</param>
    /// <param name="linked">Whether a 2D component of a GS1 Composite symbol is linked to the symbol.</param>
    /// <param name="options">The options that control the bar height and the text.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildStacked(ReadOnlySpan<char> gtin, bool linked, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray64<int> widthStorage = default;
        Span<int> widths = ((Span<int>)widthStorage)[..ElementCount];
        _ = EncodeWidths(gtin, linked, widths);

        // The three rows share one buffer, which starts zeroed. The separator loop leaves its first module and the last four at zero, light.
        InlineArray256<byte> moduleStorage = default;
        Span<byte> modules = ((Span<byte>)moduleStorage)[..(StackedWidth * 3)];
        Span<byte> upper = modules[..StackedWidth];
        Span<byte> separator = modules.Slice(StackedWidth, StackedWidth);
        Span<byte> lower = modules.Slice(StackedWidth * 2, StackedWidth);
        Halves(widths, upper, lower);

        // Each separator module is light where the rows above and below are dark. Where they differ, it takes the opposite of the module to its left, so a
        // light run under or over an edge breaks into narrow elements. The four modules at each end stay light.
        for (int i = 1; i < StackedWidth - SeparatorLightModules; i++)
        {
            separator[i] = upper[i] == lower[i]
                ? (byte)(1 - upper[i])
                : (byte)(1 - separator[i - 1]);
        }

        separator[1..SeparatorLightModules].Clear();

        double xDimension = options.ResolveXDimension(MinimumXDimension, NominalXDimension, MaximumXDimension);

        // Table 5-56 of the GS1 General Specifications gives a minimum symbol height of 3.43 millimetres, 13X, at the X dimension of 0.264 millimetre. The bar
        // height sets the lower row, and the upper row keeps its ratio of five to seven, so a symbol of 13X has a lower row of 7X. Section 5.5.2.1.3 gives no
        // maximum height.
        float lowerHeight = options.ResolveBarHeight(xDimension, StackedLowerRowHeight, StackedLowerRowHeight, float.PositiveInfinity);
        float upperHeight = lowerHeight * StackedUpperRowHeight / StackedLowerRowHeight;
        int upperRuns = DataBarRows.RunCount(upper);
        int separatorRuns = DataBarRows.RunCount(separator);
        int lowerRuns = DataBarRows.RunCount(lower);
        symbol.BeginStacked(3, upperRuns + separatorRuns + lowerRuns, ((upperRuns + 1) >> 1) + ((separatorRuns + 1) >> 1) + ((lowerRuns + 1) >> 1), xDimension);
        DataBarRows.AddRow(upper, upperHeight, ref symbol);
        DataBarRows.AddRow(separator, 1F, ref symbol);
        DataBarRows.AddRow(lower, lowerHeight, ref symbol);

        AddCaption(gtin, StackedWidth, upperHeight + 1F + lowerHeight, options, ref symbol);
        symbol.EndStacked(0F, 0F, 0F);
    }

    /// <summary>
    /// Builds GS1 DataBar Stacked Omnidirectional: the left half of the symbol over the right half, with a separator of three one module rows between them. The
    /// bar height of the options sets each row.
    /// </summary>
    /// <param name="gtin">The 14 digit GTIN.</param>
    /// <param name="linked">Whether a 2D component of a GS1 Composite symbol is linked to the symbol.</param>
    /// <param name="options">The options that control the bar height and the text.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildStackedOmnidirectional(ReadOnlySpan<char> gtin, bool linked, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray64<int> widthStorage = default;
        Span<int> widths = ((Span<int>)widthStorage)[..ElementCount];
        int rightFinder = EncodeWidths(gtin, linked, widths);

        // The five rows share one buffer, which starts zeroed. The separator rows write only some of their modules, and the rest stay light.
        InlineArray256<byte> moduleStorage = default;
        Span<byte> modules = ((Span<byte>)moduleStorage)[..(StackedWidth * 5)];
        Span<byte> upper = modules[..StackedWidth];
        Span<byte> upperSeparator = modules.Slice(StackedWidth, StackedWidth);
        Span<byte> middleSeparator = modules.Slice(StackedWidth * 2, StackedWidth);
        Span<byte> lowerSeparator = modules.Slice(StackedWidth * 3, StackedWidth);
        Span<byte> lower = modules.Slice(StackedWidth * 4, StackedWidth);
        Halves(widths, upper, lower);

        // The first and last separator rows are the inverse of the row they touch, inside four light modules at each end. Over a finder pattern the light
        // modules break into alternating narrow elements instead, so the separator does not read as part of the finder.
        Invert(upper, upperSeparator);
        Invert(lower, lowerSeparator);

        // The upper finder starts after the guard pattern and the first character, an outside character. The lower row starts with a narrow bar and a narrow
        // space, then the fourth character, an inside character. The lower finder follows it, and its two narrow leading elements stay inverted.
        const int upperFinderStart = GuardElements + OutsideModules;
        const int lowerFinderLeadStart = GuardElements + InsideModules + 2;
        AlternateOverFinder(upper, upperSeparator, upperFinderStart);

        // Finder value 3 reads 9, 1 and 3 modules in the lower row. Over it the lower separator carries a single dark module over the start of the three
        // module finder bar.
        if (rightFinder == 3)
        {
            lowerSeparator.Slice(lowerFinderLeadStart, FinderLeadModules).Clear();
            lowerSeparator[lowerFinderLeadStart + 9 + 1] = 1;
        }
        else
        {
            AlternateOverFinder(lower, lowerSeparator, lowerFinderLeadStart);
        }

        // The middle separator row alternates narrow dark and light modules between the four light modules at each end.
        for (int i = SeparatorLightModules + 1; i < StackedWidth - SeparatorLightModules; i += 2)
        {
            middleSeparator[i] = 1;
        }

        double xDimension = options.ResolveXDimension(MinimumXDimension, NominalXDimension, MaximumXDimension);
        float rowHeight = options.ResolveBarHeight(
            xDimension,
            StackedOmnidirectionalMinimumRowHeight,
            StackedOmnidirectionalRowHeight,
            float.PositiveInfinity);

        int runCount = 0;
        int barCount = 0;
        for (int row = 0; row < 5; row++)
        {
            int runs = DataBarRows.RunCount(modules.Slice(row * StackedWidth, StackedWidth));
            runCount += runs;
            barCount += (runs + 1) >> 1;
        }

        symbol.BeginStacked(5, runCount, barCount, xDimension);
        DataBarRows.AddRow(upper, rowHeight, ref symbol);
        DataBarRows.AddRow(upperSeparator, 1F, ref symbol);
        DataBarRows.AddRow(middleSeparator, 1F, ref symbol);
        DataBarRows.AddRow(lowerSeparator, 1F, ref symbol);
        DataBarRows.AddRow(lower, rowHeight, ref symbol);

        AddCaption(gtin, StackedWidth, (rowHeight * 2F) + 3F, options, ref symbol);
        symbol.EndStacked(0F, 0F, 0F);
    }

    /// <summary>
    /// Adds the human readable interpretation below the symbol: the application identifier in parentheses and the 14 digits, as section 4.14 of the GS1 General
    /// Specifications prints an element string, at the size of <see cref="Gs1Data.MinimumTextSize"/> or larger. A symbol without a font carries no text.
    /// </summary>
    /// <param name="gtin">The 14 digit GTIN.</param>
    /// <param name="width">The symbol width in modules.</param>
    /// <param name="height">The symbol height in modules.</param>
    /// <param name="options">The options that carry the font.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void AddCaption(ReadOnlySpan<char> gtin, int width, float height, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (options.Font is null)
        {
            return;
        }

        const string identifier = "(01)";
        symbol.ReserveText(1, identifier.Length + gtin.Length);
        symbol.AddText(identifier, gtin, [], 0F, width, BarcodeTextSide.BelowBars, height + BarcodeTextPlacement.Clearance);
        symbol.SetTextSize(Gs1Data.MinimumTextSize, 0F);
    }

    /// <summary>
    /// Calculates the 46 element widths of the symbol, starting with the left guard space: the guard pair, the first character, the left finder, the second
    /// character reversed, the fourth character, the right finder reversed, the third character reversed and the guard pair.
    /// </summary>
    /// <param name="gtin">The 14 digit GTIN.</param>
    /// <param name="linked">Whether a 2D component of a GS1 Composite symbol is linked to the symbol.</param>
    /// <param name="widths">The buffer of 46 element widths.</param>
    /// <returns>The value of the right finder pattern.</returns>
    private static int EncodeWidths(ReadOnlySpan<char> gtin, bool linked, Span<int> widths)
    {
        // The check digit is not encoded: the 13 digits before it are the data value. The linkage flag stands before them as a fourteenth digit, so a linked
        // symbol adds 10^13.
        long value = linked ? 1 : 0;
        for (int i = 0; i < DataDigits; i++)
        {
            value = (value * 10) + (gtin[i] - '0');
        }

        int leftPair = (int)(value / PairModulus);
        int rightPair = (int)(value % PairModulus);

        // The first and third characters are outside characters and the second and fourth inside ones. An outside value splits into its odd value by the even
        // combinations and an inside value into its even value by the odd combinations, so the quotient and the remainder swap roles. The buffer holds the 32
        // widths of the four characters, then the odd and the even set of the character being calculated.
        const int setElements = CharacterElements / 2;
        InlineArray64<int> bufferStorage = default;
        Span<int> buffer = ((Span<int>)bufferStorage)[..((CharacterCount * CharacterElements) + CharacterElements)];
        Span<int> characterWidths = buffer[..(CharacterCount * CharacterElements)];
        Span<int> odd = buffer.Slice(CharacterCount * CharacterElements, setElements);
        Span<int> even = buffer.Slice((CharacterCount * CharacterElements) + setElements, setElements);
        int check = 0;
        for (int c = 0; c < CharacterCount; c++)
        {
            bool outside = (c & 1) == 0;

            // The first two characters are the outside and inside characters of the left pair, and the last two those of the right pair.
            int pair = c < 2 ? leftPair : rightPair;
            int character = outside ? pair / CharacterModulus : pair % CharacterModulus;
            int group = outside ? 0 : FirstInsideGroup;
            int last = outside ? FirstInsideGroup - 1 : GroupSum.Length - 1;
            while (group < last && character >= GroupSum[group + 1])
            {
                group++;
            }

            int withinGroup = character - GroupSum[group];
            int quotient = withinGroup / Combinations[group];
            int remainder = withinGroup % Combinations[group];
            int totalModules = outside ? OutsideModules : InsideModules;

            // An outside character excludes odd sets without a narrow element in its even set, and an inside character in its odd set.
            DataBarWidths.Calculate(odd, outside ? quotient : remainder, OddModules[group], OddWidest[group], !outside);
            DataBarWidths.Calculate(even, outside ? remainder : quotient, totalModules - OddModules[group], 9 - OddWidest[group], outside);

            Span<int> elements = characterWidths.Slice(c * CharacterElements, CharacterElements);
            for (int k = 0; k < setElements; k++)
            {
                elements[k * 2] = odd[k];
                elements[(k * 2) + 1] = even[k];
            }

            ReadOnlySpan<byte> weights = CheckWeights.Slice(c * CharacterElements, CharacterElements);
            for (int k = 0; k < CharacterElements; k++)
            {
                check += weights[k] * elements[k];
            }
        }

        // The check value skips the finder pairs (0, 8) and (8, 0), so it maps onto the 9 x 9 finder pairs less those two.
        check %= CheckModulus;
        if (check >= FinderValues - 1)
        {
            check++;
        }

        if (check >= (FinderValues - 1) * FinderValues)
        {
            check++;
        }

        int leftFinder = check / FinderValues;
        int rightFinder = check % FinderValues;

        // The symbol reads the guard pattern, the first character, the left finder, the second character reversed, the fourth character, the right finder
        // reversed, the third character reversed and the guard pattern.
        const int leftFinderStart = GuardElements + CharacterElements;
        const int secondStart = leftFinderStart + FinderElements;
        const int fourthStart = secondStart + CharacterElements;
        const int rightFinderStart = fourthStart + CharacterElements;
        const int thirdStart = rightFinderStart + FinderElements;
        widths[..GuardElements].Fill(1);
        widths[^GuardElements..].Fill(1);
        characterWidths[..CharacterElements].CopyTo(widths[GuardElements..]);
        characterWidths.Slice(3 * CharacterElements, CharacterElements).CopyTo(widths[fourthStart..]);

        Span<int> second = widths.Slice(secondStart, CharacterElements);
        characterWidths.Slice(CharacterElements, CharacterElements).CopyTo(second);
        second.Reverse();

        Span<int> third = widths.Slice(thirdStart, CharacterElements);
        characterWidths.Slice(2 * CharacterElements, CharacterElements).CopyTo(third);
        third.Reverse();

        ReadOnlySpan<byte> left = FinderPatterns.Slice(leftFinder * FinderElements, FinderElements);
        ReadOnlySpan<byte> right = FinderPatterns.Slice(rightFinder * FinderElements, FinderElements);
        for (int k = 0; k < FinderElements; k++)
        {
            widths[leftFinderStart + k] = left[k];
            widths[rightFinderStart + k] = right[FinderElements - 1 - k];
        }

        return rightFinder;
    }

    /// <summary>
    /// Expands the 46 element widths into the two 50 module rows of a stacked symbol. The upper row is the first 23 elements and a closing narrow bar and
    /// space. The lower row is an opening narrow bar and space and the last 23 elements.
    /// </summary>
    /// <param name="widths">The 46 element widths, starting with a space.</param>
    /// <param name="upper">The 50 modules of the upper row.</param>
    /// <param name="lower">The 50 modules of the lower row.</param>
    private static void Halves(ReadOnlySpan<int> widths, Span<byte> upper, Span<byte> lower)
    {
        int position = Expand(widths[..23], 0, upper, 0);
        upper[position] = 1;
        upper[position + 1] = 0;

        lower[0] = 1;
        lower[1] = 0;
        _ = Expand(widths[23..], 1, lower, 2);
    }

    /// <summary>
    /// Writes element widths into a module row.
    /// </summary>
    /// <param name="widths">The element widths.</param>
    /// <param name="dark">The module value of the first element, 1 for a bar.</param>
    /// <param name="modules">The module row.</param>
    /// <param name="position">The module the first element starts at.</param>
    /// <returns>The module after the last element.</returns>
    private static int Expand(ReadOnlySpan<int> widths, byte dark, Span<byte> modules, int position)
    {
        for (int i = 0; i < widths.Length; i++)
        {
            modules.Slice(position, widths[i]).Fill(dark);
            position += widths[i];
            dark = (byte)(1 - dark);
        }

        return position;
    }

    /// <summary>
    /// Writes the inverse of a row into a separator row, inside the four modules at each end. The caller passes a zeroed separator row, so those modules stay
    /// light.
    /// </summary>
    /// <param name="row">The row the separator touches.</param>
    /// <param name="separator">The separator row.</param>
    private static void Invert(ReadOnlySpan<byte> row, Span<byte> separator)
    {
        for (int i = SeparatorLightModules; i < row.Length - SeparatorLightModules; i++)
        {
            separator[i] = (byte)(1 - row[i]);
        }
    }

    /// <summary>
    /// Rewrites the separator modules over the first three elements of a finder pattern, as the left finder reads them. Under a dark module the separator is
    /// light. Under a light run it alternates dark and light, starting dark, so it breaks into narrow elements.
    /// </summary>
    /// <param name="row">The row the separator touches.</param>
    /// <param name="separator">The separator row.</param>
    /// <param name="start">The first module of the <see cref="FinderLeadModules"/> modules.</param>
    private static void AlternateOverFinder(ReadOnlySpan<byte> row, Span<byte> separator, int start)
    {
        byte next = 1;
        for (int i = start; i < start + FinderLeadModules; i++)
        {
            if (row[i] == 0)
            {
                separator[i] = next;
                next = (byte)(1 - next);
            }
            else
            {
                separator[i] = 0;
                next = 1;
            }
        }
    }
}
