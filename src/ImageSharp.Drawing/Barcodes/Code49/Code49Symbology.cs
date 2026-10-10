// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Code 49 symbology, AIM USS-49. A symbol is 2 to 8 rows of four symbol characters, each carrying
/// two code characters, and carries the full ASCII character set with a row check character in every row
/// and two or three modulo 2401 symbol check characters in the last.
/// </summary>
public sealed class Code49Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Code49Symbology"/> class.
    /// </summary>
    public Code49Symbology()
    {
    }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        return Code49Encoder.BuildSymbol(Code49Encoder.Encode(text), text, options);
    }
}
