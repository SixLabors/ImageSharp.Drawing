// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Options that control how a barcode is sized and painted. The sizes are physical: the X dimension and the bar height in millimetres, and the resolution that
/// turns them into pixels.
/// </summary>
public sealed class BarcodeOptions
{
    /// <summary>
    /// The number of millimetres in one inch, which converts the millimetre sizes to pixels at <see cref="Dpi"/>.
    /// </summary>
    internal const double MillimetresPerInch = 25.4;

    private float dpi = 96F;
    private double? xDimension;
    private double? barHeight;

    /// <summary>
    /// Gets or sets the resolution in dots per inch. One module draws <see cref="XDimension"/> / 25.4 x <see cref="Dpi"/> pixels wide, and text renders at this
    /// resolution. The default is 96, the CSS reference pixel: "1px = 1/96th of 1in".
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public float Dpi
    {
        get => this.dpi;
        set
        {
            Guard.MustBeGreaterThan(value, 0, nameof(this.Dpi));
            this.dpi = value;
        }
    }

    /// <summary>
    /// Gets or sets the X dimension in millimetres: the width of the narrowest element of the symbology, which is one module. Set <see langword="null"/> to
    /// take the nominal X dimension the specification of the symbology gives. Where the specification fixes the width of the whole symbol, as for
    /// <see cref="SwissQrCodeSymbology"/>, the nominal X dimension is that width divided by the module count of the symbol. Where the specification gives no X
    /// dimension, it is one point, <see cref="BarcodeSymbology.PointXDimension"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public double? XDimension
    {
        get => this.xDimension;
        set
        {
            if (value is double x)
            {
                Guard.MustBeGreaterThan(x, 0, nameof(this.XDimension));
            }

            this.xDimension = value;
        }
    }

    /// <summary>
    /// Gets or sets the bar height in millimetres. Set <see langword="null"/> to take the nominal height that the symbology specification gives.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public double? BarHeight
    {
        get => this.barHeight;
        set
        {
            if (value is double height)
            {
                Guard.MustBeGreaterThan(height, 0, nameof(this.BarHeight));
            }

            this.barHeight = value;
        }
    }

    /// <summary>
    /// Gets or sets the brush used to fill the bars and dark modules.
    /// </summary>
    public Brush BarBrush { get; set; } = Brushes.Solid(Color.Black);

    /// <summary>
    /// Gets or sets the brush that fills the symbol background and the quiet zones. Set <see langword="null"/> to leave the background as it is. Symbology
    /// specifications require a light background for reliable scanning. A <see langword="null"/> background is safe over light content only.
    /// </summary>
    public Brush? Background { get; set; }

    /// <summary>
    /// Gets or sets the font for the human readable interpretation, whose size is in points and renders at <see cref="Dpi"/>. Set <see langword="null"/> to
    /// print no text. The specifications set the interpretation in OCR-B, at a size proportional to the X dimension. The caller chooses the font and the size,
    /// with two lower limits: the text of a linear or stacked symbol prints at 9 points or larger, and the text of a GS1 Data Matrix symbol prints at an em
    /// height of 2 mm or larger, which is about 5.67 points.
    /// </summary>
    public Font? Font { get; set; }

    /// <summary>
    /// Gets or sets the font for the caption that a data layer symbology prints above its symbol. Set <see langword="null"/> to use <see cref="Font"/>. Its
    /// size is the starting point, and <see cref="FitCaptionToSymbolWidth"/> then decides the final size. No caption prints below 9 point, the minimum the ISBN
    /// and ISMN manuals set.
    /// </summary>
    public Font? CaptionFont { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the caption spans the bars of its symbol. When <see langword="true"/>, which is the default, the ends of the
    /// caption meet the ends of the bars. When <see langword="false"/>, the caption prints at the size of its own font. The drawn area then becomes wider when
    /// the caption is wider than the bars.
    /// </summary>
    public bool FitCaptionToSymbolWidth { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the drawn area holds the mandatory quiet zones. When <see langword="false"/>, the symbol starts at the draw
    /// origin, and the caller must keep the surrounding area clear. CAUTION: scanners reject a symbol without its quiet zones.
    /// </summary>
    public bool IncludeQuietZones { get; set; } = true;

    /// <summary>
    /// Resolves the bar height in modules: <see cref="BarHeight"/> divided by the X dimension, both in millimetres, or the nominal height of the symbology when
    /// no bar height is set.
    /// </summary>
    /// <param name="xDimension">The X dimension of the symbol in millimetres, <see cref="BarcodeSymbol.XDimension"/>.</param>
    /// <param name="nominalHeight">The nominal height of the symbology in modules.</param>
    /// <returns>The bar height in modules.</returns>
    internal float ResolveBarHeight(double xDimension, float nominalHeight)
        => this.BarHeight.HasValue ? (float)(this.BarHeight.Value / xDimension) : nominalHeight;
}
