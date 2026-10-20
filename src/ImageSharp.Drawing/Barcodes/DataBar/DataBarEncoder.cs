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
    public const float StackedUpperRowHeight = 5F;

    /// <summary>
    /// The height of the lower row of GS1 DataBar Stacked in modules.
    /// </summary>
    public const float StackedLowerRowHeight = 7F;

    /// <summary>
    /// The nominal height of each row of GS1 DataBar Stacked Omnidirectional in modules. Table 5-44 gives 31.37 millimetres, 95X, at the target X dimension,
    /// and section 5.5.2.1.4 places a 3X separator between the two rows.
    /// </summary>
    public const float StackedOmnidirectionalRowHeight = 46F;

    /// <summary>
    /// The minimum height of each row of GS1 DataBar Stacked Omnidirectional in modules. Section 5.5.2.1.4: "69X is the minimum height of the symbol", and
    /// "The height of 69X includes a 3X high separator pattern between two rows of 33X each".
    /// </summary>
    private const float StackedOmnidirectionalMinimumRowHeight = 33F;

    /// <summary>
    /// The width of a single row symbol in modules, section 5.5.2.1.1: "Its dimensions are 96X wide".
    /// </summary>
    public const int Width = 96;

    /// <summary>
    /// The width of a stacked symbol in modules, section 5.5.2.1.3: "Its dimensions are 50X wide".
    /// </summary>
    public const int StackedWidth = 50;

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
    /// <param name="minimumHeight">The minimum height that the variation requires in modules, or 0 for none.</param>
    /// <param name="options">The options that control the bar height and the text.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentOutOfRangeException">The bar height is under the minimum height.</exception>
    public static void BuildLinear(
        ReadOnlySpan<char> gtin,
        bool linked,
        float nominalHeight,
        float minimumHeight,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        double xDimension = options.ResolveXDimension(MinimumXDimension, NominalXDimension, MaximumXDimension);
        if (options.IsBarHeightUnder(minimumHeight * xDimension))
        {
            double minimum = minimumHeight * xDimension;
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.BarHeight,
                $"The symbol is at least {minimumHeight}X high, {minimum:0.##} mm at its X dimension; the bar height is {options.BarHeight:0.##} mm.");
        }

        // The element widths start with the guard space, so a zero width bar leads the runs and the widths follow it.
        Span<int> runs = symbol.BeginLinear(ElementCount + 1, xDimension, out Span<float> heights, out _);
        _ = EncodeWidths(gtin, linked, runs[1..]);

        float barHeight = options.ResolveBarHeight(xDimension, nominalHeight);
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
    /// <exception cref="ArgumentOutOfRangeException">The bar height is under the minimum height of the lower row.</exception>
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
        for (int i = 1; i < StackedWidth - 4; i++)
        {
            separator[i] = upper[i] == lower[i]
                ? (byte)(1 - upper[i])
                : (byte)(1 - separator[i - 1]);
        }

        separator[1..4].Clear();

        double xDimension = options.ResolveXDimension(MinimumXDimension, NominalXDimension, MaximumXDimension);

        // Table 5-56 of the GS1 General Specifications gives a minimum symbol height of 3.43 millimetres, 13X, at the X dimension of 0.264 millimetre. The bar
        // height sets the lower row, and the upper row keeps its ratio of five to seven, so a symbol of 13X has a lower row of 7X.
        if (options.IsBarHeightUnder(StackedLowerRowHeight * xDimension))
        {
            double minimum = StackedLowerRowHeight * xDimension;
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.BarHeight,
                $"The lower row is at least {StackedLowerRowHeight}X high, {minimum:0.##} mm at its X dimension; the bar height is {options.BarHeight:0.##} mm.");
        }

        float lowerHeight = options.ResolveBarHeight(xDimension, StackedLowerRowHeight);
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
        AlternateOverFinder(upper, upperSeparator, 18);

        // The upper finder starts at module 18, after the guard pair and the 16 modules of the first character. The lower finder starts at module 17, and its
        // two narrow leading elements stay inverted. Over finder value 3 the lower separator carries a single dark module over the start of the three module
        // finder bar.
        if (rightFinder == 3)
        {
            lowerSeparator.Slice(19, 13).Clear();
            lowerSeparator[29] = 1;
        }
        else
        {
            AlternateOverFinder(lower, lowerSeparator, 19);
        }

        // The middle separator row alternates narrow dark and light modules between the four light modules at each end.
        for (int i = 5; i < StackedWidth - 4; i += 2)
        {
            middleSeparator[i] = 1;
        }

        double xDimension = options.ResolveXDimension(MinimumXDimension, NominalXDimension, MaximumXDimension);
        if (options.IsBarHeightUnder(StackedOmnidirectionalMinimumRowHeight * xDimension))
        {
            double minimum = StackedOmnidirectionalMinimumRowHeight * xDimension;
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.BarHeight,
                $"Each row is at least 33X high, {minimum:0.##} mm at its X dimension; the bar height is {options.BarHeight:0.##} mm.");
        }

        float rowHeight = options.ResolveBarHeight(xDimension, StackedOmnidirectionalRowHeight);
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
        for (int i = 0; i < 13; i++)
        {
            value = (value * 10) + (gtin[i] - '0');
        }

        int leftPair = (int)(value / PairModulus);
        int rightPair = (int)(value % PairModulus);

        // The first and third characters are outside characters and the second and fourth inside ones. An outside value splits into its odd value by the even
        // combinations and an inside value into its even value by the odd combinations, so the quotient and the remainder swap roles. The buffer holds the 32
        // widths of the four characters, then the odd and the even set of the character being calculated.
        InlineArray64<int> bufferStorage = default;
        Span<int> buffer = ((Span<int>)bufferStorage)[..40];
        Span<int> characterWidths = buffer[..32];
        Span<int> odd = buffer.Slice(32, 4);
        Span<int> even = buffer.Slice(36, 4);
        int check = 0;
        for (int c = 0; c < 4; c++)
        {
            bool outside = (c & 1) == 0;

            // The first two characters are the outside and inside characters of the left pair, and the last two those of the right pair.
            int pair = c < 2 ? leftPair : rightPair;
            int character = outside ? pair / CharacterModulus : pair % CharacterModulus;
            int group = outside ? 0 : 5;
            int last = outside ? 4 : 8;
            while (group < last && character >= GroupSum[group + 1])
            {
                group++;
            }

            int withinGroup = character - GroupSum[group];
            int quotient = withinGroup / Combinations[group];
            int remainder = withinGroup % Combinations[group];
            int totalModules = outside ? 16 : 15;

            // An outside character excludes odd sets without a narrow element in its even set, and an inside character in its odd set.
            DataBarWidths.Calculate(odd, outside ? quotient : remainder, OddModules[group], OddWidest[group], !outside);
            DataBarWidths.Calculate(even, outside ? remainder : quotient, totalModules - OddModules[group], 9 - OddWidest[group], outside);

            Span<int> elements = characterWidths.Slice(c * 8, 8);
            for (int k = 0; k < 4; k++)
            {
                elements[k * 2] = odd[k];
                elements[(k * 2) + 1] = even[k];
            }

            for (int k = 0; k < 8; k++)
            {
                check += CheckWeights[(c * 8) + k] * elements[k];
            }
        }

        // The check value skips 8 and 72, so it maps onto the 9 x 9 finder pairs less those two.
        check %= CheckModulus;
        if (check >= 8)
        {
            check++;
        }

        if (check >= 72)
        {
            check++;
        }

        int leftFinder = check / 9;
        int rightFinder = check % 9;

        widths[0] = 1;
        widths[1] = 1;
        widths[44] = 1;
        widths[45] = 1;
        for (int k = 0; k < 8; k++)
        {
            widths[2 + k] = characterWidths[k];
            widths[15 + k] = characterWidths[8 + 7 - k];
            widths[23 + k] = characterWidths[24 + k];
            widths[36 + k] = characterWidths[16 + 7 - k];
        }

        ReadOnlySpan<byte> left = FinderPatterns.Slice(leftFinder * 5, 5);
        ReadOnlySpan<byte> right = FinderPatterns.Slice(rightFinder * 5, 5);
        for (int k = 0; k < 5; k++)
        {
            widths[10 + k] = left[k];
            widths[31 + k] = right[4 - k];
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
        for (int i = 4; i < row.Length - 4; i++)
        {
            separator[i] = (byte)(1 - row[i]);
        }
    }

    /// <summary>
    /// Rewrites the 13 separator modules over a finder pattern. Under a dark module the separator is light. Under a light run it alternates dark and light,
    /// starting dark, so it breaks into narrow elements.
    /// </summary>
    /// <param name="row">The row the separator touches.</param>
    /// <param name="separator">The separator row.</param>
    /// <param name="start">The first module of the 13.</param>
    private static void AlternateOverFinder(ReadOnlySpan<byte> row, Span<byte> separator, int start)
    {
        byte next = 1;
        for (int i = start; i < start + 13; i++)
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
}
