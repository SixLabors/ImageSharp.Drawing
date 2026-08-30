// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The EAN-8 symbology specified in ISO/IEC 15420. An EAN-8 symbol is 67 modules wide: a normal guard pattern,
/// four symbol characters from number set A, a centre guard pattern, four symbol characters from number set C
/// and a closing normal guard pattern. All eight digits have symbol characters; there is no parity-encoded digit.
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
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        Span<char> digits = stackalloc char[8];
        return EanUpcEncoder.BuildEan8(EanUpcEncoder.ValidateAndApplyCheckDigit(text, 7, digits), options);
    }
}
