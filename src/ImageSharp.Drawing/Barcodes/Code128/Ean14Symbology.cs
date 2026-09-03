// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// EAN-14, a GS1-128 symbol that carries one element string: the Global Trade Item Number of GS1
/// Application Identifier (01). Section 3.3.2 of the GS1 General Specifications gives that identifier a
/// 14 digit GTIN. Table 7-6 gives the element string a total length of 16, and section 7.9 defines the
/// check digit.
/// <para>
/// The input is the element string syntax, <c>(01)</c> and then the GTIN. Spaces are ignored. Give 13
/// digits, and this class calculates the check digit. Give 14 digits, and this class compares the last
/// digit with the calculated one. The interpretation prints the number as the caller grouped it, with a
/// space before a calculated check digit.
/// </para>
/// </summary>
public sealed class Ean14Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Ean14Symbology"/> class.
    /// </summary>
    public Ean14Symbology()
    {
    }

    /// <inheritdoc/>
    public override float NominalXDimension => Code128Encoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        // The element string is "(01)" and 14 digits, so the longest input is 18 characters once the
        // spaces the caller may group the number with are dropped.
        Span<char> compact = stackalloc char[18];
        int length = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
            {
                continue;
            }

            if (length == compact.Length)
            {
                throw new ArgumentException("EAN-14 takes the (01) application identifier and 13 or 14 digits.", nameof(text));
            }

            compact[length++] = text[i];
        }

        if (length is not (17 or 18))
        {
            throw new ArgumentException(
                $"EAN-14 takes the (01) application identifier and 13 or 14 digits; got {length} characters.",
                nameof(text));
        }

        if (!compact[..4].SequenceEqual("(01)"))
        {
            throw new ArgumentException("EAN-14 begins with the (01) application identifier.", nameof(text));
        }

        ReadOnlySpan<char> supplied = compact[4..length];
        SpanCodePointEnumerator suppliedPoints = supplied.EnumerateCodePoints();
        while (suppliedPoints.MoveNext())
        {
            CodePoint current = suppliedPoints.Current;
            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException(
                    $"EAN-14 carries only digits after its application identifier; got {current.ToDisplayString()}.",
                    nameof(text));
            }
        }

        int check = EanUpcEncoder.ComputeCheckDigit(supplied[..13]);
        if (supplied.Length == 14 && supplied[13] - '0' != check)
        {
            throw new ArgumentException($"Incorrect EAN-14 check digit: expected {check}, got {supplied[13]}.", nameof(text));
        }

        // The symbol carries the identifier and the fourteen digits with no separator, because Table 7-6
        // gives this element string a predefined length.
        Span<char> encoded = stackalloc char[16];
        "01".CopyTo(encoded);
        supplied[..13].CopyTo(encoded[2..]);
        encoded[15] = (char)('0' + check);

        string caption = options.Font is null
            ? string.Empty
            : supplied.Length == 14 ? text : $"{text} {(char)('0' + check)}";

        return Code128Encoder.BuildSymbol(
            Code128Encoder.Encode(encoded, true),
            caption,
            options);
    }
}
