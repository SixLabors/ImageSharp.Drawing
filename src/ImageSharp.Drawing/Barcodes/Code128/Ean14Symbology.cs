// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

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
    /// The length of the GTIN of Application Identifier (01), section 3.3.2, with its check digit.
    /// </summary>
    private const int DataLength = 14;

    /// <summary>
    /// Initializes a new instance of the <see cref="Ean14Symbology"/> class.
    /// </summary>
    public Ean14Symbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
        => Code128Encoder.BuildFixedElementString(
            text,
            "01",
            DataLength,
            Gs1Data.Gs1128MinimumXDimension,
            Gs1Data.Gs1128MaximumXDimension,
            options,
            ref symbol);
}
