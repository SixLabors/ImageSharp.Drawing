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
    public const float PointXDimension = 25.4F / 72F;

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeSymbology"/> class.
    /// </summary>
    private protected BarcodeSymbology()
    {
    }

    /// <summary>
    /// Gets the nominal X dimension in millimetres, the width of one module. A symbology whose
    /// specification gives an X dimension returns it, and every other symbology returns
    /// <see cref="PointXDimension"/>. <see cref="BarcodeOptions.XDimension"/> overrides it.
    /// </summary>
    public virtual float NominalXDimension => PointXDimension;

    /// <summary>
    /// Encodes the given text into a device-independent symbol measured in modules.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The options that control layout choices made during encoding.</param>
    /// <returns>The encoded <see cref="BarcodeSymbol"/>.</returns>
    /// <exception cref="ArgumentException">The text is not valid for the symbology.</exception>
    internal abstract BarcodeSymbol Encode(string text, BarcodeOptions options);
}
