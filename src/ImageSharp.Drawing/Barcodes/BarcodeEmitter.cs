// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Measures or draws an encoded symbol through the emitter of its kind. The drawing endpoints, the measuring endpoints and the composites share this one choice
/// of emitter.
/// </summary>
internal static class BarcodeEmitter
{
    /// <summary>
    /// Measures the symbol without drawing anything.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <returns>The measured layout of the symbol.</returns>
    public static BarcodeMetrics Measure(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
    {
        if (symbol.Kind is BarcodeSymbolKind.Matrix or BarcodeSymbolKind.Dotted or BarcodeSymbolKind.Colored)
        {
            return MatrixEmitter.Measure(ref symbol, options, origin);
        }

        if (symbol.Kind == BarcodeSymbolKind.Hexagonal)
        {
            return HexagonalEmitter.Measure(ref symbol, options, origin);
        }

        return BarEmitter.Measure(ref symbol, options, origin);
    }

    /// <summary>
    /// Draws the symbol onto the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="background">The brush that fills the renderable bounds before the symbol draws, or <see langword="null"/> for none.</param>
    /// <returns>The measured layout of the symbol, the same as <see cref="Measure"/> returns.</returns>
    public static BarcodeMetrics Draw(DrawingCanvas canvas, ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin, Brush? background)
    {
        if (symbol.Kind is BarcodeSymbolKind.Matrix or BarcodeSymbolKind.Dotted or BarcodeSymbolKind.Colored)
        {
            return MatrixEmitter.Draw(canvas, ref symbol, options, origin, background);
        }

        if (symbol.Kind == BarcodeSymbolKind.Hexagonal)
        {
            return HexagonalEmitter.Draw(canvas, ref symbol, options, origin, background);
        }

        return BarEmitter.Draw(canvas, ref symbol, options, origin, background);
    }
}
