// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The MicroPDF417 symbology: 1 to 4 data columns in one of 34 fixed sizes, each row between row address patterns. It carries the characters U+0000 to U+00FF
/// with the symbol characters and the error correction of PDF417.
/// </summary>
public sealed class MicroPdf417Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MicroPdf417Symbology"/> class. The symbol takes the first size that holds the data.
    /// </summary>
    public MicroPdf417Symbology()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MicroPdf417Symbology"/> class.
    /// </summary>
    /// <param name="columns">The number of data columns, 1 to 4. The symbol takes the first size of that many columns that holds the data.</param>
    public MicroPdf417Symbology(int columns)
    {
        Guard.MustBeBetweenOrEqualTo(columns, MicroPdf417Encoder.MinimumColumns, MicroPdf417Encoder.MaximumColumns, nameof(columns));

        this.Columns = columns;
    }

    /// <summary>
    /// Gets the number of data columns, or <see langword="null"/> when it follows from the data.
    /// </summary>
    public int? Columns { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        MicroPdf417Encoder.BuildSymbol(text, this.Columns, options, ref symbol);
    }
}
