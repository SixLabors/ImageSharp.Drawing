// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Options that control how a barcode is sized and painted.
/// </summary>
public sealed class BarcodeOptions
{
    /// <summary>
    /// Gets or sets the width of one module in pixels. The module is the narrowest nominal element of a
    /// symbology, which ISO/IEC 15420 calls the X-dimension. Every symbol dimension scales from this value.
    /// A whole number gives every module the same pixel width. A fractional value still draws crisp bars,
    /// because bar edges snap to the pixel grid, but their widths differ by one pixel.
    /// </summary>
    public float ModuleWidth { get; set; } = 2F;

    /// <summary>
    /// Gets or sets the bar height in pixels. Set <see langword="null"/> to take the nominal height that
    /// the symbology specification gives for its X-dimension. For EAN-13 at nominal size, ISO/IEC 15420
    /// gives 22.85 mm bars at a 0.33 mm X-dimension, which is 69.24 modules.
    /// </summary>
    public float? BarHeight { get; set; }

    /// <summary>
    /// Gets or sets the brush used to fill the bars and dark modules.
    /// </summary>
    public Brush BarBrush { get; set; } = Brushes.Solid(Color.Black);

    /// <summary>
    /// Gets or sets the brush that fills the symbol background and the quiet zones. Set
    /// <see langword="null"/> to leave the background as it is. Symbology specifications require a light
    /// background for reliable scanning. A <see langword="null"/> background is safe over light content only.
    /// </summary>
    public Brush? Background { get; set; }

    /// <summary>
    /// Gets or sets the font for the human readable interpretation. Set <see langword="null"/> to print no
    /// text. The specifications set the interpretation in OCR-B, at a size proportional to the X-dimension.
    /// The caller chooses the font and the size.
    /// </summary>
    public Font? Font { get; set; }

    /// <summary>
    /// Gets or sets the font for the caption that a data layer symbology prints above its symbol, such as
    /// the ISBN line. Set <see langword="null"/> to use <see cref="Font"/>. Its size is the starting point,
    /// and <see cref="FitCaptionToSymbolWidth"/> then decides the final size. No caption prints below
    /// 9 point, because the ISBN and ISMN manuals both require 9 point or larger.
    /// </summary>
    public Font? CaptionFont { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the caption spans the bars of its symbol. When
    /// <see langword="true"/>, which is the default, the ends of the caption meet the ends of the bars.
    /// When <see langword="false"/>, the caption prints at the size of its own font. The drawn area then
    /// becomes wider when the caption is wider than the bars.
    /// </summary>
    public bool FitCaptionToSymbolWidth { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the drawn area holds the mandatory quiet zones. When
    /// <see langword="false"/>, the symbol starts at the draw origin, and the caller must keep the
    /// surrounding area clear. CAUTION: scanners reject a symbol without its quiet zones.
    /// </summary>
    public bool IncludeQuietZones { get; set; } = true;
}
