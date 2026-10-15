// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// One row of bars and spaces in a linear or stacked symbol: where its runs and bars are in the symbol
/// storage, and where it stands.
/// </summary>
internal readonly struct BarcodeRow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeRow"/> struct.
    /// </summary>
    /// <param name="runStart">The index of the first run of the row.</param>
    /// <param name="runCount">The number of runs of the row.</param>
    /// <param name="barStart">The index of the first bar of the row.</param>
    public BarcodeRow(int runStart, int runCount, int barStart)
    {
        this.RunStart = runStart;
        this.RunCount = runCount;
        this.BarStart = barStart;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeRow"/> struct.
    /// </summary>
    /// <param name="row">The row whose storage the new row takes.</param>
    /// <param name="top">The top of the row in modules from the top of the symbol.</param>
    /// <param name="height">The height of the row in modules: the lowest bar bottom of the row.</param>
    public BarcodeRow(in BarcodeRow row, float top, float height)
    {
        this.RunStart = row.RunStart;
        this.RunCount = row.RunCount;
        this.BarStart = row.BarStart;
        this.Top = top;
        this.Height = height;
    }

    /// <summary>
    /// Gets the index of the first run of the row.
    /// </summary>
    public int RunStart { get; }

    /// <summary>
    /// Gets the number of runs of the row. The runs alternate bar and space, starting with a bar.
    /// </summary>
    public int RunCount { get; }

    /// <summary>
    /// Gets the index of the first bar of the row.
    /// </summary>
    public int BarStart { get; }

    /// <summary>
    /// Gets the number of bars of the row: one for each run at an even index.
    /// </summary>
    public int BarCount => (this.RunCount + 1) >> 1;

    /// <summary>
    /// Gets the top of the row in modules from the top of the symbol.
    /// </summary>
    public float Top { get; }

    /// <summary>
    /// Gets the height of the row in modules: the lowest bar bottom of the row.
    /// </summary>
    public float Height { get; }
}
