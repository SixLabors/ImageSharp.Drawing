// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The EAN-13 symbology, which ISO/IEC 15420 specifies. An EAN-13 symbol is 95 modules wide: a normal
/// guard pattern, six symbol characters, a centre guard pattern, six more symbol characters and a closing
/// normal guard pattern. The leading digit has no symbol character. The number set parity of the six left
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
    public override float NominalXDimension => EanUpcEncoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        Span<char> digits = stackalloc char[13];
        return EanUpcEncoder.BuildEan13(EanUpcEncoder.ValidateAndApplyCheckDigit(text, 12, digits), options, null);
    }
}
