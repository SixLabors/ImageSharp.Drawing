// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The UPC-E symbology, which the GS1 General Specifications specify. UPC-E is the zero suppression form of a GTIN-12 that starts with a zero and holds four
/// or five zeros in the positions of one of four zero suppression rules. The text is the leading zero and the six digits that the rules leave, with an optional
/// check digit. Text that the rules cannot produce is rejected.
/// <para>
/// The symbol is 51 modules wide: a normal guard pattern, six symbol characters from number sets A and B, and a special right guard pattern. The number set
/// parity of the six characters carries the check digit of the GTIN-12.
/// </para>
/// </summary>
public sealed class UpcESymbology : BarcodeSymbology
{
    /// <summary>
    /// The width of the symbol in modules.
    /// </summary>
    private const int Width = 51;

    /// <summary>
    /// The special guard pattern that closes the symbol: space, bar, space, bar, space, bar.
    /// </summary>
    private const int SpecialGuard = 0b010101;

    /// <summary>
    /// The width of <see cref="SpecialGuard"/> in modules.
    /// </summary>
    private const int SpecialGuardWidth = 6;

    /// <summary>
    /// The quiet zone before the symbol in modules, which holds the leading zero. Table 5-11 of the GS1 General Specifications gives 9 modules.
    /// </summary>
    private const int LeadingQuietZone = 9;

    /// <summary>
    /// The quiet zone after the symbol in modules. Table 5-11 of the GS1 General Specifications gives 7 modules.
    /// </summary>
    private const int TrailingQuietZone = 7;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpcESymbology"/> class.
    /// </summary>
    public UpcESymbology()
    {
    }

    /// <summary>
    /// Gets the indexes of the extended bars: the two bars of the left guard pattern and the three bars of the special right guard pattern.
    /// </summary>
    private static ReadOnlySpan<int> GuardBars => [0, 1, 14, 15, 16];

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (text.Length is not (7 or 8))
        {
            throw new ArgumentException(
                $"UPC-E requires a leading zero and 6 data digits with an optional check digit; got {text.Length} characters.",
                nameof(text));
        }

        BarcodeSymbology.ValidateDigits(text);

        // Section 5.2.2.4.1 of the GS1 General Specifications: "D1 SHALL always be zero."
        if (text[0] != '0')
        {
            throw new ArgumentException("UPC-E encodes only a GTIN-12 that starts with a zero.", nameof(text));
        }

        // The four zero suppression rules of section 5.2.2.4.1 exclude each other, so a GTIN-12 suppresses one way only. The last digit selects the rule, and
        // each rule requires one digit to be nonzero: X5 (D6) when X6 is 5 to 9, X4 (D5) when X6 is 4, and X3 (D4) from 3 to 9 when X6 is 3. Other digits
        // would come from a GTIN-12 that a different rule suppresses.
        char x6 = text[6];
        if ((x6 >= '5' && text[5] == '0') || (x6 == '4' && text[4] == '0') || (x6 == '3' && text[3] < '3'))
        {
            throw new ArgumentException("The UPC-E digits are not the zero suppression of a GTIN-12.", nameof(text));
        }

        // The check digit is computed over the GTIN-12, not the compressed digits. Section 5.2.2.4.1: "D12 SHALL be the check digit calculated according to
        // the algorithm in section 7.9."
        InlineArray12<char> expandedStorage = default;
        Span<char> expanded = ((Span<char>)expandedStorage)[..11];
        int check = Gs1Data.ComputeCheckDigit(Expand(text, expanded));
        if (text.Length == 8 && text[7] - '0' != check)
        {
            throw new ArgumentException($"UPC-E check digit mismatch: expected {check}, got {text[7]}.", nameof(text));
        }

        int parity = EanUpcEncoder.UpcEParity[check];

        InlineArray64<byte> moduleStorage = default;
        Span<byte> modules = ((Span<byte>)moduleStorage)[..Width];
        InlineArray64<sbyte> correctionStorage = default;
        Span<sbyte> corrections = ((Span<sbyte>)correctionStorage)[..Width];
        int position = 0;
        EanUpcEncoder.AppendPattern(modules, ref position, EanUpcEncoder.NormalGuard, EanUpcEncoder.NormalGuardWidth);
        for (int i = 1; i <= 6; i++)
        {
            EanUpcNumberSet numberSet = ((parity >> (6 - i)) & 1) == 0 ? EanUpcNumberSet.A : EanUpcNumberSet.B;
            EanUpcEncoder.AppendCharacter(modules, corrections, ref position, numberSet, text[i]);
        }

        EanUpcEncoder.AppendPattern(modules, ref position, SpecialGuard, SpecialGuardWidth);

        EanUpcEncoder.BeginRuns(
            modules,
            corrections,
            options.ResolveXDimension(EanUpcEncoder.MinimumXDimension, EanUpcEncoder.NominalXDimension, EanUpcEncoder.MaximumXDimension),
            ref symbol,
            out Span<float> heights);

        float barHeight = options.ResolveBarHeight(symbol.XDimension, EanUpcEncoder.NominalBarHeight);
        EanUpcEncoder.FillGuardedHeights(heights, barHeight, GuardBars);

        // Section 5.2.5 of the GS1 General Specifications: "the size of the first and last digits should be reduced to a maximum width equivalent to four
        // modules". "The right-hand side of the first digit is positioned five module widths to the left of the leftmost guard bar." The left-hand side of
        // the last digit is positioned "three module widths for UPC-E barcodes" to the right of the rightmost guard bar. The digits share one baseline, which
        // aligns the bottom edges of the smaller digits with the others. Every other digit prints below its own symbol character. Digits hang one module
        // below the digit bars and flow past the extended guard bars.
        if (options.Font is not null)
        {
            float textLine = barHeight + BarcodeTextPlacement.Clearance;
            symbol.ReserveText(8, 8);
            symbol.AddTextWithinSpan(text.Slice(0, 1), -9F, -5F, BarcodeTextSide.BelowBars, textLine, BarcodeTextAlignment.Right);
            EanUpcEncoder.AddDigits(text.Slice(1, 6), 3F, 7F, BarcodeTextSide.BelowBars, textLine, ref symbol);
            symbol.AddTextWithinSpan([(char)('0' + check)], 54F, 58F, BarcodeTextSide.BelowBars, textLine, BarcodeTextAlignment.Left);
        }

        symbol.EndLinear(LeadingQuietZone, TrailingQuietZone, 0F, EanUpcEncoder.RunUnit, false);
    }

    /// <summary>
    /// Expands the compressed digits to the eleven data digits of the GTIN-12 with the zero suppression rules of section 5.2.2.4.1 of the GS1 General
    /// Specifications. The last compressed digit selects the rule.
    /// </summary>
    /// <param name="text">The leading zero followed by the six compressed digits.</param>
    /// <param name="destination">The buffer the eleven data digits are written into.</param>
    /// <returns>The eleven UPC-A data digits.</returns>
    private static ReadOnlySpan<char> Expand(ReadOnlySpan<char> text, Span<char> destination)
    {
        char d1 = text[1];
        char d2 = text[2];
        char d3 = text[3];
        char d4 = text[4];
        char d5 = text[5];
        char d6 = text[6];

        destination[0] = text[0];
        destination[1] = d1;
        destination[2] = d2;
        destination[3..11].Fill('0');
        switch (d6)
        {
            case '0':
            case '1':
            case '2':
                destination[3] = d6;
                destination[8] = d3;
                destination[9] = d4;
                destination[10] = d5;
                break;
            case '3':
                destination[3] = d3;
                destination[9] = d4;
                destination[10] = d5;
                break;
            case '4':
                destination[3] = d3;
                destination[4] = d4;
                destination[10] = d5;
                break;
            default:
                destination[3] = d3;
                destination[4] = d4;
                destination[5] = d5;
                destination[10] = d6;
                break;
        }

        return destination[..11];
    }
}
