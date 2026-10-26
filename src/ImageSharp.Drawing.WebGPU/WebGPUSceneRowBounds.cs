// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Holds the scratch bounds of an encoded scene or range for each bin of 16 tile rows. A tile-row
/// chunk window then gets a bound from the draws that reach it.
/// </summary>
/// <remarks>
/// <para>
/// The per-chunk scratch buffers are the path rows, path tiles, segment counts, segments and PTCL.
/// They hold only the work of the draws whose clipped bounds reach the chunk rows, because
/// path_row_alloc clips each path to the chunk window. A share of the target height is not a bound
/// when the draws are in a few rows. AddDraw therefore adds O(1) entries for each draw, a walked
/// draw adds one entry for each row, and a window query is O(1).
/// </para>
/// <para>
/// The entries are kept for each bin of 16 tile rows, which is also the alignment of the chunk
/// windows. A window widens to whole bins, so a chunk smaller than one bin gets the bound of its
/// bin. Bins keep the arrays 16 times smaller than rows, so a scene of any height pays almost
/// nothing to fill, merge and seal them.
/// </para>
/// <para>
/// Two kinds of sums are kept:
/// </para>
/// <list type="bullet">
/// <item><description>Area sums. A draw adds a value for each of its rows. Each bin receives the
/// value times the rows of the draw in that bin, through a difference array. The window sum is the
/// sum of its bins.</description></item>
/// <item><description>Overlap sums. A draw adds its full bound at the bin of its first row and at
/// the bin of its last row. The window sum is the full bound of every draw that reaches the window
/// bins. These are the draws whose first bin is before the window end, minus the draws whose last
/// bin is before the window start.</description></item>
/// </list>
/// <para>
/// The arrays are rented as one block and returned by <see cref="Dispose"/>.
/// </para>
/// </remarks>
internal sealed class WebGPUSceneRowBounds : IDisposable
{
    /// <summary>
    /// The tile rows of one bin. Must match N_TILE_Y, the bin height that chunk windows align to.
    /// </summary>
    public const int BinRows = 16;

    // Array indices in the rented block. Each array has binCount + 1 entries.
    private const int PathRowsArea = 0;
    private const int TilesArea = 1;
    private const int PtclArea = 2;
    private const int CrossingsArea = 3;
    private const int TilesFirst = 4;
    private const int TilesLast = 5;
    private const int CrossingsFirst = 6;
    private const int CrossingsLast = 7;
    private const int PtclFirst = 8;
    private const int PtclLast = 9;
    private const int EndCrossingsFirst = 10;
    private const int EndCrossingsLast = 11;
    private const int ArrayCount = 12;

    private long[]? data;
    private readonly int binCount;
    private readonly int stride;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneRowBounds"/> class.
    /// </summary>
    /// <param name="rowCount">The number of tile rows of the grid.</param>
    /// <param name="gridOriginY">The data-space Y position of tile row 0.</param>
    public WebGPUSceneRowBounds(int rowCount, int gridOriginY)
    {
        this.RowCount = rowCount;
        this.GridOriginY = gridOriginY;
        this.binCount = (rowCount + BinRows - 1) / BinRows;
        this.stride = this.binCount + 1;
        this.data = ArrayPool<long>.Shared.Rent(ArrayCount * this.stride);
        this.data.AsSpan(0, ArrayCount * this.stride).Clear();
    }

    /// <summary>
    /// Gets the number of tile rows of the grid.
    /// </summary>
    public int RowCount { get; }

    /// <summary>
    /// Gets the data-space Y position of tile row 0.
    /// </summary>
    public int GridOriginY { get; }

    /// <summary>
    /// Gets the path rows of retained scenes read in place. They count in every window.
    /// </summary>
    public long RetainedPathRows { get; private set; }

    /// <summary>
    /// Gets the path tiles of retained scenes read in place. They count in every window.
    /// </summary>
    public long RetainedTiles { get; private set; }

    /// <summary>
    /// Gets the tile crossings of retained scenes read in place. They count in every window.
    /// </summary>
    public long RetainedCrossings { get; private set; }

    /// <summary>
    /// Gets the PTCL words of retained scenes read in place. They count in every window.
    /// </summary>
    public long RetainedPtclWords { get; private set; }

    /// <summary>
    /// Adds the bounds of one draw.
    /// </summary>
    /// <param name="firstRow">The first tile row of the clipped draw bounds.</param>
    /// <param name="endRow">The tile row after the clipped draw bounds.</param>
    /// <param name="widthTiles">The width of the clipped draw bounds in tiles, or zero when the draw adds its rows with <see cref="AddRowTiles"/>.</param>
    /// <param name="tiles">The path-tile bound of the draw.</param>
    /// <param name="crossings">The tile-crossing bound of the draw.</param>
    /// <param name="ptclWords">The PTCL word bound of the draw.</param>
    /// <param name="ptclWordsPerTile">The largest PTCL word count that the draw writes in one tile.</param>
    /// <param name="crossingsPerRow">An upper bound for the tile crossings of the body lines of the draw in one tile row.</param>
    /// <param name="endCrossings">The tile crossings of the other lines of the draw, such as stroke joins and caps, which can all be in one row.</param>
    public void AddDraw(
        int firstRow,
        int endRow,
        int widthTiles,
        long tiles,
        long crossings,
        long ptclWords,
        int ptclWordsPerTile,
        long crossingsPerRow,
        long endCrossings)
    {
        // The encoder clips each draw to the range target, which is the grid of these bounds, so
        // the rows are inside the grid and not empty.
        Span<long> data = this.data;
        this.AddRows(data, PathRowsArea, firstRow, endRow, 1);
        this.AddRows(data, TilesArea, firstRow, endRow, widthTiles);
        this.AddRows(data, PtclArea, firstRow, endRow, (long)widthTiles * ptclWordsPerTile);
        this.AddRows(data, CrossingsArea, firstRow, endRow, crossingsPerRow);

        int firstBin = firstRow / BinRows;
        int lastBin = (endRow - 1) / BinRows;
        data[(TilesFirst * this.stride) + firstBin] += tiles;
        data[(TilesLast * this.stride) + lastBin] += tiles;
        data[(CrossingsFirst * this.stride) + firstBin] += crossings;
        data[(CrossingsLast * this.stride) + lastBin] += crossings;
        data[(PtclFirst * this.stride) + firstBin] += ptclWords;
        data[(PtclLast * this.stride) + lastBin] += ptclWords;
        data[(EndCrossingsFirst * this.stride) + firstBin] += endCrossings;
        data[(EndCrossingsLast * this.stride) + lastBin] += endCrossings;
    }

    /// <summary>
    /// Adds the walked tile span of one row of a draw. A draw whose rows are added here passes a
    /// zero width to <see cref="AddDraw"/>, so its area sums hold the walked spans.
    /// </summary>
    /// <param name="row">The tile row.</param>
    /// <param name="tiles">The tile span of the draw in the row.</param>
    /// <param name="ptclWords">The PTCL word bound of the draw in the row.</param>
    public void AddRowTiles(int row, long tiles, long ptclWords)
    {
        Span<long> data = this.data;
        int bin = row / BinRows;
        data[(TilesArea * this.stride) + bin] += tiles;
        data[(TilesArea * this.stride) + bin + 1] -= tiles;
        data[(PtclArea * this.stride) + bin] += ptclWords;
        data[(PtclArea * this.stride) + bin + 1] -= ptclWords;
    }

    /// <summary>
    /// Adds the totals of a retained scene read in place. Its rows are not tracked, so its totals
    /// count in every window.
    /// </summary>
    /// <param name="pathRows">The path-row bound of the retained scene.</param>
    /// <param name="tiles">The path-tile bound of the retained scene.</param>
    /// <param name="crossings">The tile-crossing bound of the retained scene.</param>
    /// <param name="ptclWords">The PTCL word bound of the retained scene.</param>
    public void AddRetained(long pathRows, long tiles, long crossings, long ptclWords)
    {
        this.RetainedPathRows += pathRows;
        this.RetainedTiles += tiles;
        this.RetainedCrossings += crossings;
        this.RetainedPtclWords += ptclWords;
    }

    /// <summary>
    /// Adds the bounds of another grid with the same rows, such as one partition of the same scene.
    /// </summary>
    /// <param name="other">The other bounds. Neither instance is sealed.</param>
    public void Add(WebGPUSceneRowBounds other)
    {
        Span<long> target = this.data.AsSpan(0, ArrayCount * this.stride);
        ReadOnlySpan<long> source = other.data.AsSpan(0, ArrayCount * other.stride);
        for (int i = 0; i < target.Length; i++)
        {
            target[i] += source[i];
        }

        this.AddRetained(other.RetainedPathRows, other.RetainedTiles, other.RetainedCrossings, other.RetainedPtclWords);
    }

    /// <summary>
    /// Converts the added entries to prefix sums. Call once, after the last add and before the
    /// first query.
    /// </summary>
    public void Seal()
    {
        Span<long> data = this.data;
        int bins = this.binCount;

        // An area array holds differences. The running sum is the per-bin value, and the entry at
        // bin b becomes the sum of the per-bin values of bins 0 to b - 1.
        for (int a = PathRowsArea; a <= CrossingsArea; a++)
        {
            Span<long> array = data.Slice(a * this.stride, this.stride);
            long binValue = 0;
            long sum = 0;
            for (int b = 0; b < bins; b++)
            {
                binValue += array[b];
                array[b] = sum;
                sum += binValue;
            }

            array[bins] = sum;
        }

        // A first or last array holds per-bin additions. The entry at bin b becomes the sum of
        // the additions at bins 0 to b - 1.
        for (int a = TilesFirst; a <= EndCrossingsLast; a++)
        {
            Span<long> array = data.Slice(a * this.stride, this.stride);
            long sum = 0;
            for (int b = 0; b < bins; b++)
            {
                long value = array[b];
                array[b] = sum;
                sum += value;
            }

            array[bins] = sum;
        }
    }

    /// <summary>
    /// Gets the bounds of the draws that reach a data-space row interval.
    /// </summary>
    /// <param name="dataY0">The data-space Y position of the window top.</param>
    /// <param name="dataY1">The data-space Y position of the window bottom.</param>
    /// <returns>The window bounds.</returns>
    public WebGPUSceneWindowBounds GetWindow(int dataY0, int dataY1)
    {
        // The window can start between grid rows when the target is not on the grid of the data,
        // so it widens to whole grid rows, and then to whole bins.
        const int binPixels = BinRows * WebGPUSceneEncoder.TileHeight;
        int start = Math.Clamp(FloorDivide(dataY0 - this.GridOriginY, binPixels), 0, this.binCount);
        int end = Math.Clamp(-FloorDivide(this.GridOriginY - dataY1, binPixels), start, this.binCount);
        ReadOnlySpan<long> data = this.data;

        long pathRows = this.Area(data, PathRowsArea, start, end);
        long tiles = Math.Min(this.Area(data, TilesArea, start, end), this.Overlap(data, TilesFirst, TilesLast, start, end));
        long crossings = Math.Min(
            this.Area(data, CrossingsArea, start, end) + this.Overlap(data, EndCrossingsFirst, EndCrossingsLast, start, end),
            this.Overlap(data, CrossingsFirst, CrossingsLast, start, end));

        long ptclWords = Math.Min(this.Area(data, PtclArea, start, end), this.Overlap(data, PtclFirst, PtclLast, start, end));

        return new WebGPUSceneWindowBounds(
            pathRows + this.RetainedPathRows,
            tiles + this.RetainedTiles,
            crossings + this.RetainedCrossings,
            ptclWords + this.RetainedPtclWords);
    }

    /// <summary>
    /// Returns the rented block.
    /// </summary>
    public void Dispose()
    {
        long[]? data = this.data;
        if (data is null)
        {
            return;
        }

        this.data = null;
        ArrayPool<long>.Shared.Return(data);
    }

    /// <summary>
    /// Adds a value for each tile row of an interval to an area array.
    /// </summary>
    /// <remarks>
    /// A bin receives the value times the rows of the interval in that bin. The first and last bins
    /// can be partial, so they get a single-bin entry. The full bins between them share one
    /// difference entry.
    /// </remarks>
    /// <param name="data">The unsealed block.</param>
    /// <param name="array">The array index.</param>
    /// <param name="firstRow">The first tile row.</param>
    /// <param name="endRow">The tile row after the interval.</param>
    /// <param name="valuePerRow">The value of each row.</param>
    private void AddRows(Span<long> data, int array, int firstRow, int endRow, long valuePerRow)
    {
        if (valuePerRow == 0)
        {
            return;
        }

        int offset = array * this.stride;
        int firstBin = firstRow / BinRows;
        int lastBin = (endRow - 1) / BinRows;
        if (firstBin == lastBin)
        {
            long value = valuePerRow * (endRow - firstRow);
            data[offset + firstBin] += value;
            data[offset + firstBin + 1] -= value;
            return;
        }

        long firstValue = valuePerRow * (((firstBin + 1) * BinRows) - firstRow);
        data[offset + firstBin] += firstValue;
        data[offset + firstBin + 1] -= firstValue;

        long lastValue = valuePerRow * (endRow - (lastBin * BinRows));
        data[offset + lastBin] += lastValue;
        data[offset + lastBin + 1] -= lastValue;

        if (lastBin > firstBin + 1)
        {
            long fullValue = valuePerRow * BinRows;
            data[offset + firstBin + 1] += fullValue;
            data[offset + lastBin] -= fullValue;
        }
    }

    /// <summary>
    /// Gets the window sum of an area array.
    /// </summary>
    /// <param name="data">The sealed block.</param>
    /// <param name="array">The array index.</param>
    /// <param name="start">The first window bin.</param>
    /// <param name="end">The bin after the window.</param>
    /// <returns>The window sum.</returns>
    private long Area(ReadOnlySpan<long> data, int array, int start, int end)
        => data[(array * this.stride) + end] - data[(array * this.stride) + start];

    /// <summary>
    /// Gets the full bounds of the draws that reach a window.
    /// </summary>
    /// <param name="data">The sealed block.</param>
    /// <param name="first">The index of the array keyed by the first bin of each draw.</param>
    /// <param name="last">The index of the array keyed by the last bin of each draw.</param>
    /// <param name="start">The first window bin.</param>
    /// <param name="end">The bin after the window.</param>
    /// <returns>The sum.</returns>
    private long Overlap(ReadOnlySpan<long> data, int first, int last, int start, int end)
        => data[(first * this.stride) + end] - data[(last * this.stride) + start];

    /// <summary>
    /// Divides and rounds toward negative infinity.
    /// </summary>
    /// <param name="value">The dividend.</param>
    /// <param name="divisor">The positive divisor.</param>
    /// <returns>The quotient.</returns>
    private static int FloorDivide(int value, int divisor)
        => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
}
