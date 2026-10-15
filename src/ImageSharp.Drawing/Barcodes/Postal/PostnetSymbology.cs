// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The POSTNET symbology, which section 708.4.2 of the United States Postal Service Domestic Mail Manual
/// defines: "A POSTNET barcode can represent a 5-digit ZIP Code (32 bars), a 9-digit ZIP+4 code (52
/// bars), or an 11-digit delivery point code (62 bars)." Every digit is five bars of which two are full,
/// the symbol carries a correction digit that makes the digit sum a multiple of 10, and a full frame bar
/// stands at each end. The printed line shows the digits as given.
/// </summary>
public sealed class PostnetSymbology : BarcodeSymbology
{
    /// <inheritdoc/>
    public override double NominalXDimension => UspsPostalEncoder.NominalXDimension;

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));
        if (text.Length is not (5 or 9 or 11))
        {
            throw new ArgumentException($"POSTNET carries 5, 9 or 11 digits; got {text.Length} characters.", nameof(text));
        }

        UspsPostalEncoder.ValidateDigits(text);

        // At most eleven digits and the correction digit.
        InlineArray12<char> digitStorage = default;
        Span<char> digits = ((Span<char>)digitStorage)[..(text.Length + 1)];
        text.AsSpan().CopyTo(digits);
        digits[^1] = (char)('0' + UspsPostalEncoder.CorrectionDigit(text));
        UspsPostalEncoder.BuildSymbol(digits, text, options, true, ref symbol);
    }
}
