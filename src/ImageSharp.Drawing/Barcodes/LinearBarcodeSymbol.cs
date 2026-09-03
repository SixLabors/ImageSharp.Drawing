// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The encoded form of a bar-based symbology. The symbol is a strictly alternating sequence of bars and spaces.
/// each bar additionally carries a height and a top offset so that symbologies with bars at different vertical
/// positions (the guard bars of ISO/IEC 15420 symbols, four-state postal bars) share one model.
/// </summary>
internal sealed class LinearBarcodeSymbol : BarcodeSymbol
{
    private readonly float widthInModules;
    private readonly float heightInModules;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearBarcodeSymbol"/> class.
    /// </summary>
    /// <param name="runWidths">The alternating bar and space widths, in modules.</param>
    /// <param name="barHeights">The height of each bar, in modules.</param>
    /// <param name="barTops">The top offset of each bar, in modules.</param>
    /// <param name="text">The human readable interpretation, empty when text is disabled.</param>
    /// <param name="leadingQuietZone">The quiet zone before the symbol, in modules.</param>
    /// <param name="trailingQuietZone">The quiet zone after the symbol, in modules.</param>
    public LinearBarcodeSymbol(
        int[] runWidths,
        float[] barHeights,
        float[] barTops,
        BarcodeTextPlacement[] text,
        int leadingQuietZone,
        int trailingQuietZone)
        : this(runWidths, barHeights, barTops, text, leadingQuietZone, trailingQuietZone, 0F)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearBarcodeSymbol"/> class.
    /// </summary>
    /// <param name="runWidths">The alternating bar and space widths, in modules.</param>
    /// <param name="barHeights">The height of each bar, in modules.</param>
    /// <param name="barTops">The top offset of each bar, in modules.</param>
    /// <param name="text">The human readable interpretation, empty when text is disabled.</param>
    /// <param name="leadingQuietZone">The quiet zone before the symbol, in modules.</param>
    /// <param name="trailingQuietZone">The quiet zone after the symbol, in modules.</param>
    /// <param name="bearerBarThickness">The thickness of the bearer bar that frames the symbol, in modules, or zero when there is none.</param>
    public LinearBarcodeSymbol(
        int[] runWidths,
        float[] barHeights,
        float[] barTops,
        BarcodeTextPlacement[] text,
        float leadingQuietZone,
        float trailingQuietZone,
        float bearerBarThickness)
        : this(runWidths, barHeights, barTops, text, leadingQuietZone, trailingQuietZone, bearerBarThickness, 1F)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearBarcodeSymbol"/> class.
    /// </summary>
    /// <param name="runWidths">The alternating bar and space widths, in run units.</param>
    /// <param name="barHeights">The height of each bar, in modules.</param>
    /// <param name="barTops">The top offset of each bar, in modules.</param>
    /// <param name="text">The human readable interpretation, empty when text is disabled.</param>
    /// <param name="leadingQuietZone">The quiet zone before the symbol, in modules.</param>
    /// <param name="trailingQuietZone">The quiet zone after the symbol, in modules.</param>
    /// <param name="bearerBarThickness">The thickness of the bearer bar that frames the symbol, in modules, or zero when there is none.</param>
    /// <param name="runUnit">The width of one run unit in modules.</param>
    public LinearBarcodeSymbol(
        int[] runWidths,
        float[] barHeights,
        float[] barTops,
        BarcodeTextPlacement[] text,
        float leadingQuietZone,
        float trailingQuietZone,
        float bearerBarThickness,
        float runUnit)
        : this(runWidths, barHeights, barTops, text, leadingQuietZone, trailingQuietZone, bearerBarThickness, runUnit, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearBarcodeSymbol"/> class.
    /// </summary>
    /// <param name="runWidths">The alternating bar and space widths, in run units.</param>
    /// <param name="barHeights">The height of each bar, in modules.</param>
    /// <param name="barTops">The top offset of each bar, in modules.</param>
    /// <param name="text">The human readable interpretation, empty when text is disabled.</param>
    /// <param name="leadingQuietZone">The quiet zone before the symbol, in modules.</param>
    /// <param name="trailingQuietZone">The quiet zone after the symbol, in modules.</param>
    /// <param name="bearerBarThickness">The thickness of the bearer bar that frames the symbol, in modules, or zero when there is none.</param>
    /// <param name="runUnit">The width of one run unit in modules.</param>
    /// <param name="uniformBars">Whether every bar is one width at one pitch.</param>
    public LinearBarcodeSymbol(
        int[] runWidths,
        float[] barHeights,
        float[] barTops,
        BarcodeTextPlacement[] text,
        float leadingQuietZone,
        float trailingQuietZone,
        float bearerBarThickness,
        float runUnit,
        bool uniformBars)
    {
        this.RunWidths = runWidths;
        this.BarHeights = barHeights;
        this.BarTops = barTops;
        this.Text = text;
        this.LeadingQuietZone = leadingQuietZone;
        this.TrailingQuietZone = trailingQuietZone;
        this.BearerBarThickness = bearerBarThickness;
        this.RunUnit = runUnit;
        this.UniformBars = uniformBars;

        float width = 0;
        for (int i = 0; i < runWidths.Length; i++)
        {
            width += runWidths[i] * runUnit;
        }

        float height = 0;
        for (int i = 0; i < barHeights.Length; i++)
        {
            float bottom = barTops[i] + barHeights[i];
            if (bottom > height)
            {
                height = bottom;
            }
        }

        this.widthInModules = width;
        this.heightInModules = height + bearerBarThickness;
    }

    /// <summary>
    /// Gets the thickness in modules of the bearer bar that frames the symbol, or zero when the symbology
    /// has none. The bars start that far below the symbol top, so the upper bearer bar butts against them,
    /// and the symbol height includes the lower bearer bar below them.
    /// </summary>
    public float BearerBarThickness { get; }

    /// <summary>
    /// Gets the alternating bar and space widths in run units, which are modules unless
    /// <see cref="RunUnit"/> says otherwise. The sequence starts and ends with a bar, so even indexes are
    /// bars and odd indexes are spaces. The first or the last bar can have zero width, which lets a symbol
    /// whose layout starts or ends blank keep its width. Quiet zones are not part of the sequence.
    /// </summary>
    public int[] RunWidths { get; }

    /// <summary>
    /// Gets the width of one unit of <see cref="RunWidths"/> in modules. It is 1 for a symbology whose
    /// elements are whole modules, and a fraction for a symbology whose bar pitch is not a whole number of
    /// bar widths, such as the postal symbologies at 22 bars per inch with bars of 0.020 inch.
    /// </summary>
    public float RunUnit { get; }

    /// <summary>
    /// Gets a value indicating whether every bar is one width at one pitch, as in the postal symbologies.
    /// The renderer then draws every run at a whole number of pixels of its own, so the bars keep one
    /// width and one pitch on the pixel grid.
    /// </summary>
    public bool UniformBars { get; }

    /// <summary>
    /// Gets the height of each bar in modules. The array holds one entry per bar, that is one entry per
    /// even index of <see cref="RunWidths"/>.
    /// </summary>
    public float[] BarHeights { get; }

    /// <summary>
    /// Gets the top offset of each bar in modules, measured from the symbol top. The array holds one entry
    /// per bar. All entries are zero for symbologies whose bars are top aligned.
    /// </summary>
    public float[] BarTops { get; }

    /// <summary>
    /// Gets the human readable interpretation. The array is empty when the symbol carries no text.
    /// </summary>
    public BarcodeTextPlacement[] Text { get; }

    /// <inheritdoc/>
    public override float WidthInModules => this.widthInModules;

    /// <inheritdoc/>
    public override float HeightInModules => this.heightInModules;

    /// <inheritdoc/>
    public override float LeadingQuietZone { get; }

    /// <inheritdoc/>
    public override float TrailingQuietZone { get; }
}
