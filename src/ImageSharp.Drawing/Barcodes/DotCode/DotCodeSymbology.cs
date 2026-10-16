// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The DotCode symbology: a grid of round dots with no finder pattern, at least 5 columns and 5 rows whose sum is odd, and a quiet zone of three modules on
/// every side. It carries the characters U+0000 to U+00FF with Reed-Solomon error correction.
/// </summary>
public sealed class DotCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DotCodeSymbology"/> class. The symbol takes the smallest size near a width to height ratio of 3:2 that
    /// holds the data.
    /// </summary>
    public DotCodeSymbology()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DotCodeSymbology"/> class with a fixed number of columns. The symbol takes the fewest rows that hold the
    /// data.
    /// </summary>
    /// <param name="columns">The number of columns, 5 to 200.</param>
    public DotCodeSymbology(int columns)
    {
        Guard.MustBeBetweenOrEqualTo(columns, DotCodeEncoder.MinimumSide, DotCodeEncoder.MaximumSide, nameof(columns));
        this.Columns = columns;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DotCodeSymbology"/> class with a fixed size.
    /// </summary>
    /// <param name="columns">The number of columns, 5 to 200.</param>
    /// <param name="rows">The number of rows, 5 to 200. The columns and the rows sum to an odd number.</param>
    public DotCodeSymbology(int columns, int rows)
        : this(columns)
    {
        Guard.MustBeBetweenOrEqualTo(rows, DotCodeEncoder.MinimumSide, DotCodeEncoder.MaximumSide, nameof(rows));
        if (((columns + rows) & 1) == 0)
        {
            throw new ArgumentException("The columns and the rows of a DotCode symbol must sum to an odd number.", nameof(rows));
        }

        this.Rows = rows;
    }

    /// <summary>
    /// Gets the number of columns, or <see langword="null"/> when the size follows from the data.
    /// </summary>
    public int? Columns { get; }

    /// <summary>
    /// Gets the number of rows, or <see langword="null"/> when the rows follow from the data.
    /// </summary>
    public int? Rows { get; }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));
        DotCodeEncoder.BuildSymbol(text, false, this.Columns ?? 0, this.Rows ?? 0, options.XDimension ?? PointXDimension, ref symbol);
    }
}
