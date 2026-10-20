// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// EAN-14, a GS1-128 symbol that carries one element string: the Global Trade Item Number of GS1 Application Identifier (01). Section 3.3.2 of the GS1 General
/// Specifications gives that identifier a 14 digit GTIN. Table 7-6 gives the element string a total length of 16, and section 7.9 defines the check digit.
/// The symbol specification tables of section 5.12.3 give GS1-128 X dimensions from 0.170 to 1.016 millimetres, and an X dimension outside that range is
/// clamped to it.
/// <para>
/// The input is the element string syntax, <c>(01)</c> and then the GTIN. Spaces are ignored. Give 13 digits, and this class calculates the check digit. Give
/// 14 digits, and this class compares the last digit with the calculated one. The interpretation prints the number as the caller grouped it. A
/// calculated check digit follows a space when the input groups its digits with spaces, which section 4.14 rule 2.a.v permits, and follows the digits
/// directly otherwise.
/// </para>
/// </summary>
public sealed class Ean14Symbology : BarcodeSymbology
{
    /// <summary>
    /// The length of the GTIN of Application Identifier (01), section 3.3.2.
    /// </summary>
    private const int GtinLength = 14;

    /// <summary>
    /// The length of the element string without grouping spaces: the four characters of <c>(01)</c> and the GTIN.
    /// </summary>
    private const int ElementStringLength = 4 + GtinLength;

    /// <summary>
    /// The longest input. The caption repeats the input, so the input is bounded too: at most one grouping space after each character of the element string.
    /// </summary>
    private const int MaximumInputLength = ElementStringLength * 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="Ean14Symbology"/> class.
    /// </summary>
    public Ean14Symbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumInputLength, nameof(text));

        InlineArray18<char> compactStorage = default;
        Span<char> compact = compactStorage;
        int length = 0;
        foreach (char character in text)
        {
            if (character == ' ')
            {
                continue;
            }

            if (length == compact.Length)
            {
                throw new ArgumentException("EAN-14 takes the (01) application identifier and 13 or 14 digits.", nameof(text));
            }

            compact[length++] = character;
        }

        if (length is not (ElementStringLength - 1 or ElementStringLength))
        {
            throw new ArgumentException($"EAN-14 takes the (01) application identifier and 13 or 14 digits; got {length} characters.", nameof(text));
        }

        if (!compact[..4].SequenceEqual("(01)"))
        {
            throw new ArgumentException("EAN-14 begins with the (01) application identifier.", nameof(text));
        }

        ReadOnlySpan<char> supplied = compact[4..length];
        BarcodeSymbology.ValidateDigits(supplied);

        int check = Gs1Data.ComputeCheckDigit(supplied[..(GtinLength - 1)]);
        if (supplied.Length == GtinLength && supplied[^1] - '0' != check)
        {
            throw new ArgumentException($"Incorrect EAN-14 check digit: expected {check}, got {supplied[^1]}.", nameof(text));
        }

        // The symbol carries the identifier and the fourteen digits with no separator, because Table 7-6 gives this element string a predefined length.
        InlineArray16<char> encodedStorage = default;
        Span<char> encoded = encodedStorage;
        "01".CopyTo(encoded);
        supplied[..(GtinLength - 1)].CopyTo(encoded[2..]);
        encoded[^1] = (char)('0' + check);

        // The caption is the input as given, with the check digit after it when the input left it out, set off by a space when the input groups its
        // digits with spaces.
        char checkCharacter = (char)('0' + check);
        ReadOnlySpan<char> suffix = supplied.Length == GtinLength ? [] : text.Contains(' ') ? [' ', checkCharacter] : [checkCharacter];
        Code128Encoder.BuildSymbol(
            encoded,
            true,
            [],
            text,
            suffix,
            Gs1Data.Gs1128MinimumXDimension,
            Gs1Data.Gs1128MaximumXDimension,
            Code128Linkage.None,
            options,
            ref symbol);
    }
}
