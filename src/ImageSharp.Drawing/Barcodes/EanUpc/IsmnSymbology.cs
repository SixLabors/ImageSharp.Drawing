// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The ISMN data layer over EAN-13. An International Standard Music Number (ISO 10957) renders as an EAN-13
/// symbol in the GS1 979-0 range, with the ISMN itself printed above the bars. Input is a hyphenated or
/// plain thirteen digit ISMN starting 9790, or the older ten character form starting with M, which converts
/// by replacing M with 9790. ISO 10957:2009 defines the check digit of both forms as the EAN-13 check digit
/// of the thirteen digit number; the check digit is optional and is computed when absent.
/// </summary>
public sealed class IsmnSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IsmnSymbology"/> class.
    /// </summary>
    public IsmnSymbology()
    {
    }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        string compact = text.Replace("-", string.Empty);
        bool mForm = compact.Length > 0 && compact[0] == 'M';
        string body = mForm ? compact.Substring(1) : compact;
        foreach (char c in body)
        {
            if (c is < '0' or > '9')
            {
                throw new ArgumentException($"ISMN accepts only digits, hyphens and a leading M; got '{c}'.", nameof(text));
            }
        }

        string ean12;
        string captionBody;
        if (mForm && body.Length is 8 or 9)
        {
            // ISO 10957:2009 displays an ISMN only in its 979-0 form, so the caption converts the older
            // M prefix rather than echoing it.
            ean12 = string.Concat("9790", body.AsSpan(0, 8));
            string remainder = text[1] == '-' ? text.Substring(2) : text.Substring(1);
            captionBody = "979-0-" + EanUpcEncoder.TakeHyphenatedPrefix(remainder, 8);
        }
        else if (!mForm && body.Length is 12 or 13 && body.StartsWith("9790", StringComparison.Ordinal))
        {
            ean12 = body.Substring(0, 12);
            captionBody = EanUpcEncoder.TakeHyphenatedPrefix(text, 12);
        }
        else
        {
            throw new ArgumentException("ISMN requires M plus 8 digits or the 13 digit 9790 form, with an optional check digit.", nameof(text));
        }

        int check = EanUpcEncoder.ComputeCheckDigit(ean12);
        bool hasCheck = (mForm && body.Length == 9) || (!mForm && body.Length == 13);
        if (hasCheck && body[body.Length - 1] - '0' != check)
        {
            throw new ArgumentException("Incorrect ISMN check digit.", nameof(text));
        }

        string digits = ean12 + (char)('0' + check);
        string? caption = options.Font is null ? null : $"ISMN {captionBody}-{(char)('0' + check)}";
        return Ean13Symbology.EncodeDigits(digits, options, caption);
    }
}
