// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A single piece of the human readable interpretation of a barcode, positioned in module space.
/// The renderer centers the text horizontally within the span and flows it downward from the top line.
/// The symbol height grows at render time to hold the text, the way the nominal ISO/IEC 15420 symbol is
/// sized so its text region extends below the guard bars.
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
    /// <param name="y">The top line of the text, in modules from the symbol top.</param>
    public BarcodeTextPlacement(string text, float left, float right, float y)
        : this(text, left, right, y, 1F)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeTextPlacement"/> struct.
    /// </summary>
    /// <param name="text">The characters to render.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="y">The top line of the text, in modules from the symbol top.</param>
    /// <param name="fontScale">The factor applied to the caller's font size for this placement.</param>
    public BarcodeTextPlacement(string text, float left, float right, float y, float fontScale)
        : this(text, left, right, y, fontScale, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeTextPlacement"/> struct.
    /// </summary>
    /// <param name="text">The characters to render.</param>
    /// <param name="left">The left edge of the horizontal span, in modules.</param>
    /// <param name="right">The right edge of the horizontal span, in modules.</param>
    /// <param name="y">The top line of the text, in modules from the symbol top.</param>
    /// <param name="fontScale">The factor applied to the caller's font size for this placement.</param>
    /// <param name="isCaption">Whether this placement is a data layer caption rendered with the caption font.</param>
    public BarcodeTextPlacement(string text, float left, float right, float y, float fontScale, bool isCaption)
    {
        this.Text = text;
        this.Left = left;
        this.Right = right;
        this.Y = y;
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
    /// Gets the top line of the text, in modules from the symbol top. The digits of an EAN-13 symbol hang
    /// just below the digit bars, flowing past the extended guard bars; add-on digits hang from the symbol
    /// top inside their text band.
    /// </summary>
    public float Y { get; }

    /// <summary>
    /// Gets the factor applied to the caller's font size for this placement. The UPC number system and
    /// check digits print in smaller type in the quiet zones than the digits below the symbol.
    /// </summary>
    public float FontScale { get; }

    /// <summary>
    /// Gets a value indicating whether this placement is a data layer caption, such as the ISBN line above
    /// its symbol. Captions render with the caption font when one is set, and they anchor to their own top
    /// line rather than the shared digit baseline.
    /// </summary>
    public bool IsCaption { get; }
}
