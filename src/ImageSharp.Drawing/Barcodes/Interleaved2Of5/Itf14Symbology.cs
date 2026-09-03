// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The ITF-14 symbology, which section 5.3 of the GS1 General Specifications defines as an Interleaved 2
/// of 5 symbol inside a bearer bar. Section 5.3.1 fixes the data at 14 digits, and section 5.3.2.1.3
/// requires the check digit of section 7.9. The input is 13 digits, and the encoder calculates the check
/// digit, or 14 digits, and the encoder validates it. Section 4.14 rule 2.a permits spaces in the human
/// readable interpretation and forbids them in the symbol, so the input can carry spaces, which the
/// printed line keeps and the symbol drops.
/// <para>
/// Section 5.3.2.4 requires the bearer bar, which for plate printing "has a constant thickness of 4.83
/// millimetres (0.190 inch) and must completely surround the symbol, including its Quiet Zones and butt
/// directly against the top and bottom of the bars". Section 5.3.2.2 gives the target X of 1.016
/// millimetres and the 10X quiet zones, and the symbol height table of section 5.12.3.2 gives a minimum
/// height of 31.75 millimetres. A calculated check digit prints after the data, and after a space when
/// the input carries spaces, as Figure 5-32 shows.
/// </para>
/// </summary>
public sealed class Itf14Symbology : BarcodeSymbology
{
    /// <summary>
    /// The number of digits the symbol carries, the check digit included. Section 5.3.1: "Data string
    /// length encodable: fixed length at 14 digits."
    /// </summary>
    private const int Digits = 14;

    /// <summary>
    /// The nominal X dimension in millimetres: the 1.016 millimetre target X of section 5.3.2.2.
    /// </summary>
    private const float XDimension = 1.016F;

    /// <summary>
    /// The bar height in modules when the caller sets none: the 31.75 millimetre minimum of the symbol
    /// height table in section 5.12.3.2, footnote (****), at the target X.
    /// </summary>
    private const float NominalBarHeight = 31.75F / XDimension;

    /// <summary>
    /// The bearer bar thickness in modules: the 4.83 millimetres of section 5.3.2.4 at the target X.
    /// </summary>
    private const float BearerBarThickness = 4.83F / XDimension;

    /// <inheritdoc/>
    public override float NominalXDimension => XDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));

        Span<char> digits = stackalloc char[Digits];
        int count = 0;
        bool hasSpaces = false;
        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (current.Value == ' ')
            {
                hasSpaces = true;
                continue;
            }

            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException($"ITF-14 carries only digits; got {current.ToDisplayString()}.", nameof(text));
            }

            if (count == Digits)
            {
                throw new ArgumentException($"ITF-14 carries 13 digits and a check digit; got more than {Digits}.", nameof(text));
            }

            digits[count++] = (char)current.Value;
        }

        if (count != Digits - 1 && count != Digits)
        {
            throw new ArgumentException($"ITF-14 carries 13 digits and a check digit; got {count}.", nameof(text));
        }

        int check = EanUpcEncoder.ComputeCheckDigit(digits[..(Digits - 1)]);
        if (count == Digits && digits[Digits - 1] - '0' != check)
        {
            throw new ArgumentException($"Incorrect check digit: expected {check}, got {digits[Digits - 1]}.", nameof(text));
        }

        digits[Digits - 1] = (char)('0' + check);

        string readable = string.Empty;
        if (options.Font is not null)
        {
            readable = count == Digits
                ? text
                : hasSpaces
                    ? $"{text} {digits[Digits - 1]}"
                    : $"{text}{digits[Digits - 1]}";
        }

        return Interleaved2Of5Encoder.BuildSymbol(Interleaved2Of5Encoder.Encode(digits), readable, options, XDimension, NominalBarHeight, BearerBarThickness);
    }
}
