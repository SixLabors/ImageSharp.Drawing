// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// GS1 DataBar Stacked Omnidirectional, GS1 DataBar Omnidirectional in two rows that an omnidirectional
/// scanner reads. Section 5.5.2.1.4 of the GS1 General Specifications gives a width of 50X and a minimum
/// height of 69X, "a 3X high separator pattern between two rows of 33X each". The nominal height is the over
/// square 95X that Table 5-44 requires for omnidirectional scanning, two rows of 46X. The bar height of the
/// options sets each row. No quiet zone is required.
/// <para>
/// The input is <c>(01)</c> and 13 digits, to which the check digit is added, or 14 digits whose last digit
/// is validated as the check digit. The human readable interpretation prints the element string with the
/// check digit.
/// </para>
/// </summary>
public sealed class Gs1DataBarStackedOmnidirectionalSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarStackedOmnidirectionalSymbology"/> class.
    /// </summary>
    public Gs1DataBarStackedOmnidirectionalSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray14<char> gtinStorage = default;
        Span<char> gtin = gtinStorage;
        Gs1Data.ReadGtin(text, gtin);
        DataBarEncoder.BuildStackedOmnidirectional(gtin, options, ref symbol);
    }
}
