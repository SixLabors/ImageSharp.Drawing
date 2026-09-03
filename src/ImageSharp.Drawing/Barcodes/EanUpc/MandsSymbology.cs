// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Marks &amp; Spencer in-house symbology. It is an EAN-8 symbol that holds the centre guard bars at
/// digit bar height and prints the letters M and S in the quiet zones. It has no public specification.
/// <para>
/// A zero pads a seven character number to eight. The printed interpretation shows neither that leading
/// zero nor the check digit.
/// </para>
/// </summary>
public sealed class MandsSymbology : BarcodeSymbology
{
    /// <summary>
    /// The quiet zone in modules. Wider than the EAN-8 minimum of seven so the M and S letters, placed
    /// twelve modules before and two modules after the symbol, stay inside the reserved area.
    /// </summary>
    private const int QuietZone = 12;

    /// <summary>
    /// Initializes a new instance of the <see cref="MandsSymbology"/> class.
    /// </summary>
    public MandsSymbology()
    {
    }

    /// <inheritdoc/>
    public override float NominalXDimension => EanUpcEncoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        if (text.Length != 7 && text.Length != 8)
        {
            throw new ArgumentException($"An M&S barcode must be 7 or 8 characters; got {text.Length}.", nameof(text));
        }

        // An M&S number is seven or eight characters, and a seven character one carries an implied
        // leading zero, so the padded form and its check digit are both built on the stack.
        bool padded = text.Length == 7;
        Span<char> paddedBuffer = stackalloc char[8];
        if (padded)
        {
            paddedBuffer[0] = '0';
            text.CopyTo(paddedBuffer[1..]);
        }
        else
        {
            text.CopyTo(paddedBuffer);
        }

        Span<char> digitBuffer = stackalloc char[8];
        ReadOnlySpan<char> digits = EanUpcEncoder.ValidateAndApplyCheckDigit(paddedBuffer[..(padded ? 8 : text.Length)], 7, digitBuffer);

        LinearBarcodeSymbol ean8 = EanUpcEncoder.BuildEan8(digits, options);

        // The centre guard bars stay at digit bar height; only the outer guards extend beside the text.
        float digitHeight = ean8.BarHeights[2];
        ean8.BarHeights[10] = digitHeight;
        ean8.BarHeights[11] = digitHeight;

        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null)
        {
            // A padded number hides its leading zero and its check digit: seven digits spread over the first
            // seven character cells with the eighth cell left empty. The full eight digit form prints as-is.
            float textLine = digitHeight + BarcodeTextPlacement.Clearance;
            int shown = padded ? 7 : 8;
            placements = new BarcodeTextPlacement[shown + 2];
            for (int i = 0; i < shown; i++)
            {
                float left = i < 4 ? 3F + (i * 7F) : 36F + ((i - 4) * 7F);
                placements[i] = new BarcodeTextPlacement(EanUpcEncoder.DigitString(digits[padded ? i + 1 : i]), left, left + 7F, BarcodeTextSide.BelowBars, textLine);
            }

            placements[shown] = new BarcodeTextPlacement("M", -12F, -5F, BarcodeTextSide.BelowBars, textLine);
            placements[shown + 1] = new BarcodeTextPlacement("S", 69F, 76F, BarcodeTextSide.BelowBars, textLine);
        }

        return new LinearBarcodeSymbol(ean8.RunWidths, ean8.BarHeights, ean8.BarTops, placements, QuietZone, QuietZone);
    }
}
