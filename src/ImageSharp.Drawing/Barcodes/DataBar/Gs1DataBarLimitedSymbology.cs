// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// GS1 DataBar Limited, a single row symbol for small items that are not read by omnidirectional scanners. Section 5.5.2.2 of the GS1 General Specifications
/// gives "79X wide, starting with a 1X space and ending with a 5X space, by 10X high". The symbol carries a GTIN-12, a GTIN-13 or a GTIN-14 whose indicator
/// digit is 1: "when using the GTIN-14 data structure, only the indicator value 1 is allowed". "The minimum height SHALL be 10X".
/// A bar height under 10X draws at 10X. No quiet zone is required.
/// <para>
/// The input is <c>(01)</c> and 13 or 14 digits. When the input has 13 digits, the symbology adds the check digit. When the input has 14 digits, the symbology
/// validates the last digit as the check digit. The first digit is 0 or 1. The human readable interpretation prints the element string with the check digit.
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
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray14<char> gtinStorage = default;
        Span<char> gtin = gtinStorage;
        Gs1Data.ReadGtin(text, gtin);
        if (gtin[0] is not ('0' or '1'))
        {
            throw new ArgumentException($"GS1 DataBar Limited carries a GTIN whose first digit is 0 or 1; got {gtin[0]}.", nameof(text));
        }

        DataBarLimitedEncoder.Build(gtin, false, options, ref symbol);
    }
}
