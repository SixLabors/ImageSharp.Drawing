// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// One piece of the human readable interpretation of a barcode, positioned in module space. The placement names the edge its ink starts or ends on and the side
/// of the bars it prints on. The room that the line needs belongs to the renderer, because only the renderer knows the font. The characters are a range of the
/// character storage of the symbol that holds the placement.
/// </summary>
internal readonly struct BarcodeTextPlacement
{
    /// <summary>
    /// The clear space in modules between a bar edge and the ink of a line where the standard of the symbology gives no figure. Section 5.2.5 of the GS1
    /// General Specifications sets the minimum at 0.5X both below the main symbol and above an add-on symbol, and states: "Normally the minimum is one module,
    /// which is close enough to keep the human readable interpretation associated with the symbol." The same space stands above a caption.
    /// </summary>
    public const float Clearance = 1F;

    /// <summary>
    /// The character that starts a new line within the characters of a placement. The lines of one placement stack downwards and stand within its span as its
    /// alignment gives.
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
    /// <param name="withinSpan">Whether the ink of the line must stay within its span.</param>
    public BarcodeTextPlacement(
        int textStart,
        int textLength,
        float left,
        float right,
        BarcodeTextSide side,
        float textEdge,
        float fontScale,
        bool isCaption,
        BarcodeTextAlignment alignment,
        bool withinSpan)
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
        this.WithinSpan = withinSpan;
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
    /// Gets the left edge of the horizontal span, in modules. The value is negative for text that prints inside the leading quiet zone, such as the first digit
    /// of an EAN-13 symbol.
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
    /// Gets the edge of the ink of the line, in modules from the symbol top, before any room is made for text above the bars. The ink of a line below the bars
    /// starts on it, and the ink of a line above the bars ends on it. The EAN-13 digits hang below the digit bars rather than the extended guard bars, which
    /// they flow past.
    /// </summary>
    public float TextEdge { get; }

    /// <summary>
    /// Gets the factor applied to the caller's font size for this placement. The UPC number system and check digits print in smaller type in the quiet zones
    /// than the digits below the symbol.
    /// </summary>
    public float FontScale { get; }

    /// <summary>
    /// Gets a value indicating whether this placement is a data layer caption, such as the ISBN line above its symbol. Captions render with the caption font
    /// when one is set.
    /// </summary>
    public bool IsCaption { get; }

    /// <summary>
    /// Gets where the line stands within its span.
    /// </summary>
    public BarcodeTextAlignment Alignment { get; }

    /// <summary>
    /// Gets a value indicating whether the line must stay within its span. The line then prints in a smaller font when it is wider than its span: its ink
    /// for a line aligned on an edge, and its renderable bounds for a centred line.
    /// </summary>
    public bool WithinSpan { get; }

    /// <summary>
    /// Returns the x origin of the line in a span. A centred line straddles the middle of the span. A left aligned line puts the left edge of its ink on the
    /// left edge of the span, and a right aligned line puts the right edge of its ink on the right edge of the span.
    /// </summary>
    /// <param name="spanLeft">The left edge of the span in pixels.</param>
    /// <param name="spanRight">The right edge of the span in pixels.</param>
    /// <param name="bounds">The ink of the measured line, from <see cref="TextMetrics.Bounds"/>.</param>
    /// <param name="renderableWidth">The width of the measured line, from <see cref="TextMetrics.RenderableBounds"/>.</param>
    /// <returns>The x origin in pixels.</returns>
    public float Origin(float spanLeft, float spanRight, FontRectangle bounds, float renderableWidth) => this.Alignment switch
    {
        BarcodeTextAlignment.Left => spanLeft - bounds.Left,
        BarcodeTextAlignment.Right => spanRight - bounds.Right,
        _ => ((spanLeft + spanRight) * 0.5F) - (renderableWidth * 0.5F),
    };

    /// <summary>
    /// Returns the x origin of the line on the drawn symbol. The span stands on the cell edges where the modules draw, rounded onto the pixel grid, not on the
    /// exact fractional position, so a digit stays over its own symbol character. With unrounded edges, the text and the modules can disagree by up to half a
    /// module.
    /// </summary>
    /// <param name="bounds">The ink of the measured line, from <see cref="TextMetrics.Bounds"/>.</param>
    /// <param name="renderableWidth">The width of the measured line, from <see cref="TextMetrics.RenderableBounds"/>.</param>
    /// <param name="symbolLeft">The x of the first module, in pixels.</param>
    /// <param name="moduleWidth">The module width in pixels.</param>
    /// <returns>The x origin in pixels.</returns>
    public float GridOrigin(FontRectangle bounds, float renderableWidth, float symbolLeft, float moduleWidth)
        => this.Origin(MathF.Round(symbolLeft + (this.Left * moduleWidth)), MathF.Round(symbolLeft + (this.Right * moduleWidth)), bounds, renderableWidth);
}
