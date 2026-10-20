// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Data Matrix ECC 200 symbology, ISO/IEC 16022: a matrix of square modules inside a finder pattern, a solid L on the left and bottom edges and alternating
/// modules on the top and right edges. A symbol is one of 24 square sizes, 10 x 10 to 144 x 144, or six rectangular sizes, 8 x 18 to 16 x 48. It carries the
/// characters U+0000 to U+00FF with Reed-Solomon error correction.
/// </summary>
public sealed class DataMatrixSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataMatrixSymbology"/> class. The symbol takes the smallest square size that holds the data.
    /// </summary>
    public DataMatrixSymbology()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DataMatrixSymbology"/> class with a fixed symbol size.
    /// </summary>
    /// <param name="rows">The number of rows, finder pattern included.</param>
    /// <param name="columns">The number of columns, finder pattern included.</param>
    /// <exception cref="ArgumentException">The size is not a square or rectangular Data Matrix size.</exception>
    public DataMatrixSymbology(int rows, int columns)
    {
        if (!DataMatrixEncoder.IsSize(rows, columns, DataMatrixFormat.SquareOrRectangular))
        {
            throw new ArgumentException($"{rows} x {columns} is not a square or rectangular Data Matrix size.", nameof(rows));
        }

        this.Rows = rows;
        this.Columns = columns;
    }

    /// <summary>
    /// Gets the number of rows, or <see langword="null"/> when the size follows from the data.
    /// </summary>
    public int? Rows { get; }

    /// <summary>
    /// Gets the number of columns, or <see langword="null"/> when the size follows from the data.
    /// </summary>
    public int? Columns { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (this.Rows.HasValue)
        {
            DataMatrixEncoder.BuildSymbol(
                text,
                false,
                DataMatrixFormat.SquareOrRectangular,
                this.Rows.Value,
                this.Columns!.Value,
                options.ResolveXDimension(0, DefaultMatrixXDimension, double.PositiveInfinity),
                ref symbol);
        }
        else
        {
            double xDimension = options.ResolveXDimension(0, DefaultMatrixXDimension, double.PositiveInfinity);
            DataMatrixEncoder.BuildSymbol(text, false, DataMatrixFormat.Square, 0, 0, xDimension, ref symbol);
        }
    }
}
