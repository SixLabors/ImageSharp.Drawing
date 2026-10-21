// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// SSCC-18, a GS1-128 symbol that carries one element string: the Serial Shipping Container Code of GS1 Application Identifier (00). Section 3.3.1 of the GS1
/// General Specifications gives that identifier an 18 digit SSCC. Table 7-6 gives the element string a total length of 20, and section 7.9 defines the check
/// digit. Table 5-51, symbol specification table 5 for logistic units, gives an X dimension of 0.495 to 0.940 millimetres, and 0.250 millimetre when the
/// logistic unit is too small for the minimum. An X dimension outside 0.250 to 0.940 millimetres draws at the nearest limit.
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
    /// The length of the SSCC of Application Identifier (00), section 3.3.1, with its check digit.
    /// </summary>
    private const int DataLength = 18;

    /// <summary>
    /// Initializes a new instance of the <see cref="Sscc18Symbology"/> class.
    /// </summary>
    public Sscc18Symbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
        => Code128Encoder.BuildFixedElementString(text, "00", DataLength, Gs1Data.SsccMinimumXDimension, Gs1Data.SsccMaximumXDimension, options, ref symbol);
}
