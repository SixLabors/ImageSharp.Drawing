// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// One piece of the human readable interpretation of a barcode, positioned in module space. The placement
/// names the edge its ink starts or ends on and the side of the bars it prints on. The room that the line
/// needs belongs to the renderer, because only the renderer knows the font. The characters are a range of
/// the character storage of the symbol that holds the placement.
/// </summary>
internal readonly struct BarcodeTextPlacement
{
    /// <summary>
    /// The clear space in modules between a bar edge and the ink of a line where the standard of the
    /// symbology gives no figure. Section 5.2.5 of the GS1 General Specifications sets the minimum at 0.5X
    /// both below the main symbol and above an add-on symbol, and states: "Normally the minimum is one
    /// module, which is close enough to keep the human readable interpretation associated with the
    /// symbol." The same space stands above a caption.
    /// </summary>
    public const float Clearance = 1F;

    /// <summary>
    /// The character that starts a new line within the characters of a placement. The lines of one
    /// placement stack downwards and stand within its span as its alignment gives.
    /// </summary>
    public const char LineBreak = '\n';

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeTextPlacement"/> struct.
    /// </summary>
    /// <param name="textStart">The index of the first character in the character storage of the symbol.</param>
    /// <param name="textLength">The number of characters.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="side">The side of the bars the line prints on.</param>
    /// <param name="textEdge">The edge of the ink of the line, in modules from the symbol top.</param>
    /// <param name="fontScale">The factor applied to the caller's font size for this placement.</param>
    /// <param name="isCaption">Whether this placement is a data layer caption rendered with the caption font.</param>
    /// <param name="alignment">Where the line stands within its span.</param>
    public BarcodeTextPlacement(
        int textStart,
        int textLength,
        float left,
        float right,
        BarcodeTextSide side,
        float textEdge,
        float fontScale,
        bool isCaption,
        BarcodeTextAlignment alignment)
    {
        this.TextStart = textStart;
        this.TextLength = textLength;
        this.Left = left;
        this.Right = right;
        this.Side = side;
        this.TextEdge = textEdge;
        this.FontScale = fontScale;
        this.IsCaption = isCaption;
        this.Alignment = alignment;
    }

    /// <summary>
    /// Gets the index of the first character in the character storage of the symbol.
    /// </summary>
    public int TextStart { get; }

    /// <summary>
    /// Gets the number of characters.
    /// </summary>
    public int TextLength { get; }

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
    /// Gets the edge of the ink of the line, in modules from the symbol top, before any room is made for
    /// text above the bars. The ink of a line below the bars starts on it, and the ink of a line above the
    /// bars ends on it. The EAN-13 digits hang below the digit bars rather than the extended guard bars,
    /// which they flow past.
    /// </summary>
    public float TextEdge { get; }

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

    /// <summary>
    /// Gets where the line stands within its span.
    /// </summary>
    public BarcodeTextAlignment Alignment { get; }
}
