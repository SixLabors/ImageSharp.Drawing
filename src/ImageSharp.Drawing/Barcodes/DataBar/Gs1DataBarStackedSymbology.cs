// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// GS1 DataBar Stacked, GS1 DataBar Truncated in two rows for small items that are not read by omnidirectional scanners. Section 5.5.2.1.3 of the GS1 General
/// Specifications gives "50X wide by 13X high" with "a 1X high separator pattern between the two rows". The rows share the other 12X as an upper row of 5X
/// and a lower row of 7X. The bar height of the options sets the lower row, and the upper row keeps its ratio of five to seven. No quiet zone is required.
/// <para>
/// The input is <c>(01)</c> and 13 digits, to which the check digit is added, or 14 digits whose last digit is validated as the check digit. The human readable
/// interpretation prints the element string with the check digit.
/// </para>
/// </summary>
public sealed class Gs1DataBarStackedSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DataBarStackedSymbology"/> class.
    /// </summary>
    public Gs1DataBarStackedSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray14<char> gtinStorage = default;
        Span<char> gtin = gtinStorage;
        Gs1Data.ReadGtin(text, gtin);
        DataBarEncoder.BuildStacked(gtin, options, ref symbol);
    }
}
