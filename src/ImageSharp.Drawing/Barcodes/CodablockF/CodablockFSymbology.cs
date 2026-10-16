// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Codablock F symbology: 2 to 44 rows of Code 128 characters, each row with its own start, code set selector, row indicator, check and stop characters,
/// and two modulo 86 check characters of the whole message in the last row. The data prints as a text line below the symbol when a font is set.
/// </summary>
public sealed class CodablockFSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodablockFSymbology"/> class with eight data columns.
    /// </summary>
    public CodablockFSymbology()
        : this(CodablockFEncoder.DefaultColumns)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodablockFSymbology"/> class.
    /// </summary>
    /// <param name="columns">The number of data columns in a row, 4 to 62. The row count follows from the data.</param>
    public CodablockFSymbology(int columns)
    {
        Guard.MustBeBetweenOrEqualTo(columns, CodablockFEncoder.MinimumColumns, CodablockFEncoder.MaximumColumns, nameof(columns));

        this.Columns = columns;
    }

    /// <summary>
    /// Gets the number of data columns in a row.
    /// </summary>
    public int Columns { get; }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        CodablockFEncoder.BuildSymbol(text, this.Columns, [], text, [], options, ref symbol);
    }
}
