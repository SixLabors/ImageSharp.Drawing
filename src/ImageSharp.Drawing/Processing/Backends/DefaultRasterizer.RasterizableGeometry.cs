// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static partial class DefaultRasterizer
{
    /// <summary>
    /// Flush-scoped retained row-local raster payload for one prepared fill geometry.
    /// </summary>
    internal sealed class RasterizableGeometry : IDisposable
    {
        private readonly RasterizableBandInfo[] bandInfos;
        private readonly LineArrayX16Y16Block?[]? linesX16;
        private readonly LineArrayX32Y16Block?[]? linesX32;
        private readonly int[] firstBlockLineCounts;
        private readonly IMemoryOwner<int>?[] startCoverTable;

        /// <summary>
        /// Initializes a new instance of the <see cref="RasterizableGeometry"/> class.
        /// </summary>
        /// <param name="firstRowBandIndex">The first absolute row-band index touched by the geometry.</param>
        /// <param name="rowBandCount">The number of retained local row bands owned by the geometry.</param>
        /// <param name="width">The geometry-local visible band width in pixels.</param>
        /// <param name="wordsPerRow">The bit-vector width in machine words required by the geometry.</param>
        /// <param name="coverStride">The scanner cover/area stride required by the geometry.</param>
        /// <param name="bandHeight">The retained row-band height in pixels.</param>
        /// <param name="isX16">Indicates whether the geometry uses the narrow X16Y16 line encoding.</param>
        /// <param name="bandInfos">The retained metadata for each local row band.</param>
        /// <param name="linesX16">The retained narrow line chains for each local row band.</param>
        /// <param name="linesX32">The retained wide line chains for each local row band.</param>
        /// <param name="firstBlockLineCounts">The valid line count in each front retained block.</param>
        /// <param name="startCoverTable">The retained start-cover table for each local row band.</param>
        public RasterizableGeometry(
            int firstRowBandIndex,
            int rowBandCount,
            int width,
            int wordsPerRow,
            int coverStride,
            int bandHeight,
            bool isX16,
            RasterizableBandInfo[] bandInfos,
            LineArrayX16Y16Block?[]? linesX16,
            LineArrayX32Y16Block?[]? linesX32,
            int[] firstBlockLineCounts,
            IMemoryOwner<int>?[] startCoverTable)
        {
            this.FirstRowBandIndex = firstRowBandIndex;
            this.RowBandCount = rowBandCount;
            this.Width = width;
            this.WordsPerRow = wordsPerRow;
            this.CoverStride = coverStride;
            this.BandHeight = bandHeight;
            this.IsX16 = isX16;
            this.bandInfos = bandInfos;
            this.linesX16 = linesX16;
            this.linesX32 = linesX32;
            this.firstBlockLineCounts = firstBlockLineCounts;
            this.startCoverTable = startCoverTable;
        }

        /// <summary>
        /// Gets the first absolute row-band index touched by this geometry.
        /// </summary>
        public int FirstRowBandIndex { get; }

        /// <summary>
        /// Gets the number of retained local row bands owned by this geometry.
        /// </summary>
        public int RowBandCount { get; }

        /// <summary>
        /// Gets the geometry-local visible band width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the bit-vector width in machine words required by this geometry.
        /// </summary>
        public int WordsPerRow { get; }

        /// <summary>
        /// Gets the scanner cover/area stride required by this geometry.
        /// </summary>
        public int CoverStride { get; }

        /// <summary>
        /// Gets the retained row-band height in pixels.
        /// </summary>
        public int BandHeight { get; }

        /// <summary>
        /// Gets a value indicating whether this geometry uses Blaze's narrow X16Y16 line arrays.
        /// </summary>
        public bool IsX16 { get; }

        /// <summary>
        /// Returns <see langword="true"/> when the given local row band has retained coverage payload.
        /// </summary>
        /// <param name="localRowIndex">The local row band index.</param>
        /// <returns><see langword="true"/> when the row band has retained coverage; otherwise <see langword="false"/>.</returns>
        public bool HasCoverage(int localRowIndex) => this.bandInfos[localRowIndex].HasCoverage;

        /// <summary>
        /// Gets the retained narrow line block chain for one local row.
        /// </summary>
        /// <param name="localRowIndex">The local row band index.</param>
        /// <returns>The retained narrow line chain for the row.</returns>
        public LineArrayX16Y16Block? GetLinesX16ForRow(int localRowIndex) => this.linesX16![localRowIndex];

        /// <summary>
        /// Gets the retained wide line block chain for one local row.
        /// </summary>
        /// <param name="localRowIndex">The local row band index.</param>
        /// <returns>The retained wide line chain for the row.</returns>
        public LineArrayX32Y16Block? GetLinesX32ForRow(int localRowIndex) => this.linesX32![localRowIndex];

        /// <summary>
        /// Gets the number of valid lines in the first retained block for a local row.
        /// </summary>
        /// <param name="localRowIndex">The local row band index.</param>
        /// <returns>The valid line count in the front retained block.</returns>
        public int GetFirstBlockLineCountForRow(int localRowIndex) => this.firstBlockLineCounts[localRowIndex];

        /// <summary>
        /// Gets the retained start-cover table entry for a local row, if one exists.
        /// </summary>
        /// <param name="localRowIndex">The local row band index.</param>
        /// <returns>The retained start-cover span for the row.</returns>
        public ReadOnlySpan<int> GetCoversForRow(int localRowIndex)
        {
            IMemoryOwner<int>? covers = this.startCoverTable[localRowIndex];
            return covers is null ? ReadOnlySpan<int>.Empty : covers.Memory.Span[..this.BandHeight];
        }

        /// <summary>
        /// Gets the retained start-cover row payload without further interpretation, matching Blaze naming.
        /// </summary>
        /// <param name="localRowIndex">The local row band index.</param>
        /// <returns>The retained start-cover span for the row.</returns>
        public ReadOnlySpan<int> GetActualCoversForRow(int localRowIndex) => this.GetCoversForRow(localRowIndex);

        /// <summary>
        /// Gets retained metadata for one local row band.
        /// </summary>
        /// <param name="localRowIndex">The local row band index.</param>
        /// <returns>The retained band metadata.</returns>
        public RasterizableBandInfo GetBandInfo(int localRowIndex) => this.bandInfos[localRowIndex];

        /// <summary>
        /// Releases the retained line blocks and start-cover storage.
        /// </summary>
        public void Dispose()
        {
            if (this.linesX16 is not null)
            {
                Array.Clear(this.linesX16);
            }

            if (this.linesX32 is not null)
            {
                Array.Clear(this.linesX32);
            }

            for (int i = 0; i < this.startCoverTable.Length; i++)
            {
                this.startCoverTable[i]?.Dispose();
            }
        }
    }
}
