// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The encoded form of a multi-row symbology. The rows stack from the top down, each one a
/// <see cref="LinearBarcodeSymbol"/> with its own bars, and a separator between two rows is a row of its
/// own whose bars are the separator. Edge bars close the top and the bottom of the symbol and extend
/// across the quiet zones when those are drawn. The rows share one width, one run unit and one bar
/// rounding rule.
/// </summary>
internal sealed class StackedBarcodeSymbol : BarcodeSymbol
{
    private readonly float[] rowTops;
    private readonly float widthInModules;
    private readonly float heightInModules;

    /// <summary>
    /// Initializes a new instance of the <see cref="StackedBarcodeSymbol"/> class.
    /// </summary>
    /// <param name="rows">The rows from the top down, separators included.</param>
    /// <param name="edgeBarHeight">The height in modules of the bars that close the top and the bottom of the symbol, or zero when there are none.</param>
    /// <param name="text">The human readable interpretation, empty when text is disabled.</param>
    /// <param name="leadingQuietZone">The quiet zone before the symbol, in modules.</param>
    /// <param name="trailingQuietZone">The quiet zone after the symbol, in modules.</param>
    public StackedBarcodeSymbol(LinearBarcodeSymbol[] rows, float edgeBarHeight, BarcodeTextPlacement[] text, float leadingQuietZone, float trailingQuietZone)
        : this(rows, edgeBarHeight, text, leadingQuietZone, trailingQuietZone, 0F, 0F)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StackedBarcodeSymbol"/> class.
    /// </summary>
    /// <param name="rows">The rows from the top down, separators included.</param>
    /// <param name="edgeBarHeight">The height in modules of the bars that close the top and the bottom of the symbol, or zero when there are none.</param>
    /// <param name="text">The human readable interpretation, empty when text is disabled.</param>
    /// <param name="leadingQuietZone">The quiet zone before the symbol, in modules.</param>
    /// <param name="trailingQuietZone">The quiet zone after the symbol, in modules.</param>
    /// <param name="topQuietZone">The quiet zone above the symbol, in modules.</param>
    /// <param name="bottomQuietZone">The quiet zone below the symbol, in modules.</param>
    public StackedBarcodeSymbol(
        LinearBarcodeSymbol[] rows,
        float edgeBarHeight,
        BarcodeTextPlacement[] text,
        float leadingQuietZone,
        float trailingQuietZone,
        float topQuietZone,
        float bottomQuietZone)
    {
        Guard.NotNull(rows, nameof(rows));
        Guard.MustBeGreaterThan(rows.Length, 0, nameof(rows));

        LinearBarcodeSymbol first = rows[0];
        this.rowTops = new float[rows.Length];

        // Each row top adds every row above it, so the sum is in double and each top converts to float
        // once.
        double top = edgeBarHeight;
        for (int i = 0; i < rows.Length; i++)
        {
            LinearBarcodeSymbol row = rows[i];
            if (row.WidthInModules != first.WidthInModules || row.RunUnit != first.RunUnit || row.UniformBars != first.UniformBars)
            {
                throw new ArgumentException("Every row must share the width, the run unit and the bar rounding of the first row.", nameof(rows));
            }

            this.rowTops[i] = (float)top;
            top += row.HeightInModules;
        }

        this.Rows = rows;
        this.EdgeBarHeight = edgeBarHeight;
        this.Text = text;
        this.LeadingQuietZone = leadingQuietZone;
        this.TrailingQuietZone = trailingQuietZone;
        this.TopQuietZone = topQuietZone;
        this.BottomQuietZone = bottomQuietZone;
        this.widthInModules = first.WidthInModules;
        this.heightInModules = (float)(top + edgeBarHeight);
    }

    /// <summary>
    /// Gets the rows from the top down, separators included.
    /// </summary>
    public LinearBarcodeSymbol[] Rows { get; }

    /// <summary>
    /// Gets the top of each row in modules from the symbol top. The first row starts below the top edge
    /// bar, and each row that follows starts where the row above it ends.
    /// </summary>
    public ReadOnlySpan<float> RowTops => this.rowTops;

    /// <summary>
    /// Gets the height in modules of the bars that close the top and the bottom of the symbol, or zero
    /// when the symbology has none. The symbol height includes both.
    /// </summary>
    public float EdgeBarHeight { get; }

    /// <inheritdoc/>
    public override BarcodeTextPlacement[] Text { get; }

    /// <inheritdoc/>
    public override float WidthInModules => this.widthInModules;

    /// <inheritdoc/>
    public override float HeightInModules => this.heightInModules;

    /// <inheritdoc/>
    public override float LeadingQuietZone { get; }

    /// <inheritdoc/>
    public override float TrailingQuietZone { get; }

    /// <inheritdoc/>
    public override float TopQuietZone { get; }

    /// <inheritdoc/>
    public override float BottomQuietZone { get; }
}
