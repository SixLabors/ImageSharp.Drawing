// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The ISMN data layer over EAN-13. An International Standard Music Number (ISO 10957) draws as an EAN-13
/// symbol in the GS1 979-0 range. The ISMN itself prints above the bars.
/// <para>
/// The input is a thirteen digit ISMN that starts 9790, with or without hyphens. The older ten character
/// form that starts with M is also accepted, and 9790 replaces that M. ISO 10957:2009 makes the check digit
/// of both forms the EAN-13 check digit of the thirteen digit number. The check digit is optional, and this
/// class calculates it when the input carries none.
/// </para>
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
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        // An ISMN is at most seventeen characters: thirteen digits and four hyphens. Longer text is
        // rejected before any buffer, so the hyphenless form fits eighteen inline characters.
        Guard.MustBeLessThanOrEqualTo(text.Length, 17, nameof(text));
        InlineArray18<char> compactStorage = default;
        Span<char> compactBuffer = compactStorage;
        int compactLength = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '-')
            {
                compactBuffer[compactLength++] = text[i];
            }
        }

        ReadOnlySpan<char> compact = compactBuffer[..compactLength];
        bool mForm = compact.Length > 0 && compact[0] == 'M';
        ReadOnlySpan<char> body = mForm ? compact[1..] : compact;
        SpanCodePointEnumerator codePoints = body.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException($"ISMN accepts only digits, hyphens and a leading M; got {current.ToDisplayString()}.", nameof(text));
            }
        }

        // The encoded form is thirteen digits: twelve data digits and the check digit the caption repeats.
        InlineArray14<char> digitStorage = default;
        Span<char> digits = ((Span<char>)digitStorage)[..13];
        ReadOnlySpan<char> captionPrefix;
        ReadOnlySpan<char> captionBody;
        if (mForm && body.Length is 8 or 9)
        {
            // ISO 10957:2009 displays an ISMN only in its 979-0 form, so the caption converts the older
            // M prefix rather than echoing it.
            "9790".CopyTo(digits);
            body[..8].CopyTo(digits[4..]);
            captionPrefix = "ISMN 979-0-";
            captionBody = EanUpcEncoder.TakeHyphenatedPrefix(text.AsSpan(text[1] == '-' ? 2 : 1), 8);
        }
        else if (!mForm && body.Length is 12 or 13 && body.StartsWith("9790", StringComparison.Ordinal))
        {
            body[..12].CopyTo(digits);
            captionPrefix = "ISMN ";
            captionBody = EanUpcEncoder.TakeHyphenatedPrefix(text, 12);
        }
        else
        {
            throw new ArgumentException("ISMN requires M plus 8 digits or the 13 digit 9790 form, with an optional check digit.", nameof(text));
        }

        int check = Gs1Data.ComputeCheckDigit(digits[..12]);
        bool hasCheck = (mForm && body.Length == 9) || (!mForm && body.Length == 13);
        if (hasCheck && body[body.Length - 1] - '0' != check)
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
            EanUpcEncoder.BuildEan13(digits, options, captionPrefix, captionBody, ['-', digits[12]], ref symbol);
        }
    }
}
