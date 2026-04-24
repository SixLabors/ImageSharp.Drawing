// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Fixed-point rasterizer that converts retained fill geometry into per-row coverage.
/// </summary>
/// <remarks>
/// The rasterizer works in scene-aligned row bands. Each retained band stores compact line blocks
/// plus optional start-cover seeds, and execution replays that retained payload directly against
/// worker-local scratch without rebuilding geometry on every row.
/// </remarks>
internal static partial class DefaultRasterizer
{
    // Tile height used by the parallel row-tiling pipeline.
    internal const int DefaultTileHeight = 16;

    private const int FixedShift = 8;
    private const int FixedOne = 1 << FixedShift;
    private const int MaximumDelta = 2048 << FixedShift;
    private static readonly int WordBitCount = nint.Size * 8;
    private const int AreaToCoverageShift = 9;
    private const int CoverageStepCount = 256;
    private const int EvenOddMask = (CoverageStepCount * 2) - 1;
    private const int EvenOddPeriod = CoverageStepCount * 2;
    private const float CoverageScale = 1F / CoverageStepCount;

    /// <summary>
    /// Gets the preferred scene row height used by the CPU rasterizer.
    /// </summary>
    internal static int PreferredRowHeight => DefaultTileHeight;

    /// <summary>
    /// Executes one retained rasterizable row item against a reusable scanner context.
    /// </summary>
    internal static void ExecuteRasterizableItem<TRowHandler>(
        ref Context context,
        in RasterizableItem item,
        in RasterizableBandInfo bandInfo,
        Span<float> scanline,
        ref TRowHandler rowHandler)
        where TRowHandler : struct, IRasterizerCoverageRowHandler
    {
        context.Reconfigure(
            bandInfo.Width,
            bandInfo.WordsPerRow,
            bandInfo.CoverStride,
            bandInfo.BandHeight,
            bandInfo.IntersectionRule,
            bandInfo.RasterizationMode,
            bandInfo.AntialiasThreshold);

        context.SeedStartCovers(item.GetActualCovers());
        if (item.Rasterizable.IsX16)
        {
            LineArrayX16Y16Block? lines = item.GetLineArrayX16();
            lines?.Iterate(item.GetFirstBlockLineCount(), ref context);
        }
        else
        {
            LineArrayX32Y16Block? lines = item.GetLineArrayX32();
            lines?.Iterate(item.GetFirstBlockLineCount(), ref context);
        }

        context.EmitCoverageRows(bandInfo.DestinationTop, bandInfo.DestinationLeft, scanline, ref rowHandler);
        context.ResetTouchedRows();
    }

    /// <summary>
    /// Executes one retained stroke row item against a reusable scanner context.
    /// </summary>
    internal static void ExecuteStrokeRasterizableItem<TRowHandler>(
        ref Context context,
        in StrokeRasterizableItem item,
        in RasterizableBandInfo bandInfo,
        Span<float> scanline,
        Span<float> strokeBandCoverage,
        ref TRowHandler rowHandler)
        where TRowHandler : struct, IRasterizerCoverageRowHandler
    {
        context.Reconfigure(
            bandInfo.Width,
            bandInfo.WordsPerRow,
            bandInfo.CoverStride,
            bandInfo.BandHeight,
            bandInfo.IntersectionRule,
            bandInfo.RasterizationMode,
            bandInfo.AntialiasThreshold);

        item.Rasterizable.ExecuteBand(ref context, in bandInfo, scanline, strokeBandCoverage, ref rowHandler);
    }

    /// <summary>
    /// Converts bit count to the number of machine words needed to hold the bitset row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BitVectorsForMaxBitCount(int maxBitCount) => (maxBitCount + WordBitCount - 1) / WordBitCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static WorkerScratch CreateWorkerScratch(MemoryAllocator allocator, int width)
        => WorkerScratch.Create(allocator, BitVectorsForMaxBitCount(width), checked(width << 1), width, PreferredRowHeight);

    /// <summary>
    /// Converts a float coordinate to signed 24.8 fixed-point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FloatToFixed24Dot8(float value) => (int)MathF.Round(value * FixedOne);

    /// <summary>
    /// Returns one when a fixed-point value lies exactly on a cell boundary at or below zero.
    /// This is used to keep edge ownership consistent for vertical lines.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindAdjustment(int value)
    {
        int lte0 = ~((value - 1) >> 31) & 1;
        int divisibleBy256 = (((value & (FixedOne - 1)) - 1) >> 31) & 1;
        return lte0 & divisibleBy256;
    }

    /// <summary>
    /// Machine-word trailing zero count used for sparse bitset iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int TrailingZeroCount(nuint value)
        => nint.Size == sizeof(ulong)
            ? BitOperations.TrailingZeroCount((ulong)value)
            : BitOperations.TrailingZeroCount((uint)value);

    /// <summary>
    /// Throws when the requested raster interest exceeds the scanner's indexing limits.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInterestBoundsTooLarge()
        => throw new ImageProcessingException("The rasterizer interest bounds are too large for DefaultRasterizer buffers.");

    /// <summary>
    /// Creates retained row-local raster payload for one lowered geometry.
    /// </summary>
    internal static RasterizableGeometry? CreateRasterizableGeometry(
        LinearGeometry geometry,
        Matrix4x4 residual,
        int translateX,
        int translateY,
        in RasterizerOptions options,
        MemoryAllocator allocator)
    {
        float samplingOffsetX = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        float samplingOffsetY = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;

        RectangleF translatedBounds = residual.IsIdentity ? geometry.Info.Bounds : RectangleF.Transform(geometry.Info.Bounds, residual);
        translatedBounds.Offset(translateX + samplingOffsetX, translateY + samplingOffsetY);

        // The retained clipper ignores segments at the maximum X edge,
        // so extend the right bound by one pixel to keep closing vertical edges available.
        Rectangle geometryBounds = Rectangle.FromLTRB(
            (int)MathF.Floor(translatedBounds.Left),
            (int)MathF.Floor(translatedBounds.Top),
            (int)MathF.Ceiling(translatedBounds.Right) + 1,
            (int)MathF.Ceiling(translatedBounds.Bottom));

        Rectangle clippedBounds = Rectangle.Intersect(geometryBounds, options.Interest);
        if (clippedBounds.Width <= 0 || clippedBounds.Height <= 0)
        {
            return null;
        }

        int width = clippedBounds.Width;
        int height = clippedBounds.Height;
        int firstRowBandIndex = clippedBounds.Top / PreferredRowHeight;
        int lastRowBandIndex = (clippedBounds.Bottom - 1) / PreferredRowHeight;
        int rowBandCount = lastRowBandIndex - firstRowBandIndex + 1;
        int wordsPerRow = BitVectorsForMaxBitCount(width);
        int coverStride = checked(width << 1);

        if (wordsPerRow <= 0 || coverStride <= 0)
        {
            ThrowInterestBoundsTooLarge();
        }

        if (width < 128)
        {
            LinearizerX16Y16 linearizer = new(
                geometry,
                residual,
                translateX,
                translateY,
                clippedBounds.Left,
                clippedBounds.Top,
                width,
                height,
                firstRowBandIndex,
                rowBandCount,
                samplingOffsetX,
                samplingOffsetY,
                allocator);

            if (!linearizer.TryProcess(out LinearizedRasterData<LineArrayX16Y16Block> result))
            {
                return null;
            }

            RasterizableBandInfo[] bandInfos = new RasterizableBandInfo[rowBandCount];
            for (int i = 0; i < rowBandCount; i++)
            {
                int bandTop = (firstRowBandIndex + i) * PreferredRowHeight;
                bool hasStartCovers = result.StartCoverTable[i] is not null;
                bandInfos[i] = new RasterizableBandInfo(
                    CountLines(result.Lines[i], result.FirstBlockLineCounts[i]),
                    PreferredRowHeight,
                    width,
                    wordsPerRow,
                    coverStride,
                    clippedBounds.Left,
                    bandTop,
                    options.IntersectionRule,
                    options.RasterizationMode,
                    options.AntialiasThreshold,
                    hasStartCovers);
            }

            return new RasterizableGeometry(
                firstRowBandIndex,
                rowBandCount,
                width,
                wordsPerRow,
                coverStride,
                PreferredRowHeight,
                isX16: true,
                bandInfos,
                result.Lines,
                null,
                result.FirstBlockLineCounts,
                result.StartCoverTable);
        }
        else
        {
            LinearizerX32Y16 linearizer = new(
                geometry,
                residual,
                translateX,
                translateY,
                clippedBounds.Left,
                clippedBounds.Top,
                width,
                height,
                firstRowBandIndex,
                rowBandCount,
                samplingOffsetX,
                samplingOffsetY,
                allocator);

            if (!linearizer.TryProcess(out LinearizedRasterData<LineArrayX32Y16Block> result))
            {
                return null;
            }

            RasterizableBandInfo[] bandInfos = new RasterizableBandInfo[rowBandCount];
            for (int i = 0; i < rowBandCount; i++)
            {
                int bandTop = (firstRowBandIndex + i) * PreferredRowHeight;
                bool hasStartCovers = result.StartCoverTable[i] is not null;
                bandInfos[i] = new RasterizableBandInfo(
                    CountLines(result.Lines[i], result.FirstBlockLineCounts[i]),
                    PreferredRowHeight,
                    width,
                    wordsPerRow,
                    coverStride,
                    clippedBounds.Left,
                    bandTop,
                    options.IntersectionRule,
                    options.RasterizationMode,
                    options.AntialiasThreshold,
                    hasStartCovers);
            }

            return new RasterizableGeometry(
                firstRowBandIndex,
                rowBandCount,
                width,
                wordsPerRow,
                coverStride,
                PreferredRowHeight,
                isX16: false,
                bandInfos,
                null,
                result.Lines,
                result.FirstBlockLineCounts,
                result.StartCoverTable);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountLines<TLineBlock>(TLineBlock? firstLineBlock, int firstBlockLineCount)
        where TLineBlock : class, ILineBlock<TLineBlock>
    {
        if (firstLineBlock is null)
        {
            return 0;
        }

        int count = firstBlockLineCount;
        TLineBlock? block = firstLineBlock.Next;
        while (block is not null)
        {
            count += TLineBlock.LineCount;
            block = block.Next;
        }

        return count;
    }

    /// <summary>
    /// Band/tile-local scanner context that owns mutable coverage accumulation state.
    /// </summary>
    /// <remarks>
    /// Instances are intentionally stack-bound to keep hot-path data in spans and avoid heap churn.
    /// </remarks>
    internal ref struct Context
    {
        private readonly Span<nuint> bitVectors;
        private readonly Span<int> coverArea;
        private readonly Span<int> startCover;
        private readonly Span<int> rowMinTouchedColumn;
        private readonly Span<int> rowMaxTouchedColumn;
        private readonly Span<byte> rowHasBits;
        private readonly Span<byte> rowTouched;
        private readonly Span<int> touchedRows;
        private int width;
        private int height;
        private int wordsPerRow;
        private int coverStride;
        private IntersectionRule intersectionRule;
        private RasterizationMode rasterizationMode;
        private float antialiasThreshold;
        private int touchedRowCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="Context"/> struct.
        /// </summary>
        /// <param name="bitVectors">Scratch bit vectors that record which cells in each row received edge contributions.</param>
        /// <param name="coverArea">Scratch cell table that accumulates signed cover/area values for the current band.</param>
        /// <param name="startCover">Scratch per-row start-cover values carried into coverage emission.</param>
        /// <param name="rowMinTouchedColumn">Scratch per-row minimum touched column bounds.</param>
        /// <param name="rowMaxTouchedColumn">Scratch per-row maximum touched column bounds.</param>
        /// <param name="rowHasBits">Scratch flags indicating whether a row has any bit-vector backed cell data.</param>
        /// <param name="rowTouched">Scratch flags indicating whether a row has received any contribution in the current band.</param>
        /// <param name="touchedRows">Scratch list of rows touched in the current band so emission can skip untouched rows.</param>
        /// <param name="intersectionRule">The fill rule used when converting accumulated winding/coverage into final alpha.</param>
        /// <param name="rasterizationMode">The rasterization mode that controls how antialiasing thresholds are interpreted.</param>
        /// <param name="antialiasThreshold">The threshold used when antialiasing is conditionally reduced or disabled.</param>
        public Context(
            Span<nuint> bitVectors,
            Span<int> coverArea,
            Span<int> startCover,
            Span<int> rowMinTouchedColumn,
            Span<int> rowMaxTouchedColumn,
            Span<byte> rowHasBits,
            Span<byte> rowTouched,
            Span<int> touchedRows,
            IntersectionRule intersectionRule,
            RasterizationMode rasterizationMode,
            float antialiasThreshold)
        {
            this.bitVectors = bitVectors;
            this.coverArea = coverArea;
            this.startCover = startCover;
            this.rowMinTouchedColumn = rowMinTouchedColumn;
            this.rowMaxTouchedColumn = rowMaxTouchedColumn;
            this.rowHasBits = rowHasBits;
            this.rowTouched = rowTouched;
            this.touchedRows = touchedRows;
            this.width = 0;
            this.height = 0;
            this.wordsPerRow = 0;
            this.coverStride = 0;
            this.intersectionRule = intersectionRule;
            this.rasterizationMode = rasterizationMode;
            this.antialiasThreshold = antialiasThreshold;
            this.touchedRowCount = 0;
        }

        /// <summary>
        /// Reconfigures this reusable context for a specific destination band without reallocating its scratch storage.
        /// </summary>
        /// <param name="width">The width, in pixels, of the current destination band.</param>
        /// <param name="wordsPerRow">The number of machine words used to represent one row of bit-vector coverage.</param>
        /// <param name="coverStride">The stride, in cells, between rows in the cover/area table.</param>
        /// <param name="height">The height, in pixels, of the current destination band.</param>
        /// <param name="intersectionRule">The fill rule used when converting accumulated winding/coverage into final alpha.</param>
        /// <param name="rasterizationMode">The rasterization mode that controls how antialiasing thresholds are interpreted.</param>
        /// <param name="antialiasThreshold">The threshold used when antialiasing is conditionally reduced or disabled.</param>
        public void Reconfigure(
            int width,
            int wordsPerRow,
            int coverStride,
            int height,
            IntersectionRule intersectionRule,
            RasterizationMode rasterizationMode,
            float antialiasThreshold)
        {
            this.width = width;
            this.height = height;
            this.wordsPerRow = wordsPerRow;
            this.coverStride = coverStride;
            this.intersectionRule = intersectionRule;
            this.rasterizationMode = rasterizationMode;
            this.antialiasThreshold = antialiasThreshold;
        }

        /// <summary>
        /// Seeds the current band with carry-over start-cover values produced while linearizing retained geometry.
        /// </summary>
        /// <param name="startCovers">The per-row start-cover contributions for the destination band being rasterized.</param>
        public void SeedStartCovers(ReadOnlySpan<int> startCovers)
        {
            int count = Math.Min(this.height, startCovers.Length);
            for (int i = 0; i < count; i++)
            {
                int cover = startCovers[i];
                if (cover == 0)
                {
                    continue;
                }

                this.startCover[i] += cover;
                this.MarkRowTouched(i);
            }
        }

        /// <summary>
        /// Applies one clipped left-of-band winding interval directly to the current start-cover rows.
        /// </summary>
        /// <param name="y0">The starting Y coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="y1">The ending Y coordinate in 24.8 fixed-point band-local space.</param>
        public void AddClippedStartCover(int y0, int y1)
        {
            if (y0 == y1)
            {
                return;
            }

            if (y0 < y1)
            {
                int rowIndex0 = y0 >> FixedShift;
                int rowIndex1 = (y1 - 1) >> FixedShift;
                int fy0 = y0 - (rowIndex0 << FixedShift);
                int fy1 = y1 - (rowIndex1 << FixedShift);

                if (rowIndex0 == rowIndex1)
                {
                    this.AddStartCoverCell(rowIndex0, -(fy1 - fy0));
                    return;
                }

                this.AddStartCoverCell(rowIndex0, -(FixedOne - fy0));
                for (int row = rowIndex0 + 1; row < rowIndex1; row++)
                {
                    this.AddStartCoverCell(row, -FixedOne);
                }

                this.AddStartCoverCell(rowIndex1, -fy1);
                return;
            }

            int upRowIndex0 = (y0 - 1) >> FixedShift;
            int upRowIndex1 = y1 >> FixedShift;
            int upFy0 = y0 - (upRowIndex0 << FixedShift);
            int upFy1 = y1 - (upRowIndex1 << FixedShift);

            if (upRowIndex0 == upRowIndex1)
            {
                this.AddStartCoverCell(upRowIndex0, upFy0 - upFy1);
                return;
            }

            this.AddStartCoverCell(upRowIndex0, upFy0);
            for (int row = upRowIndex0 - 1; row > upRowIndex1; row--)
            {
                this.AddStartCoverCell(row, FixedOne);
            }

            this.AddStartCoverCell(upRowIndex1, FixedOne - upFy1);
        }

        /// <summary>
        /// Rasterizes a single retained line segment into the current band scratch tables.
        /// </summary>
        /// <param name="x0">The starting X coordinate in 24.8 fixed-point destination space.</param>
        /// <param name="y0">The starting Y coordinate in 24.8 fixed-point destination space.</param>
        /// <param name="x1">The ending X coordinate in 24.8 fixed-point destination space.</param>
        /// <param name="y1">The ending Y coordinate in 24.8 fixed-point destination space.</param>
        public void RasterizeLineSegment(int x0, int y0, int x1, int y1)
            => this.RasterizeLine(x0, y0, x1, y1);

        /// <summary>
        /// Converts accumulated cover/area tables into non-zero coverage span callbacks.
        /// </summary>
        /// <param name="destinationTop">Absolute destination Y corresponding to row zero in this context.</param>
        /// <param name="destinationLeft">Absolute destination X corresponding to column zero in this context.</param>
        /// <param name="scanline">Reusable scanline scratch buffer used to materialize emitted spans.</param>
        /// <param name="rowHandler">Coverage callback invoked for each emitted non-zero span.</param>
        public readonly void EmitCoverageRows<TRowHandler>(
            int destinationTop,
            int destinationLeft,
            Span<float> scanline,
            ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            // Iterate only rows that actually received coverage contributions.
            // MarkRowTouched is called from AddCell for all contributions, including
            // column-less startCover accumulations, so touchedRows is complete.
            for (int i = 0; i < this.touchedRowCount; i++)
            {
                int row = this.touchedRows[i];
                int rowCover = this.startCover[row];
                bool rowHasBits = this.rowHasBits[row] != 0;

                if (!rowHasBits)
                {
                    // No touched cells in this row, but carry cover from x < 0 can still
                    // produce a full-width constant span.
                    float coverage = this.AreaToCoverage(rowCover << AreaToCoverageShift);
                    if (coverage > 0F)
                    {
                        scanline[..this.width].Fill(coverage);
                        rowHandler.Handle(destinationTop + row, destinationLeft, scanline[..this.width]);
                    }

                    continue;
                }

                int minTouchedColumn = this.rowMinTouchedColumn[row];
                int maxTouchedColumn = this.rowMaxTouchedColumn[row];
                ReadOnlySpan<nuint> rowBitVectors = this.bitVectors.Slice(row * this.wordsPerRow, this.wordsPerRow);
                this.EmitRowCoverage(
                    rowBitVectors,
                    row,
                    rowCover,
                    minTouchedColumn,
                    maxTouchedColumn,
                    destinationLeft,
                    destinationTop + row,
                    scanline,
                    ref rowHandler);
            }
        }

        /// <summary>
        /// Clears only rows touched during the previous rasterization pass.
        /// </summary>
        /// <remarks>
        /// This sparse reset strategy avoids clearing full scratch buffers when geometry is sparse.
        /// </remarks>
        public void ResetTouchedRows()
        {
            // Reset only rows that received contributions in this band. This avoids clearing
            // full temporary buffers when geometry is sparse relative to the interest bounds.
            for (int i = 0; i < this.touchedRowCount; i++)
            {
                int row = this.touchedRows[i];
                this.startCover[row] = 0;
                this.rowTouched[row] = 0;

                if (this.rowHasBits[row] == 0)
                {
                    continue;
                }

                this.rowHasBits[row] = 0;

                // Clear only touched bitset words for this row.
                int minWord = this.rowMinTouchedColumn[row] / WordBitCount;
                int maxWord = this.rowMaxTouchedColumn[row] / WordBitCount;
                int wordCount = (maxWord - minWord) + 1;
                this.bitVectors.Slice((row * this.wordsPerRow) + minWord, wordCount).Clear();
            }

            this.touchedRowCount = 0;
        }

        /// <summary>
        /// Emits one row by iterating touched columns and coalescing equal-coverage spans.
        /// </summary>
        /// <param name="rowBitVectors">Bitset words indicating touched columns in this row.</param>
        /// <param name="row">Row index inside the context.</param>
        /// <param name="cover">Initial carry cover value from x less than zero contributions.</param>
        /// <param name="minTouchedColumn">Minimum touched column index in this row.</param>
        /// <param name="maxTouchedColumn">Maximum touched column index in this row.</param>
        /// <param name="destinationLeft">Absolute destination X corresponding to column zero in this context.</param>
        /// <param name="destinationY">Absolute destination y for this row.</param>
        /// <param name="scanline">Reusable scanline coverage buffer used for per-span materialization.</param>
        /// <param name="rowHandler">Coverage callback invoked for each emitted non-zero span.</param>
        private readonly void EmitRowCoverage<TRowHandler>(
            ReadOnlySpan<nuint> rowBitVectors,
            int row,
            int cover,
            int minTouchedColumn,
            int maxTouchedColumn,
            int destinationLeft,
            int destinationY,
            Span<float> scanline,
            ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            int rowOffset = row * this.coverStride;
            int spanStart = 0;
            int spanEnd = 0;
            float spanCoverage = 0F;
            int runStart = -1;
            int runEnd = -1;
            int minWord = minTouchedColumn / WordBitCount;
            int maxWord = maxTouchedColumn / WordBitCount;

            for (int wordIndex = minWord; wordIndex <= maxWord; wordIndex++)
            {
                // Iterate touched columns sparsely by scanning set bits only.
                nuint bitset = rowBitVectors[wordIndex];
                while (bitset != 0)
                {
                    int localBitIndex = TrailingZeroCount(bitset);
                    bitset &= bitset - 1;

                    int x = (wordIndex * WordBitCount) + localBitIndex;
                    if ((uint)x >= (uint)this.width)
                    {
                        continue;
                    }

                    int tableIndex = rowOffset + (x << 1);

                    // Area uses current cover before adding this cell's delta. This matches
                    // scan-conversion math where area integrates the edge state at cell entry.
                    int area = this.coverArea[tableIndex + 1] + (cover << AreaToCoverageShift);
                    float coverage = this.AreaToCoverage(area);

                    if (spanEnd == x)
                    {
                        if (coverage <= 0F)
                        {
                            // Zero coverage is a hard break. Everything buffered so far belongs
                            // to the contiguous non-zero region immediately before x, and the
                            // current pixel is outside that region. Flush now so a later non-zero
                            // span cannot be merged across this hole into the same row callback.
                            BufferSpan(scanline, spanStart, spanEnd, spanCoverage, ref runStart, ref runEnd);
                            FlushBufferedRun(ref rowHandler, destinationY, destinationLeft, scanline, ref runStart, ref runEnd);
                            spanStart = x + 1;
                            spanEnd = spanStart;
                            spanCoverage = 0F;
                        }
                        else if (coverage == spanCoverage)
                        {
                            spanEnd = x + 1;
                        }
                        else
                        {
                            BufferSpan(scanline, spanStart, spanEnd, spanCoverage, ref runStart, ref runEnd);
                            spanStart = x;
                            spanEnd = x + 1;
                            spanCoverage = coverage;
                        }
                    }
                    else
                    {
                        // We jumped over untouched columns. If cover != 0 the gap has a constant
                        // non-zero coverage and must be emitted as its own run.
                        if (cover == 0)
                        {
                            // A zero-coverage gap is the same kind of hard break as a zero
                            // coverage cell above: the buffered run must end before the gap so
                            // the next visible span starts a new contiguous non-zero interval.
                            BufferSpan(scanline, spanStart, spanEnd, spanCoverage, ref runStart, ref runEnd);
                            FlushBufferedRun(ref rowHandler, destinationY, destinationLeft, scanline, ref runStart, ref runEnd);
                            spanStart = x;
                            spanEnd = x + 1;
                            spanCoverage = coverage;
                        }
                        else
                        {
                            float gapCoverage = this.AreaToCoverage(cover << AreaToCoverageShift);
                            if (gapCoverage <= 0F)
                            {
                                // Even-odd can map non-zero winding to zero coverage.
                                // Treat this as a hard run break so we don't bridge across a
                                // zero-alpha hole and emit one callback for what is really two
                                // separate visible regions.
                                BufferSpan(scanline, spanStart, spanEnd, spanCoverage, ref runStart, ref runEnd);
                                FlushBufferedRun(ref rowHandler, destinationY, destinationLeft, scanline, ref runStart, ref runEnd);
                                spanStart = x;
                                spanEnd = x + 1;
                                spanCoverage = coverage;
                            }
                            else if (spanCoverage == gapCoverage)
                            {
                                if (coverage == gapCoverage)
                                {
                                    spanEnd = x + 1;
                                }
                                else
                                {
                                    BufferSpan(scanline, spanStart, x, spanCoverage, ref runStart, ref runEnd);
                                    spanStart = x;
                                    spanEnd = x + 1;
                                    spanCoverage = coverage;
                                }
                            }
                            else
                            {
                                BufferSpan(scanline, spanStart, spanEnd, spanCoverage, ref runStart, ref runEnd);
                                BufferSpan(scanline, spanEnd, x, gapCoverage, ref runStart, ref runEnd);
                                spanStart = x;
                                spanEnd = x + 1;
                                spanCoverage = coverage;
                            }
                        }
                    }

                    cover += this.coverArea[tableIndex];
                }
            }

            BufferSpan(scanline, spanStart, spanEnd, spanCoverage, ref runStart, ref runEnd);

            if (cover != 0 && spanEnd < this.width)
            {
                BufferSpan(scanline, spanEnd, this.width, this.AreaToCoverage(cover << AreaToCoverageShift), ref runStart, ref runEnd);
            }

            // At this point the buffered run, if any, represents one contiguous destination-space
            // interval whose pixels all have non-zero coverage. Emitting that interval in one
            // callback preserves the exact per-pixel coverage values already written into the
            // scratch scanline while avoiding a stream of tiny span callbacks.
            FlushBufferedRun(ref rowHandler, destinationY, destinationLeft, scanline, ref runStart, ref runEnd);
        }

        /// <summary>
        /// Converts accumulated signed area to normalized coverage under the selected fill rule.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly float AreaToCoverage(int area)
        {
            int signedArea = area >> AreaToCoverageShift;
            int absoluteArea = signedArea < 0 ? -signedArea : signedArea;
            float coverage;

            if (this.intersectionRule == IntersectionRule.NonZero)
            {
                // Non-zero winding clamps absolute winding accumulation to [0, 1].
                if (absoluteArea >= CoverageStepCount)
                {
                    coverage = 1F;
                }
                else
                {
                    coverage = absoluteArea * CoverageScale;
                }
            }
            else
            {
                // Even-odd wraps every 2*CoverageStepCount and mirrors second half.
                int wrapped = absoluteArea & EvenOddMask;
                if (wrapped > CoverageStepCount)
                {
                    wrapped = EvenOddPeriod - wrapped;
                }

                coverage = wrapped >= CoverageStepCount ? 1F : wrapped * CoverageScale;
            }

            if (this.rasterizationMode == RasterizationMode.Aliased)
            {
                // Aliased mode quantizes final coverage to hard 0/1 per pixel
                // using the configurable threshold from GraphicsOptions.AntialiasThreshold.
                return coverage >= this.antialiasThreshold ? 1F : 0F;
            }

            return coverage;
        }

        /// <summary>
        /// Buffers one non-zero span into the current contiguous row run.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BufferSpan(
            Span<float> scanline,
            int start,
            int end,
            float coverage,
            ref int runStart,
            ref int runEnd)
        {
            if (coverage <= 0F || end <= start)
            {
                return;
            }

            if (runStart < 0)
            {
                runStart = start;
                runEnd = end;
            }
            else if (end > runEnd)
            {
                runEnd = end;
            }

            // All spans in one buffered run are contiguous in destination space. That lets us
            // pack them into one scratch slice, keep their exact per-pixel coverage values, and
            // later hand the whole visible interval to the renderer in a single callback.
            scanline[(start - runStart)..(end - runStart)].Fill(coverage);
        }

        /// <summary>
        /// Emits the currently buffered contiguous run, if any.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FlushBufferedRun<TRowHandler>(
            ref TRowHandler rowHandler,
            int destinationY,
            int destinationLeft,
            Span<float> scanline,
            ref int runStart,
            ref int runEnd)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            if (runStart < 0)
            {
                return;
            }

            rowHandler.Handle(destinationY, destinationLeft + runStart, scanline[..(runEnd - runStart)]);
            runStart = -1;
            runEnd = -1;
        }

        /// <summary>
        /// Sets a row/column bit and reports whether it was newly set.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly bool ConditionalSetBit(int row, int column, out bool rowHadBits)
        {
            int bitIndex = row * this.wordsPerRow;
            int wordIndex = bitIndex + (column / WordBitCount);
            nuint mask = (nuint)1 << (column % WordBitCount);
            ref nuint word = ref this.bitVectors[wordIndex];
            bool newlySet = (word & mask) == 0;
            word |= mask;

            // Single read of rowHasBits serves both the conditional store
            // and the caller's min/max column tracking.
            rowHadBits = this.rowHasBits[row] != 0;
            if (!rowHadBits)
            {
                this.rowHasBits[row] = 1;
            }

            return newlySet;
        }

        /// <summary>
        /// Adds one cell contribution into cover/area accumulators.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddCell(int row, int column, int delta, int area)
        {
            if ((uint)row >= (uint)this.height)
            {
                return;
            }

            this.MarkRowTouched(row);

            if (column < 0)
            {
                // Contributions left of x=0 accumulate into the row carry.
                this.startCover[row] += delta;
                return;
            }

            if ((uint)column >= (uint)this.width)
            {
                return;
            }

            int index = (row * this.coverStride) + (column << 1);
            if (this.ConditionalSetBit(row, column, out bool rowHadBits))
            {
                // First write wins initialization path avoids reading old values.
                this.coverArea[index] = delta;
                this.coverArea[index + 1] = area;
            }
            else
            {
                // Multiple edges can hit the same cell; accumulate signed values.
                this.coverArea[index] += delta;
                this.coverArea[index + 1] += area;
            }

            if (!rowHadBits)
            {
                this.rowMinTouchedColumn[row] = column;
                this.rowMaxTouchedColumn[row] = column;
            }
            else
            {
                if (column < this.rowMinTouchedColumn[row])
                {
                    this.rowMinTouchedColumn[row] = column;
                }

                if (column > this.rowMaxTouchedColumn[row])
                {
                    this.rowMaxTouchedColumn[row] = column;
                }
            }
        }

        /// <summary>
        /// Adds one start-cover delta for a touched row.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddStartCoverCell(int row, int delta)
        {
            if (delta == 0 || (uint)row >= (uint)this.height)
            {
                return;
            }

            this.MarkRowTouched(row);
            this.startCover[row] += delta;
        }

        /// <summary>
        /// Marks a row as touched once so sparse reset can clear it later.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MarkRowTouched(int row)
        {
            if (this.rowTouched[row] != 0)
            {
                return;
            }

            this.rowTouched[row] = 1;
            this.touchedRows[this.touchedRowCount++] = row;
        }

        /// <summary>
        /// Emits one vertical cell contribution.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CellVertical(int px, int py, int x, int y0, int y1)
        {
            int delta = y0 - y1;
            int area = delta * ((FixedOne * 2) - x - x);
            this.AddCell(py, px, delta, area);
        }

        /// <summary>
        /// Emits one general cell contribution.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Cell(int row, int px, int x0, int y0, int x1, int y1)
        {
            int delta = y0 - y1;
            int area = delta * ((FixedOne * 2) - x0 - x1);
            this.AddCell(row, px, delta, area);
        }

        /// <summary>
        /// Rasterizes a downward vertical edge segment.
        /// </summary>
        private void VerticalDown(int columnIndex, int y0, int y1, int x)
        {
            int rowIndex0 = y0 >> FixedShift;
            int rowIndex1 = (y1 - 1) >> FixedShift;
            int fy0 = y0 - (rowIndex0 << FixedShift);
            int fy1 = y1 - (rowIndex1 << FixedShift);
            int fx = x - (columnIndex << FixedShift);

            if (rowIndex0 == rowIndex1)
            {
                // Entire segment stays within one row.
                this.CellVertical(columnIndex, rowIndex0, fx, fy0, fy1);
                return;
            }

            // First partial row, full middle rows, last partial row.
            this.CellVertical(columnIndex, rowIndex0, fx, fy0, FixedOne);

            for (int row = rowIndex0 + 1; row < rowIndex1; row++)
            {
                this.CellVertical(columnIndex, row, fx, 0, FixedOne);
            }

            this.CellVertical(columnIndex, rowIndex1, fx, 0, fy1);
        }

        /// <summary>
        /// Rasterizes an upward vertical edge segment.
        /// </summary>
        private void VerticalUp(int columnIndex, int y0, int y1, int x)
        {
            int rowIndex0 = (y0 - 1) >> FixedShift;
            int rowIndex1 = y1 >> FixedShift;
            int fy0 = y0 - (rowIndex0 << FixedShift);
            int fy1 = y1 - (rowIndex1 << FixedShift);
            int fx = x - (columnIndex << FixedShift);

            if (rowIndex0 == rowIndex1)
            {
                // Entire segment stays within one row.
                this.CellVertical(columnIndex, rowIndex0, fx, fy0, fy1);
                return;
            }

            // First partial row, full middle rows, last partial row (upward direction).
            this.CellVertical(columnIndex, rowIndex0, fx, fy0, 0);

            for (int row = rowIndex0 - 1; row > rowIndex1; row--)
            {
                this.CellVertical(columnIndex, row, fx, FixedOne, 0);
            }

            this.CellVertical(columnIndex, rowIndex1, fx, FixedOne, fy1);
        }

        // The following row/line helpers are directional variants of the same fixed-point edge
        // walker. They are intentionally split to minimize branch costs in hot loops.

        /// <summary>
        /// Rasterizes a downward, left-to-right segment within a single row.
        /// </summary>
        private void RowDownR(int rowIndex, int p0x, int p0y, int p1x, int p1y)
        {
            int columnIndex0 = p0x >> FixedShift;
            int columnIndex1 = (p1x - 1) >> FixedShift;
            int fx0 = p0x - (columnIndex0 << FixedShift);
            int fx1 = p1x - (columnIndex1 << FixedShift);

            if (columnIndex0 == columnIndex1)
            {
                this.Cell(rowIndex, columnIndex0, fx0, p0y, fx1, p1y);
                return;
            }

            int dx = p1x - p0x;
            int dy = p1y - p0y;
            int pp = (FixedOne - fx0) * dy;
            int cy = p0y + (pp / dx);

            this.Cell(rowIndex, columnIndex0, fx0, p0y, FixedOne, cy);

            int idx = columnIndex0 + 1;

            if (idx != columnIndex1)
            {
                int mod = (pp % dx) - dx;
                int p = FixedOne * dy;
                int lift = p / dx;
                int rem = p % dx;

                for (; idx != columnIndex1; idx++)
                {
                    int delta = lift;
                    mod += rem;
                    if (mod >= 0)
                    {
                        mod -= dx;
                        delta++;
                    }

                    int ny = cy + delta;
                    this.Cell(rowIndex, idx, 0, cy, FixedOne, ny);
                    cy = ny;
                }
            }

            this.Cell(rowIndex, columnIndex1, 0, cy, fx1, p1y);
        }

        /// <summary>
        /// RowDownR variant that handles perfectly vertical edge ownership consistently.
        /// </summary>
        private void RowDownR_V(int rowIndex, int p0x, int p0y, int p1x, int p1y)
        {
            if (p0x < p1x)
            {
                this.RowDownR(rowIndex, p0x, p0y, p1x, p1y);
            }
            else
            {
                int columnIndex = (p0x - FindAdjustment(p0x)) >> FixedShift;
                int x = p0x - (columnIndex << FixedShift);
                this.CellVertical(columnIndex, rowIndex, x, p0y, p1y);
            }
        }

        /// <summary>
        /// Rasterizes an upward, left-to-right segment within a single row.
        /// </summary>
        private void RowUpR(int rowIndex, int p0x, int p0y, int p1x, int p1y)
        {
            int columnIndex0 = p0x >> FixedShift;
            int columnIndex1 = (p1x - 1) >> FixedShift;
            int fx0 = p0x - (columnIndex0 << FixedShift);
            int fx1 = p1x - (columnIndex1 << FixedShift);

            if (columnIndex0 == columnIndex1)
            {
                this.Cell(rowIndex, columnIndex0, fx0, p0y, fx1, p1y);
                return;
            }

            int dx = p1x - p0x;
            int dy = p0y - p1y;
            int pp = (FixedOne - fx0) * dy;
            int cy = p0y - (pp / dx);

            this.Cell(rowIndex, columnIndex0, fx0, p0y, FixedOne, cy);

            int idx = columnIndex0 + 1;

            if (idx != columnIndex1)
            {
                int mod = (pp % dx) - dx;
                int p = FixedOne * dy;
                int lift = p / dx;
                int rem = p % dx;

                for (; idx != columnIndex1; idx++)
                {
                    int delta = lift;
                    mod += rem;
                    if (mod >= 0)
                    {
                        mod -= dx;
                        delta++;
                    }

                    int ny = cy - delta;
                    this.Cell(rowIndex, idx, 0, cy, FixedOne, ny);
                    cy = ny;
                }
            }

            this.Cell(rowIndex, columnIndex1, 0, cy, fx1, p1y);
        }

        /// <summary>
        /// RowUpR variant that handles perfectly vertical edge ownership consistently.
        /// </summary>
        private void RowUpR_V(int rowIndex, int p0x, int p0y, int p1x, int p1y)
        {
            if (p0x < p1x)
            {
                this.RowUpR(rowIndex, p0x, p0y, p1x, p1y);
            }
            else
            {
                int columnIndex = (p0x - FindAdjustment(p0x)) >> FixedShift;
                int x = p0x - (columnIndex << FixedShift);
                this.CellVertical(columnIndex, rowIndex, x, p0y, p1y);
            }
        }

        /// <summary>
        /// Rasterizes a downward, right-to-left segment within a single row.
        /// </summary>
        private void RowDownL(int rowIndex, int p0x, int p0y, int p1x, int p1y)
        {
            int columnIndex0 = (p0x - 1) >> FixedShift;
            int columnIndex1 = p1x >> FixedShift;
            int fx0 = p0x - (columnIndex0 << FixedShift);
            int fx1 = p1x - (columnIndex1 << FixedShift);

            if (columnIndex0 == columnIndex1)
            {
                this.Cell(rowIndex, columnIndex0, fx0, p0y, fx1, p1y);
                return;
            }

            int dx = p0x - p1x;
            int dy = p1y - p0y;
            int pp = fx0 * dy;
            int cy = p0y + (pp / dx);

            this.Cell(rowIndex, columnIndex0, fx0, p0y, 0, cy);

            int idx = columnIndex0 - 1;

            if (idx != columnIndex1)
            {
                int mod = (pp % dx) - dx;
                int p = FixedOne * dy;
                int lift = p / dx;
                int rem = p % dx;

                for (; idx != columnIndex1; idx--)
                {
                    int delta = lift;
                    mod += rem;
                    if (mod >= 0)
                    {
                        mod -= dx;
                        delta++;
                    }

                    int ny = cy + delta;
                    this.Cell(rowIndex, idx, FixedOne, cy, 0, ny);
                    cy = ny;
                }
            }

            this.Cell(rowIndex, columnIndex1, FixedOne, cy, fx1, p1y);
        }

        /// <summary>
        /// RowDownL variant that handles perfectly vertical edge ownership consistently.
        /// </summary>
        private void RowDownL_V(int rowIndex, int p0x, int p0y, int p1x, int p1y)
        {
            if (p0x > p1x)
            {
                this.RowDownL(rowIndex, p0x, p0y, p1x, p1y);
            }
            else
            {
                int columnIndex = (p0x - FindAdjustment(p0x)) >> FixedShift;
                int x = p0x - (columnIndex << FixedShift);
                this.CellVertical(columnIndex, rowIndex, x, p0y, p1y);
            }
        }

        /// <summary>
        /// Rasterizes an upward, right-to-left segment within a single row.
        /// </summary>
        private void RowUpL(int rowIndex, int p0x, int p0y, int p1x, int p1y)
        {
            int columnIndex0 = (p0x - 1) >> FixedShift;
            int columnIndex1 = p1x >> FixedShift;
            int fx0 = p0x - (columnIndex0 << FixedShift);
            int fx1 = p1x - (columnIndex1 << FixedShift);

            if (columnIndex0 == columnIndex1)
            {
                this.Cell(rowIndex, columnIndex0, fx0, p0y, fx1, p1y);
                return;
            }

            int dx = p0x - p1x;
            int dy = p0y - p1y;
            int pp = fx0 * dy;
            int cy = p0y - (pp / dx);

            this.Cell(rowIndex, columnIndex0, fx0, p0y, 0, cy);

            int idx = columnIndex0 - 1;

            if (idx != columnIndex1)
            {
                int mod = (pp % dx) - dx;
                int p = FixedOne * dy;
                int lift = p / dx;
                int rem = p % dx;

                for (; idx != columnIndex1; idx--)
                {
                    int delta = lift;
                    mod += rem;
                    if (mod >= 0)
                    {
                        mod -= dx;
                        delta++;
                    }

                    int ny = cy - delta;
                    this.Cell(rowIndex, idx, FixedOne, cy, 0, ny);
                    cy = ny;
                }
            }

            this.Cell(rowIndex, columnIndex1, FixedOne, cy, fx1, p1y);
        }

        /// <summary>
        /// RowUpL variant that handles perfectly vertical edge ownership consistently.
        /// </summary>
        private void RowUpL_V(int rowIndex, int p0x, int p0y, int p1x, int p1y)
        {
            if (p0x > p1x)
            {
                this.RowUpL(rowIndex, p0x, p0y, p1x, p1y);
            }
            else
            {
                int columnIndex = (p0x - FindAdjustment(p0x)) >> FixedShift;
                int x = p0x - (columnIndex << FixedShift);
                this.CellVertical(columnIndex, rowIndex, x, p0y, p1y);
            }
        }

        /// <summary>
        /// Rasterizes a downward, left-to-right segment spanning multiple rows.
        /// </summary>
        private void LineDownR(int rowIndex0, int rowIndex1, int x0, int y0, int x1, int y1)
        {
            int dx = x1 - x0;
            int dy = y1 - y0;
            int fy0 = y0 - (rowIndex0 << FixedShift);
            int fy1 = y1 - (rowIndex1 << FixedShift);

            // p/delta/mod/rem implement an integer DDA that advances x at row boundaries
            // without per-row floating-point math.
            int p = (FixedOne - fy0) * dx;
            int delta = p / dy;
            int cx = x0 + delta;

            this.RowDownR_V(rowIndex0, x0, fy0, cx, FixedOne);

            int row = rowIndex0 + 1;

            if (row != rowIndex1)
            {
                int mod = (p % dy) - dy;
                p = FixedOne * dx;
                int lift = p / dy;
                int rem = p % dy;

                for (; row != rowIndex1; row++)
                {
                    delta = lift;
                    mod += rem;
                    if (mod >= 0)
                    {
                        mod -= dy;
                        delta++;
                    }

                    int nx = cx + delta;
                    this.RowDownR_V(row, cx, 0, nx, FixedOne);
                    cx = nx;
                }
            }

            this.RowDownR_V(rowIndex1, cx, 0, x1, fy1);
        }

        /// <summary>
        /// Rasterizes an upward, left-to-right segment spanning multiple rows.
        /// </summary>
        private void LineUpR(int rowIndex0, int rowIndex1, int x0, int y0, int x1, int y1)
        {
            int dx = x1 - x0;
            int dy = y0 - y1;
            int fy0 = y0 - (rowIndex0 << FixedShift);
            int fy1 = y1 - (rowIndex1 << FixedShift);

            // Upward version of the same integer DDA stepping as LineDownR.
            int p = fy0 * dx;
            int delta = p / dy;
            int cx = x0 + delta;

            this.RowUpR_V(rowIndex0, x0, fy0, cx, 0);

            int row = rowIndex0 - 1;
            if (row != rowIndex1)
            {
                int mod = (p % dy) - dy;
                p = FixedOne * dx;
                int lift = p / dy;
                int rem = p % dy;

                for (; row != rowIndex1; row--)
                {
                    delta = lift;
                    mod += rem;
                    if (mod >= 0)
                    {
                        mod -= dy;
                        delta++;
                    }

                    int nx = cx + delta;
                    this.RowUpR_V(row, cx, FixedOne, nx, 0);
                    cx = nx;
                }
            }

            this.RowUpR_V(rowIndex1, cx, FixedOne, x1, fy1);
        }

        /// <summary>
        /// Rasterizes a downward, right-to-left segment spanning multiple rows.
        /// </summary>
        private void LineDownL(int rowIndex0, int rowIndex1, int x0, int y0, int x1, int y1)
        {
            int dx = x0 - x1;
            int dy = y1 - y0;
            int fy0 = y0 - (rowIndex0 << FixedShift);
            int fy1 = y1 - (rowIndex1 << FixedShift);

            // Right-to-left variant of the integer DDA.
            int p = (FixedOne - fy0) * dx;
            int delta = p / dy;
            int cx = x0 - delta;

            this.RowDownL_V(rowIndex0, x0, fy0, cx, FixedOne);

            int row = rowIndex0 + 1;
            if (row != rowIndex1)
            {
                int mod = (p % dy) - dy;
                p = FixedOne * dx;
                int lift = p / dy;
                int rem = p % dy;

                for (; row != rowIndex1; row++)
                {
                    delta = lift;
                    mod += rem;
                    if (mod >= 0)
                    {
                        mod -= dy;
                        delta++;
                    }

                    int nx = cx - delta;
                    this.RowDownL_V(row, cx, 0, nx, FixedOne);
                    cx = nx;
                }
            }

            this.RowDownL_V(rowIndex1, cx, 0, x1, fy1);
        }

        /// <summary>
        /// Rasterizes an upward, right-to-left segment spanning multiple rows.
        /// </summary>
        private void LineUpL(int rowIndex0, int rowIndex1, int x0, int y0, int x1, int y1)
        {
            int dx = x0 - x1;
            int dy = y0 - y1;
            int fy0 = y0 - (rowIndex0 << FixedShift);
            int fy1 = y1 - (rowIndex1 << FixedShift);

            // Upward + right-to-left variant of the integer DDA.
            int p = fy0 * dx;
            int delta = p / dy;
            int cx = x0 - delta;

            this.RowUpL_V(rowIndex0, x0, fy0, cx, 0);

            int row = rowIndex0 - 1;
            if (row != rowIndex1)
            {
                int mod = (p % dy) - dy;
                p = FixedOne * dx;
                int lift = p / dy;
                int rem = p % dy;

                for (; row != rowIndex1; row--)
                {
                    delta = lift;
                    mod += rem;
                    if (mod >= 0)
                    {
                        mod -= dy;
                        delta++;
                    }

                    int nx = cx - delta;
                    this.RowUpL_V(row, cx, FixedOne, nx, 0);
                    cx = nx;
                }
            }

            this.RowUpL_V(rowIndex1, cx, FixedOne, x1, fy1);
        }

        /// <summary>
        /// Dispatches a clipped edge to the correct directional fixed-point walker.
        /// </summary>
        private void RasterizeLine(int x0, int y0, int x1, int y1)
        {
            if (x0 == x1)
            {
                // Vertical edges need ownership adjustment to avoid double counting at cell seams.
                int columnIndex = (x0 - FindAdjustment(x0)) >> FixedShift;
                if (y0 < y1)
                {
                    this.VerticalDown(columnIndex, y0, y1, x0);
                }
                else
                {
                    this.VerticalUp(columnIndex, y0, y1, x0);
                }

                return;
            }

            if (y0 < y1)
            {
                // Downward edges use inclusive top/exclusive bottom row mapping.
                int rowIndex0 = y0 >> FixedShift;
                int rowIndex1 = (y1 - 1) >> FixedShift;

                if (rowIndex0 == rowIndex1)
                {
                    int rowBase = rowIndex0 << FixedShift;
                    int localY0 = y0 - rowBase;
                    int localY1 = y1 - rowBase;
                    if (x0 < x1)
                    {
                        this.RowDownR(rowIndex0, x0, localY0, x1, localY1);
                    }
                    else
                    {
                        this.RowDownL(rowIndex0, x0, localY0, x1, localY1);
                    }
                }
                else if (x0 < x1)
                {
                    this.LineDownR(rowIndex0, rowIndex1, x0, y0, x1, y1);
                }
                else
                {
                    this.LineDownL(rowIndex0, rowIndex1, x0, y0, x1, y1);
                }

                return;
            }

            // Upward edges mirror the mapping to preserve winding consistency.
            int upRowIndex0 = (y0 - 1) >> FixedShift;
            int upRowIndex1 = y1 >> FixedShift;

            if (upRowIndex0 == upRowIndex1)
            {
                int rowBase = upRowIndex0 << FixedShift;
                int localY0 = y0 - rowBase;
                int localY1 = y1 - rowBase;
                if (x0 < x1)
                {
                    this.RowUpR(upRowIndex0, x0, localY0, x1, localY1);
                }
                else
                {
                    this.RowUpL(upRowIndex0, x0, localY0, x1, localY1);
                }
            }
            else if (x0 < x1)
            {
                this.LineUpR(upRowIndex0, upRowIndex1, x0, y0, x1, y1);
            }
            else
            {
                this.LineUpL(upRowIndex0, upRowIndex1, x0, y0, x1, y1);
            }
        }
    }

    /// <summary>
    /// Immutable scanner-local edge record (16 bytes).
    /// </summary>
    /// <remarks>
    /// All coordinates are stored as signed 24.8 fixed-point integers for predictable hot-path
    /// access without per-read unpacking. Row bounds are computed inline from Y coordinates
    /// where needed.
    /// </remarks>
    internal readonly struct EdgeData
    {
        /// <summary>
        /// Gets edge start X in scanner-local coordinates (24.8 fixed-point).
        /// </summary>
        public readonly int X0;

        /// <summary>
        /// Gets edge start Y in scanner-local coordinates (24.8 fixed-point).
        /// </summary>
        public readonly int Y0;

        /// <summary>
        /// Gets edge end X in scanner-local coordinates (24.8 fixed-point).
        /// </summary>
        public readonly int X1;

        /// <summary>
        /// Gets edge end Y in scanner-local coordinates (24.8 fixed-point).
        /// </summary>
        public readonly int Y1;

        /// <summary>
        /// Initializes a new instance of the <see cref="EdgeData"/> struct.
        /// </summary>
        public EdgeData(int x0, int y0, int x1, int y1)
        {
            this.X0 = x0;
            this.Y0 = y0;
            this.X1 = x1;
            this.Y1 = y1;
        }
    }

    /// <summary>
    /// Reusable per-worker scratch buffers used by raster band execution.
    /// </summary>
    internal sealed class WorkerScratch : IDisposable
    {
        private readonly int wordsPerRow;
        private readonly int coverStride;
        private readonly int width;
        private readonly int tileCapacity;
        private readonly MemoryAllocator allocator;
        private readonly IMemoryOwner<nuint> bitVectorsOwner;
        private readonly IMemoryOwner<int> coverAreaOwner;
        private readonly IMemoryOwner<int> startCoverOwner;
        private readonly IMemoryOwner<int> rowMinTouchedColumnOwner;
        private readonly IMemoryOwner<int> rowMaxTouchedColumnOwner;
        private readonly IMemoryOwner<byte> rowHasBitsOwner;
        private readonly IMemoryOwner<byte> rowTouchedOwner;
        private readonly IMemoryOwner<int> touchedRowsOwner;
        private readonly IMemoryOwner<float> scanlineOwner;
        private IMemoryOwner<float>? strokeBandCoverageOwner;

        private WorkerScratch(
            MemoryAllocator allocator,
            int wordsPerRow,
            int coverStride,
            int width,
            int tileCapacity,
            IMemoryOwner<nuint> bitVectorsOwner,
            IMemoryOwner<int> coverAreaOwner,
            IMemoryOwner<int> startCoverOwner,
            IMemoryOwner<int> rowMinTouchedColumnOwner,
            IMemoryOwner<int> rowMaxTouchedColumnOwner,
            IMemoryOwner<byte> rowHasBitsOwner,
            IMemoryOwner<byte> rowTouchedOwner,
            IMemoryOwner<int> touchedRowsOwner,
            IMemoryOwner<float> scanlineOwner)
        {
            this.allocator = allocator;
            this.wordsPerRow = wordsPerRow;
            this.coverStride = coverStride;
            this.width = width;
            this.tileCapacity = tileCapacity;
            this.bitVectorsOwner = bitVectorsOwner;
            this.coverAreaOwner = coverAreaOwner;
            this.startCoverOwner = startCoverOwner;
            this.rowMinTouchedColumnOwner = rowMinTouchedColumnOwner;
            this.rowMaxTouchedColumnOwner = rowMaxTouchedColumnOwner;
            this.rowHasBitsOwner = rowHasBitsOwner;
            this.rowTouchedOwner = rowTouchedOwner;
            this.touchedRowsOwner = touchedRowsOwner;
            this.scanlineOwner = scanlineOwner;
        }

        /// <summary>
        /// Gets reusable scanline scratch for this worker.
        /// </summary>
        public Span<float> Scanline => this.scanlineOwner.Memory.Span;

        /// <summary>
        /// Gets reusable per-band stroke coverage scratch for this worker.
        /// </summary>
        public Span<float> StrokeBandCoverage
            => (this.strokeBandCoverageOwner ??=
                this.allocator.Allocate<float>(checked(this.width * this.tileCapacity * DirectStrokeVerticalSampleCount)))
                .Memory.Span;

        /// <summary>
        /// Returns <see langword="true"/> when this scratch has compatible dimensions and sufficient
        /// capacity for the requested parameters, making it safe to reuse without reallocation.
        /// </summary>
        internal bool CanReuse(int requiredWordsPerRow, int requiredCoverStride, int requiredWidth, int minCapacity)
            => this.wordsPerRow >= requiredWordsPerRow
            && this.coverStride >= requiredCoverStride
            && this.width >= requiredWidth
            && this.tileCapacity >= minCapacity;

        /// <summary>
        /// Returns <see langword="true"/> when this scratch can be reused for the default band configuration
        /// at the requested width.
        /// </summary>
        internal bool CanReuse(int requiredWidth)
            => this.CanReuse(BitVectorsForMaxBitCount(requiredWidth), checked(requiredWidth << 1), requiredWidth, PreferredRowHeight);

        /// <summary>
        /// Allocates worker-local scratch sized for the configured tile/band capacity.
        /// </summary>
        public static WorkerScratch Create(MemoryAllocator allocator, int wordsPerRow, int coverStride, int width, int tileCapacity)
        {
            int bitVectorCapacity = checked(wordsPerRow * tileCapacity);
            int coverAreaCapacity = checked(coverStride * tileCapacity);
            IMemoryOwner<nuint> bitVectorsOwner = allocator.Allocate<nuint>(bitVectorCapacity, AllocationOptions.Clean);
            IMemoryOwner<int> coverAreaOwner = allocator.Allocate<int>(coverAreaCapacity);
            IMemoryOwner<int> startCoverOwner = allocator.Allocate<int>(tileCapacity, AllocationOptions.Clean);
            IMemoryOwner<int> rowMinTouchedColumnOwner = allocator.Allocate<int>(tileCapacity);
            IMemoryOwner<int> rowMaxTouchedColumnOwner = allocator.Allocate<int>(tileCapacity);
            IMemoryOwner<byte> rowHasBitsOwner = allocator.Allocate<byte>(tileCapacity, AllocationOptions.Clean);
            IMemoryOwner<byte> rowTouchedOwner = allocator.Allocate<byte>(tileCapacity, AllocationOptions.Clean);
            IMemoryOwner<int> touchedRowsOwner = allocator.Allocate<int>(tileCapacity);
            IMemoryOwner<float> scanlineOwner = allocator.Allocate<float>(width);

            return new WorkerScratch(
                allocator,
                wordsPerRow,
                coverStride,
                width,
                tileCapacity,
                bitVectorsOwner,
                coverAreaOwner,
                startCoverOwner,
                rowMinTouchedColumnOwner,
                rowMaxTouchedColumnOwner,
                rowHasBitsOwner,
                rowTouchedOwner,
                touchedRowsOwner,
                scanlineOwner);
        }

        /// <summary>
        /// Creates a context view over a compatible prefix of this scratch for the requested geometry width.
        /// </summary>
        public Context CreateContext(
            IntersectionRule intersectionRule,
            RasterizationMode rasterizationMode,
            float antialiasThreshold)
            => new(
                this.bitVectorsOwner.Memory.Span,
                this.coverAreaOwner.Memory.Span,
                this.startCoverOwner.Memory.Span,
                this.rowMinTouchedColumnOwner.Memory.Span,
                this.rowMaxTouchedColumnOwner.Memory.Span,
                this.rowHasBitsOwner.Memory.Span,
                this.rowTouchedOwner.Memory.Span,
                this.touchedRowsOwner.Memory.Span,
                intersectionRule,
                rasterizationMode,
                antialiasThreshold);

        /// <summary>
        /// Releases worker-local scratch buffers back to the allocator.
        /// </summary>
        public void Dispose()
        {
            this.bitVectorsOwner.Dispose();
            this.coverAreaOwner.Dispose();
            this.startCoverOwner.Dispose();
            this.rowMinTouchedColumnOwner.Dispose();
            this.rowMaxTouchedColumnOwner.Dispose();
            this.rowHasBitsOwner.Dispose();
            this.rowTouchedOwner.Dispose();
            this.touchedRowsOwner.Dispose();
            this.scanlineOwner.Dispose();
            this.strokeBandCoverageOwner?.Dispose();
        }
    }
}
