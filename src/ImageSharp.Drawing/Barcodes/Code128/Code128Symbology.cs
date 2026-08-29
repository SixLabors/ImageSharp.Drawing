// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Code 128 symbology, described in section 5.4 of the GS1 General Specifications and defined in full
/// by ISO/IEC 15417. A symbol carries a start character, the data, one check character and the stop
/// character, all at eleven modules each except the thirteen module stop character.
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
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
        => Code128Encoder.BuildSymbol(Code128Encoder.Encode(text, false, "Code 128"), text, options);
}
