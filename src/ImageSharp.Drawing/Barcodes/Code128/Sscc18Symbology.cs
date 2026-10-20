// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// SSCC-18, a GS1-128 symbol that carries one element string: the Serial Shipping Container Code of GS1 Application Identifier (00). Section 3.3.1 of the GS1
/// General Specifications gives that identifier an 18 digit SSCC. Table 7-6 gives the element string a total length of 20, and section 7.9 defines the check
/// digit. Table 5-51, symbol specification table 5 for logistic units, gives an X dimension of 0.495 to 0.940 millimetres, and 0.250 millimetre when the
/// logistic unit is too small for the minimum. An X dimension outside 0.250 to 0.940 millimetres is clamped to that range.
/// <para>
/// The input is the element string syntax, <c>(00)</c> and then the SSCC. Spaces are ignored. Give 17 digits, and this class calculates the check digit. Give
/// 18 digits, and this class compares the last digit with the calculated one. The interpretation prints the number as the caller grouped it. A
/// calculated check digit follows a space when the input groups its digits with spaces, which section 4.14 rule 2.a.v permits, and follows the digits
/// directly otherwise.
/// </para>
/// </summary>
public sealed class Sscc18Symbology : BarcodeSymbology
{
    /// <summary>
    /// The length of the SSCC of Application Identifier (00), section 3.3.1.
    /// </summary>
    private const int SsccLength = 18;

    /// <summary>
    /// The length of the element string without grouping spaces: the four characters of <c>(00)</c> and the SSCC.
    /// </summary>
    private const int ElementStringLength = 4 + SsccLength;

    /// <summary>
    /// The longest input. The caption repeats the input, so the input is bounded too: at most one grouping space after each character of the element string.
    /// </summary>
    private const int MaximumInputLength = ElementStringLength * 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="Sscc18Symbology"/> class.
    /// </summary>
    public Sscc18Symbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumInputLength, nameof(text));

        InlineArray24<char> compactStorage = default;
        Span<char> compact = ((Span<char>)compactStorage)[..ElementStringLength];
        int length = 0;
        foreach (char character in text)
        {
            if (character == ' ')
            {
                continue;
            }

            if (length == compact.Length)
            {
                throw new ArgumentException("SSCC-18 takes the (00) application identifier and 17 or 18 digits.", nameof(text));
            }

            compact[length++] = character;
        }

        if (length is not (ElementStringLength - 1 or ElementStringLength))
        {
            throw new ArgumentException($"SSCC-18 takes the (00) application identifier and 17 or 18 digits; got {length} characters.", nameof(text));
        }

        if (!compact[..4].SequenceEqual("(00)"))
        {
            throw new ArgumentException("SSCC-18 begins with the (00) application identifier.", nameof(text));
        }

        ReadOnlySpan<char> supplied = compact[4..length];
        BarcodeSymbology.ValidateDigits(supplied);

        int check = Gs1Data.ComputeCheckDigit(supplied[..(SsccLength - 1)]);
        if (supplied.Length == SsccLength && supplied[^1] - '0' != check)
        {
            throw new ArgumentException($"Incorrect SSCC-18 check digit: expected {check}, got {supplied[^1]}.", nameof(text));
        }

        // The symbol carries the identifier and the eighteen digits with no separator, because Table 7-6 gives this element string a predefined length.
        InlineArray24<char> encodedStorage = default;
        Span<char> encoded = ((Span<char>)encodedStorage)[..(2 + SsccLength)];
        "00".CopyTo(encoded);
        supplied[..(SsccLength - 1)].CopyTo(encoded[2..]);
        encoded[^1] = (char)('0' + check);

        // The caption is the input as given, with the check digit after it when the input left it out, set off by a space when the input groups its
        // digits with spaces.
        char checkCharacter = (char)('0' + check);
        ReadOnlySpan<char> suffix = supplied.Length == SsccLength ? [] : text.Contains(' ') ? [' ', checkCharacter] : [checkCharacter];
        Code128Encoder.BuildSymbol(
            encoded,
            true,
            [],
            text,
            suffix,
            Gs1Data.SsccMinimumXDimension,
            Gs1Data.SsccMaximumXDimension,
            Code128Linkage.None,
            options,
            ref symbol);
    }
}
