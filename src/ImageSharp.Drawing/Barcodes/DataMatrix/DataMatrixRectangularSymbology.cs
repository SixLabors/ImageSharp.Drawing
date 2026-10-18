// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Data Matrix ECC 200 symbology, ISO/IEC 16022, in a rectangular size. The symbol takes the smallest of the six rectangular sizes, 8 x 18 to 16 x 48, that
/// holds the data.
/// </summary>
public sealed class DataMatrixRectangularSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataMatrixRectangularSymbology"/> class.
    /// </summary>
    public DataMatrixRectangularSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        DataMatrixEncoder.BuildSymbol(text, false, DataMatrixFormat.Rectangular, 0, 0, options.XDimension ?? PointXDimension, ref symbol);
    }
}
