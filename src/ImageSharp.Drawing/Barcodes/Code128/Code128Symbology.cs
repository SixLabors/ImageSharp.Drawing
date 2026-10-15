// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Code 128 symbology. Section 5.4 of the GS1 General Specifications describes it, and ISO/IEC 15417
/// defines it in full. A symbol carries a start character, the data, one check character and the stop
/// character. Each character takes eleven modules, and the stop character takes thirteen.
/// </summary>
public sealed class Code128Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Code128Symbology"/> class.
    /// </summary>
    public Code128Symbology()
    {
    }

    /// <inheritdoc/>
    public override double NominalXDimension => Code128Encoder.NominalXDimension;

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        Code128Encoder.BuildSymbol(text, false, [], text, [], options, ref symbol);
    }
}
