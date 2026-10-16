// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The ISSN data layer over EAN-13. An International Standard Serial Number (ISO 3297) draws as an EAN-13 symbol in the GS1 977 prefix range. The ISSN itself
/// prints above the bars.
/// <para>
/// The input is the standard NNNN-NNNC form, where the check character C is optional and X stands for ten. A space and a two digit sequence variant can follow
/// it, which separates issue level variations. That variant is 00 when the input carries none.
/// </para>
/// <para>
/// The EAN-13 form is 977, the seven ISSN data digits, the sequence variant and the EAN check digit. The bars do not carry the ISSN check character itself.
/// </para>
/// </summary>
public sealed class IssnSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IssnSymbology"/> class.
    /// </summary>
    public IssnSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        ReadOnlySpan<char> issn = text;
        ReadOnlySpan<char> variant = "00";
        int space = text.IndexOf(' ');
        if (space >= 0)
        {
            issn = text.AsSpan(0, space);
            variant = text.AsSpan(space + 1);
        }

        if (variant.Length != 2 || variant.ContainsAnyExceptInRange('0', '9'))
        {
            throw new ArgumentException("The ISSN sequence variant must be exactly two digits.", nameof(text));
        }

        if (issn.Length is not (8 or 9) || issn[4] != '-')
        {
            throw new ArgumentException("ISSN requires the form NNNN-NNN with an optional check character.", nameof(text));
        }

        // The hyphen at index 4 is checked above. Every other character is a digit, except that the check character at index 8 may be X. The message decodes
        // the code point at the first other character, so a surrogate pair reports as the one character it is.
        for (int i = 0; i < issn.Length; i++)
        {
            char character = issn[i];
            if (i != 4 && !char.IsAsciiDigit(character) && !(i == 8 && character == 'X'))
            {
                throw new ArgumentException(
                    $"ISSN accepts only digits, one hyphen and a trailing X check character; got {issn.CodePointAt(i).ToDisplayString()}.",
                    nameof(text));
            }
        }

        // ISO 3297 check: weights 8 down to 2 over the seven data digits, modulus 11, X representing ten. The encoded form is thirteen digits: the 977 prefix,
        // the seven data digits, the sequence variant and the EAN-13 check digit.
        InlineArray14<char> digitStorage = default;
        Span<char> digits = ((Span<char>)digitStorage)[..13];
        "977".CopyTo(digits);
        issn[..4].CopyTo(digits[3..]);
        issn.Slice(5, 3).CopyTo(digits[7..]);
        variant.CopyTo(digits[10..]);
        ReadOnlySpan<char> digits7 = digits.Slice(3, 7);
        int sum = 0;
        for (int i = 0; i < 7; i++)
        {
            sum += (8 - i) * (digits7[i] - '0');
        }

        int check = (11 - (sum % 11)) % 11;
        if (issn.Length == 9)
        {
            int provided = issn[8] == 'X' ? 10 : issn[8] - '0';
            if (provided != check)
            {
                throw new ArgumentException("Incorrect ISSN check digit.", nameof(text));
            }
        }

        digits[12] = (char)('0' + Gs1Data.ComputeCheckDigit(digits[..12]));
        char checkChar = check == 10 ? 'X' : (char)('0' + check);
        if (options.Font is null)
        {
            EanUpcEncoder.BuildEan13(digits, options, ref symbol);
        }
        else
        {
            EanUpcEncoder.BuildEan13(digits, options, "ISSN ", issn[..8], [checkChar], ref symbol);
        }
    }
}
