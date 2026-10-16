// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// GS1 DataBar Omnidirectional, a single row symbol 96 modules wide that carries the GTIN of GS1 Application Identifier (01) and can be read in four segments
/// by an omnidirectional scanner. Section 5.5.2.1.1 of the GS1 General Specifications gives a minimum height of 33X, and the nominal height is the over square
/// 46X that Table 5-44 requires for omnidirectional scanning. No quiet zone is required.
/// <para>
/// The input is <c>(01)</c> and 13 digits, to which the check digit is added, or 14 digits whose last digit is validated as the check digit. The human readable
/// interpretation prints the element string with the check digit.
/// </para>
/// </summary>
public sealed class Gs1DataBarOmnidirectionalSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarOmnidirectionalSymbology"/> class.
    /// </summary>
    public Gs1DataBarOmnidirectionalSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray14<char> gtinStorage = default;
        Span<char> gtin = gtinStorage;
        Gs1Data.ReadGtin(text, gtin);
        DataBarEncoder.BuildLinear(gtin, DataBarEncoder.OmnidirectionalHeight, options, ref symbol);
    }
}
