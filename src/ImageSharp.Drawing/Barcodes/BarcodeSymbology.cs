// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A barcode symbology: one machine readable encoding scheme, such as EAN-13 or Code 128. A symbology validates
/// input, applies the check character rules of its specification and encodes the input into a symbol that the
/// canvas renders via <see cref="Processing.DrawingCanvas.DrawBarcode(BarcodeSymbology, string, BarcodeOptions, PointF)"/>.
/// </summary>
public abstract class BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeSymbology"/> class. The constructor is internal
    /// because the encoded symbol types are internal; the set of symbologies is defined by this library.
    /// </summary>
    private protected BarcodeSymbology()
    {
    }

    /// <summary>
    /// Encodes the given text into a device-independent symbol measured in modules.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The options that control layout choices made during encoding.</param>
    /// <returns>The encoded <see cref="BarcodeSymbol"/>.</returns>
    /// <exception cref="ArgumentException">The text is not valid for the symbology.</exception>
    internal abstract BarcodeSymbol Encode(string text, BarcodeOptions options);
}
