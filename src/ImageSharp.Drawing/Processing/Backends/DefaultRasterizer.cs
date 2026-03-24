// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Default fixed-point rasterizer that converts polygon edges into per-row coverage.
/// </summary>
/// <remarks>
/// Fill rasterization is organized around scene-aligned row bands. Each band builds a compact
/// line list and optional start-cover seed, then executes directly against worker-local scratch.
/// Stroke rasterization uses the same fixed-point scan conversion core, but expands centerline
/// edges into outline geometry before rasterization.
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

    private static void VerticalDown(
        LineArrayX32Y16[] lineArrays,
        MemoryAllocator allocator,
        int tileHeight,
        int rowBandCount,
        int x,
        int y0,
        int y1,
        int[] lineCounts,
        ref bool hasAnyCoverage)
        => SplitAcrossBands(lineArrays, allocator, tileHeight, rowBandCount, x, y0, x, y1, lineCounts, ref hasAnyCoverage);

    private static void VerticalUp(
        LineArrayX32Y16[] lineArrays,
        MemoryAllocator allocator,
        int tileHeight,
        int rowBandCount,
        int x,
        int y0,
        int y1,
        int[] lineCounts,
        ref bool hasAnyCoverage)
        => SplitAcrossBands(lineArrays, allocator, tileHeight, rowBandCount, x, y0, x, y1, lineCounts, ref hasAnyCoverage);

    private static void VerticalDown(
        LineArrayX16Y16[] lineArrays,
        MemoryAllocator allocator,
        int tileHeight,
        int rowBandCount,
        int x,
        int y0,
        int y1,
        int[] lineCounts,
        ref bool hasAnyCoverage)
        => SplitAcrossBands(lineArrays, allocator, tileHeight, rowBandCount, x, y0, x, y1, lineCounts, ref hasAnyCoverage);

    private static void VerticalUp(
        LineArrayX16Y16[] lineArrays,
        MemoryAllocator allocator,
        int tileHeight,
        int rowBandCount,
        int x,
        int y0,
        int y1,
        int[] lineCounts,
        ref bool hasAnyCoverage)
        => SplitAcrossBands(lineArrays, allocator, tileHeight, rowBandCount, x, y0, x, y1, lineCounts, ref hasAnyCoverage);

    private static void AddContainedLineF24Dot8(
        LineArrayX32Y16[] lineArrays,
        MemoryAllocator allocator,
        int tileHeight,
        int rowBandCount,
        int x0,
        int y0,
        int x1,
        int y1,
        int[] lineCounts,
        ref bool hasAnyCoverage)
    {
        if (y0 == y1)
        {
            return;
        }

        if (x0 == x1)
        {
            if (y0 < y1)
            {
                VerticalDown(lineArrays, allocator, tileHeight, rowBandCount, x0, y0, y1, lineCounts, ref hasAnyCoverage);
            }
            else
            {
                VerticalUp(lineArrays, allocator, tileHeight, rowBandCount, x0, y0, y1, lineCounts, ref hasAnyCoverage);
            }

            return;
        }

        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        if (dx > MaximumDelta || dy > MaximumDelta)
        {
            int mx = (x0 + x1) >> 1;
            int my = (y0 + y1) >> 1;
            AddContainedLineF24Dot8(lineArrays, allocator, tileHeight, rowBandCount, x0, y0, mx, my, lineCounts, ref hasAnyCoverage);
            AddContainedLineF24Dot8(lineArrays, allocator, tileHeight, rowBandCount, mx, my, x1, y1, lineCounts, ref hasAnyCoverage);
            return;
        }

        int rowIndex0;
        int rowIndex1;
        if (y0 < y1)
        {
            rowIndex0 = y0 / (tileHeight * FixedOne);
            rowIndex1 = (y1 - 1) / (tileHeight * FixedOne);
        }
        else
        {
            rowIndex0 = (y0 - 1) / (tileHeight * FixedOne);
            rowIndex1 = y1 / (tileHeight * FixedOne);
        }

        if ((uint)rowIndex0 >= (uint)rowBandCount || (uint)rowIndex1 >= (uint)rowBandCount)
        {
            return;
        }

        if (rowIndex0 == rowIndex1)
        {
            int rowTop = rowIndex0 * tileHeight * FixedOne;
            lineArrays[rowIndex0].AppendLine(allocator, x0, y0 - rowTop, x1, y1 - rowTop);
            lineCounts[rowIndex0]++;
            hasAnyCoverage = true;
            return;
        }

        SplitAcrossBands(lineArrays, allocator, tileHeight, rowBandCount, x0, y0, x1, y1, lineCounts, ref hasAnyCoverage);
    }

    private static void AddContainedLineF24Dot8(
        LineArrayX16Y16[] lineArrays,
        MemoryAllocator allocator,
        int tileHeight,
        int rowBandCount,
        int x0,
        int y0,
        int x1,
        int y1,
        int[] lineCounts,
        ref bool hasAnyCoverage)
    {
        if (y0 == y1)
        {
            return;
        }

        if (x0 == x1)
        {
            if (y0 < y1)
            {
                VerticalDown(lineArrays, allocator, tileHeight, rowBandCount, x0, y0, y1, lineCounts, ref hasAnyCoverage);
            }
            else
            {
                VerticalUp(lineArrays, allocator, tileHeight, rowBandCount, x0, y0, y1, lineCounts, ref hasAnyCoverage);
            }

            return;
        }

        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        if (dx > MaximumDelta || dy > MaximumDelta)
        {
            int mx = (x0 + x1) >> 1;
            int my = (y0 + y1) >> 1;
            AddContainedLineF24Dot8(lineArrays, allocator, tileHeight, rowBandCount, x0, y0, mx, my, lineCounts, ref hasAnyCoverage);
            AddContainedLineF24Dot8(lineArrays, allocator, tileHeight, rowBandCount, mx, my, x1, y1, lineCounts, ref hasAnyCoverage);
            return;
        }

        int rowIndex0;
        int rowIndex1;
        if (y0 < y1)
        {
            rowIndex0 = y0 / (tileHeight * FixedOne);
            rowIndex1 = (y1 - 1) / (tileHeight * FixedOne);
        }
        else
        {
            rowIndex0 = (y0 - 1) / (tileHeight * FixedOne);
            rowIndex1 = y1 / (tileHeight * FixedOne);
        }

        if ((uint)rowIndex0 >= (uint)rowBandCount || (uint)rowIndex1 >= (uint)rowBandCount)
        {
            return;
        }

        if (rowIndex0 == rowIndex1)
        {
            int rowTop = rowIndex0 * tileHeight * FixedOne;
            lineArrays[rowIndex0].AppendLine(allocator, x0, y0 - rowTop, x1, y1 - rowTop);
            lineCounts[rowIndex0]++;
            hasAnyCoverage = true;
            return;
        }

        SplitAcrossBands(lineArrays, allocator, tileHeight, rowBandCount, x0, y0, x1, y1, lineCounts, ref hasAnyCoverage);
    }

    private static void SplitAcrossBands(
        LineArrayX32Y16[] lineArrays,
        MemoryAllocator allocator,
        int tileHeight,
        int rowBandCount,
        int x0,
        int y0,
        int x1,
        int y1,
        int[] lineCounts,
        ref bool hasAnyCoverage)
    {
        int dy = y1 - y0;
        int dx = x1 - x0;
        int startBand = dy > 0 ? y0 / (tileHeight * FixedOne) : (y0 - 1) / (tileHeight * FixedOne);
        int endBand = dy > 0 ? (y1 - 1) / (tileHeight * FixedOne) : y1 / (tileHeight * FixedOne);
        int step = dy > 0 ? 1 : -1;
        int currentBand = startBand;
        int currentX = x0;
        int currentY = y0;
        while (currentBand != endBand)
        {
            int bandBoundaryY = dy > 0 ? ((currentBand + 1) * tileHeight * FixedOne) : (currentBand * tileHeight * FixedOne);
            int deltaY = bandBoundaryY - currentY;
            int nextX = currentX + (int)(((long)dx * deltaY) / dy);
            int rowTop = currentBand * tileHeight * FixedOne;
            lineArrays[currentBand].AppendLine(allocator, currentX, currentY - rowTop, nextX, bandBoundaryY - rowTop);
            lineCounts[currentBand]++;
            hasAnyCoverage = true;
            currentX = nextX;
            currentY = bandBoundaryY;
            currentBand += step;
            if ((uint)currentBand >= (uint)rowBandCount)
            {
                return;
            }
        }

        int finalRowTop = endBand * tileHeight * FixedOne;
        lineArrays[endBand].AppendLine(allocator, currentX, currentY - finalRowTop, x1, y1 - finalRowTop);
        lineCounts[endBand]++;
        hasAnyCoverage = true;
    }

    private static void SplitAcrossBands(
        LineArrayX16Y16[] lineArrays,
        MemoryAllocator allocator,
        int tileHeight,
        int rowBandCount,
        int x0,
        int y0,
        int x1,
        int y1,
        int[] lineCounts,
        ref bool hasAnyCoverage)
    {
        int dy = y1 - y0;
        int dx = x1 - x0;
        int startBand = dy > 0 ? y0 / (tileHeight * FixedOne) : (y0 - 1) / (tileHeight * FixedOne);
        int endBand = dy > 0 ? (y1 - 1) / (tileHeight * FixedOne) : y1 / (tileHeight * FixedOne);
        int step = dy > 0 ? 1 : -1;
        int currentBand = startBand;
        int currentX = x0;
        int currentY = y0;
        while (currentBand != endBand)
        {
            int bandBoundaryY = dy > 0 ? ((currentBand + 1) * tileHeight * FixedOne) : (currentBand * tileHeight * FixedOne);
            int deltaY = bandBoundaryY - currentY;
            int nextX = currentX + (int)(((long)dx * deltaY) / dy);
            int rowTop = currentBand * tileHeight * FixedOne;
            lineArrays[currentBand].AppendLine(allocator, currentX, currentY - rowTop, nextX, bandBoundaryY - rowTop);
            lineCounts[currentBand]++;
            hasAnyCoverage = true;
            currentX = nextX;
            currentY = bandBoundaryY;
            currentBand += step;
            if ((uint)currentBand >= (uint)rowBandCount)
            {
                return;
            }
        }

        int finalRowTop = endBand * tileHeight * FixedOne;
        lineArrays[endBand].AppendLine(allocator, currentX, currentY - finalRowTop, x1, y1 - finalRowTop);
        lineCounts[endBand]++;
        hasAnyCoverage = true;
    }

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
        int translateX,
        int translateY,
        in RasterizerOptions options,
        MemoryAllocator allocator)
    {
        float samplingOffsetX = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        float samplingOffsetY = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;

        RectangleF translatedBounds = geometry.Info.Bounds;
        translatedBounds.Offset(translateX + samplingOffsetX, translateY + samplingOffsetY);

        Rectangle geometryBounds = Rectangle.FromLTRB(
            (int)MathF.Floor(translatedBounds.Left),
            (int)MathF.Floor(translatedBounds.Top),
            (int)MathF.Ceiling(translatedBounds.Right),
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
        public bool HasCoverage(int localRowIndex) => this.bandInfos[localRowIndex].HasCoverage;

        /// <summary>
        /// Gets the retained narrow line block chain for one local row.
        /// </summary>
        public LineArrayX16Y16Block? GetLinesX16ForRow(int localRowIndex) => this.linesX16![localRowIndex];

        /// <summary>
        /// Gets the retained wide line block chain for one local row.
        /// </summary>
        public LineArrayX32Y16Block? GetLinesX32ForRow(int localRowIndex) => this.linesX32![localRowIndex];

        /// <summary>
        /// Gets the number of valid lines in the first retained block for a local row.
        /// </summary>
        public int GetFirstBlockLineCountForRow(int localRowIndex) => this.firstBlockLineCounts[localRowIndex];

        /// <summary>
        /// Gets the retained start-cover table entry for a local row, if one exists.
        /// </summary>
        public ReadOnlySpan<int> GetCoversForRow(int localRowIndex)
        {
            IMemoryOwner<int>? covers = this.startCoverTable[localRowIndex];
            return covers is null ? ReadOnlySpan<int>.Empty : covers.Memory.Span[..this.BandHeight];
        }

        /// <summary>
        /// Gets the retained start-cover row payload without further interpretation, matching Blaze naming.
        /// </summary>
        public ReadOnlySpan<int> GetActualCoversForRow(int localRowIndex) => this.GetCoversForRow(localRowIndex);

        /// <summary>
        /// Gets retained metadata for one local row band.
        /// </summary>
        public RasterizableBandInfo GetBandInfo(int localRowIndex) => this.bandInfos[localRowIndex];

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.IsX16)
            {
                for (int i = 0; i < this.linesX16!.Length; i++)
                {
                    LineArrayX16Y16Block? block = this.linesX16[i];
                    while (block is not null)
                    {
                        LineArrayX16Y16Block? next = block.Next;
                        block.Dispose();
                        block = next;
                    }
                }
            }
            else
            {
                for (int i = 0; i < this.linesX32!.Length; i++)
                {
                    LineArrayX32Y16Block? block = this.linesX32[i];
                    while (block is not null)
                    {
                        LineArrayX32Y16Block? next = block.Next;
                        block.Dispose();
                        block = next;
                    }
                }
            }

            for (int i = 0; i < this.startCoverTable.Length; i++)
            {
                this.startCoverTable[i]?.Dispose();
            }
        }
    }

#pragma warning disable SA1201 // Keep the retained row item adjacent to the retained geometry and line storage it uses.
    /// <summary>
    /// Tiny retained row item that mirrors Blaze: one rasterizable plus one local row index.
    /// </summary>
    internal readonly struct RasterizableItem
    {
        public RasterizableItem(RasterizableGeometry rasterizable, int localRowIndex)
        {
            this.Rasterizable = rasterizable;
            this.LocalRowIndex = localRowIndex;
        }

        public RasterizableGeometry Rasterizable { get; }

        public int LocalRowIndex { get; }

        public int GetFirstBlockLineCount() => this.Rasterizable.GetFirstBlockLineCountForRow(this.LocalRowIndex);

        public LineArrayX16Y16Block? GetLineArrayX16() => this.Rasterizable.GetLinesX16ForRow(this.LocalRowIndex);

        public LineArrayX32Y16Block? GetLineArrayX32() => this.Rasterizable.GetLinesX32ForRow(this.LocalRowIndex);

        public ReadOnlySpan<int> GetActualCovers() => this.Rasterizable.GetActualCoversForRow(this.LocalRowIndex);
    }
#pragma warning restore SA1201

    internal sealed class LineArrayX32Y16
    {
        private LineArrayX32Y16Block? current;
        private int count = LineArrayX32Y16Block.LineCount;

        public LineArrayX32Y16Block? GetFrontBlock() => this.current;

        public int GetFrontBlockLineCount() => this.current is null ? 0 : this.count;

        public void AppendLine(MemoryAllocator allocator, int x0, int y0, int x1, int y1)
        {
            if (y0 == y1)
            {
                return;
            }

            int packedY0Y1 = Pack(y0, y1);
            LineArrayX32Y16Block? block = this.current;
            int currentCount = this.count;
            if (currentCount < LineArrayX32Y16Block.LineCount)
            {
                block!.Set(currentCount, packedY0Y1, x0, x1);
                this.count = currentCount + 1;
            }
            else
            {
                LineArrayX32Y16Block next = new(allocator, block);
                next.Set(0, packedY0Y1, x0, x1);
                this.current = next;
                this.count = 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Pack(int lo, int hi) => (lo & 0xFFFF) | (hi << 16);
    }

    internal sealed class LineArrayX32Y16Block : ILineBlock<LineArrayX32Y16Block>, IDisposable
    {
        // These retained line blocks are one of the few places where direct native allocation
        // consistently outperforms the upstream allocator on larger retained-fill workloads.
        // Keep this isolated to the retained block storage rather than broadening native allocation usage.
        private readonly unsafe PackedLineX32Y16* lines;

        public unsafe LineArrayX32Y16Block(MemoryAllocator allocator, LineArrayX32Y16Block? next)
        {
            this.lines = (PackedLineX32Y16*)NativeMemory.Alloc((nuint)LineCount, (nuint)sizeof(PackedLineX32Y16));
            this.Next = next;
        }

        public static int LineCount => 32;

        public LineArrayX32Y16Block? Next { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Set(int index, int packedY0Y1, int x0, int x1)
        {
            ref PackedLineX32Y16 line = ref this.lines[index];
            line.PackedY0Y1 = packedY0Y1;
            line.X0 = x0;
            line.X1 = x1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Rasterize(int count, ref Context context)
        {
            ReadOnlySpan<PackedLineX32Y16> lines = new(this.lines, LineCount);
            for (int i = 0; i < count; i++)
            {
                PackedLineX32Y16 line = lines[i];
                context.RasterizeLineSegment(line.X0, UnpackLo(line.PackedY0Y1), line.X1, UnpackHi(line.PackedY0Y1));
            }
        }

        public void Iterate(int firstBlockLineCount, ref Context context)
        {
            int count = firstBlockLineCount;
            LineArrayX32Y16Block? lineBlock = this;
            while (lineBlock is not null)
            {
                lineBlock.Rasterize(count, ref context);
                lineBlock = lineBlock.Next;
                count = LineCount;
            }
        }

        public unsafe void Dispose() => NativeMemory.Free(this.lines);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int UnpackLo(int packed) => (short)(packed & 0xFFFF);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int UnpackHi(int packed) => packed >> 16;

        private struct PackedLineX32Y16
        {
            public int PackedY0Y1;
            public int X0;
            public int X1;
        }
    }

    internal sealed class LineArrayX16Y16
    {
        private LineArrayX16Y16Block? current;
        private int count = LineArrayX16Y16Block.LineCount;

        public LineArrayX16Y16Block? GetFrontBlock() => this.current;

        public int GetFrontBlockLineCount() => this.current is null ? 0 : this.count;

        public void AppendLine(MemoryAllocator allocator, int x0, int y0, int x1, int y1)
        {
            if (y0 == y1)
            {
                return;
            }

            int packedY0Y1 = Pack(y0, y1);
            int packedX0X1 = Pack(x0, x1);
            LineArrayX16Y16Block? block = this.current;
            int currentCount = this.count;
            if (currentCount < LineArrayX16Y16Block.LineCount)
            {
                block!.Set(currentCount, packedY0Y1, packedX0X1);
                this.count = currentCount + 1;
            }
            else
            {
                LineArrayX16Y16Block next = new(allocator, block);
                next.Set(0, packedY0Y1, packedX0X1);
                this.current = next;
                this.count = 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Pack(int lo, int hi) => (lo & 0xFFFF) | (hi << 16);
    }

    internal sealed class LineArrayX16Y16Block : ILineBlock<LineArrayX16Y16Block>, IDisposable
    {
        // Match the X32 block rationale above: this tiny retained block is hot enough that
        // direct native backing beats allocator-owned storage on larger retained-fill workloads.
        private readonly unsafe PackedLineX16Y16* lines;

        public unsafe LineArrayX16Y16Block(MemoryAllocator allocator, LineArrayX16Y16Block? next)
        {
            this.lines = (PackedLineX16Y16*)NativeMemory.Alloc((nuint)LineCount, (nuint)sizeof(PackedLineX16Y16));
            this.Next = next;
        }

        public static int LineCount => 32;

        public LineArrayX16Y16Block? Next { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Set(int index, int packedY0Y1, int packedX0X1)
        {
            ref PackedLineX16Y16 line = ref this.lines[index];
            line.PackedY0Y1 = packedY0Y1;
            line.PackedX0X1 = packedX0X1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Rasterize(int count, ref Context context)
        {
            ReadOnlySpan<PackedLineX16Y16> lines = new(this.lines, LineCount);
            for (int i = 0; i < count; i++)
            {
                PackedLineX16Y16 line = lines[i];
                context.RasterizeLineSegment(
                    UnpackLo(line.PackedX0X1),
                    UnpackLo(line.PackedY0Y1),
                    UnpackHi(line.PackedX0X1),
                    UnpackHi(line.PackedY0Y1));
            }
        }

        public void Iterate(int firstBlockLineCount, ref Context context)
        {
            int count = firstBlockLineCount;
            LineArrayX16Y16Block? lineBlock = this;
            while (lineBlock is not null)
            {
                lineBlock.Rasterize(count, ref context);
                lineBlock = lineBlock.Next;
                count = LineCount;
            }
        }

        public unsafe void Dispose() => NativeMemory.Free(this.lines);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int UnpackLo(int packed) => (short)(packed & 0xFFFF);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int UnpackHi(int packed) => packed >> 16;

        private struct PackedLineX16Y16
        {
            public int PackedY0Y1;
            public int PackedX0X1;
        }
    }

#pragma warning disable SA1201 // Keep the lightweight band metadata adjacent to the retained geometry that owns it.
    /// <summary>
    /// Metadata that describes one prepared rasterizable band.
    /// </summary>
    internal readonly struct RasterizableBandInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RasterizableBandInfo"/> struct.
        /// </summary>
        public RasterizableBandInfo(
            int lineCount,
            int bandHeight,
            int width,
            int wordsPerRow,
            int coverStride,
            int destinationLeft,
            int destinationTop,
            IntersectionRule intersectionRule,
            RasterizationMode rasterizationMode,
            float antialiasThreshold,
            bool hasStartCovers)
        {
            this.LineCount = lineCount;
            this.BandHeight = bandHeight;
            this.Width = width;
            this.WordsPerRow = wordsPerRow;
            this.CoverStride = coverStride;
            this.DestinationLeft = destinationLeft;
            this.DestinationTop = destinationTop;
            this.IntersectionRule = intersectionRule;
            this.RasterizationMode = rasterizationMode;
            this.AntialiasThreshold = antialiasThreshold;
            this.HasStartCovers = hasStartCovers;
        }

        /// <summary>
        /// Gets the number of visible raster lines stored for the band.
        /// </summary>
        public int LineCount { get; }

        /// <summary>
        /// Gets the band height in pixels.
        /// </summary>
        public int BandHeight { get; }

        /// <summary>
        /// Gets the visible band width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the bit-vector width in machine words.
        /// </summary>
        public int WordsPerRow { get; }

        /// <summary>
        /// Gets the scanner cover/area stride.
        /// </summary>
        public int CoverStride { get; }

        /// <summary>
        /// Gets the absolute destination X coordinate of the band's left column.
        /// </summary>
        public int DestinationLeft { get; }

        /// <summary>
        /// Gets the absolute destination Y coordinate of the band's top row.
        /// </summary>
        public int DestinationTop { get; }

        /// <summary>
        /// Gets the fill rule used when resolving accumulated winding.
        /// </summary>
        public IntersectionRule IntersectionRule { get; }

        /// <summary>
        /// Gets the coverage mode used by the band.
        /// </summary>
        public RasterizationMode RasterizationMode { get; }

        /// <summary>
        /// Gets the aliased threshold used when the band runs in aliased mode.
        /// </summary>
        public float AntialiasThreshold { get; }

        /// <summary>
        /// Gets a value indicating whether the band has non-zero start-cover seeds.
        /// </summary>
        public bool HasStartCovers { get; }

        /// <summary>
        /// Gets a value indicating whether the band would emit any coverage.
        /// </summary>
        public bool HasCoverage => this.LineCount > 0 || this.HasStartCovers;
    }
#pragma warning restore SA1201

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
        private readonly int widthCapacity;
        private readonly int heightCapacity;
        private readonly int wordsPerRowCapacity;
        private readonly int coverStrideCapacity;
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
        public Context(
            Span<nuint> bitVectors,
            Span<int> coverArea,
            Span<int> startCover,
            Span<int> rowMinTouchedColumn,
            Span<int> rowMaxTouchedColumn,
            Span<byte> rowHasBits,
            Span<byte> rowTouched,
            Span<int> touchedRows,
            int width,
            int height,
            int wordsPerRow,
            int coverStride,
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
            this.widthCapacity = width;
            this.heightCapacity = height;
            this.wordsPerRowCapacity = wordsPerRow;
            this.coverStrideCapacity = coverStride;
            this.width = width;
            this.height = height;
            this.wordsPerRow = wordsPerRow;
            this.coverStride = coverStride;
            this.intersectionRule = intersectionRule;
            this.rasterizationMode = rasterizationMode;
            this.antialiasThreshold = antialiasThreshold;
            this.touchedRowCount = 0;
        }

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
                if (rowCover == 0 && !rowHasBits)
                {
                    // Safety guard — should not fire in practice.
                    continue;
                }

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
                            WriteSpan(scanline, spanStart, spanEnd, spanCoverage, ref runStart, ref runEnd);
                            EmitRun(ref rowHandler, destinationY, destinationLeft, scanline, ref runStart, ref runEnd);
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
                            WriteSpan(scanline, spanStart, spanEnd, spanCoverage, ref runStart, ref runEnd);
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
                            WriteSpan(scanline, spanStart, spanEnd, spanCoverage, ref runStart, ref runEnd);
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
                                // Treat this as a hard run break so we don't bridge holes.
                                WriteSpan(scanline, spanStart, spanEnd, spanCoverage, ref runStart, ref runEnd);
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
                                    WriteSpan(scanline, spanStart, x, spanCoverage, ref runStart, ref runEnd);
                                    spanStart = x;
                                    spanEnd = x + 1;
                                    spanCoverage = coverage;
                                }
                            }
                            else
                            {
                                WriteSpan(scanline, spanStart, spanEnd, spanCoverage, ref runStart, ref runEnd);
                                WriteSpan(scanline, spanEnd, x, gapCoverage, ref runStart, ref runEnd);
                                spanStart = x;
                                spanEnd = x + 1;
                                spanCoverage = coverage;
                            }
                        }
                    }

                    cover += this.coverArea[tableIndex];
                }
            }

            // Flush tail run and any remaining constant-cover tail after the last touched cell.
            WriteSpan(scanline, spanStart, spanEnd, spanCoverage, ref runStart, ref runEnd);

            if (cover != 0 && spanEnd < this.width)
            {
                WriteSpan(scanline, spanEnd, this.width, this.AreaToCoverage(cover << AreaToCoverageShift), ref runStart, ref runEnd);
            }

            EmitRun(ref rowHandler, destinationY, destinationLeft, scanline, ref runStart, ref runEnd);
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
        /// Writes one non-zero coverage segment into the scanline and expands the active run.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteSpan(
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

            scanline[(start - runStart)..(end - runStart)].Fill(coverage);
        }

        /// <summary>
        /// Emits the currently accumulated non-zero run, if any.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EmitRun<TRowHandler>(
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
    /// Immutable line record stored in band-local raster coordinates.
    /// </summary>
    /// <summary>
    /// Reusable per-worker scratch buffers used by raster band execution.
    /// </summary>
    internal sealed class WorkerScratch : IDisposable
    {
        private readonly int wordsPerRow;
        private readonly int coverStride;
        private readonly int width;
        private readonly int tileCapacity;
        private readonly IMemoryOwner<nuint> bitVectorsOwner;
        private readonly IMemoryOwner<int> coverAreaOwner;
        private readonly IMemoryOwner<int> startCoverOwner;
        private readonly IMemoryOwner<int> rowMinTouchedColumnOwner;
        private readonly IMemoryOwner<int> rowMaxTouchedColumnOwner;
        private readonly IMemoryOwner<byte> rowHasBitsOwner;
        private readonly IMemoryOwner<byte> rowTouchedOwner;
        private readonly IMemoryOwner<int> touchedRowsOwner;
        private readonly IMemoryOwner<float> scanlineOwner;

        private WorkerScratch(
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
            int width,
            int wordsPerRow,
            int coverStride,
            int bandHeight,
            IntersectionRule intersectionRule,
            RasterizationMode rasterizationMode,
            float antialiasThreshold)
            => new Context(
                this.bitVectorsOwner.Memory.Span,
                this.coverAreaOwner.Memory.Span,
                this.startCoverOwner.Memory.Span,
                this.rowMinTouchedColumnOwner.Memory.Span,
                this.rowMaxTouchedColumnOwner.Memory.Span,
                this.rowHasBitsOwner.Memory.Span,
                this.rowTouchedOwner.Memory.Span,
                this.touchedRowsOwner.Memory.Span,
                this.width,
                this.tileCapacity,
                this.wordsPerRow,
                this.coverStride,
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
        }
    }
}
