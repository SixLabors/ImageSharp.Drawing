// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The UPC-E symbology, which ISO/IEC 15420 specifies. UPC-E is the zero suppression form of UPC-A. A UPC-A
/// number compresses to six digits when its manufacturer and product codes match one of four zero patterns.
/// <para>
/// The symbol is 51 modules wide: a normal guard pattern, six symbol characters from number sets A and B,
/// and a special right guard pattern. The number set parity of the six characters carries the number system
/// digit (0 or 1) and the check digit of the expanded UPC-A number.
/// </para>
/// </summary>
public sealed class UpcESymbology : BarcodeSymbology
{
    private const int Width = 51;
    private const int BarCount = 17;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpcESymbology"/> class.
    /// </summary>
    public UpcESymbology()
    {
    }

    /// <summary>
    /// Gets the indexes of the extended bars: the two bars of the left guard pattern and the three bars of
    /// the special right guard pattern.
    /// </summary>
    private static ReadOnlySpan<int> GuardBars => [0, 1, 14, 15, 16];

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        if (text.Length != 7 && text.Length != 8)
        {
            throw new ArgumentException(
                $"UPC-E requires a number system digit and 6 data digits with an optional check digit; got {text.Length} characters.",
                nameof(text));
        }

        EanUpcEncoder.ValidateDigits(text);

        int numberSystem = text[0] - '0';
        if (numberSystem is not 0 and not 1)
        {
            throw new ArgumentException("UPC-E supports only the number systems 0 and 1.", nameof(text));
        }

        // The check digit is computed over the expanded UPC-A number, not the compressed digits, per the
        // zero suppression rules of ISO/IEC 15420.
        Span<char> expanded = stackalloc char[11];
        int check = EanUpcEncoder.ComputeCheckDigit(Expand(text, expanded));
        if (text.Length == 8 && text[7] - '0' != check)
        {
            throw new ArgumentException($"UPC-E check digit mismatch: expected {check}, got {text[7]}.", nameof(text));
        }

        int parity = EanUpcEncoder.UpcEParity[check];
        if (numberSystem == 1)
        {
            parity = ~parity & 0b111111;
        }

        Span<byte> modules = stackalloc byte[Width];
        int position = 0;
        EanUpcEncoder.AppendPattern(modules, ref position, 0b101, 3);
        for (int i = 1; i <= 6; i++)
        {
            ReadOnlySpan<byte> numberSet = ((parity >> (6 - i)) & 1) == 0 ? EanUpcEncoder.NumberSetA : EanUpcEncoder.NumberSetB;
            EanUpcEncoder.AppendPattern(modules, ref position, numberSet[text[i] - '0'], 7);
        }

        EanUpcEncoder.AppendPattern(modules, ref position, 0b010101, 6);

        float barHeight = EanUpcEncoder.ResolveBarHeight(options, EanUpcEncoder.NominalBarHeight);
        EanUpcEncoder.BuildGuardedHeights(BarCount, barHeight, GuardBars, options, out float[] heights, out float[] tops);

        // ISO/IEC 15420 prints the number system digit in the leading quiet zone, the check digit in the
        // trailing quiet zone and every other digit below its own symbol character. Digits hang one module below the
        // digit bars and flow past the extended guard bars, as in the nominal symbol drawing.
        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null)
        {
            float textLine = barHeight;
            placements = new BarcodeTextPlacement[8];
            placements[0] = new BarcodeTextPlacement(EanUpcEncoder.DigitString(text[0]), -9F, -2F, BarcodeTextSide.BelowBars, textLine, EanUpcEncoder.QuietZoneDigitScale);
            EanUpcEncoder.FillDigitPlacements(placements, 1, text, 1, 6, 3F, 7F, BarcodeTextSide.BelowBars, textLine);
            placements[7] = new BarcodeTextPlacement(EanUpcEncoder.DigitString((char)('0' + check)), 52F, 59F, BarcodeTextSide.BelowBars, textLine, EanUpcEncoder.QuietZoneDigitScale);
        }

        return new LinearBarcodeSymbol(EanUpcEncoder.ToRuns(modules), heights, tops, placements, 9, 7);
    }

    /// <summary>
    /// Expands the compressed digits to the eleven data digits of the equivalent UPC-A number using the
    /// zero suppression rules of ISO/IEC 15420. The last compressed digit selects the pattern.
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
