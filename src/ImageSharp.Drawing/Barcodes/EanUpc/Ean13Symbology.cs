// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The EAN-13 symbology specified in ISO/IEC 15420. An EAN-13 symbol is 95 modules wide: a normal guard
/// pattern, six symbol characters, a centre guard pattern, six more symbol characters and a closing normal
/// guard pattern. The thirteenth (leading) digit has no symbol character; it is conveyed by the number set
/// parity of the six left-half characters.
/// </summary>
public sealed class Ean13Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Ean13Symbology"/> class.
    /// </summary>
    public Ean13Symbology()
    {
    }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
        => EanUpcEncoder.BuildEan13(EanUpcEncoder.ValidateAndApplyCheckDigit(text, 12, "EAN-13"), options, null);
}
