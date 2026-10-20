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

    /// <summary>
    /// The tolerance in pixels that keeps an X dimension limit on the whole pixel it falls on against the rounding error of the millimetre to pixel conversion.
    /// </summary>
    private const double LimitTolerance = 1E-9;

    private float dpi = 96F;
    private double? xDimension;
    private double? barHeight;

    /// <summary>
    /// Gets or sets the resolution in dots per inch. One module draws <see cref="XDimension"/> / 25.4 x <see cref="Dpi"/> pixels wide, and text renders at
    /// this resolution. The default is 96, the CSS reference pixel: "1px = 1/96th of 1in".
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
    /// dimension, it is <see cref="BarcodeSymbology.DefaultMatrixXDimension"/> for Aztec Code, Code One, Data Matrix, DotCode, Han Xin Code, QR Code and
    /// Ultracode, 4 pixels at the default <see cref="Dpi"/>, and <see cref="BarcodeSymbology.DefaultXDimension"/> for the other symbologies, 2 pixels.
    /// <para>
    /// A symbol draws at the X dimension of the nearest whole number of pixels at <see cref="Dpi"/>, at least one pixel, so every module of the symbol is
    /// the same number of pixels wide. The symbol can then be slightly larger or smaller than this X dimension gives. Where the specification of the symbology
    /// gives a smallest and a largest X dimension, the number of pixels is clamped to that range, so a value outside the range draws at the nearest limit. At
    /// a resolution where no whole number of pixels is inside the range, the symbol draws at the smallest whole number of pixels over the smallest X
    /// dimension. <see cref="MaxiCodeSymbology"/>, whose
    /// hexagonal modules do not stand on whole pixels, and <see cref="SwissQrCodeSymbology"/>, whose width the specification fixes, draw at the exact X
    /// dimension.
    /// </para>
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
    /// print no text. The text prints at the size of this font, except where the standard of a symbology sets a size. The text of an Intelligent Mail Package
    /// Barcode moves into its printed character height of 0.090 to 0.125 inch, and prints bold, as its standard requires, with a synthetic weight when the font
    /// has no bold face. The text of an Intelligent Mail barcode prints at 10 points or larger, and the
    /// text of an Australia Post barcode at 8 points or smaller. The text of a GS1-128, GS1 DataBar, ITF-14 or GS1 two-dimensional symbol prints at an em
    /// height of 2 mm or larger, which is about 5.67 points. A line that must stay within its span prints smaller when it is wider than the span, such as the
    /// quiet zone digits of a UPC symbol.
    /// </summary>
    public Font? Font { get; set; }

    /// <summary>
    /// Gets or sets the font for the caption that a data layer symbology prints above its symbol. Set <see langword="null"/> to use <see cref="Font"/>. Its
    /// size is the starting point, and <see cref="FitCaptionToSymbolWidth"/> then decides the final size. An ISBN or ISMN caption does not print below 9 point,
    /// the minimum the ISBN and ISMN manuals set.
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
    /// Resolves the X dimension that a symbol draws at, in millimetres, for a symbology whose specification gives no range of X dimensions. The result is
    /// <see cref="XDimension"/>, or the nominal X dimension when no X dimension is set, at the nearest whole number of pixels at <see cref="Dpi"/> and at
    /// least one pixel.
    /// </summary>
    /// <param name="nominal">The nominal X dimension of the symbology in millimetres.</param>
    /// <returns>The X dimension in millimetres.</returns>
    internal double ResolveXDimension(double nominal)
        => this.ResolveXDimension(0, nominal, double.PositiveInfinity);

    /// <summary>
    /// Resolves the X dimension that a symbol draws at, in millimetres: <see cref="XDimension"/>, or the nominal X dimension when no X dimension is set, at
    /// the nearest whole number of pixels at <see cref="Dpi"/>, clamped to the whole numbers of pixels inside the range of the specification. A module of a
    /// whole number of pixels draws every bar and space of one width at the same number of pixels. The encoder converts the millimetre sizes of the symbol,
    /// such as the bar height, to modules with this X dimension, so they keep their sizes.
    /// </summary>
    /// <param name="minimum">The smallest X dimension the specification permits, in millimetres.</param>
    /// <param name="nominal">The nominal X dimension of the symbology in millimetres.</param>
    /// <param name="maximum">The largest X dimension the specification permits, in millimetres.</param>
    /// <returns>The X dimension in millimetres.</returns>
    internal double ResolveXDimension(double minimum, double nominal, double maximum)
    {
        double pixelsPerMillimetre = this.Dpi / MillimetresPerInch;
        double pixels = PixelsOf(this.XDimension ?? nominal, pixelsPerMillimetre);

        // The clamp works in whole pixels: the largest whole number of pixels at or under the maximum, and the smallest at or over the minimum and at least
        // one. The tolerance keeps a limit that falls on a whole pixel, such as 0.254 mm at 100 dpi, on that pixel against the rounding error of the
        // conversion. At a low resolution a narrow range can hold no whole pixel, and the lowest count is then over the highest. The minimum applies last, so the
        // module is never under the minimum and is then over the maximum.
        double highest = Math.Floor((maximum * pixelsPerMillimetre) + LimitTolerance);
        double lowest = Math.Max(1, Math.Ceiling((minimum * pixelsPerMillimetre) - LimitTolerance));
        pixels = Math.Max(Math.Min(pixels, highest), lowest);
        return pixels / pixelsPerMillimetre;
    }

    /// <summary>
    /// Returns the number of pixels a module of an X dimension from <see cref="ResolveXDimension(double)"/> draws at, at <see cref="Dpi"/>. A symbology whose
    /// elements are not whole modules counts its runs in these pixels, so every element draws at a whole number of pixels.
    /// </summary>
    /// <param name="xDimension">The resolved X dimension in millimetres.</param>
    /// <returns>The number of pixels in one module.</returns>
    internal int ModulePixels(double xDimension)
        => (int)PixelsOf(xDimension, this.Dpi / MillimetresPerInch);

    /// <summary>
    /// Returns the nearest whole number of pixels to an X dimension, at least one pixel. Half a pixel rounds up, to the larger module.
    /// </summary>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="pixelsPerMillimetre">The number of pixels in one millimetre.</param>
    /// <returns>The number of pixels.</returns>
    private static double PixelsOf(double xDimension, double pixelsPerMillimetre)
        => Math.Max(1, Math.Round(xDimension * pixelsPerMillimetre, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Resolves the bar height in modules: <see cref="BarHeight"/> divided by the X dimension, both in millimetres, or the nominal height of the symbology when
    /// no bar height is set.
    /// </summary>
    /// <param name="xDimension">The X dimension of the symbol in millimetres, <see cref="BarcodeSymbol.XDimension"/>.</param>
    /// <param name="nominalHeight">The nominal height of the symbology in modules.</param>
    /// <returns>The bar height in modules.</returns>
    internal float ResolveBarHeight(double xDimension, float nominalHeight)
        => this.BarHeight.HasValue ? (float)(this.BarHeight.Value / xDimension) : nominalHeight;

    /// <summary>
    /// Returns whether <see cref="BarHeight"/> is set and under a minimum height that the standard of a symbology requires. The symbology rejects such a bar
    /// height. The tolerance keeps a bar height that equals the minimum against the error of the conversion of the minimum to millimetres.
    /// </summary>
    /// <param name="millimetres">The minimum height in millimetres.</param>
    /// <returns><see langword="true"/> when the bar height is under the minimum.</returns>
    internal bool IsBarHeightUnder(double millimetres)
        => this.BarHeight < millimetres - 1E-9;
}
