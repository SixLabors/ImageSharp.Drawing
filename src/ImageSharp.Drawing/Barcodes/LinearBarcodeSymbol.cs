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
    {
        this.RunWidths = runWidths;
        this.BarHeights = barHeights;
        this.BarTops = barTops;
        this.Text = text;
        this.LeadingQuietZone = leadingQuietZone;
        this.TrailingQuietZone = trailingQuietZone;

        int width = 0;
        for (int i = 0; i < runWidths.Length; i++)
        {
            width += runWidths[i];
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
        this.heightInModules = height;
    }

    /// <summary>
    /// Gets the alternating bar and space widths in modules. The sequence starts and ends with a bar, so even
    /// indexes are bars and odd indexes are spaces. Quiet zones are not part of the sequence.
    /// </summary>
    public int[] RunWidths { get; }

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
    public override int LeadingQuietZone { get; }

    /// <inheritdoc/>
    public override int TrailingQuietZone { get; }
}
