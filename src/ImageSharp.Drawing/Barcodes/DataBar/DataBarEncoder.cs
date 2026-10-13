// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the first group of GS1 DataBar symbols: GS1 DataBar Omnidirectional, Truncated, Stacked
/// and Stacked Omnidirectional. A symbol carries the item identification of GS1 Application Identifier (01)
/// as four symbol characters and two finder patterns, between a guard pattern of a narrow space and a
/// narrow bar at each end. The finder patterns encode a modulo 79 check value. The four variations encode
/// the data in the same way and differ in height and in how the two halves of the symbol stack.
/// <para>
/// The 13 digits before the check digit form a number below 10^13. It splits into a left and a right pair
/// value modulo 4537077, and each pair splits into an outside and an inside symbol character modulo 1597.
/// A symbol character is 8 elements, an odd set of 4 and an even set of 4 interleaved. Its group gives the
/// module count and the widest element of each set, and its value within the group gives the width of
/// every element.
/// </para>
/// </summary>
internal static class DataBarEncoder
{
    /// <summary>
    /// The nominal X dimension in millimetres, the target X dimension of Table 5-44 of the GS1 General
    /// Specifications.
    /// </summary>
    public const double NominalXDimension = 0.33;

    /// <summary>
    /// The nominal height of GS1 DataBar Omnidirectional in modules. Table 5-44 gives 15.19 millimetres at
    /// the target X dimension, and its note (****) requires an over square symbol of 46X in an
    /// omnidirectional scanning environment.
    /// </summary>
    public const float OmnidirectionalHeight = 46F;

    /// <summary>
    /// The height of GS1 DataBar Truncated in modules. Section 5.5.2.1.2 of the GS1 General Specifications
    /// gives "96X wide by 13X high".
    /// </summary>
    public const float TruncatedHeight = 13F;

    /// <summary>
    /// The height of the upper row of GS1 DataBar Stacked in modules. With the lower row and the separator
    /// it makes the "50X wide by 13X high" of section 5.5.2.1.3.
    /// </summary>
    public const float StackedUpperRowHeight = 5F;

    /// <summary>
    /// The height of the lower row of GS1 DataBar Stacked in modules.
    /// </summary>
    public const float StackedLowerRowHeight = 7F;

    /// <summary>
    /// The nominal height of each row of GS1 DataBar Stacked Omnidirectional in modules. Table 5-44 gives
    /// 31.37 millimetres, 95X, at the target X dimension, and section 5.5.2.1.4 places a 3X separator
    /// between the two rows.
    /// </summary>
    public const float StackedOmnidirectionalRowHeight = 46F;

    /// <summary>
    /// The width of a single row symbol in modules, section 5.5.2.1.1: "Its dimensions are 96X wide".
    /// </summary>
    public const int Width = 96;

    /// <summary>
    /// The width of a stacked symbol in modules, section 5.5.2.1.3: "Its dimensions are 50X wide".
    /// </summary>
    public const int StackedWidth = 50;

    /// <summary>
    /// The number of elements in a symbol: a guard pair at each end, four symbol characters of 8 elements
    /// and two finder patterns of 5, starting with a space.
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
    /// Gets the first symbol character value of each group: groups 0 to 4 are the outside characters and
    /// groups 5 to 8 the inside characters.
    /// </summary>
    private static ReadOnlySpan<int> GroupSum => [0, 161, 961, 2015, 2715, 0, 336, 1036, 1516];

    /// <summary>
    /// Gets the number of even set combinations of an outside group, and of odd set combinations of an
    /// inside group. A value within a group splits into the two set values by this number.
    /// </summary>
    private static ReadOnlySpan<int> Combinations => [1, 10, 34, 70, 126, 4, 20, 48, 81];

    /// <summary>
    /// Gets the module count of the odd set of each group. The even set takes the rest of the 16 modules of
    /// an outside character or the 15 modules of an inside character.
    /// </summary>
    private static ReadOnlySpan<int> OddModules => [12, 10, 8, 6, 4, 5, 7, 9, 11];

    /// <summary>
    /// Gets the widest element of the odd set of each group. The widest element of the even set is nine
    /// less this.
    /// </summary>
    private static ReadOnlySpan<int> OddWidest => [8, 6, 4, 3, 1, 2, 4, 6, 8];

    /// <summary>
    /// Gets the element widths of the nine finder patterns, five elements each, as the left finder reads
    /// them. The right finder reads the same widths in reverse.
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
    /// <param name="nominalHeight">The nominal height of the variation in modules.</param>
    /// <param name="options">The options that control the bar height and the text.</param>
    /// <returns>The encoded symbol.</returns>
    public static LinearBarcodeSymbol BuildLinear(ReadOnlySpan<char> gtin, float nominalHeight, BarcodeOptions options)
    {
        // The element widths start with the guard space, so a zero width bar leads the runs and the widths
        // follow it.
        int[] runs = new int[ElementCount + 1];
        _ = EncodeWidths(gtin, runs.AsSpan(1));

        float barHeight = options.ResolveBarHeight(NominalXDimension, nominalHeight);
        int barCount = (runs.Length + 1) / 2;
        float[] heights = new float[barCount];
        heights.AsSpan().Fill(barHeight);

        BarcodeTextPlacement[] placements = Caption(gtin, Width, barHeight, options);
        return new LinearBarcodeSymbol(runs, heights, new float[barCount], placements, 0, 0);
    }

    /// <summary>
    /// Builds GS1 DataBar Stacked: the left half of the symbol over the right half, with a one module
    /// separator between them. The bar height of the options sets the lower row, and the upper row keeps
    /// its ratio of five to seven.
    /// </summary>
    /// <param name="gtin">The 14 digit GTIN.</param>
    /// <param name="options">The options that control the bar height and the text.</param>
    /// <returns>The encoded symbol.</returns>
    public static StackedBarcodeSymbol BuildStacked(ReadOnlySpan<char> gtin, BarcodeOptions options)
    {
        Span<int> widths = stackalloc int[ElementCount];
        _ = EncodeWidths(gtin, widths);

        // The three rows share one buffer, which starts zeroed because the project does not skip local
        // initialization. The separator loop leaves its first module and the last four at zero, light.
        Span<byte> modules = stackalloc byte[StackedWidth * 3];
        Span<byte> upper = modules[..StackedWidth];
        Span<byte> separator = modules.Slice(StackedWidth, StackedWidth);
        Span<byte> lower = modules.Slice(StackedWidth * 2, StackedWidth);
        Halves(widths, upper, lower);

        // Each separator module is light where the rows above and below are dark. Where they differ, it
        // takes the opposite of the module to its left, so a light run under or over an edge breaks into
        // narrow elements. The four modules at each end stay light.
        for (int i = 1; i < StackedWidth - 4; i++)
        {
            separator[i] = upper[i] == lower[i]
                ? (byte)(1 - upper[i])
                : (byte)(1 - separator[i - 1]);
        }

        separator[1..4].Clear();

        float lowerHeight = options.ResolveBarHeight(NominalXDimension, StackedLowerRowHeight);
        float upperHeight = lowerHeight * StackedUpperRowHeight / StackedLowerRowHeight;
        LinearBarcodeSymbol[] rows =
        [
            DataBarRows.Row(upper, upperHeight),
            DataBarRows.Row(separator, 1F),
            DataBarRows.Row(lower, lowerHeight)
        ];

        BarcodeTextPlacement[] placements = Caption(gtin, StackedWidth, upperHeight + 1F + lowerHeight, options);
        return new StackedBarcodeSymbol(rows, 0F, placements, 0F, 0F);
    }

    /// <summary>
    /// Builds GS1 DataBar Stacked Omnidirectional: the left half of the symbol over the right half, with a
    /// separator of three one module rows between them. The bar height of the options sets each row.
    /// </summary>
    /// <param name="gtin">The 14 digit GTIN.</param>
    /// <param name="options">The options that control the bar height and the text.</param>
    /// <returns>The encoded symbol.</returns>
    public static StackedBarcodeSymbol BuildStackedOmnidirectional(ReadOnlySpan<char> gtin, BarcodeOptions options)
    {
        Span<int> widths = stackalloc int[ElementCount];
        int rightFinder = EncodeWidths(gtin, widths);

        // The five rows share one buffer, which starts zeroed because the project does not skip local
        // initialization. The separator rows write only some of their modules, and the rest stay light.
        Span<byte> modules = stackalloc byte[StackedWidth * 5];
        Span<byte> upper = modules[..StackedWidth];
        Span<byte> upperSeparator = modules.Slice(StackedWidth, StackedWidth);
        Span<byte> middleSeparator = modules.Slice(StackedWidth * 2, StackedWidth);
        Span<byte> lowerSeparator = modules.Slice(StackedWidth * 3, StackedWidth);
        Span<byte> lower = modules.Slice(StackedWidth * 4, StackedWidth);
        Halves(widths, upper, lower);

        // The first and last separator rows are the inverse of the row they touch, inside four light
        // modules at each end. Over a finder pattern the light modules break into alternating narrow
        // elements instead, so the separator does not read as part of the finder.
        Invert(upper, upperSeparator);
        Invert(lower, lowerSeparator);
        AlternateOverFinder(upper, upperSeparator, 18);

        // The upper finder starts at module 18, after the guard pair and the 16 modules of the first
        // character. The lower finder starts at module 17, and its two narrow leading elements stay
        // inverted. Over finder value 3 the lower separator carries a single dark module over the start
        // of the three module finder bar.
        if (rightFinder == 3)
        {
            lowerSeparator.Slice(19, 13).Clear();
            lowerSeparator[29] = 1;
        }
        else
        {
            AlternateOverFinder(lower, lowerSeparator, 19);
        }

        // The middle separator row alternates narrow dark and light modules between the four light
        // modules at each end.
        for (int i = 5; i < StackedWidth - 4; i += 2)
        {
            middleSeparator[i] = 1;
        }

        float rowHeight = options.ResolveBarHeight(NominalXDimension, StackedOmnidirectionalRowHeight);
        LinearBarcodeSymbol[] rows =
        [
            DataBarRows.Row(upper, rowHeight),
            DataBarRows.Row(upperSeparator, 1F),
            DataBarRows.Row(middleSeparator, 1F),
            DataBarRows.Row(lowerSeparator, 1F),
            DataBarRows.Row(lower, rowHeight)
        ];

        BarcodeTextPlacement[] placements = Caption(gtin, StackedWidth, (rowHeight * 2F) + 3F, options);
        return new StackedBarcodeSymbol(rows, 0F, placements, 0F, 0F);
    }

    /// <summary>
    /// Calculates the 46 element widths of the symbol, starting with the left guard space: the guard pair,
    /// the first character, the left finder, the second character reversed, the fourth character, the right
    /// finder reversed, the third character reversed and the guard pair.
    /// </summary>
    /// <param name="gtin">The 14 digit GTIN.</param>
    /// <param name="widths">The buffer of 46 element widths.</param>
    /// <returns>The value of the right finder pattern.</returns>
    private static int EncodeWidths(ReadOnlySpan<char> gtin, Span<int> widths)
    {
        // The check digit is not encoded: the 13 digits before it are the data value.
        long value = 0;
        for (int i = 0; i < 13; i++)
        {
            value = (value * 10) + (gtin[i] - '0');
        }

        int leftPair = (int)(value / PairModulus);
        int rightPair = (int)(value % PairModulus);

        // The first and third characters are outside characters and the second and fourth inside ones. An
        // outside value splits into its odd value by the even combinations and an inside value into its
        // even value by the odd combinations, so the quotient and the remainder swap roles.
        // The buffer holds the 32 widths of the four characters, then the odd and the even set of the
        // character being calculated.
        Span<int> buffer = stackalloc int[40];
        Span<int> characterWidths = buffer[..32];
        Span<int> odd = buffer.Slice(32, 4);
        Span<int> even = buffer.Slice(36, 4);
        int check = 0;
        for (int c = 0; c < 4; c++)
        {
            bool outside = (c & 1) == 0;

            // The first two characters are the outside and inside characters of the left pair, and the last
            // two those of the right pair.
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

            // An outside character excludes odd sets without a narrow element in its even set, and an
            // inside character in its odd set.
            DataBarWidths.Calculate(odd, outside ? quotient : remainder, OddModules[group], 4, OddWidest[group], !outside);
            DataBarWidths.Calculate(even, outside ? remainder : quotient, totalModules - OddModules[group], 4, 9 - OddWidest[group], outside);

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
    /// Expands the 46 element widths into the two 50 module rows of a stacked symbol. The upper row is the
    /// first 23 elements and a closing narrow bar and space. The lower row is an opening narrow bar and
    /// space and the last 23 elements.
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
    /// Writes the inverse of a row into a separator row, inside the four modules at each end. The caller
    /// passes a zeroed separator row, so those modules stay light.
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
    /// Rewrites the 13 separator modules over a finder pattern. Under a dark module the separator is light.
    /// Under a light run it alternates dark and light, starting dark, so it breaks into narrow elements.
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
    /// Builds the human readable interpretation below the symbol: the application identifier in
    /// parentheses and the 14 digits, as section 4.14 of the GS1 General Specifications prints an element
    /// string.
    /// </summary>
    /// <param name="gtin">The 14 digit GTIN.</param>
    /// <param name="width">The symbol width in modules.</param>
    /// <param name="height">The symbol height in modules.</param>
    /// <param name="options">The options that carry the font.</param>
    /// <returns>The placements, empty when the options carry no font.</returns>
    private static BarcodeTextPlacement[] Caption(ReadOnlySpan<char> gtin, int width, float height, BarcodeOptions options)
    {
        if (options.Font is null)
        {
            return [];
        }

        return [new BarcodeTextPlacement($"(01){gtin}", 0F, width, BarcodeTextSide.BelowBars, height + BarcodeTextPlacement.Clearance)];
    }
}
