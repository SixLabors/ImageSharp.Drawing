// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The ISBN data layer over EAN-13. An International Standard Book Number (ISO 2108) draws as an EAN-13 symbol in the GS1 978 or 979 prefix range. The ISBN
/// itself prints above the bars.
/// <para>
/// The input is an ISBN-13, with or without hyphens, or an ISBN-10. This class compares the modulus 11 check digit of an ISBN-10, where X stands for ten, and
/// then converts the number to its 978 prefixed EAN-13 form. The check digit is optional in either form, and this class calculates it when the input carries
/// none.
/// </para>
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
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        // An ISBN is at most seventeen characters: thirteen digits and four hyphens. Longer text is rejected before any buffer, so the hyphenless form fits
        // eighteen inline characters.
        Guard.MustBeLessThanOrEqualTo(text.Length, 17, nameof(text));
        InlineArray18<char> compactStorage = default;
        Span<char> compactBuffer = compactStorage;
        int compactLength = 0;
        foreach (char character in text)
        {
            if (character != '-')
            {
                compactBuffer[compactLength++] = character;
            }
        }

        // Every character is a digit, except that the last character of an ISBN-10 may be the check character X. The message decodes the code point at the
        // first other character, so a surrogate pair reports as the one character it is.
        ReadOnlySpan<char> compact = compactBuffer[..compactLength];
        int invalid = compact.IndexOfAnyExceptInRange('0', '9');
        if (invalid >= 0 && !(invalid == 9 && compact.Length == 10 && compact[9] == 'X'))
        {
            throw new ArgumentException(
                $"ISBN accepts only digits, hyphens and a trailing X check character; got {compact.CodePointAt(invalid).ToDisplayString()}.",
                nameof(text));
        }

        // The encoded form is thirteen digits: twelve data digits and the check digit the caption repeats.
        InlineArray14<char> digitStorage = default;
        Span<char> digits = ((Span<char>)digitStorage)[..13];
        ReadOnlySpan<char> captionPrefix;
        ReadOnlySpan<char> captionBody;
        if (compact.Length is 12 or 13)
        {
            if (!compact.StartsWith("978", StringComparison.Ordinal) && !compact.StartsWith("979", StringComparison.Ordinal))
            {
                throw new ArgumentException("An ISBN-13 must start with the 978 or 979 prefix.", nameof(text));
            }

            if (compact.Length == 13 && compact[12] - '0' != Gs1Data.ComputeCheckDigit(compact[..12]))
            {
                throw new ArgumentException("Incorrect ISBN-13 check digit.", nameof(text));
            }

            compact[..12].CopyTo(digits);
            captionPrefix = "ISBN ";
            captionBody = EanUpcEncoder.TakeHyphenatedPrefix(text, 12);
        }
        else if (compact.Length is 9 or 10)
        {
            // ISO 2108 ISBN-10 check: weights 10 down to 2 over the nine data digits, modulus 11, with X representing the value ten.
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

            "978".CopyTo(digits);
            compact[..9].CopyTo(digits[3..]);
            captionPrefix = "ISBN 978-";
            captionBody = EanUpcEncoder.TakeHyphenatedPrefix(text, 9);
        }
        else
        {
            throw new ArgumentException($"ISBN requires 9, 10, 12 or 13 digits; got {compact.Length}.", nameof(text));
        }

        // The caption always carries the EAN-13 check digit of the encoded form, so the printed number and the symbol agree even when an ISBN-10 was supplied.
        int eanCheck = Gs1Data.ComputeCheckDigit(digits[..12]);
        digits[12] = (char)('0' + eanCheck);
        if (options.Font is null)
        {
            EanUpcEncoder.BuildEan13(digits, options, ref symbol);
        }
        else
        {
            EanUpcEncoder.BuildEan13(digits, options, captionPrefix, captionBody, ['-', digits[12]], ref symbol);
        }
    }
}
