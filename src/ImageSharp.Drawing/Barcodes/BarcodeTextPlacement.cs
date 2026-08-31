// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// One piece of the human readable interpretation of a barcode, positioned in module space. The placement
/// names the bar edge that the line faces and the side that it prints on. The renderer owns the clear
/// space between the two and the room that the line needs, because only the renderer knows the font.
/// </summary>
internal readonly struct BarcodeTextPlacement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeTextPlacement"/> struct rendered at the full
    /// font size.
    /// </summary>
    /// <param name="text">The characters to render.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="barEdge">The bar edge the line faces, in modules from the symbol top.</param>
    public BarcodeTextPlacement(string text, float left, float right, BarcodeTextSide side, float barEdge)
        : this(text, left, right, side, barEdge, 1F)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeTextPlacement"/> struct.
    /// </summary>
    /// <param name="text">The characters to render.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="barEdge">The bar edge the line faces, in modules from the symbol top.</param>
    /// <param name="fontScale">The factor applied to the caller's font size for this placement.</param>
    public BarcodeTextPlacement(string text, float left, float right, BarcodeTextSide side, float barEdge, float fontScale)
        : this(text, left, right, side, barEdge, fontScale, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeTextPlacement"/> struct.
    /// </summary>
    /// <param name="text">The characters to render.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="barEdge">The bar edge the line faces, in modules from the symbol top.</param>
    /// <param name="fontScale">The factor applied to the caller's font size for this placement.</param>
    /// <param name="isCaption">Whether this placement is a data layer caption rendered with the caption font.</param>
    public BarcodeTextPlacement(string text, float left, float right, BarcodeTextSide side, float barEdge, float fontScale, bool isCaption)
    {
        this.Text = text;
        this.Left = left;
        this.Right = right;
        this.Side = side;
        this.BarEdge = barEdge;
        this.FontScale = fontScale;
        this.IsCaption = isCaption;
    }

    /// <summary>
    /// Gets the characters to render.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the left edge of the horizontal span, in modules. The value is negative for text that prints
    /// inside the leading quiet zone, such as the first digit of an EAN-13 symbol.
    /// </summary>
    public float Left { get; }

    /// <summary>
    /// Gets the right edge of the horizontal span, in modules.
    /// </summary>
    public float Right { get; }

    /// <summary>
    /// Gets the side of the bars the line prints on.
    /// </summary>
    public BarcodeTextSide Side { get; }

    /// <summary>
    /// Gets the bar edge the line faces, in modules from the symbol top, before any room is made for text
    /// above the bars. A line below the bars faces a bar bottom, and the EAN-13 digits face the bottom of
    /// the digit bars rather than the extended guard bars, which they flow past. A line above the bars
    /// faces a bar top.
    /// </summary>
    public float BarEdge { get; }

    /// <summary>
    /// Gets the factor applied to the caller's font size for this placement. The UPC number system and
    /// check digits print in smaller type in the quiet zones than the digits below the symbol.
    /// </summary>
    public float FontScale { get; }

    /// <summary>
    /// Gets a value indicating whether this placement is a data layer caption, such as the ISBN line above
    /// its symbol. Captions render with the caption font when one is set.
    /// </summary>
    public bool IsCaption { get; }
}
