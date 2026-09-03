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
    public override float NominalXDimension => Code128Encoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        return Code128Encoder.BuildSymbol(Code128Encoder.Encode(text, false), text, options);
    }
}
