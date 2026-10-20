// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Measures or draws an encoded symbol through the emitter of its kind. The drawing endpoints, the measuring endpoints and the composites share this one choice
/// of emitter. A symbol shapes its text one time: <see cref="Shape"/> writes the lines and the blocks, and the layout of each measure and draw reads them.
/// </summary>
internal static class BarcodeEmitter
{
    /// <summary>
    /// Returns the number of text lines the symbol prints: one for each text placement when the options carry a font, and none otherwise.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <returns>The number of lines.</returns>
    public static int LineCount(ref BarcodeSymbol symbol, BarcodeOptions options)
        => options.Font is null ? 0 : symbol.Text.Length;

    /// <summary>
    /// Rents the buffer that holds the measured lines of the symbol, <see cref="LineCount"/> of them.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <returns>The line buffer.</returns>
    public static PooledBuffer<MeasuredLine> CreateLineBuffer(ref BarcodeSymbol symbol, BarcodeOptions options)
    {
        int count = LineCount(ref symbol, options);
        return count == 0 ? default : new(count);
    }

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
            return HexagonalEmitter.Measure(ref symbol, options, origin, out _);
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
    /// <returns>The measured layout of the symbol, the same as <see cref="Measure(ref BarcodeSymbol, BarcodeOptions, PointF)"/> returns.</returns>
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

    /// <summary>
    /// Shapes the text of the symbol one time: one text block and one measured line for each text placement. A hexagonal symbol prints no text.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="lines">The measured lines to write, <see cref="LineCount"/> of them.</param>
    /// <param name="blocks">The text blocks to write, one for each line.</param>
    public static void Shape(ref BarcodeSymbol symbol, BarcodeOptions options, Span<MeasuredLine> lines, Span<TextBlock> blocks)
    {
        if (symbol.Kind is BarcodeSymbolKind.Matrix or BarcodeSymbolKind.Dotted or BarcodeSymbolKind.Colored)
        {
            MatrixEmitter.Shape(ref symbol, options, lines, blocks);
        }
        else if (symbol.Kind != BarcodeSymbolKind.Hexagonal)
        {
            BarEmitter.Shape(ref symbol, options, lines, blocks);
        }
    }

    /// <summary>
    /// Measures the symbol from its shaped text without drawing anything, and reports where the symbol stands. Nothing in the layout box rounds, so the
    /// symbol stands at the same offset from the origin wherever the origin is.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="lines">The lines that <see cref="Shape"/> wrote. The layout writes the origin of each line.</param>
    /// <param name="symbolOrigin">The top left corner of the first module of the symbol, in pixels, before it rounds onto the pixel grid.</param>
    /// <returns>The measured layout of the symbol.</returns>
    public static BarcodeMetrics Measure(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin, Span<MeasuredLine> lines, out PointF symbolOrigin)
    {
        if (symbol.Kind is BarcodeSymbolKind.Matrix or BarcodeSymbolKind.Dotted or BarcodeSymbolKind.Colored)
        {
            return MatrixEmitter.Measure(ref symbol, options, origin, lines, out symbolOrigin);
        }

        if (symbol.Kind == BarcodeSymbolKind.Hexagonal)
        {
            return HexagonalEmitter.Measure(ref symbol, options, origin, out symbolOrigin);
        }

        return BarEmitter.Measure(ref symbol, options, origin, lines, out symbolOrigin);
    }

    /// <summary>
    /// Draws the symbol from its shaped text onto the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="lines">The lines that <see cref="Shape"/> wrote. The layout writes the origin of each line.</param>
    /// <param name="blocks">The blocks that <see cref="Shape"/> wrote, one for each line.</param>
    /// <param name="background">The brush that fills the renderable bounds before the symbol draws, or <see langword="null"/> for none.</param>
    /// <returns>The measured layout of the symbol.</returns>
    public static BarcodeMetrics Draw(
        DrawingCanvas canvas,
        ref BarcodeSymbol symbol,
        BarcodeOptions options,
        PointF origin,
        Span<MeasuredLine> lines,
        ReadOnlySpan<TextBlock> blocks,
        Brush? background)
    {
        if (symbol.Kind is BarcodeSymbolKind.Matrix or BarcodeSymbolKind.Dotted or BarcodeSymbolKind.Colored)
        {
            return MatrixEmitter.Draw(canvas, ref symbol, options, origin, lines, blocks, background);
        }

        if (symbol.Kind == BarcodeSymbolKind.Hexagonal)
        {
            return HexagonalEmitter.Draw(canvas, ref symbol, options, origin, background);
        }

        return BarEmitter.Draw(canvas, ref symbol, options, origin, lines, blocks, background);
    }
}
