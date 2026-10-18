// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The PDF417 symbology, ISO/IEC 15438. A symbol is 3 to 90 rows of 1 to 30 data columns, each row between a start character, a left and a right row indicator
/// and a stop character. It carries the characters U+0000 to U+00FF with 2 to 512 error correction codewords.
/// </summary>
public sealed class Pdf417Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Pdf417Symbology"/> class. The number of data columns follows from the data. The error correction level is
    /// the recommended minimum of Annex E of ISO/IEC 15438 for the data, raised to the highest level the rows leave room for.
    /// </summary>
    public Pdf417Symbology()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pdf417Symbology"/> class. The error correction level is the recommended minimum of Annex E of ISO/IEC 15438
    /// for the data, raised to the highest level the rows leave room for.
    /// </summary>
    /// <param name="columns">The number of data columns in a row, 1 to 30. The row count follows from the data.</param>
    public Pdf417Symbology(int columns)
    {
        Guard.MustBeBetweenOrEqualTo(columns, Pdf417Encoder.MinimumColumns, Pdf417Encoder.MaximumColumns, nameof(columns));

        this.Columns = columns;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pdf417Symbology"/> class.
    /// </summary>
    /// <param name="columns">The number of data columns in a row, 1 to 30. The row count follows from the data.</param>
    /// <param name="errorCorrectionLevel">The error correction level, 0 to 8, which gives 2 to 512 error correction codewords.</param>
    public Pdf417Symbology(int columns, int errorCorrectionLevel)
        : this(columns)
    {
        Guard.MustBeBetweenOrEqualTo(errorCorrectionLevel, 0, Pdf417Encoder.MaximumErrorCorrectionLevel, nameof(errorCorrectionLevel));

        this.ErrorCorrectionLevel = errorCorrectionLevel;
    }

    /// <summary>
    /// Gets the number of data columns in a row, or <see langword="null"/> when it follows from the data.
    /// </summary>
    public int? Columns { get; }

    /// <summary>
    /// Gets the error correction level, or <see langword="null"/> when it follows from the data.
    /// </summary>
    public int? ErrorCorrectionLevel { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Pdf417Encoder.BuildSymbol(text, this.Columns, this.ErrorCorrectionLevel, false, options, ref symbol);
    }
}
