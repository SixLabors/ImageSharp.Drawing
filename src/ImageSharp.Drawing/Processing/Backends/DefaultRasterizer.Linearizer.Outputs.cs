// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static partial class DefaultRasterizer
{
    /// <summary>
    /// Contract implemented by retained line-block payloads.
    /// </summary>
    /// <typeparam name="TSelf">The concrete retained line-block type.</typeparam>
    internal interface ILineBlock<TSelf>
        where TSelf : class, ILineBlock<TSelf>
    {
        /// <summary>
        /// Gets the number of lines stored in a full block.
        /// </summary>
        public static abstract int LineCount { get; }

        /// <summary>
        /// Gets the next block in the retained chain.
        /// </summary>
        public TSelf? Next { get; }

        /// <summary>
        /// Rasterizes the leading <paramref name="count"/> lines from this block.
        /// </summary>
        /// <param name="count">The number of leading lines to rasterize from this block.</param>
        /// <param name="context">The mutable scan-conversion context to write into.</param>
        public void Rasterize(int count, ref Context context);
    }

    /// <summary>
    /// Retained tile-space bounds for one linearized geometry payload.
    /// </summary>
    internal readonly struct TileBounds
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TileBounds"/> struct.
        /// </summary>
        /// <param name="x">The tile-space left coordinate.</param>
        /// <param name="y">The tile-space top coordinate.</param>
        /// <param name="columnCount">The tile-space column count.</param>
        /// <param name="rowCount">The tile-space row count.</param>
        public TileBounds(int x, int y, int columnCount, int rowCount)
        {
            this.X = x;
            this.Y = y;
            this.ColumnCount = columnCount;
            this.RowCount = rowCount;
        }

        /// <summary>
        /// Gets the tile-space left coordinate.
        /// </summary>
        public int X { get; }

        /// <summary>
        /// Gets the tile-space top coordinate.
        /// </summary>
        public int Y { get; }

        /// <summary>
        /// Gets the tile-space column count.
        /// </summary>
        public int ColumnCount { get; }

        /// <summary>
        /// Gets the tile-space row count.
        /// </summary>
        public int RowCount { get; }
    }

    /// <summary>
    /// Holds the finalized retained raster payload for one line-block encoding.
    /// </summary>
    /// <typeparam name="TLineBlock">The concrete retained line-block type.</typeparam>
    internal sealed class LinearizedRasterData<TLineBlock>
        where TLineBlock : class, ILineBlock<TLineBlock>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LinearizedRasterData{TLineBlock}"/> class.
        /// </summary>
        /// <param name="geometry">The source linear geometry.</param>
        /// <param name="bounds">The retained tile-space bounds.</param>
        /// <param name="lines">The retained line-block chain for each row band.</param>
        /// <param name="firstBlockLineCounts">The valid line count in each row's front block.</param>
        /// <param name="startCoverTable">The retained start-cover seeds for each row band.</param>
        public LinearizedRasterData(
            LinearGeometry geometry,
            TileBounds bounds,
            TLineBlock?[] lines,
            int[] firstBlockLineCounts,
            IMemoryOwner<int>?[] startCoverTable)
        {
            this.Geometry = geometry;
            this.Bounds = bounds;
            this.Lines = lines;
            this.FirstBlockLineCounts = firstBlockLineCounts;
            this.StartCoverTable = startCoverTable;
        }

        /// <summary>
        /// Gets the source linear geometry.
        /// </summary>
        public LinearGeometry Geometry { get; }

        /// <summary>
        /// Gets the retained tile-space bounds.
        /// </summary>
        public TileBounds Bounds { get; }

        /// <summary>
        /// Gets the retained line-block chain for each row band.
        /// </summary>
        public TLineBlock?[] Lines { get; }

        /// <summary>
        /// Gets the valid front-block line count for each row band.
        /// </summary>
        public int[] FirstBlockLineCounts { get; }

        /// <summary>
        /// Gets the retained start-cover seeds for each row band.
        /// </summary>
        public IMemoryOwner<int>?[] StartCoverTable { get; }

        /// <summary>
        /// Iterates the retained line blocks for one row band.
        /// </summary>
        /// <param name="rowIndex">The row band index to iterate.</param>
        /// <param name="context">The mutable scan-conversion context.</param>
        public void Iterate(int rowIndex, ref Context context)
        {
            int count = this.FirstBlockLineCounts[rowIndex];
            TLineBlock? lineBlock = this.Lines[rowIndex];
            while (lineBlock is not null)
            {
                lineBlock.Rasterize(count, ref context);
                lineBlock = lineBlock.Next;
                count = TLineBlock.LineCount;
            }
        }
    }
}
