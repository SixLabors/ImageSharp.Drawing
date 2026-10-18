// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The measured layout of a barcode in pixel units.
/// </summary>
public readonly struct BarcodeMetrics
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeMetrics"/> struct.
    /// </summary>
    /// <param name="advance">The layout box of the barcode and its text lines, relative to the origin.</param>
    /// <param name="bounds">The exact ink of the bars or modules and of the text.</param>
    /// <param name="renderableBounds">The union of the advance, positioned at the origin, and the bounds.</param>
    internal BarcodeMetrics(RectangleF advance, RectangleF bounds, RectangleF renderableBounds)
    {
        this.Advance = advance;
        this.Bounds = bounds;
        this.RenderableBounds = renderableBounds;
    }

    /// <summary>
    /// Gets the logical advance rectangle of the barcode in pixel units, relative to the origin.
    /// </summary>
    /// <remarks>
    /// The rectangle holds the barcode and the line box of each text line. Its top left corner is the origin, so its position is always zero.
    /// </remarks>
    public RectangleF Advance { get; }

    /// <summary>
    /// Gets the rendered ink bounds of the barcode in pixel units: the bars or modules as they are drawn and the glyphs of the text.
    /// </summary>
    /// <remarks>
    /// The rectangle can be smaller or larger than the advance. It can have a non-zero origin.
    /// </remarks>
    public RectangleF Bounds { get; }

    /// <summary>
    /// Gets the union of the advance rectangle, positioned at the origin, and the bounds, in pixel units. The background fills this rectangle.
    /// </summary>
    /// <remarks>
    /// Use this rectangle when the advance and the ink must fit in the same box.
    /// </remarks>
    public RectangleF RenderableBounds { get; }
}
