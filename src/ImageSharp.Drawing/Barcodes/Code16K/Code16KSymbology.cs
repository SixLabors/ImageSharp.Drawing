// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Code 16K symbology, AIM USS-16K. A symbol is 2 to 16 rows of five Code 128 symbol characters,
/// each row between its own start and stop characters, and carries the full ASCII character set with two
/// modulo 107 check characters.
/// </summary>
public sealed class Code16KSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Code16KSymbology"/> class.
    /// </summary>
    public Code16KSymbology()
    {
    }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        return Code16KEncoder.BuildSymbol(Code16KEncoder.Encode(text), text, options);
    }
}
