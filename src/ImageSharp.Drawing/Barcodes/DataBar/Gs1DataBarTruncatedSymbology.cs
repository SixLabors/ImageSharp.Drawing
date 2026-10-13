// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// GS1 DataBar Truncated, the reduced height form of GS1 DataBar Omnidirectional for small items that are
/// not read by omnidirectional scanners. Section 5.5.2.1.2 of the GS1 General Specifications gives "96X
/// wide by 13X high". No quiet zone is required.
/// <para>
/// The input is <c>(01)</c> and 13 digits, to which the check digit is added, or 14 digits whose last digit
/// is validated as the check digit. The human readable interpretation prints the element string with the
/// check digit.
/// </para>
/// </summary>
public sealed class Gs1DataBarTruncatedSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarTruncatedSymbology"/> class.
    /// </summary>
    public Gs1DataBarTruncatedSymbology()
    {
    }

    /// <inheritdoc/>
    public override float NominalXDimension => DataBarEncoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Span<char> gtin = stackalloc char[14];
        Gs1Data.ReadGtin(text, gtin);
        return DataBarEncoder.BuildLinear(gtin, DataBarEncoder.TruncatedHeight, options);
    }
}
