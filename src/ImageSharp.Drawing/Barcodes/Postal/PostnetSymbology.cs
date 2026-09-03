// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

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
    public override float NominalXDimension => UspsPostalEncoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        if (text.Length is not (5 or 9 or 11))
        {
            throw new ArgumentException($"POSTNET carries 5, 9 or 11 digits; got {text.Length} characters.", nameof(text));
        }

        UspsPostalEncoder.ValidateDigits(text);

        Span<char> digits = stackalloc char[text.Length + 1];
        text.AsSpan().CopyTo(digits);
        digits[^1] = (char)('0' + UspsPostalEncoder.CorrectionDigit(text));
        string readable = options.Font is null ? string.Empty : text;
        return UspsPostalEncoder.BuildSymbol(digits, readable, options, true);
    }
}
