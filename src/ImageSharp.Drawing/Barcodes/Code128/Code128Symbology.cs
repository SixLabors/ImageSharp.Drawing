// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Code 128 symbology. Section 5.4 of the GS1 General Specifications describes it, and ISO/IEC 15417 defines it in full. A symbol carries a start
/// character, the data, one check character and the stop character. Each character takes eleven modules, and the stop character takes thirteen. It carries
/// the characters U+0000 to U+00FF, ISO/IEC 8859-1. A character above U+007F takes an extra FNC4 character, or a run of them takes extended mode.
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
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Code128Encoder.BuildSymbol(text, false, [], text, [], options, ref symbol);
    }
}
