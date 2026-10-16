// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The EAN-8 symbology, which ISO/IEC 15420 specifies. An EAN-8 symbol is 67 modules wide: a normal guard pattern, four symbol characters from number set A, a
/// centre guard pattern, four symbol characters from number set C and a closing normal guard pattern. All eight digits have symbol characters, so no digit
/// depends on parity.
/// </summary>
public sealed class Ean8Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Ean8Symbology"/> class.
    /// </summary>
    public Ean8Symbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        InlineArray8<char> digitStorage = default;
        EanUpcEncoder.BuildEan8(EanUpcEncoder.ValidateAndApplyCheckDigit(text, 7, digitStorage), options, ref symbol);
    }
}
