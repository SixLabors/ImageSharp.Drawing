// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// GS1 DataBar Limited, a single row symbol for small items that are not read by omnidirectional scanners.
/// Section 5.5.2.2 of the GS1 General Specifications gives "79X wide, starting with a 1X space and ending
/// with a 5X space, by 10X high". The symbol carries a GTIN-12, a GTIN-13 or a GTIN-14 whose indicator
/// digit is 1: "when using the GTIN-14 data structure, only the indicator value 1 is allowed". No quiet
/// zone is required.
/// <para>
/// The input is <c>(01)</c> and 13 digits, to which the check digit is added, or 14 digits whose last digit
/// is validated as the check digit. The first digit is 0 or 1. The human readable interpretation prints
/// the element string with the check digit.
/// </para>
/// </summary>
public sealed class Gs1DataBarLimitedSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarLimitedSymbology"/> class.
    /// </summary>
    public Gs1DataBarLimitedSymbology()
    {
    }

    /// <inheritdoc/>
    public override float NominalXDimension => DataBarLimitedEncoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Span<char> gtin = stackalloc char[14];
        Gs1Data.ReadGtin(text, gtin);
        if (gtin[0] is not ('0' or '1'))
        {
            throw new ArgumentException($"GS1 DataBar Limited carries a GTIN whose first digit is 0 or 1; got {gtin[0]}.", nameof(text));
        }

        return DataBarLimitedEncoder.Build(gtin, options);
    }
}
