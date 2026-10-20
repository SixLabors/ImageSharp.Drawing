// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodes barcode symbols and measures their layout without drawing them.
/// </summary>
public static class BarcodeMeasurer
{
    /// <summary>
    /// Measures the layout of the barcode in pixel units.
    /// </summary>
    /// <param name="symbology">The barcode symbology to encode with.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The advance, the bounds and the renderable bounds of the barcode.</returns>
    /// <exception cref="ArgumentException">
    /// The text is not valid for the symbology, or the options break a requirement of the standard of the symbology, such as its minimum bar
    /// height or its maximum size.
    /// </exception>
    public static BarcodeMetrics Measure(BarcodeSymbology symbology, BarcodeOptions options, PointF origin, ReadOnlySpan<char> text)
    {
        Guard.NotNull(symbology, nameof(symbology));
        Guard.NotNull(options, nameof(options));

        BarcodeSymbol symbol = default;
        try
        {
            symbology.Encode(text, options, ref symbol);
            return BarcodeEmitter.Measure(ref symbol, options, origin);
        }
        finally
        {
            symbol.Dispose();
        }
    }

    /// <summary>
    /// Measures the layout of a composite barcode of two parts in pixel units.
    /// </summary>
    /// <param name="symbology">The composite barcode symbology to encode with.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="first">The text of the first symbology of the composite.</param>
    /// <param name="second">The text of the second symbology of the composite.</param>
    /// <returns>The advance, the bounds and the renderable bounds of the barcode.</returns>
    /// <exception cref="ArgumentException">
    /// The composite does not have two parts, a text is not valid, the parts do not join, or the options break a requirement of the standard of the
    /// symbology, such as its minimum bar height or its maximum size.
    /// </exception>
    public static BarcodeMetrics Measure(
        CompositeBarcodeSymbology symbology,
        BarcodeOptions options,
        PointF origin,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second)
    {
        Guard.NotNull(symbology, nameof(symbology));
        Guard.NotNull(options, nameof(options));

        return symbology.Measure(options, origin, first, second);
    }

    /// <summary>
    /// Measures the layout of a composite barcode of three parts in pixel units.
    /// </summary>
    /// <param name="symbology">The composite barcode symbology to encode with.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="first">The text of the first symbology of the composite.</param>
    /// <param name="second">The text of the second symbology of the composite.</param>
    /// <param name="third">The text of the third symbology of the composite.</param>
    /// <returns>The advance, the bounds and the renderable bounds of the barcode.</returns>
    /// <exception cref="ArgumentException">
    /// The composite does not have three parts, a text is not valid, the parts do not join, or the options break a requirement of the standard of the
    /// symbology, such as its minimum bar height or its maximum size.
    /// </exception>
    public static BarcodeMetrics Measure(
        CompositeBarcodeSymbology symbology,
        BarcodeOptions options,
        PointF origin,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third)
    {
        Guard.NotNull(symbology, nameof(symbology));
        Guard.NotNull(options, nameof(options));

        return symbology.Measure(options, origin, first, second, third);
    }

    /// <summary>
    /// Measures the logical advance of the barcode in pixel units, relative to the origin.
    /// </summary>
    /// <param name="symbology">The barcode symbology to encode with.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The rectangle that holds the barcode and the line box of each text line, relative to the origin.</returns>
    /// <exception cref="ArgumentException">
    /// The text is not valid for the symbology, or the options break a requirement of the standard of the symbology, such as its minimum bar
    /// height or its maximum size.
    /// </exception>
    public static RectangleF MeasureAdvance(BarcodeSymbology symbology, BarcodeOptions options, PointF origin, ReadOnlySpan<char> text)
        => Measure(symbology, options, origin, text).Advance;

    /// <summary>
    /// Measures the logical advance of a composite barcode of two parts in pixel units, relative to the origin.
    /// </summary>
    /// <param name="symbology">The composite barcode symbology to encode with.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="first">The text of the first symbology of the composite.</param>
    /// <param name="second">The text of the second symbology of the composite.</param>
    /// <returns>The rectangle that holds the parts and the line box of each text line, relative to the origin.</returns>
    /// <exception cref="ArgumentException">
    /// The composite does not have two parts, a text is not valid, the parts do not join, or the options break a requirement of the standard of the
    /// symbology, such as its minimum bar height or its maximum size.
    /// </exception>
    public static RectangleF MeasureAdvance(
        CompositeBarcodeSymbology symbology,
        BarcodeOptions options,
        PointF origin,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second)
        => Measure(symbology, options, origin, first, second).Advance;

    /// <summary>
    /// Measures the logical advance of a composite barcode of three parts in pixel units, relative to the origin.
    /// </summary>
    /// <param name="symbology">The composite barcode symbology to encode with.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="first">The text of the first symbology of the composite.</param>
    /// <param name="second">The text of the second symbology of the composite.</param>
    /// <param name="third">The text of the third symbology of the composite.</param>
    /// <returns>The rectangle that holds the parts and the line box of each text line, relative to the origin.</returns>
    /// <exception cref="ArgumentException">
    /// The composite does not have three parts, a text is not valid, the parts do not join, or the options break a requirement of the standard of the
    /// symbology, such as its minimum bar height or its maximum size.
    /// </exception>
    public static RectangleF MeasureAdvance(
        CompositeBarcodeSymbology symbology,
        BarcodeOptions options,
        PointF origin,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third)
        => Measure(symbology, options, origin, first, second, third).Advance;

    /// <summary>
    /// Measures the rendered ink bounds of the barcode in pixel units.
    /// </summary>
    /// <param name="symbology">The barcode symbology to encode with.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The rectangle that the bars or modules and the glyphs of the text cover.</returns>
    /// <exception cref="ArgumentException">
    /// The text is not valid for the symbology, or the options break a requirement of the standard of the symbology, such as its minimum bar
    /// height or its maximum size.
    /// </exception>
    public static RectangleF MeasureBounds(BarcodeSymbology symbology, BarcodeOptions options, PointF origin, ReadOnlySpan<char> text)
        => Measure(symbology, options, origin, text).Bounds;

    /// <summary>
    /// Measures the rendered ink bounds of a composite barcode of two parts in pixel units.
    /// </summary>
    /// <param name="symbology">The composite barcode symbology to encode with.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="first">The text of the first symbology of the composite.</param>
    /// <param name="second">The text of the second symbology of the composite.</param>
    /// <returns>The rectangle that the bars or modules and the glyphs of the text of all parts cover.</returns>
    /// <exception cref="ArgumentException">
    /// The composite does not have two parts, a text is not valid, the parts do not join, or the options break a requirement of the standard of the
    /// symbology, such as its minimum bar height or its maximum size.
    /// </exception>
    public static RectangleF MeasureBounds(
        CompositeBarcodeSymbology symbology,
        BarcodeOptions options,
        PointF origin,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second)
        => Measure(symbology, options, origin, first, second).Bounds;

    /// <summary>
    /// Measures the rendered ink bounds of a composite barcode of three parts in pixel units.
    /// </summary>
    /// <param name="symbology">The composite barcode symbology to encode with.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="first">The text of the first symbology of the composite.</param>
    /// <param name="second">The text of the second symbology of the composite.</param>
    /// <param name="third">The text of the third symbology of the composite.</param>
    /// <returns>The rectangle that the bars or modules and the glyphs of the text of all parts cover.</returns>
    /// <exception cref="ArgumentException">
    /// The composite does not have three parts, a text is not valid, the parts do not join, or the options break a requirement of the standard of the
    /// symbology, such as its minimum bar height or its maximum size.
    /// </exception>
    public static RectangleF MeasureBounds(
        CompositeBarcodeSymbology symbology,
        BarcodeOptions options,
        PointF origin,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third)
        => Measure(symbology, options, origin, first, second, third).Bounds;

    /// <summary>
    /// Measures the full renderable bounds of the barcode in pixel units.
    /// </summary>
    /// <param name="symbology">The barcode symbology to encode with.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The union of the advance, positioned at the origin, and the ink bounds.</returns>
    /// <exception cref="ArgumentException">
    /// The text is not valid for the symbology, or the options break a requirement of the standard of the symbology, such as its minimum bar
    /// height or its maximum size.
    /// </exception>
    public static RectangleF MeasureRenderableBounds(BarcodeSymbology symbology, BarcodeOptions options, PointF origin, ReadOnlySpan<char> text)
        => Measure(symbology, options, origin, text).RenderableBounds;

    /// <summary>
    /// Measures the full renderable bounds of a composite barcode of two parts in pixel units.
    /// </summary>
    /// <param name="symbology">The composite barcode symbology to encode with.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="first">The text of the first symbology of the composite.</param>
    /// <param name="second">The text of the second symbology of the composite.</param>
    /// <returns>The union of the advance, positioned at the origin, and the ink bounds.</returns>
    /// <exception cref="ArgumentException">
    /// The composite does not have two parts, a text is not valid, the parts do not join, or the options break a requirement of the standard of the
    /// symbology, such as its minimum bar height or its maximum size.
    /// </exception>
    public static RectangleF MeasureRenderableBounds(
        CompositeBarcodeSymbology symbology,
        BarcodeOptions options,
        PointF origin,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second)
        => Measure(symbology, options, origin, first, second).RenderableBounds;

    /// <summary>
    /// Measures the full renderable bounds of a composite barcode of three parts in pixel units.
    /// </summary>
    /// <param name="symbology">The composite barcode symbology to encode with.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="first">The text of the first symbology of the composite.</param>
    /// <param name="second">The text of the second symbology of the composite.</param>
    /// <param name="third">The text of the third symbology of the composite.</param>
    /// <returns>The union of the advance, positioned at the origin, and the ink bounds.</returns>
    /// <exception cref="ArgumentException">
    /// The composite does not have three parts, a text is not valid, the parts do not join, or the options break a requirement of the standard of the
    /// symbology, such as its minimum bar height or its maximum size.
    /// </exception>
    public static RectangleF MeasureRenderableBounds(
        CompositeBarcodeSymbology symbology,
        BarcodeOptions options,
        PointF origin,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third)
        => Measure(symbology, options, origin, first, second, third).RenderableBounds;
}
