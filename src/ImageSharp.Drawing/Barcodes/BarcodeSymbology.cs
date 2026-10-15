// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A barcode symbology: one machine readable encoding scheme. A symbology validates the input and applies
/// the check character rules of its specification. It then encodes the input into a symbol. The canvas
/// draws that symbol through
/// <see cref="Processing.DrawingCanvas.DrawBarcode(BarcodeSymbology, string, BarcodeOptions, PointF)"/>.
/// </summary>
public abstract class BarcodeSymbology
{
    /// <summary>
    /// The X dimension in millimetres of a symbology whose specification gives none: one point, 25.4 / 72.
    /// </summary>
    public const double PointXDimension = 25.4 / 72;

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeSymbology"/> class.
    /// </summary>
    private protected BarcodeSymbology()
    {
    }

    /// <summary>
    /// Validates the given text and encodes it into a device-independent symbol measured in modules.
    /// The text length is checked against the maximum of the symbology before any buffer is sized from it.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The options that control layout choices made during encoding.</param>
    /// <param name="symbol">The symbol to write. It is empty on entry.</param>
    /// <exception cref="ArgumentException">The text is not valid for the symbology.</exception>
    internal abstract void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol);
}
