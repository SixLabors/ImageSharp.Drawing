// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Options that control how a barcode is sized and painted. The sizes are physical: the X dimension and
/// the bar height in millimetres, and the resolution that turns them into pixels.
/// </summary>
public sealed class BarcodeOptions
{
    /// <summary>
    /// Gets or sets the resolution in dots per inch. One module draws <see cref="XDimension"/> / 25.4 x
    /// <see cref="Dpi"/> pixels wide, and text renders at this resolution. The default is 96, the CSS
    /// reference pixel: "1px = 1/96th of 1in".
    /// </summary>
    public float Dpi { get; set; } = 96F;

    /// <summary>
    /// Gets or sets the X dimension in millimetres: the width of the narrowest element of the symbology,
    /// which is one module. Set <see langword="null"/> to take the nominal X dimension of the symbology,
    /// <see cref="BarcodeSymbology.NominalXDimension"/>.
    /// </summary>
    public float? XDimension { get; set; }

    /// <summary>
    /// Gets or sets the bar height in millimetres. Set <see langword="null"/> to take the nominal height
    /// that the symbology specification gives.
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
    /// Gets or sets the font for the human readable interpretation, whose size is in points and renders
    /// at <see cref="Dpi"/>. Set <see langword="null"/> to print no text. The specifications set the
    /// interpretation in OCR-B, at a size proportional to the X dimension. The caller chooses the font
    /// and the size.
    /// </summary>
    public Font? Font { get; set; }

    /// <summary>
    /// Gets or sets the font for the caption that a data layer symbology prints above its symbol. Set
    /// <see langword="null"/> to use <see cref="Font"/>. Its size is the starting point, and
    /// <see cref="FitCaptionToSymbolWidth"/> then decides the final size. No caption prints below 9 point,
    /// the minimum the ISBN and ISMN manuals set.
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
