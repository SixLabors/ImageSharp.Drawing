// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The ISBN data layer over EAN-13. An International Standard Book Number (ISO 2108) renders as an EAN-13
/// symbol in the GS1 978 or 979 prefix range, with the ISBN itself printed above the bars. Input is a
/// hyphenated or plain ISBN-13, or an ISBN-10 whose modulus 11 check digit (X representing ten) is verified
/// before the number converts to its 978 prefixed EAN-13 form. The check digit in either form is optional
/// and is computed when absent.
/// </summary>
public sealed class IsbnSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IsbnSymbology"/> class.
    /// </summary>
    public IsbnSymbology()
    {
    }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        string compact = text.Replace("-", string.Empty);
        for (int i = 0; i < compact.Length; i++)
        {
            char c = compact[i];
            bool checkX = c == 'X' && compact.Length == 10 && i == 9;
            if ((c is < '0' or > '9') && !checkX)
            {
                throw new ArgumentException($"ISBN accepts only digits, hyphens and a trailing X check character; got '{c}'.", nameof(text));
            }
        }

        string ean12;
        string captionBody;
        if (compact.Length is 12 or 13)
        {
            if (!compact.StartsWith("978", StringComparison.Ordinal) && !compact.StartsWith("979", StringComparison.Ordinal))
            {
                throw new ArgumentException("An ISBN-13 must start with the 978 or 979 prefix.", nameof(text));
            }

            if (compact.Length == 13 && compact[12] - '0' != EanUpcEncoder.ComputeCheckDigit(compact.AsSpan(0, 12)))
            {
                throw new ArgumentException("Incorrect ISBN-13 check digit.", nameof(text));
            }

            ean12 = compact.Substring(0, 12);
            captionBody = EanUpcEncoder.TakeHyphenatedPrefix(text, 12);
        }
        else if (compact.Length is 9 or 10)
        {
            // ISO 2108 ISBN-10 check: weights 10 down to 2 over the nine data digits, modulus 11,
            // with X representing the value ten.
            int sum = 0;
            for (int i = 0; i < 9; i++)
            {
                sum += (10 - i) * (compact[i] - '0');
            }

            int check = (11 - (sum % 11)) % 11;
            if (compact.Length == 10)
            {
                int provided = compact[9] == 'X' ? 10 : compact[9] - '0';
                if (provided != check)
                {
                    throw new ArgumentException("Incorrect ISBN-10 check digit.", nameof(text));
                }
            }

            ean12 = string.Concat("978", compact.AsSpan(0, 9));
            captionBody = "978-" + EanUpcEncoder.TakeHyphenatedPrefix(text, 9);
        }
        else
        {
            throw new ArgumentException($"ISBN requires 9, 10, 12 or 13 digits; got {compact.Length}.", nameof(text));
        }

        // The caption always carries the EAN-13 check digit of the encoded form, so the printed number and
        // the symbol agree even when an ISBN-10 was supplied.
        int eanCheck = EanUpcEncoder.ComputeCheckDigit(ean12);
        string digits = ean12 + (char)('0' + eanCheck);
        string? caption = options.Font is null ? null : $"ISBN {captionBody}-{(char)('0' + eanCheck)}";
        return EanUpcEncoder.BuildEan13(digits, options, caption);
    }
}
