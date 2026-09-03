// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encapsulates logic for encoding and then measuring barcode symbols.
/// </summary>
public static class BarcodeMeasurer
{
    /// <summary>
    /// Measures the full renderable bounds of the barcode in pixel units.
    /// </summary>
    /// <param name="symbology">The barcode symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner the barcode would draw from, in pixels.</param>
    /// <returns>
    /// The union of the symbol, including any quiet zones, and the human readable interpretation if the
    /// barcode was to be rendered.
    /// </returns>
    /// <exception cref="ArgumentException">The text is not valid for the symbology.</exception>
    public static RectangleF MeasureRenderableBounds(BarcodeSymbology symbology, string text, BarcodeOptions options, PointF origin)
    {
        Guard.NotNull(symbology, nameof(symbology));
        Guard.NotNull(options, nameof(options));

        BarcodeSymbol symbol = symbology.Encode(text, options);
        float xDimension = options.XDimension ?? symbology.NominalXDimension;
        return symbol switch
        {
            LinearBarcodeSymbol linear => LinearBarcodeEmitter.Measure(linear, options, xDimension, origin),
            _ => throw new InvalidOperationException($"Unsupported barcode symbol type: {symbol.GetType()}."),
        };
    }
}
