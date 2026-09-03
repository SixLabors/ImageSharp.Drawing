// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The PLANET symbology, the Postal Alpha Numeric Encoding Technique of the United States Postal
/// Service. It carries 11 or 13 digits with the bars of POSTNET inverted: every digit is five bars of
/// which two are half bars, the symbol carries a correction digit that makes the digit sum a multiple
/// of 10, and a full frame bar stands at each end. The dimensions are those of section 708.4.2.5 of the
/// Domestic Mail Manual. The printed line shows the digits as given.
/// </summary>
public sealed class PlanetSymbology : BarcodeSymbology
{
    /// <inheritdoc/>
    public override float NominalXDimension => UspsPostalEncoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        if (text.Length is not (11 or 13))
        {
            throw new ArgumentException($"PLANET carries 11 or 13 digits; got {text.Length} characters.", nameof(text));
        }

        UspsPostalEncoder.ValidateDigits(text);

        Span<char> digits = stackalloc char[text.Length + 1];
        text.AsSpan().CopyTo(digits);
        digits[^1] = (char)('0' + UspsPostalEncoder.CorrectionDigit(text));
        string readable = options.Font is null ? string.Empty : text;
        return UspsPostalEncoder.BuildSymbol(digits, readable, options, false);
    }
}
