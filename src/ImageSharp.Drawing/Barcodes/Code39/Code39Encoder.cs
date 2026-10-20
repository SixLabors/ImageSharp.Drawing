// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the Code 39 symbology family, defined by ISO/IEC 16388. Section 4.1 c) gives every symbol character nine elements, "5 bars and 4
/// spaces" of which "3 wide and 6 narrow", and section 4.2 separates the characters within the symbol with an inter-character gap. Section 4.2 also gives the
/// structure: a quiet zone, the start character, the data and its optional check character, the stop character and a quiet zone.
/// </summary>
internal static class Code39Encoder
{
    /// <summary>
    /// The largest number of characters a symbol carries. Section 4.1 e) makes the data string length variable, so the symbology sets no maximum of its own.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The quiet zone in modules on each side. Section 4.4 d): "Minimum width of quiet zone: 10 X".
    /// </summary>
    public const int QuietZone = 10;

    /// <summary>
    /// The number of characters a caller holds inline to build symbol data in. Data this long covers the labels the symbology is used for, and longer data
    /// rents its buffer once.
    /// </summary>
    public const int InlineBufferLength = 64;

    /// <summary>
    /// The element count of a symbol character and the gap that follows it.
    /// </summary>
    private const int ElementsPerCharacter = 10;

    /// <summary>
    /// The pattern index of the start and stop character, which carries no data.
    /// </summary>
    private const int StartStop = 43;

    /// <summary>
    /// The width in modules of a symbol character and its gap: three wide elements, six narrow elements and the narrow gap, the sixteen modules section 4.1 h)
    /// gives at the top of the ratio range.
    /// </summary>
    private const int ModulesPerCharacter = (3 * WideElement) + 6 + 1;

    /// <summary>
    /// The width of a wide element in modules. Section 4.4 b) allows a wide to narrow ratio of "2,0 : 1 to 3,0 : 1", and this is the widest of that range, so a
    /// symbol character and its gap take the sixteen modules at the top of the range section 4.1 h) gives. An application standard that names a nominal ratio
    /// inside that range builds the symbol with a <see cref="Code39Layout"/>.
    /// </summary>
    public const int WideElement = 3;

    /// <summary>
    /// The bar height as a fraction of the symbol width, quiet zones excluded, when the caller sets no height. Section 4.4 e) recommends a minimum of "5,0 mm
    /// or 15 % of symbol width excluding quiet zones, whichever is greater".
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <summary>
    /// The smallest bar height in millimetres when the caller sets none, section 4.4 e).
    /// </summary>
    private const float MinimumBarHeightMillimetres = 5F;

    /// <summary>
    /// Gets the element widths of every symbol character, one bit per element, most significant element first: a set bit is a wide element and a clear bit a
    /// narrow one. The first nine bits are the pattern Table 1 of ISO/IEC 16388 gives in bar and space order, and the tenth is the inter-character gap, which
    /// section 4.4 c) gives a minimum width of one narrow element. The last entry is the start and stop character.
    /// </summary>
    private static ReadOnlySpan<ushort> Patterns =>
    [
        0x068, 0x242, 0x0C2, 0x2C0, 0x062, 0x260, 0x0E0, 0x04A,
        0x248, 0x0C8, 0x212, 0x092, 0x290, 0x032, 0x230, 0x0B0,
        0x01A, 0x218, 0x098, 0x038, 0x206, 0x086, 0x284, 0x026,
        0x224, 0x0A4, 0x00E, 0x20C, 0x08C, 0x02C, 0x302, 0x182,
        0x380, 0x122, 0x320, 0x1A0, 0x10A, 0x308, 0x188, 0x150,
        0x144, 0x114, 0x054, 0x128,
    ];

    /// <summary>
    /// Calculates the check character over the given data. Annex A.1.1 assigns each data character the value Table A.1 gives it, sums those values, divides the
    /// sum by 43, and takes "the character whose value (from Table A.1) is the remainder from the division" as the check character. Table A.1 is the 43
    /// characters of <see cref="AlphanumericCharacterSet"/>.
    /// </summary>
    /// <param name="data">The data the check character covers.</param>
    /// <returns>The check character.</returns>
    public static char CheckCharacter(ReadOnlySpan<char> data)
    {
        int sum = 0;
        foreach (char character in data)
        {
            sum += AlphanumericCharacterSet.Value(character);
        }

        return AlphanumericCharacterSet.CheckCharacter(sum);
    }

    /// <summary>
    /// Validates the given text against the Code 39 character set. Walking code points rather than UTF-16 units reports a surrogate pair as the one character
    /// it is, instead of showing half of it back to the caller.
    /// </summary>
    /// <param name="text">The text to validate.</param>
    /// <exception cref="ArgumentException">The text carries a character outside the set.</exception>
    public static void Validate(ReadOnlySpan<char> text)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (AlphanumericCharacterSet.Value(current.Value) < 0)
            {
                throw new ArgumentException(
                    $"Code 39 carries only digits, capital letters, spaces and the symbols -.$/+%; {current.ToDisplayString()} is outside that set.",
                    nameof(text));
            }
        }
    }

    /// <summary>
    /// Builds the symbol. Code 39 carries no guard bars, so every bar runs the full height, at the proportional height of section 4.4 e), and the human
    /// readable interpretation sits below the symbol.
    /// </summary>
    /// <param name="data">The data to encode, already validated against the character set.</param>
    /// <param name="check">The check character to carry after the data, or <see langword="null"/>.</param>
    /// <param name="readablePrefix">The characters of the human readable interpretation before its body.</param>
    /// <param name="readableBody">The body of the human readable interpretation.</param>
    /// <param name="readableSuffix">The characters of the human readable interpretation after its body.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="BarcodeSymbol.XDimension"/>.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(
        ReadOnlySpan<char> data,
        char? check,
        ReadOnlySpan<char> readablePrefix,
        ReadOnlySpan<char> readableBody,
        ReadOnlySpan<char> readableSuffix,
        BarcodeOptions options,
        double xDimension,
        ref BarcodeSymbol symbol)
    {
        // Section 4.4 e) gives a "Recommended minimum bar code height" and no limit, so it sets the nominal bar height and does not clamp the bar height.
        int widthInModules = SymbolWidth(CharacterCount(data.Length, check));
        Code39Layout layout = new()
        {
            XDimension = xDimension,
            NominalBarHeight = ProportionalBarHeight(widthInModules, xDimension),
            MinimumBarHeight = 0F,
            MaximumBarHeight = float.PositiveInfinity,
            NarrowElement = 1,
            WideElement = WideElement,
        };

        BuildSymbol(data, check, readablePrefix, readableBody, readableSuffix, in layout, options, ref symbol);
    }

    /// <summary>
    /// Returns the bar height in modules that section 4.4 e) recommends when the caller sets none: 15 % of the symbol width, quiet zones excluded, and at
    /// least 5 mm.
    /// </summary>
    /// <param name="widthInModules">The width of the symbol in modules, quiet zones excluded.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <returns>The bar height in modules.</returns>
    public static float ProportionalBarHeight(float widthInModules, double xDimension)
        => MathF.Max(widthInModules * NominalBarHeightFraction, (float)(MinimumBarHeightMillimetres / xDimension));

    /// <summary>
    /// Builds the symbol at the dimensions of an application standard, which can name a wide to narrow ratio that is not a whole number. The runs count run
    /// units of a module divided by the narrow width. The runs carry the start character, the data, an optional check character and the stop character, each
    /// followed by its inter-character gap. Section 4.2 separates the characters "within the symbol" with that gap, and section 4.3.3 puts the stop character
    /// at the right end, so no gap follows it and the runs end on a bar. Annex A.1.1 places the check character "immediately following the final data
    /// character and before the stop character".
    /// </summary>
    /// <param name="data">The data to encode, already validated against the character set.</param>
    /// <param name="check">The check character to carry after the data, or <see langword="null"/>.</param>
    /// <param name="readablePrefix">The characters of the human readable interpretation before its body.</param>
    /// <param name="readableBody">The body of the human readable interpretation.</param>
    /// <param name="readableSuffix">The characters of the human readable interpretation after its body.</param>
    /// <param name="layout">The dimensions of the application standard.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(
        ReadOnlySpan<char> data,
        char? check,
        ReadOnlySpan<char> readablePrefix,
        ReadOnlySpan<char> readableBody,
        ReadOnlySpan<char> readableSuffix,
        in Code39Layout layout,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        int narrow = layout.NarrowElement;
        int wide = layout.WideElement;
        int characters = CharacterCount(data.Length, check);
        Span<int> runs = symbol.BeginLinear((characters * ElementsPerCharacter) - 1, layout.XDimension, out Span<float> heights, out _);
        int written = 0;

        AppendElements(runs, ref written, StartStop, ElementsPerCharacter, narrow, wide);
        foreach (char character in data)
        {
            AppendElements(runs, ref written, AlphanumericCharacterSet.Value(character), ElementsPerCharacter, narrow, wide);
        }

        if (check is not null)
        {
            AppendElements(runs, ref written, AlphanumericCharacterSet.Value(check.Value), ElementsPerCharacter, narrow, wide);
        }

        AppendElements(runs, ref written, StartStop, ElementsPerCharacter - 1, narrow, wide);

        // Every character has three wide and six narrow elements and a narrow gap, and the stop character carries no gap.
        float runUnit = 1F / narrow;
        float widthInModules = ((characters * ((3 * wide) + (7 * narrow))) - narrow) * runUnit;
        float barHeight = options.ResolveBarHeight(layout.XDimension, layout.MinimumBarHeight, layout.NominalBarHeight, layout.MaximumBarHeight);
        heights.Fill(barHeight);

        if (options.Font is not null)
        {
            symbol.ReserveText(1, readablePrefix.Length + readableBody.Length + readableSuffix.Length);
            symbol.AddText(
                readablePrefix,
                readableBody,
                readableSuffix,
                0F,
                widthInModules,
                BarcodeTextSide.BelowBars,
                barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(QuietZone, QuietZone, 0F, runUnit, false);
    }

    /// <summary>
    /// Gets the number of symbol characters a symbol carries: the start character, the data, the optional check character and the stop character.
    /// </summary>
    /// <param name="dataLength">The number of data characters.</param>
    /// <param name="check">The check character to carry after the data, or <see langword="null"/>.</param>
    /// <returns>The number of symbol characters.</returns>
    private static int CharacterCount(int dataLength, char? check) => dataLength + (check is null ? 2 : 3);

    /// <summary>
    /// Gets the width of a symbol in modules, quiet zones excluded. Every pattern has three wide elements among its nine, so each character and its gap take
    /// <see cref="ModulesPerCharacter"/> modules, and the stop character carries no gap.
    /// </summary>
    /// <param name="characters">The number of symbol characters, the start and stop characters included.</param>
    /// <returns>The width in modules.</returns>
    private static int SymbolWidth(int characters) => (characters * ModulesPerCharacter) - 1;

    /// <summary>
    /// Writes the leading element widths of one symbol character: all ten for a character and its gap, or the first nine for the stop character.
    /// </summary>
    /// <param name="runs">The buffer the widths are written to.</param>
    /// <param name="written">The write position, advanced by the element count.</param>
    /// <param name="index">The pattern index, which is the character value.</param>
    /// <param name="count">The number of elements to write.</param>
    /// <param name="narrow">The width of a narrow element in run units.</param>
    /// <param name="wide">The width of a wide element in run units.</param>
    private static void AppendElements(Span<int> runs, ref int written, int index, int count, int narrow, int wide)
    {
        ushort pattern = Patterns[index];
        for (int element = ElementsPerCharacter - 1; element >= ElementsPerCharacter - count; element--)
        {
            runs[written++] = ((pattern >> element) & 1) == 1 ? wide : narrow;
        }
    }
}
