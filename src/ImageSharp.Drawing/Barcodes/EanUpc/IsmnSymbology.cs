// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The ISMN data layer over EAN-13. An International Standard Music Number (ISO 10957) draws as an EAN-13 symbol in the GS1 979-0 range. The ISMN itself prints
/// above the bars.
/// <para>
/// The input is a thirteen digit ISMN that starts 9790, with or without hyphens. The older ten character form that starts with M is also accepted, and 9790
/// replaces that M. ISO 10957:2009 makes the check digit of both forms the EAN-13 check digit of the thirteen digit number. The check digit is optional, and
/// this class calculates it when the input carries none.
/// </para>
/// <para>
/// The printed ISMN separates its four elements with hyphens. The publisher element ranges of the ISMN Users' Manual give the length of the publisher
/// element, so the hyphens of the input do not change the printed ISMN.
/// </para>
/// </summary>
public sealed class IsmnSymbology : BarcodeSymbology
{
    /// <summary>
    /// The smallest size of the caption in points. Section 7 of the ISMN Users' Manual, 2008: the ISMN "should always be printed in easily legible type (9
    /// point or larger)."
    /// </summary>
    private const float MinimumCaptionSize = 9F;

    /// <summary>
    /// Initializes a new instance of the <see cref="IsmnSymbology"/> class.
    /// </summary>
    public IsmnSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        // An ISMN is at most seventeen characters: thirteen digits and four hyphens. Longer text is rejected before any buffer, so the hyphenless form fits
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

        ReadOnlySpan<char> compact = compactBuffer[..compactLength];
        bool mForm = compact.Length > 0 && compact[0] == 'M';
        ReadOnlySpan<char> body = mForm ? compact[1..] : compact;
        BarcodeSymbology.ValidateDigits(body);

        // The encoded form is thirteen digits: twelve data digits and the check digit the caption repeats. ISO 10957:2009 displays an ISMN only in its 979-0
        // form, so the caption converts the older M prefix rather than echoing it.
        InlineArray14<char> digitStorage = default;
        Span<char> digits = ((Span<char>)digitStorage)[..13];
        if (mForm && body.Length is 8 or 9)
        {
            "9790".CopyTo(digits);
            body[..8].CopyTo(digits[4..]);
        }
        else if (!mForm && body.Length is 12 or 13 && body.StartsWith("9790", StringComparison.Ordinal))
        {
            body[..12].CopyTo(digits);
        }
        else
        {
            throw new ArgumentException("ISMN requires M plus 8 digits or the 13 digit 9790 form, with an optional check digit.", nameof(text));
        }

        int check = Gs1Data.ComputeCheckDigit(digits[..12]);
        bool hasCheck = (mForm && body.Length == 9) || (!mForm && body.Length == 13);
        if (hasCheck && body[^1] - '0' != check)
        {
            throw new ArgumentException("Incorrect ISMN check digit.", nameof(text));
        }

        digits[12] = (char)('0' + check);
        if (options.Font is null)
        {
            EanUpcEncoder.BuildEan13(digits, options, ref symbol);
        }
        else
        {
            // Section 2.1 of the ISMN Users' Manual, 2008: "When the ISMN is printed, each element is separated by a hyphen or space." Section 2.2 gives the
            // publisher element the ranges 000 - 099, 1000 - 3999, 40000 - 69999, 700000 - 899999 and 9000000 - 9999999, so its first digit gives its
            // length. The item element takes the rest of the eight digits after 979-0.
            ReadOnlySpan<char> number = digits[4..12];
            int publisherLength = number[0] switch
            {
                '0' => 3,
                <= '3' => 4,
                <= '6' => 5,
                <= '8' => 6,
                _ => 7,
            };

            InlineArray10<char> captionStorage = default;
            Span<char> captionBody = ((Span<char>)captionStorage)[..9];
            number[..publisherLength].CopyTo(captionBody);
            captionBody[publisherLength] = '-';
            number[publisherLength..].CopyTo(captionBody[(publisherLength + 1)..]);
            EanUpcEncoder.BuildEan13(digits, options, "ISMN 979-0-", captionBody, ['-', digits[12]], ref symbol);
            symbol.SetCaptionMinimumSize(MinimumCaptionSize);
        }
    }
}
