// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The PLANET symbology, the Postal Alpha Numeric Encoding Technique of the United States Postal Service. It carries 11 or 13 digits with the bars of POSTNET
/// inverted: every digit is five bars of which two are half bars, the symbol carries a correction digit that makes the digit sum a multiple of 10, and a full
/// frame bar stands at each end. The dimensions are those of section 708.4.2.5 of the Domestic Mail Manual. The symbol prints no text line, because the
/// Domestic Mail Manual defines none.
/// </summary>
public sealed class PlanetSymbology : BarcodeSymbology
{
    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (text.Length is not (11 or 13))
        {
            throw new ArgumentException($"PLANET carries 11 or 13 digits; got {text.Length} characters.", nameof(text));
        }

        BarcodeSymbology.ValidateDigits(text);

        // At most thirteen digits and the correction digit.
        InlineArray14<char> digitStorage = default;
        Span<char> digits = ((Span<char>)digitStorage)[..(text.Length + 1)];
        text.CopyTo(digits);
        digits[^1] = (char)('0' + UspsPostalEncoder.CorrectionDigit(text));
        UspsPostalEncoder.BuildSymbol(digits, options, false, ref symbol);
    }
}
