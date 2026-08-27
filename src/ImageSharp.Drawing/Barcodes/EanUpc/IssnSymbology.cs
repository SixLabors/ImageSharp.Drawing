// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The ISSN data layer over EAN-13. An International Standard Serial Number (ISO 3297) renders as an EAN-13
/// symbol in the GS1 977 prefix range, with the ISSN itself printed above the bars. Input is the standard
/// NNNN-NNNC form where the check character C is optional and X represents ten, optionally followed by a
/// space and a two digit sequence variant that distinguishes issue level variations; the variant defaults
/// to 00. The EAN-13 form is 977, the seven ISSN data digits, the sequence variant and the EAN check digit;
/// the ISSN check character itself is not encoded in the bars.
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
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        string issn = text;
        string variant = "00";
        int space = text.IndexOf(' ');
        if (space >= 0)
        {
            issn = text.Substring(0, space);
            variant = text.Substring(space + 1);
        }

        if (variant.Length != 2 || variant[0] is < '0' or > '9' || variant[1] is < '0' or > '9')
        {
            throw new ArgumentException("The ISSN sequence variant must be exactly two digits.", nameof(text));
        }

        if ((issn.Length != 8 && issn.Length != 9) || issn[4] != '-')
        {
            throw new ArgumentException("ISSN requires the form NNNN-NNN with an optional check character.", nameof(text));
        }

        for (int i = 0; i < issn.Length; i++)
        {
            char c = issn[i];
            bool checkX = c == 'X' && i == 8;
            if (c is (< '0' or > '9') && i != 4 && !checkX)
            {
                throw new ArgumentException($"ISSN accepts only digits, one hyphen and a trailing X check character; got '{c}'.", nameof(text));
            }
        }

        // ISO 3297 check: weights 8 down to 2 over the seven data digits, modulus 11, X representing ten.
        string digits7 = string.Concat(issn.AsSpan(0, 4), issn.AsSpan(5, 3));
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

        string ean12 = "977" + digits7 + variant;
        string digits = ean12 + (char)('0' + EanUpcEncoder.ComputeCheckDigit(ean12));
        char checkChar = check == 10 ? 'X' : (char)('0' + check);
        string? caption = options.Font is null ? null : $"ISSN {issn.Substring(0, 8)}{checkChar}";
        return Ean13Symbology.EncodeDigits(digits, options, caption);
    }
}
