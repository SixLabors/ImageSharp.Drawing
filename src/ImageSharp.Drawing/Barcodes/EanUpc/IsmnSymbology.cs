// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;

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

        // An ISMN is at most seventeen characters, so the hyphenless form is built on the stack.
        Span<char> compactBuffer = stackalloc char[text.Length];
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
            if (current.Value is < '0' or > '9')
            {
                throw new ArgumentException($"ISMN accepts only digits, hyphens and a leading M; got U+{current.Value:X4}.", nameof(text));
            }
        }

        // The encoded form is thirteen digits: twelve data digits and the check digit the caption repeats.
        Span<char> digits = stackalloc char[13];
        string captionBody;
        if (mForm && body.Length is 8 or 9)
        {
            // ISO 10957:2009 displays an ISMN only in its 979-0 form, so the caption converts the older
            // M prefix rather than echoing it.
            "9790".CopyTo(digits);
            body[..8].CopyTo(digits[4..]);
            captionBody = "979-0-" + EanUpcEncoder.TakeHyphenatedPrefix(text.AsSpan(text[1] == '-' ? 2 : 1), 8);
        }
        else if (!mForm && body.Length is 12 or 13 && body.StartsWith("9790", StringComparison.Ordinal))
        {
            body[..12].CopyTo(digits);
            captionBody = EanUpcEncoder.TakeHyphenatedPrefix(text, 12);
        }
        else
        {
            throw new ArgumentException("ISMN requires M plus 8 digits or the 13 digit 9790 form, with an optional check digit.", nameof(text));
        }

        int check = EanUpcEncoder.ComputeCheckDigit(digits[..12]);
        bool hasCheck = (mForm && body.Length == 9) || (!mForm && body.Length == 13);
        if (hasCheck && body[body.Length - 1] - '0' != check)
        {
            throw new ArgumentException("Incorrect ISMN check digit.", nameof(text));
        }

        digits[12] = (char)('0' + check);
        string? caption = options.Font is null ? null : $"ISMN {captionBody}-{(char)('0' + check)}";
        return EanUpcEncoder.BuildEan13(digits, options, caption);
    }
}
