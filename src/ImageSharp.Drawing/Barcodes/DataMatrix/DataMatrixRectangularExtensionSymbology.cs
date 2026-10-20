// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Data Matrix ECC 200 symbology with the rectangular extension, ISO/IEC 21471. A symbol is one of the six rectangular sizes of ISO/IEC 16022 or one of the
/// 18 rectangular extension sizes, 8 x 18 to 26 x 64.
/// </summary>
public sealed class DataMatrixRectangularExtensionSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataMatrixRectangularExtensionSymbology"/> class. The symbol takes the smallest rectangular size that holds
    /// the data.
    /// </summary>
    public DataMatrixRectangularExtensionSymbology()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DataMatrixRectangularExtensionSymbology"/> class with a fixed symbol size.
    /// </summary>
    /// <param name="rows">The number of rows, finder pattern included.</param>
    /// <param name="columns">The number of columns, finder pattern included.</param>
    /// <exception cref="ArgumentException">The size is not a rectangular Data Matrix size.</exception>
    public DataMatrixRectangularExtensionSymbology(int rows, int columns)
    {
        if (!DataMatrixEncoder.IsSize(rows, columns, DataMatrixFormat.RectangularExtension))
        {
            throw new ArgumentException($"{rows} x {columns} is not a rectangular Data Matrix size.", nameof(rows));
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
        DataMatrixEncoder.BuildSymbol(
            text,
            false,
            DataMatrixFormat.RectangularExtension,
            this.Rows ?? 0,
            this.Columns ?? 0,
            options.ResolveXDimension(DefaultMatrixXDimension),
            ref symbol);
    }
}
