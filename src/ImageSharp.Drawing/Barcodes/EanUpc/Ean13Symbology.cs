// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The EAN-13 symbology, which ISO/IEC 15420 specifies. An EAN-13 symbol is 95 modules wide: a normal guard pattern, six symbol characters, a centre guard
/// pattern, six more symbol characters and a closing normal guard pattern. The leading digit has no symbol character. The number set parity of the six left
/// half characters carries it.
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
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray14<char> digitStorage = default;
        Span<char> digits = ((Span<char>)digitStorage)[..13];
        EanUpcEncoder.BuildEan13(EanUpcEncoder.ValidateAndApplyCheckDigit(text, 12, digits), options, ref symbol);
    }
}
