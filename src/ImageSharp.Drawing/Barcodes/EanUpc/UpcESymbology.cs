// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The UPC-E symbology, which ISO/IEC 15420 specifies. UPC-E is the zero suppression form of UPC-A. A UPC-A number compresses to six digits when its
/// manufacturer and product codes match one of four zero patterns.
/// <para>
/// The symbol is 51 modules wide: a normal guard pattern, six symbol characters from number sets A and B, and a special right guard pattern. The number set
/// parity of the six characters carries the number system digit (0 or 1) and the check digit of the expanded UPC-A number.
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
    /// The quiet zone before the symbol in modules, which holds the number system digit.
    /// </summary>
    private const int LeadingQuietZone = 9;

    /// <summary>
    /// The quiet zone after the symbol in modules.
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
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        if (text.Length is not (7 or 8))
        {
            throw new ArgumentException(
                $"UPC-E requires a number system digit and 6 data digits with an optional check digit; got {text.Length} characters.",
                nameof(text));
        }

        BarcodeSymbology.ValidateDigits(text);

        int numberSystem = text[0] - '0';
        if (numberSystem is not 0 and not 1)
        {
            throw new ArgumentException("UPC-E supports only the number systems 0 and 1.", nameof(text));
        }

        // The check digit is computed over the expanded UPC-A number, not the compressed digits, per the zero suppression rules of ISO/IEC 15420.
        InlineArray12<char> expandedStorage = default;
        Span<char> expanded = ((Span<char>)expandedStorage)[..11];
        int check = Gs1Data.ComputeCheckDigit(Expand(text, expanded));
        if (text.Length == 8 && text[7] - '0' != check)
        {
            throw new ArgumentException($"UPC-E check digit mismatch: expected {check}, got {text[7]}.", nameof(text));
        }

        int parity = EanUpcEncoder.UpcEParity[check];
        if (numberSystem == 1)
        {
            parity = ~parity & 0b111111;
        }

        InlineArray64<byte> moduleStorage = default;
        Span<byte> modules = ((Span<byte>)moduleStorage)[..Width];
        int position = 0;
        EanUpcEncoder.AppendPattern(modules, ref position, EanUpcEncoder.NormalGuard, EanUpcEncoder.NormalGuardWidth);
        for (int i = 1; i <= 6; i++)
        {
            ReadOnlySpan<byte> numberSet = ((parity >> (6 - i)) & 1) == 0 ? EanUpcEncoder.NumberSetA : EanUpcEncoder.NumberSetB;
            EanUpcEncoder.AppendCharacter(modules, ref position, numberSet, text[i]);
        }

        EanUpcEncoder.AppendPattern(modules, ref position, SpecialGuard, SpecialGuardWidth);

        EanUpcEncoder.BeginRuns(modules, options.XDimension ?? EanUpcEncoder.NominalXDimension, ref symbol, out Span<float> heights);
        float barHeight = options.ResolveBarHeight(symbol.XDimension, EanUpcEncoder.NominalBarHeight);
        EanUpcEncoder.FillGuardedHeights(heights, barHeight, GuardBars, options);

        // ISO/IEC 15420 prints the number system digit in the leading quiet zone, the check digit in the trailing quiet zone and every other digit below its
        // own symbol character. Digits hang one module below the digit bars and flow past the extended guard bars, as in the nominal symbol drawing.
        if (options.Font is not null)
        {
            float textLine = barHeight + BarcodeTextPlacement.Clearance;
            symbol.ReserveText(8, 8);
            symbol.AddText(text.AsSpan(0, 1), -9F, -2F, BarcodeTextSide.BelowBars, textLine, EanUpcEncoder.QuietZoneDigitScale);
            EanUpcEncoder.AddDigits(text.AsSpan(1, 6), 3F, 7F, BarcodeTextSide.BelowBars, textLine, ref symbol);
            symbol.AddText([(char)('0' + check)], 52F, 59F, BarcodeTextSide.BelowBars, textLine, EanUpcEncoder.QuietZoneDigitScale);
        }

        symbol.EndLinear(LeadingQuietZone, TrailingQuietZone);
    }

    /// <summary>
    /// Expands the compressed digits to the eleven data digits of the equivalent UPC-A number using the zero suppression rules of ISO/IEC 15420. The last
    /// compressed digit selects the pattern.
    /// </summary>
    /// <param name="text">The number system digit followed by the six compressed digits.</param>
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
