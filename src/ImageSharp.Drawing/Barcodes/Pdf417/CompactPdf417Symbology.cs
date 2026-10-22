// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Compact PDF417 symbology, Annex G of ISO/IEC 15438. A symbol is a PDF417 symbol whose rows omit the right row indicator and end with a single module bar
/// in place of the stop character. The bar height of the options sets each row, and section 5.8.2 requires "Y &gt;= 3X", so a bar height under 3X draws at 3X.
/// </summary>
public sealed class CompactPdf417Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompactPdf417Symbology"/> class. The number of data columns follows from the data. The error correction
    /// level is the recommended minimum of Annex E of ISO/IEC 15438 for the data, raised to the highest level the rows leave room for. Table E.1 ends at 863
    /// data codewords, and more data takes the highest level that fits.
    /// </summary>
    public CompactPdf417Symbology()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompactPdf417Symbology"/> class. The error correction level is the recommended minimum of Annex E of
    /// ISO/IEC 15438 for the data, raised to the highest level the rows leave room for. Table E.1 ends at 863 data codewords, and more data takes the highest
    /// level that fits.
    /// </summary>
    /// <param name="columns">The number of data columns in a row, 1 to 30. The row count follows from the data.</param>
    public CompactPdf417Symbology(int columns)
    {
        Guard.MustBeBetweenOrEqualTo(columns, Pdf417Encoder.MinimumColumns, Pdf417Encoder.MaximumColumns, nameof(columns));

        this.Columns = columns;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompactPdf417Symbology"/> class.
    /// </summary>
    /// <param name="columns">The number of data columns in a row, 1 to 30. The row count follows from the data.</param>
    /// <param name="errorCorrectionLevel">The error correction level, 0 to 8, which gives 2 to 512 error correction codewords.</param>
    public CompactPdf417Symbology(int columns, int errorCorrectionLevel)
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
        Pdf417Encoder.BuildSymbol(text, this.Columns, this.ErrorCorrectionLevel, true, options, ref symbol);
    }
}
