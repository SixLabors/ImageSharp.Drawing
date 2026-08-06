// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Fixed-point rasterizer that converts retained fill geometry into per-row coverage.
/// </summary>
/// <remarks>
/// <para>
/// The rasterizer works in scene-aligned row bands of <see cref="DefaultTileHeight"/> pixels. Each
/// retained band stores compact line blocks plus optional start-cover seeds, and execution replays
/// that retained payload directly against worker-local scratch without rebuilding geometry on every row.
/// </para>
/// <para>
/// Execution is parallel across row bands: the drawing backend runs a Parallel.For over scene rows
/// (or over a single geometry's row bands), and each worker replays the retained items that touch
/// its band sequentially into a worker-local <see cref="Context"/>. All static members here are
/// stateless; every piece of mutable state lives in a <see cref="WorkerScratch"/> owned by exactly
/// one worker, so no synchronization is required.
/// </para>
/// </remarks>
internal static partial class DefaultRasterizer
{
    /// <summary>
    /// Tile height, in pixels, of one row band in the parallel row-tiling pipeline.
    /// </summary>
    public const int DefaultTileHeight = 16;

    /// <summary>
    /// Number of fractional bits in the 24.8 fixed-point coordinate format.
    /// </summary>
    private const int FixedShift = 8;

    /// <summary>
    /// The value 1.0 in 24.8 fixed-point, i.e. one pixel cell.
    /// </summary>
    private const int FixedOne = 1 << FixedShift;

    /// <summary>
    /// Maximum per-segment coordinate delta (2048 pixels in 24.8 fixed-point) accepted by the
    /// linearizer before it splits a segment, keeping DDA intermediate products inside 32 bits.
    /// </summary>
    private const int MaximumDelta = 2048 << FixedShift;

    /// <summary>
    /// Number of bits in one native machine word; bitset rows are sized in these units.
    /// </summary>
    private static readonly int WordBitCount = nint.Size * 8;

    /// <summary>
    /// Right-shift that converts an accumulated doubled cell area (max 2 * 256 * 256) down to the
    /// 0..256 coverage step domain used by <see cref="Context.AreaToCoverage"/>.
    /// </summary>
    private const int AreaToCoverageShift = 9;

    /// <summary>
    /// Half of <see cref="FixedOne"/>, the offset from a pixel's left edge to its centre.
    /// </summary>
    private const int FixedHalf = FixedOne >> 1;

    /// <summary>
    /// Maximum number of crossings stored for one column in a 16-row band. Later crossings do not
    /// take part in the secondary column pass. The primary row result is unchanged.
    /// </summary>
    private const int ColumnCrossingCapacity = 16;

    /// <summary>
    /// Maximum number of pixels produced by the secondary column pass for one band. The fixed
    /// bound keeps this temporary list on the stack.
    /// </summary>
    private const int MaxColumnDropoutsPerBand = 256;

    /// <summary>
    /// Number of low bits reserved for a crossing's profile identifier.
    /// </summary>
    private const int ProfileIdBits = 16;

    /// <summary>
    /// Mask for the low sixteen-bit profile field in a packed crossing.
    /// </summary>
    private const int ProfileIdMask = (1 << ProfileIdBits) - 1;

    /// <summary>
    /// The bit inside a packed crossing that records a positive crossing direction.
    /// </summary>
    private const long CrossingDirectionBit = 1L << ProfileIdBits;

    /// <summary>
    /// The left shift that positions a crossing coordinate above its direction and profile bits.
    /// </summary>
    private const int CrossingShift = ProfileIdBits + 1;

    /// <summary>
    /// Number of discrete coverage steps per fully covered pixel (one 24.8 unit of winding).
    /// </summary>
    private const int CoverageStepCount = 256;

    /// <summary>
    /// Bitmask implementing modulo 2 * <see cref="CoverageStepCount"/> for even-odd wrapping.
    /// </summary>
    private const int EvenOddMask = (CoverageStepCount * 2) - 1;

    /// <summary>
    /// Length of one even-odd winding period; values past the midpoint mirror back down.
    /// </summary>
    private const int EvenOddPeriod = CoverageStepCount * 2;

    /// <summary>
    /// Multiplier converting integer coverage steps to normalized [0, 1] coverage.
    /// </summary>
    private const float CoverageScale = 1F / CoverageStepCount;

    /// <summary>
    /// Gets the preferred scene row height used by the CPU rasterizer.
    /// </summary>
    public static int PreferredRowHeight => DefaultTileHeight;

    /// <summary>
    /// Executes one retained rasterizable row item against a reusable scanner context.
    /// </summary>
    /// <typeparam name="TRowHandler">The struct coverage handler type; constrained to a value type so calls devirtualize.</typeparam>
    /// <param name="context">The worker-local scanner context to replay the item into.</param>
    /// <param name="item">The retained fill row item to execute.</param>
    /// <param name="bandInfo">The retained metadata describing the destination band.</param>
    /// <param name="scanline">Reusable scanline scratch used to materialize emitted coverage spans.</param>
    /// <param name="rowHandler">The coverage callback invoked for each emitted non-zero span.</param>
    public static void ExecuteRasterizableItem<TRowHandler>(
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
            bandInfo.CoverageBoost);

        context.SeedStartCovers(item.GetActualCovers());
        if (bandInfo.RasterizationMode == RasterizationMode.Aliased)
        {
            context.SetProfileTables(
                item.Rasterizable.Profiles,
                item.Rasterizable.ProfileTranslateX,
                item.Rasterizable.ProfileTranslateY);

            if (item.Rasterizable.IsX16)
            {
                LineArrayX16Y16Block? lines = item.GetLineArrayX16();
                lines?.IterateTagged(item.GetFirstBlockLineCount(), ref context);
            }
            else
            {
                LineArrayX32Y16Block? lines = item.GetLineArrayX32();
                lines?.IterateTagged(item.GetFirstBlockLineCount(), ref context);
            }
        }
        else if (item.Rasterizable.IsX16)
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
    /// <typeparam name="TRowHandler">The struct coverage handler type; constrained to a value type so calls devirtualize.</typeparam>
    /// <param name="context">The worker-local scanner context to replay the item into.</param>
    /// <param name="item">The retained stroke row item to execute.</param>
    /// <param name="bandInfo">The retained metadata describing the destination band.</param>
    /// <param name="scanline">Reusable scanline scratch used to materialize emitted coverage spans.</param>
    /// <param name="strokeBandCoverage">Reusable per-band stroke coverage scratch used by the direct stroke path.</param>
    /// <param name="rowHandler">The coverage callback invoked for each emitted non-zero span.</param>
    public static void ExecuteStrokeRasterizableItem<TRowHandler>(
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
            bandInfo.CoverageBoost);

        // Stroke outline edges have no source profiles. Clear the previous fill's profile data so
        // a stroke cannot use it when classifying a centre-free interval.
        context.SetProfileTables(default, 0F, 0F);
        item.Rasterizable.ExecuteBand(ref context, in bandInfo, scanline, strokeBandCoverage, ref rowHandler);
    }

    /// <summary>
    /// Converts bit count to the number of machine words needed to hold the bitset row.
    /// </summary>
    /// <param name="maxBitCount">The maximum number of bits the row must represent.</param>
    /// <returns>The number of machine words required, rounded up.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BitVectorsForMaxBitCount(int maxBitCount) => (maxBitCount + WordBitCount - 1) / WordBitCount;

    /// <summary>
    /// Allocates worker-local scratch sized for the default band configuration at the given width.
    /// </summary>
    /// <param name="allocator">The memory allocator that owns the scratch buffers.</param>
    /// <param name="width">The maximum band width, in pixels, the scratch must support.</param>
    /// <returns>A new <see cref="WorkerScratch"/> instance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static WorkerScratch CreateWorkerScratch(MemoryAllocator allocator, int width)
        => WorkerScratch.Create(allocator, BitVectorsForMaxBitCount(width), checked(width << 1), width, PreferredRowHeight);

    /// <summary>
    /// Converts a float coordinate to signed 24.8 fixed-point.
    /// </summary>
    /// <param name="value">The floating-point coordinate to convert.</param>
    /// <returns>The rounded 24.8 fixed-point value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FloatToFixed24Dot8(float value) => (int)MathF.Round(value * FixedOne);

    /// <summary>
    /// Returns one when a fixed-point value lies exactly on a cell boundary at or below zero.
    /// This is used to keep edge ownership consistent for vertical lines.
    /// </summary>
    /// <param name="value">The 24.8 fixed-point coordinate to test.</param>
    /// <returns>One when the value is a non-positive exact cell boundary; otherwise zero.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindAdjustment(int value)
    {
        // Branchless: sign-extend (value - 1) so lte0 is 1 for value <= 0, and test the low
        // fractional bits for an exact multiple of FixedOne the same way. The product of the two
        // flags nudges boundary-sitting vertical edges into the cell to their left.
        int lte0 = ~((value - 1) >> 31) & 1;
        int divisibleBy256 = (((value & (FixedOne - 1)) - 1) >> 31) & 1;
        return lte0 & divisibleBy256;
    }

    /// <summary>
    /// Machine-word trailing zero count used for sparse bitset iteration.
    /// </summary>
    /// <param name="value">The word to scan.</param>
    /// <returns>The number of trailing zero bits in <paramref name="value"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int TrailingZeroCount(nuint value)
        => nint.Size == sizeof(ulong)
            ? BitOperations.TrailingZeroCount((ulong)value)
            : BitOperations.TrailingZeroCount((uint)value);

    /// <summary>
    /// Throws when the requested raster interest exceeds the scanner's indexing limits.
    /// </summary>
    /// <exception cref="ImageProcessingException">Always thrown.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInterestBoundsTooLarge()
        => throw new ImageProcessingException("The rasterizer interest bounds are too large for DefaultRasterizer buffers.");

    /// <summary>
    /// Creates retained row-local raster payload for one lowered geometry.
    /// </summary>
    /// <param name="geometry">The lowered linear geometry to linearize into retained line blocks.</param>
    /// <param name="residual">The residual transform to apply to the geometry before rasterization.</param>
    /// <param name="translateX">The integer X translation applied after the residual transform.</param>
    /// <param name="translateY">The integer Y translation applied after the residual transform.</param>
    /// <param name="options">The rasterizer options carrying interest bounds, fill rule, and antialias settings.</param>
    /// <param name="allocator">The memory allocator used for retained start-cover storage.</param>
    /// <param name="profileStorage">The partition-owned profile arena used by aliased fills.</param>
    /// <returns>The retained geometry payload, or <see langword="null"/> when nothing is visible.</returns>
    public static RasterizableGeometry? CreateRasterizableGeometry(
        LinearGeometry geometry,
        Matrix4x4 residual,
        int translateX,
        int translateY,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        LinearGeometryProfileStorage profileStorage)
    {
        RectangleF translatedBounds = residual.IsIdentity ? geometry.Info.Bounds : RectangleF.Transform(geometry.Info.Bounds, residual);
        translatedBounds.Offset(translateX, translateY);

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

        // Narrow geometries pack both X endpoints into one 32-bit word: band-local 24.8 X values
        // stay below 128 * 256 = 32768, so they fit signed 16 bits and halve retained line memory.
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
                allocator,
                profileStorage,
                options.RasterizationMode == RasterizationMode.Aliased);

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
                    options.CoverageBoost,
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
                result.StartCoverTable,
                result.Profiles,
                result.ProfileTranslateX,
                result.ProfileTranslateY);
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
                allocator,
                profileStorage,
                options.RasterizationMode == RasterizationMode.Aliased);

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
                    options.CoverageBoost,
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
                result.StartCoverTable,
                result.Profiles,
                result.ProfileTranslateX,
                result.ProfileTranslateY);
        }
    }

    /// <summary>
    /// Counts the total retained lines in a block chain.
    /// </summary>
    /// <typeparam name="TLineBlock">The retained line block type.</typeparam>
    /// <param name="firstLineBlock">The front block of the chain, or <see langword="null"/> when the chain is empty.</param>
    /// <param name="firstBlockLineCount">The number of valid lines in the front block.</param>
    /// <returns>The total line count across the chain.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountLines<TLineBlock>(TLineBlock? firstLineBlock, int firstBlockLineCount)
        where TLineBlock : class, ILineBlock<TLineBlock>
    {
        if (firstLineBlock is null)
        {
            return 0;
        }

        // Only the front block is partially filled; every block behind it is full by construction
        // because the collectors allocate a new front block only after the previous one overflows.
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
        private readonly Span<long> crossings;
        private readonly Span<int> crossingCounts;
        private readonly Span<uint> columnCrossings;
        private readonly Span<int> columnCrossingCounts;
        private readonly Span<int> touchedColumns;
        private LinearGeometryProfiles profiles;
        private float columnProfileTranslate;
        private float rowProfileTranslate;
        private int touchedColumnCount;
        private readonly int crossingStride;
        private int width;
        private int height;
        private int wordsPerRow;
        private int coverStride;
        private IntersectionRule intersectionRule;
        private RasterizationMode rasterizationMode;
        private float coverageBoost;
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
        /// <param name="crossings">Scratch storage for where the outline crosses each scanline's centre line.</param>
        /// <param name="crossingCounts">Scratch storage for the number of crossings recorded on each row.</param>
        /// <param name="columnCrossings">Scratch storage for where the outline crosses each column's centre line.</param>
        /// <param name="columnCrossingCounts">Scratch storage for the number of crossings recorded on each column.</param>
        /// <param name="touchedColumns">Scratch list of columns that received centre line crossings.</param>
        /// <param name="crossingStride">The exact per-row crossing capacity for the retained band.</param>
        /// <param name="intersectionRule">The fill rule used when converting accumulated winding/coverage into final alpha.</param>
        /// <param name="rasterizationMode">The rasterization mode that selects continuous or centre-sampled coverage.</param>
        public Context(
            Span<nuint> bitVectors,
            Span<int> coverArea,
            Span<int> startCover,
            Span<int> rowMinTouchedColumn,
            Span<int> rowMaxTouchedColumn,
            Span<byte> rowHasBits,
            Span<byte> rowTouched,
            Span<int> touchedRows,
            Span<long> crossings,
            Span<int> crossingCounts,
            Span<uint> columnCrossings,
            Span<int> columnCrossingCounts,
            Span<int> touchedColumns,
            int crossingStride,
            IntersectionRule intersectionRule,
            RasterizationMode rasterizationMode)
        {
            this.crossings = crossings;
            this.crossingCounts = crossingCounts;
            this.columnCrossings = columnCrossings;
            this.columnCrossingCounts = columnCrossingCounts;
            this.touchedColumns = touchedColumns;
            this.profiles = default;
            this.columnProfileTranslate = 0F;
            this.rowProfileTranslate = 0F;
            this.touchedColumnCount = 0;
            this.crossingStride = crossingStride;
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
            this.coverageBoost = 0F;
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
        /// <param name="rasterizationMode">The rasterization mode that selects continuous or centre-sampled coverage.</param>
        /// <param name="coverageBoost">The perceptual coverage boost applied to antialiased coverage; zero disables it.</param>
        public void Reconfigure(
            int width,
            int wordsPerRow,
            int coverStride,
            int height,
            IntersectionRule intersectionRule,
            RasterizationMode rasterizationMode,
            float coverageBoost)
        {
            this.width = width;
            this.height = height;
            this.wordsPerRow = wordsPerRow;
            this.coverStride = coverStride;
            this.intersectionRule = intersectionRule;
            this.rasterizationMode = rasterizationMode;
            this.coverageBoost = coverageBoost;
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

            // Sign convention mirrors CellVertical's delta = y0 - y1: downward intervals subtract
            // winding and upward intervals add it, so left-of-band edges stay consistent with
            // edges rasterized through the cell tables.
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
            => this.RasterizeLine(x0, y0, x1, y1, LinearGeometryProfiles.SentinelTag);

        /// <summary>
        /// Rasterizes one line segment and retains its profile identifiers for aliased tip classification.
        /// </summary>
        /// <param name="x0">The starting X coordinate in 24.8 fixed-point destination space.</param>
        /// <param name="y0">The starting Y coordinate in 24.8 fixed-point destination space.</param>
        /// <param name="x1">The ending X coordinate in 24.8 fixed-point destination space.</param>
        /// <param name="y1">The ending Y coordinate in 24.8 fixed-point destination space.</param>
        /// <param name="tag">The segment's profile tag.</param>
        public void RasterizeLineSegment(int x0, int y0, int x1, int y1, uint tag)
            => this.RasterizeLine(x0, y0, x1, y1, tag);

        /// <summary>
        /// Converts accumulated cover/area tables into non-zero coverage span callbacks.
        /// </summary>
        /// <typeparam name="TRowHandler">The struct coverage handler type; constrained to a value type so calls devirtualize.</typeparam>
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
            if (this.rasterizationMode == RasterizationMode.Aliased)
            {
                this.EmitCentreSampledRows(destinationTop, destinationLeft, scanline, ref rowHandler);
                return;
            }

            this.EmitRows(destinationTop, destinationLeft, scanline, ref rowHandler);
        }

        /// <summary>
        /// Walks every touched row and hands its spans to the supplied handler.
        /// </summary>
        /// <typeparam name="TRowHandler">The struct coverage handler type; constrained to a value type so calls devirtualize.</typeparam>
        /// <param name="destinationTop">Absolute destination Y corresponding to row zero in this context.</param>
        /// <param name="destinationLeft">Absolute destination X corresponding to column zero in this context.</param>
        /// <param name="scanline">Reusable scanline scratch buffer used to materialize emitted spans.</param>
        /// <param name="rowHandler">Coverage callback invoked for each emitted non-zero span.</param>
        private readonly void EmitRows<TRowHandler>(
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
        /// Selects the profile data used to classify thin intervals in the next retained geometry.
        /// </summary>
        /// <param name="profiles">The geometry-space profile data, or an empty value.</param>
        /// <param name="translateX">The X translation from geometry space to destination space.</param>
        /// <param name="translateY">The Y translation from geometry space to destination space.</param>
        public void SetProfileTables(LinearGeometryProfiles profiles, float translateX, float translateY)
        {
            this.profiles = profiles;
            this.columnProfileTranslate = translateX;
            this.rowProfileTranslate = translateY;
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
            bool hasCrossings = !this.crossingCounts.IsEmpty;
            for (int i = 0; i < this.touchedRowCount; i++)
            {
                int row = this.touchedRows[i];
                this.startCover[row] = 0;
                this.rowTouched[row] = 0;

                if (hasCrossings)
                {
                    this.crossingCounts[row] = 0;
                }

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

            // Column crossing scratch is pooled alongside the row scratch, so the columns this
            // pass touched must be cleared the same sparse way before the next pass reuses it.
            for (int i = 0; i < this.touchedColumnCount; i++)
            {
                this.columnCrossingCounts[this.touchedColumns[i]] = 0;
            }

            this.touchedColumnCount = 0;
        }

        /// <summary>
        /// Emits one row by iterating touched columns and coalescing equal-coverage spans.
        /// </summary>
        /// <typeparam name="TRowHandler">The struct coverage handler type; constrained to a value type so calls devirtualize.</typeparam>
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
        /// Emits an aliased band by setting each pixel whose centre the shape covers.
        /// </summary>
        /// <typeparam name="TRowHandler">The struct coverage handler type; constrained to a value type so calls devirtualize.</typeparam>
        /// <param name="destinationTop">Absolute destination Y corresponding to row zero in this context.</param>
        /// <param name="destinationLeft">Absolute destination X corresponding to column zero in this context.</param>
        /// <param name="scanline">Reusable scanline scratch buffer used to materialize emitted spans.</param>
        /// <param name="rowHandler">Coverage callback invoked for each emitted run.</param>
        private readonly void EmitCentreSampledRows<TRowHandler>(
            int destinationTop,
            int destinationLeft,
            Span<float> scanline,
            ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            // The main pass walks horizontal centre lines. It cannot see a horizontal feature that
            // lies completely between two row centres. The column pass finds those features first.
            // For each closed vertical interval that contains no row centre, it records the pixel
            // containing the interval midpoint. Packing row and column into one integer lets one
            // sort group the results by row and then by column.
            Span<int> columnDropouts = stackalloc int[MaxColumnDropoutsPerBand];
            int columnDropoutCount = this.CollectColumnDropouts(columnDropouts, destinationLeft);
            columnDropouts = columnDropouts[..columnDropoutCount];
            columnDropouts.Sort();

            for (int i = 0; i < this.touchedRowCount; i++)
            {
                int row = this.touchedRows[i];
                int count = this.crossingCounts[row];

                // startCover contains signed vertical cover from edges left of this band. One full
                // pixel of cover represents one crossing, so round it to the winding at the first
                // pixel centre before processing the retained crossings.
                int winding = NearestCrossingCount(this.startCover[row]);
                if (this.intersectionRule == IntersectionRule.EvenOdd)
                {
                    winding &= 1;
                }

                if (count == 0)
                {
                    if (winding != 0)
                    {
                        scanline[..this.width].Fill(1F);
                        rowHandler.Handle(destinationTop + row, destinationLeft, scanline[..this.width]);
                    }

                    continue;
                }

                Span<long> rowCrossings = this.crossings.Slice(row * this.crossingStride, count);
                rowCrossings.Sort();

                // Binary searches select this row's range from the sorted column-pass results.
                // Results covered by a normal row interval are discarded below. The remaining
                // results are inserted as one-pixel intervals.
                int rowKey = row << 16;
                int dropoutStart = LowerBound(columnDropouts, rowKey);
                int dropoutEnd = LowerBound(columnDropouts, rowKey + (1 << 16));

                int intervalStart = 0;
                long intervalStartCrossing = long.MinValue;
                int runStart = -1;
                int runEnd = -1;

                for (int c = 0; c <= count; c++)
                {
                    int x;
                    long currentCrossing = long.MinValue;
                    int previousWinding = winding;

                    if (c < count)
                    {
                        long crossing = rowCrossings[c];
                        currentCrossing = crossing;
                        x = (int)(crossing >> CrossingShift);
                        winding = this.intersectionRule == IntersectionRule.NonZero
                            ? winding + (((crossing & CrossingDirectionBit) != 0) ? 1 : -1)
                            : winding ^ 1;
                    }
                    else
                    {
                        // A non-zero winding after the last retained crossing means the fill
                        // continues through the right clip edge. Close that interval at the edge.
                        if (previousWinding == 0)
                        {
                            break;
                        }

                        x = this.width << FixedShift;
                        winding = 0;
                    }

                    if (previousWinding == 0 && winding != 0)
                    {
                        intervalStart = x;
                        intervalStartCrossing = currentCrossing;
                        continue;
                    }

                    if (previousWinding == 0 || winding != 0)
                    {
                        continue;
                    }

                    // Coverage is closed at both ends. CeilingPixel finds the first centre at or
                    // after the start; FloorPixel finds the last centre at or before the end.
                    int first = CeilingPixel(intervalStart);
                    int last = FloorPixel(x);
                    if (last < first)
                    {
                        // This interval contains no pixel centre. Normally, preserve the thin
                        // feature by lighting the pixel that contains its midpoint. Do not light it
                        // when the two boundary profiles identify a terminating contour tip.
                        if (intervalStartCrossing != long.MinValue && currentCrossing != long.MinValue && IsStubInterval(
                                this.profiles,
                                xAxis: false,
                                this.rowProfileTranslate,
                                intervalStartCrossing,
                                currentCrossing,
                                ((destinationTop + row) << FixedShift) + FixedHalf,
                                x - intervalStart))
                        {
                            continue;
                        }

                        first = ((intervalStart >> 1) + (x >> 1)) >> FixedShift;
                        last = first;
                    }

                    first = Math.Max(first, 0);
                    last = Math.Min(last, this.width - 1);
                    if (last < first)
                    {
                        continue;
                    }

                    // Insert pending column results before this row interval. Discard a result
                    // inside the interval because the row pass already covers that pixel.
                    while (dropoutStart < dropoutEnd)
                    {
                        int dropColumn = columnDropouts[dropoutStart] & 0xFFFF;
                        if (dropColumn >= first)
                        {
                            if (dropColumn <= last)
                            {
                                dropoutStart++;
                                continue;
                            }

                            break;
                        }

                        dropoutStart++;
                        AccumulateRun(scanline, dropColumn, dropColumn, ref runStart, ref runEnd, destinationTop + row, destinationLeft, ref rowHandler);
                    }

                    AccumulateRun(scanline, first, last, ref runStart, ref runEnd, destinationTop + row, destinationLeft, ref rowHandler);
                }

                while (dropoutStart < dropoutEnd)
                {
                    int dropColumn = columnDropouts[dropoutStart++] & 0xFFFF;
                    AccumulateRun(scanline, dropColumn, dropColumn, ref runStart, ref runEnd, destinationTop + row, destinationLeft, ref rowHandler);
                }

                if (runStart >= 0)
                {
                    scanline[..(runEnd - runStart)].Fill(1F);
                    rowHandler.Handle(destinationTop + row, destinationLeft + runStart, scanline[..(runEnd - runStart)]);
                }
            }
        }

        /// <summary>
        /// Adds one inclusive pixel interval to the row's run, coalescing with the open run or
        /// flushing it when a gap intervenes.
        /// </summary>
        /// <typeparam name="TRowHandler">The struct coverage handler type; constrained to a value type so calls devirtualize.</typeparam>
        /// <param name="scanline">Reusable scanline scratch buffer used to materialize emitted spans.</param>
        /// <param name="first">The inclusive first pixel of the interval.</param>
        /// <param name="last">The inclusive last pixel of the interval.</param>
        /// <param name="runStart">The open run's inclusive start pixel; negative when no run is open.</param>
        /// <param name="runEnd">The open run's exclusive end pixel.</param>
        /// <param name="destinationY">Absolute destination Y for the emitted row.</param>
        /// <param name="destinationLeft">Absolute destination X corresponding to column zero in this context.</param>
        /// <param name="rowHandler">Coverage callback invoked for each flushed run.</param>
        private static void AccumulateRun<TRowHandler>(
            Span<float> scanline,
            int first,
            int last,
            ref int runStart,
            ref int runEnd,
            int destinationY,
            int destinationLeft,
            ref TRowHandler rowHandler)
            where TRowHandler : struct, IRasterizerCoverageRowHandler
        {
            if (runStart < 0)
            {
                runStart = first;
                runEnd = last + 1;
                return;
            }

            if (first <= runEnd)
            {
                runEnd = Math.Max(runEnd, last + 1);
                return;
            }

            scanline[..(runEnd - runStart)].Fill(1F);
            rowHandler.Handle(destinationY, destinationLeft + runStart, scanline[..(runEnd - runStart)]);
            runStart = first;
            runEnd = last + 1;
        }

        /// <summary>
        /// Finds the first index in a sorted span whose value is not less than the given key.
        /// </summary>
        /// <param name="values">The sorted values.</param>
        /// <param name="key">The search key.</param>
        /// <returns>The lower bound index.</returns>
        private static int LowerBound(ReadOnlySpan<int> values, int key)
        {
            int lo = 0;
            int hi = values.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (values[mid] < key)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        /// <summary>
        /// Tests whether a centre-free interval ends at a terminating contour tip.
        /// </summary>
        /// <remarks>
        /// A centre-free interval is the closed span between two crossings when that span contains
        /// no pixel centre. The normal thin-feature rule lights its midpoint pixel. It must not
        /// extend a contour tip into the next empty pixel. The two boundary profiles identify that
        /// case because they meet in one contour and both end inside the same pixel gap.
        /// </remarks>
        /// <param name="profiles">The geometry-space profile data.</param>
        /// <param name="xAxis">Whether to read the X-axis profile table.</param>
        /// <param name="translate">The absolute translation applied to the profile extents.</param>
        /// <param name="enterCrossing">The packed crossing that opened the interval.</param>
        /// <param name="exitCrossing">The packed crossing that closed the interval.</param>
        /// <param name="centre">The scanline's centre coordinate in 24.8 fixed-point.</param>
        /// <param name="intervalSpan">The interval's length along the scanline in 24.8 fixed-point.</param>
        /// <returns>
        /// <see langword="true"/> when the interval must remain unlit because it is a terminating tip.
        /// </returns>
        private static bool IsStubInterval(
            LinearGeometryProfiles profiles,
            bool xAxis,
            float translate,
            long enterCrossing,
            long exitCrossing,
            int centre,
            int intervalSpan)
        {
            int a = (int)(enterCrossing & ProfileIdMask);
            int b = (int)(exitCrossing & ProfileIdMask);

            // The sentinel means that the segment has no usable profile. Without both profiles,
            // the interval cannot be identified as a terminating tip and must remain visible.
            if (a == ProfileIdMask || b == ProfileIdMask || profiles.IsEmpty)
            {
                return false;
            }

            if (xAxis)
            {
                profiles.GetXProfile(a, out float minAFloat, out float maxAFloat, out int linkA);
                profiles.GetXProfile(b, out float minBFloat, out float maxBFloat, out int linkB);
                return IsStubIntervalCore(
                    minAFloat,
                    maxAFloat,
                    linkA,
                    minBFloat,
                    maxBFloat,
                    linkB,
                    a,
                    b,
                    translate,
                    centre,
                    intervalSpan);
            }

            profiles.GetYProfile(a, out float rowMinA, out float rowMaxA, out int rowLinkA);
            profiles.GetYProfile(b, out float rowMinB, out float rowMaxB, out int rowLinkB);
            return IsStubIntervalCore(
                rowMinA,
                rowMaxA,
                rowLinkA,
                rowMinB,
                rowMaxB,
                rowLinkB,
                a,
                b,
                translate,
                centre,
                intervalSpan);
        }

        /// <summary>
        /// Applies the terminating-tip test to two profile records.
        /// </summary>
        /// <param name="minAFloat">The first profile's minimum extent.</param>
        /// <param name="maxAFloat">The first profile's maximum extent.</param>
        /// <param name="linkA">The first profile's adjacency link.</param>
        /// <param name="minBFloat">The second profile's minimum extent.</param>
        /// <param name="maxBFloat">The second profile's maximum extent.</param>
        /// <param name="linkB">The second profile's adjacency link.</param>
        /// <param name="a">The first profile identifier.</param>
        /// <param name="b">The second profile identifier.</param>
        /// <param name="translate">The absolute translation applied to the profile extents.</param>
        /// <param name="centre">The scanline's centre coordinate in 24.8 fixed-point.</param>
        /// <param name="intervalSpan">The interval's length along the scanline in 24.8 fixed-point.</param>
        /// <returns><see langword="true"/> when the interval must remain unlit.</returns>
        private static bool IsStubIntervalCore(
            float minAFloat,
            float maxAFloat,
            int linkA,
            float minBFloat,
            float maxBFloat,
            int linkB,
            int a,
            int b,
            float translate,
            int centre,
            int intervalSpan)
        {
            bool adjacent = (b == a + 1 && (linkB & 1) != 0)
                || (a == b + 1 && (linkA & 1) != 0)
                || (linkA >> 1) == b + 1
                || (linkB >> 1) == a + 1;

            // Only two profiles joined in the same contour can describe one terminating tip.
            // Separate contours or non-adjacent profiles describe a continuing thin feature.
            if (!adjacent)
            {
                return false;
            }

            // Profiles are stored in geometry coordinates. Translate only the two records needed
            // by this rare test. This avoids creating translated profile arrays for every draw.
            int minA = TranslateProfileExtent(minAFloat, translate);
            int maxA = TranslateProfileExtent(maxAFloat, translate);
            int minB = TranslateProfileExtent(minBFloat, translate);
            int maxB = TranslateProfileExtent(maxBFloat, translate);

            // Both profiles must turn around before the next centre on the same side of this
            // scanline. That places the interval at the end of the contour feature. A tip still
            // remains visible when it reaches at least halfway into that pixel and the interval
            // itself is at least half a pixel long.
            if (maxA < centre + FixedOne && maxB < centre + FixedOne)
            {
                int tip = Math.Max(maxA, maxB);
                return !((tip & (FixedOne - 1)) >= FixedHalf && intervalSpan >= FixedHalf);
            }

            if (minA > centre - FixedOne && minB > centre - FixedOne)
            {
                int tip = Math.Min(minA, minB);
                int fraction = tip & (FixedOne - 1);
                return !(fraction != 0 && fraction <= FixedHalf && intervalSpan >= FixedHalf);
            }

            return false;
        }

        /// <summary>
        /// Converts one translated geometry-space profile extent to signed 24.8 fixed-point.
        /// </summary>
        /// <param name="extent">The geometry-space extent.</param>
        /// <param name="translate">The absolute device translation.</param>
        /// <returns>The translated extent in signed 24.8 fixed-point.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int TranslateProfileExtent(float extent, float translate)
            => FloatToFixed24Dot8(Math.Clamp(extent + translate, -8388608F, 8388607F));

        /// <summary>
        /// Records where one segment crosses the vertical centre line of each affected column.
        /// </summary>
        /// <param name="x0">The segment's start X in 24.8 fixed-point.</param>
        /// <param name="y0">The segment's start Y in 24.8 fixed-point.</param>
        /// <param name="x1">The segment's end X in 24.8 fixed-point.</param>
        /// <param name="y1">The segment's end Y in 24.8 fixed-point.</param>
        /// <param name="xProfileId">The segment's x-profile identifier.</param>
        private void CaptureColumnCrossings(int x0, int y0, int x1, int y1, uint xProfileId)
        {
            if (x0 == x1)
            {
                return;
            }

            int direction = x1 > x0 ? 1 : -1;
            int left = direction > 0 ? x0 : x1;
            int right = direction > 0 ? x1 : x0;

            int firstColumn = Math.Max(0, left >> FixedShift);
            int lastColumn = Math.Min(this.width - 1, (right - 1) >> FixedShift);

            for (int column = firstColumn; column <= lastColumn; column++)
            {
                int sampleX = (column << FixedShift) + FixedHalf;

                // Use a half-open X range so a shared vertex is owned by one edge only. This is
                // the transposed form of the row-crossing rule used below.
                if (sampleX < left || sampleX >= right)
                {
                    continue;
                }

                int count = this.columnCrossingCounts[column];
                if (count >= ColumnCrossingCapacity)
                {
                    continue;
                }

                if (count == 0 && this.touchedColumnCount < this.touchedColumns.Length)
                {
                    this.touchedColumns[this.touchedColumnCount++] = column;
                }

                int y = y0 + (int)(((long)(y1 - y0) * (sampleX - x0)) / (x1 - x0));

                // Column coordinates are band-local and need at most 13 bits in 24.8 form.
                // The complete packed crossing therefore fits in one word, unlike row crossings
                // whose X coordinate spans the full destination width.
                this.columnCrossings[(column * ColumnCrossingCapacity) + count] = ((uint)y << CrossingShift) | (direction > 0 ? (uint)CrossingDirectionBit : 0U) | xProfileId;
                this.columnCrossingCounts[column] = count + 1;
            }
        }

        /// <summary>
        /// Finds closed vertical intervals that contain no row centre.
        /// </summary>
        /// <param name="dropouts">The destination for packed midpoint pixel coordinates.</param>
        /// <param name="destinationLeft">Absolute destination X corresponding to column zero, used to compare the column centre with absolute profile ranges.</param>
        /// <returns>
        /// The number of midpoint pixels written. Each result is packed as
        /// <c>(row &lt;&lt; 16) | column</c>.
        /// </returns>
        private readonly int CollectColumnDropouts(Span<int> dropouts, int destinationLeft)
        {
            int count = 0;
            for (int i = 0; i < this.touchedColumnCount; i++)
            {
                int column = this.touchedColumns[i];
                int crossingCount = this.columnCrossingCounts[column];
                if (crossingCount < 2)
                {
                    continue;
                }

                Span<uint> crossings = this.columnCrossings.Slice(column * ColumnCrossingCapacity, crossingCount);
                crossings.Sort();

                int winding = 0;
                int intervalStart = 0;
                long intervalStartCrossing = long.MinValue;
                for (int c = 0; c < crossingCount; c++)
                {
                    long crossing = crossings[c];
                    int y = (int)(crossing >> CrossingShift);
                    int previousWinding = winding;
                    winding = this.intersectionRule == IntersectionRule.NonZero
                        ? winding + (((crossing & CrossingDirectionBit) != 0) ? 1 : -1)
                        : winding ^ 1;

                    if (previousWinding == 0 && winding != 0)
                    {
                        intervalStart = y;
                        intervalStartCrossing = crossing;
                        continue;
                    }

                    if (previousWinding == 0 || winding != 0)
                    {
                        continue;
                    }

                    if (FloorPixel(y) >= CeilingPixel(intervalStart))
                    {
                        // The primary row pass sees this interval and needs no secondary result.
                        continue;
                    }

                    if (IsStubInterval(
                            this.profiles,
                            xAxis: true,
                            this.columnProfileTranslate,
                            intervalStartCrossing,
                            crossing,
                            ((destinationLeft + column) << FixedShift) + FixedHalf,
                            y - intervalStart))
                    {
                        // The interval ends at a contour tip and must remain unlit.
                        continue;
                    }

                    int row = ((intervalStart >> 1) + (y >> 1)) >> FixedShift;
                    if (row < 0 || row >= this.height || count == dropouts.Length)
                    {
                        continue;
                    }

                    dropouts[count++] = (row << 16) | column;
                }
            }

            return count;
        }

        /// <summary>
        /// Converts a row's signed 24.8 start cover to an integer winding count.
        /// </summary>
        /// <remarks>
        /// One complete cover unit represents one crossing to the left of the band. A partial unit
        /// represents an edge that crosses only part of the row. Rounding to the nearest signed
        /// unit determines whether that edge has crossed the row centre.
        /// </remarks>
        /// <param name="startCover">The row's accumulated carry cover in 24.8 units.</param>
        /// <returns>The signed crossing count.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NearestCrossingCount(int startCover)
            => startCover >= 0
                ? (startCover + FixedHalf) >> FixedShift
                : -((-startCover + FixedHalf) >> FixedShift);

        /// <summary>
        /// Returns the first pixel whose centre is at or after the given 24.8 coordinate.
        /// </summary>
        /// <param name="value">The 24.8 fixed-point coordinate.</param>
        /// <returns>The pixel index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CeilingPixel(int value)
        {
            int shifted = value - FixedHalf;
            return shifted >= 0
                ? (shifted + FixedOne - 1) >> FixedShift
                : -((-shifted) >> FixedShift);
        }

        /// <summary>
        /// Returns the last pixel whose centre is at or before the given 24.8 coordinate.
        /// </summary>
        /// <param name="value">The 24.8 fixed-point coordinate.</param>
        /// <returns>The pixel index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorPixel(int value)
        {
            int shifted = value - FixedHalf;
            return shifted >= 0
                ? shifted >> FixedShift
                : -(((-shifted) + FixedOne - 1) >> FixedShift);
        }

        /// <summary>
        /// Records where one segment crosses the horizontal centre line of each affected row.
        /// </summary>
        /// <remarks>
        /// Aliased coverage depends on whether the shape contains the pixel centre. Area coverage
        /// alone cannot answer this because equal covered areas can lie on opposite sides of the
        /// centre. The sorted crossings define the exact filled intervals along the centre line.
        /// </remarks>
        /// <param name="x0">The segment's start X in 24.8 fixed-point.</param>
        /// <param name="y0">The segment's start Y in 24.8 fixed-point.</param>
        /// <param name="x1">The segment's end X in 24.8 fixed-point.</param>
        /// <param name="y1">The segment's end Y in 24.8 fixed-point.</param>
        /// <param name="tag">The segment's profile tag: the x-profile identifier in the low sixteen bits and the y-profile identifier in the high sixteen bits.</param>
        private void CaptureCrossings(int x0, int y0, int x1, int y1, uint tag)
        {
            this.CaptureColumnCrossings(x0, y0, x1, y1, tag & ProfileIdMask);

            if (y0 == y1)
            {
                return;
            }

            int direction = y1 > y0 ? 1 : -1;
            int top = direction > 0 ? y0 : y1;
            int bottom = direction > 0 ? y1 : y0;

            int firstRow = Math.Max(0, top >> FixedShift);
            int lastRow = Math.Min(this.height - 1, (bottom - 1) >> FixedShift);

            for (int row = firstRow; row <= lastRow; row++)
            {
                int sampleY = (row << FixedShift) + FixedHalf;

                // Use a half-open Y range so two edges meeting at a vertex do not both contribute
                // that vertex to the winding count.
                if (sampleY < top || sampleY >= bottom)
                {
                    continue;
                }

                int count = this.crossingCounts[row];
                int x = x0 + (int)(((long)(x1 - x0) * (sampleY - y0)) / (y1 - y0));

                // Position, direction, and profile pack into one value ordered by position, so a
                // row sorts with a single primitive sort. The lower bits only break ties between
                // equal positions, where crossing order cannot change the winding result.
                this.crossings[(row * this.crossingStride) + count] = ((long)x << CrossingShift) | (direction > 0 ? CrossingDirectionBit : 0L) | (tag >> 16);
                this.crossingCounts[row] = count + 1;
                this.MarkRowTouched(row);
            }
        }

        /// <summary>
        /// Converts accumulated signed area to normalized coverage under the selected fill rule.
        /// </summary>
        /// <param name="area">
        /// The accumulated doubled signed area in fixed-point cell units; a fully covered pixel
        /// corresponds to 2 * 256 * 256, which <see cref="AreaToCoverageShift"/> maps to <see cref="CoverageStepCount"/>.
        /// </param>
        /// <returns>The normalized coverage value in [0, 1].</returns>
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

            if (this.coverageBoost != 0F)
            {
                // Perceptual contrast boost for text: an S-curve that darkens coverage above
                // one half and lightens it below, so stems solidify while nearly-open counters
                // stay bright. 0, 0.5, and 1 are fixed points; at full strength the remap is
                // exactly smoothstep (3a^2 - 2a^3), so the boost blends identity to smoothstep.
                coverage += this.coverageBoost * coverage * (1F - coverage) * ((2F * coverage) - 1F);
            }

            return coverage;
        }

        /// <summary>
        /// Buffers one non-zero span into the current contiguous row run.
        /// </summary>
        /// <param name="scanline">The scratch scanline that stores per-pixel coverage for the buffered run.</param>
        /// <param name="start">The inclusive start column of the span.</param>
        /// <param name="end">The exclusive end column of the span.</param>
        /// <param name="coverage">The constant coverage value for the span.</param>
        /// <param name="runStart">The inclusive start column of the buffered run; negative when no run is open.</param>
        /// <param name="runEnd">The exclusive end column of the buffered run.</param>
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
        /// <typeparam name="TRowHandler">The struct coverage handler type; constrained to a value type so calls devirtualize.</typeparam>
        /// <param name="rowHandler">The coverage callback receiving the run.</param>
        /// <param name="destinationY">Absolute destination Y for the emitted row.</param>
        /// <param name="destinationLeft">Absolute destination X corresponding to column zero in this context.</param>
        /// <param name="scanline">The scratch scanline containing the buffered per-pixel coverage.</param>
        /// <param name="runStart">The inclusive start column of the buffered run; reset to -1 after flushing.</param>
        /// <param name="runEnd">The exclusive end column of the buffered run; reset to -1 after flushing.</param>
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
        /// <param name="row">The band-local row index.</param>
        /// <param name="column">The band-local column index.</param>
        /// <param name="rowHadBits">Receives whether the row already had any bit-vector backed cell data.</param>
        /// <returns><see langword="true"/> when the bit was newly set; otherwise <see langword="false"/>.</returns>
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
        /// <param name="row">The band-local row index; out-of-range rows are ignored.</param>
        /// <param name="column">The band-local column index; negative columns fold into the row carry.</param>
        /// <param name="delta">The signed winding delta contributed by the edge crossing this cell.</param>
        /// <param name="area">The signed doubled area contributed inside this cell.</param>
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
        /// <param name="row">The band-local row index; out-of-range rows are ignored.</param>
        /// <param name="delta">The signed winding delta carried into the row from left of the band.</param>
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
        /// <param name="row">The band-local row index to mark.</param>
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
        /// <param name="px">The band-local column index of the cell.</param>
        /// <param name="py">The band-local row index of the cell.</param>
        /// <param name="x">The cell-local X coordinate of the vertical edge (0 to <see cref="FixedOne"/>).</param>
        /// <param name="y0">The cell-local starting Y coordinate.</param>
        /// <param name="y1">The cell-local ending Y coordinate.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CellVertical(int px, int py, int x, int y0, int y1)
        {
            // Signed winding is y0 - y1; area is twice the trapezoid between the edge and the
            // cell's right boundary, keeping the math integral by deferring the divide by two
            // until AreaToCoverage shifts it out.
            int delta = y0 - y1;
            int area = delta * ((FixedOne * 2) - x - x);
            this.AddCell(py, px, delta, area);
        }

        /// <summary>
        /// Emits one general cell contribution.
        /// </summary>
        /// <param name="row">The band-local row index of the cell.</param>
        /// <param name="px">The band-local column index of the cell.</param>
        /// <param name="x0">The cell-local starting X coordinate (0 to <see cref="FixedOne"/>).</param>
        /// <param name="y0">The cell-local starting Y coordinate.</param>
        /// <param name="x1">The cell-local ending X coordinate (0 to <see cref="FixedOne"/>).</param>
        /// <param name="y1">The cell-local ending Y coordinate.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Cell(int row, int px, int x0, int y0, int x1, int y1)
        {
            // Same doubled trapezoid formulation as CellVertical, using the average of the two
            // X endpoints as the edge position within the cell.
            int delta = y0 - y1;
            int area = delta * ((FixedOne * 2) - x0 - x1);
            this.AddCell(row, px, delta, area);
        }

        /// <summary>
        /// Rasterizes a downward vertical edge segment.
        /// </summary>
        /// <param name="columnIndex">The band-local column index owning the edge.</param>
        /// <param name="y0">The starting Y coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="y1">The ending Y coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="x">The X coordinate of the edge in 24.8 fixed-point band-local space.</param>
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
        /// <param name="columnIndex">The band-local column index owning the edge.</param>
        /// <param name="y0">The starting Y coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="y1">The ending Y coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="x">The X coordinate of the edge in 24.8 fixed-point band-local space.</param>
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
        /// <param name="rowIndex">The band-local row index containing the segment.</param>
        /// <param name="p0x">The starting X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p0y">The starting Y coordinate in 24.8 fixed-point row-local space.</param>
        /// <param name="p1x">The ending X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p1y">The ending Y coordinate in 24.8 fixed-point row-local space.</param>
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

            // pp/mod/lift/rem implement an integer DDA that advances y at column boundaries
            // without accumulating rounding error; the remainder carries the exact fraction.
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
        /// <param name="rowIndex">The band-local row index containing the segment.</param>
        /// <param name="p0x">The starting X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p0y">The starting Y coordinate in 24.8 fixed-point row-local space.</param>
        /// <param name="p1x">The ending X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p1y">The ending Y coordinate in 24.8 fixed-point row-local space.</param>
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
        /// <param name="rowIndex">The band-local row index containing the segment.</param>
        /// <param name="p0x">The starting X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p0y">The starting Y coordinate in 24.8 fixed-point row-local space.</param>
        /// <param name="p1x">The ending X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p1y">The ending Y coordinate in 24.8 fixed-point row-local space.</param>
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
        /// <param name="rowIndex">The band-local row index containing the segment.</param>
        /// <param name="p0x">The starting X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p0y">The starting Y coordinate in 24.8 fixed-point row-local space.</param>
        /// <param name="p1x">The ending X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p1y">The ending Y coordinate in 24.8 fixed-point row-local space.</param>
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
        /// <param name="rowIndex">The band-local row index containing the segment.</param>
        /// <param name="p0x">The starting X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p0y">The starting Y coordinate in 24.8 fixed-point row-local space.</param>
        /// <param name="p1x">The ending X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p1y">The ending Y coordinate in 24.8 fixed-point row-local space.</param>
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
        /// <param name="rowIndex">The band-local row index containing the segment.</param>
        /// <param name="p0x">The starting X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p0y">The starting Y coordinate in 24.8 fixed-point row-local space.</param>
        /// <param name="p1x">The ending X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p1y">The ending Y coordinate in 24.8 fixed-point row-local space.</param>
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
        /// <param name="rowIndex">The band-local row index containing the segment.</param>
        /// <param name="p0x">The starting X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p0y">The starting Y coordinate in 24.8 fixed-point row-local space.</param>
        /// <param name="p1x">The ending X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p1y">The ending Y coordinate in 24.8 fixed-point row-local space.</param>
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
        /// <param name="rowIndex">The band-local row index containing the segment.</param>
        /// <param name="p0x">The starting X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p0y">The starting Y coordinate in 24.8 fixed-point row-local space.</param>
        /// <param name="p1x">The ending X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="p1y">The ending Y coordinate in 24.8 fixed-point row-local space.</param>
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
        /// <param name="rowIndex0">The band-local index of the first touched row.</param>
        /// <param name="rowIndex1">The band-local index of the last touched row.</param>
        /// <param name="x0">The starting X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="y0">The starting Y coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="x1">The ending X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="y1">The ending Y coordinate in 24.8 fixed-point band-local space.</param>
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
        /// <param name="rowIndex0">The band-local index of the first touched row.</param>
        /// <param name="rowIndex1">The band-local index of the last touched row.</param>
        /// <param name="x0">The starting X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="y0">The starting Y coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="x1">The ending X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="y1">The ending Y coordinate in 24.8 fixed-point band-local space.</param>
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
        /// <param name="rowIndex0">The band-local index of the first touched row.</param>
        /// <param name="rowIndex1">The band-local index of the last touched row.</param>
        /// <param name="x0">The starting X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="y0">The starting Y coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="x1">The ending X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="y1">The ending Y coordinate in 24.8 fixed-point band-local space.</param>
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
        /// <param name="rowIndex0">The band-local index of the first touched row.</param>
        /// <param name="rowIndex1">The band-local index of the last touched row.</param>
        /// <param name="x0">The starting X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="y0">The starting Y coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="x1">The ending X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="y1">The ending Y coordinate in 24.8 fixed-point band-local space.</param>
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
        /// <param name="x0">The starting X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="y0">The starting Y coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="x1">The ending X coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="y1">The ending Y coordinate in 24.8 fixed-point band-local space.</param>
        /// <param name="tag">The segment's profile tag.</param>
        private void RasterizeLine(int x0, int y0, int x1, int y1, uint tag)
        {
            if (this.rasterizationMode == RasterizationMode.Aliased)
            {
                this.CaptureCrossings(x0, y0, x1, y1, tag);
            }

            // Horizontal segments are retained only so the aliased column pass can find features
            // between row centres. They add no area, and the row walker cannot divide by zero Y.
            if (y0 == y1)
            {
                return;
            }

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
        private IMemoryOwner<long>? crossingsOwner;
        private IMemoryOwner<uint>? aliasedScratchOwner;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkerScratch"/> class taking ownership of the supplied buffers.
        /// </summary>
        /// <param name="allocator">The allocator used for lazily created stroke coverage scratch.</param>
        /// <param name="wordsPerRow">The bit-vector row width, in machine words, this scratch supports.</param>
        /// <param name="coverStride">The cover/area stride, in cells, this scratch supports.</param>
        /// <param name="width">The maximum band width, in pixels, this scratch supports.</param>
        /// <param name="tileCapacity">The maximum band height, in rows, this scratch supports.</param>
        /// <param name="bitVectorsOwner">The owned bit-vector storage.</param>
        /// <param name="coverAreaOwner">The owned cover/area cell storage.</param>
        /// <param name="startCoverOwner">The owned per-row start-cover storage.</param>
        /// <param name="rowMinTouchedColumnOwner">The owned per-row minimum touched column storage.</param>
        /// <param name="rowMaxTouchedColumnOwner">The owned per-row maximum touched column storage.</param>
        /// <param name="rowHasBitsOwner">The owned per-row bit-data flag storage.</param>
        /// <param name="rowTouchedOwner">The owned per-row touched flag storage.</param>
        /// <param name="touchedRowsOwner">The owned touched-row index list storage.</param>
        /// <param name="scanlineOwner">The owned scanline scratch storage.</param>
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
        /// Gets reusable per-row crossing count scratch, created on first aliased use.
        /// </summary>
        public Span<int> CrossingCounts
            => MemoryMarshal.Cast<uint, int>(this.GetAliasedScratch()[..this.tileCapacity]);

        /// <summary>
        /// Gets reusable per-column centre line crossing scratch, created on first aliased use.
        /// </summary>
        public Span<uint> ColumnCrossings
            => this.GetAliasedScratch().Slice(this.tileCapacity, checked(this.width * ColumnCrossingCapacity));

        /// <summary>
        /// Gets reusable per-column crossing count scratch, created on first aliased use.
        /// </summary>
        public Span<int> ColumnCrossingCounts
        {
            get
            {
                int offset = checked(this.tileCapacity + (this.width * ColumnCrossingCapacity));
                return MemoryMarshal.Cast<uint, int>(this.GetAliasedScratch().Slice(offset, this.width));
            }
        }

        /// <summary>
        /// Gets the reusable touched column index list, created on first aliased use.
        /// </summary>
        public Span<int> TouchedColumns
        {
            get
            {
                int offset = checked(this.tileCapacity + (this.width * (ColumnCrossingCapacity + 1)));
                return MemoryMarshal.Cast<uint, int>(this.GetAliasedScratch().Slice(offset, this.width));
            }
        }

        /// <summary>
        /// Gets the worker's fixed-size aliased scratch rental, creating it on first use.
        /// </summary>
        /// <returns>The packed row-count, column-crossing, column-count, and touched-column storage.</returns>
        private Span<uint> GetAliasedScratch()
        {
            if (this.aliasedScratchOwner is null)
            {
                // This allocation belongs to WorkerScratch, not to a geometry. The worker reuses it
                // for every aliased geometry until the worker is disposed.
                int columnCrossingLength = checked(this.width * ColumnCrossingCapacity);
                int length = checked(this.tileCapacity + columnCrossingLength + this.width + this.width);
                this.aliasedScratchOwner = this.allocator.Allocate<uint>(length);

                // Only counts require an initial zero. Crossing values and touched indices are
                // overwritten before use, so clearing their much larger slices would be wasted work.
                Span<uint> scratch = this.aliasedScratchOwner.Memory.Span;
                scratch[..this.tileCapacity].Clear();
                scratch.Slice(this.tileCapacity + columnCrossingLength, this.width).Clear();
            }

            return this.aliasedScratchOwner.Memory.Span;
        }

        /// <summary>
        /// Gets the worker's growable row-crossing rental. Each row reserves one slot per retained
        /// line because one line can cross a row centre no more than once.
        /// </summary>
        /// <param name="crossingStride">The required crossing slots for each row.</param>
        /// <returns>The rented crossing storage.</returns>
        private Span<long> GetCrossings(int crossingStride)
        {
            int requiredLength = checked(this.tileCapacity * crossingStride);
            if (this.crossingsOwner is null || this.crossingsOwner.Memory.Length < requiredLength)
            {
                // Keep the largest rental used by this worker. Later geometries at or below that
                // complexity reuse it without another allocation.
                IMemoryOwner<long> replacement = this.allocator.Allocate<long>(requiredLength);

                this.crossingsOwner?.Dispose();
                this.crossingsOwner = replacement;
            }

            return this.crossingsOwner.Memory.Span;
        }

        /// <summary>
        /// Returns <see langword="true"/> when this scratch has compatible dimensions and sufficient
        /// capacity for the requested parameters, making it safe to reuse without reallocation.
        /// </summary>
        /// <param name="requiredWordsPerRow">The bit-vector row width, in machine words, the caller needs.</param>
        /// <param name="requiredCoverStride">The cover/area stride, in cells, the caller needs.</param>
        /// <param name="requiredWidth">The band width, in pixels, the caller needs.</param>
        /// <param name="minCapacity">The minimum band height, in rows, the caller needs.</param>
        /// <returns><see langword="true"/> when reuse is safe; otherwise <see langword="false"/>.</returns>
        private bool CanReuse(int requiredWordsPerRow, int requiredCoverStride, int requiredWidth, int minCapacity)
            => this.wordsPerRow >= requiredWordsPerRow
            && this.coverStride >= requiredCoverStride
            && this.width >= requiredWidth
            && this.tileCapacity >= minCapacity;

        /// <summary>
        /// Returns <see langword="true"/> when this scratch can be reused for the default band configuration
        /// at the requested width.
        /// </summary>
        /// <param name="requiredWidth">The band width, in pixels, the caller needs.</param>
        /// <returns><see langword="true"/> when reuse is safe; otherwise <see langword="false"/>.</returns>
        public bool CanReuse(int requiredWidth)
            => this.CanReuse(BitVectorsForMaxBitCount(requiredWidth), checked(requiredWidth << 1), requiredWidth, PreferredRowHeight);

        /// <summary>
        /// Allocates worker-local scratch sized for the configured tile/band capacity.
        /// </summary>
        /// <param name="allocator">The memory allocator that owns the scratch buffers.</param>
        /// <param name="wordsPerRow">The bit-vector row width in machine words.</param>
        /// <param name="coverStride">The cover/area stride in cells (two cells per pixel).</param>
        /// <param name="width">The maximum band width in pixels.</param>
        /// <param name="tileCapacity">The maximum band height in rows.</param>
        /// <returns>A new <see cref="WorkerScratch"/> instance.</returns>
        public static WorkerScratch Create(MemoryAllocator allocator, int wordsPerRow, int coverStride, int width, int tileCapacity)
        {
            int bitVectorCapacity = checked(wordsPerRow * tileCapacity);
            int coverAreaCapacity = checked(coverStride * tileCapacity);
            IMemoryOwner<nuint>? bitVectorsOwner = null;
            IMemoryOwner<int>? coverAreaOwner = null;
            IMemoryOwner<int>? startCoverOwner = null;
            IMemoryOwner<int>? rowMinTouchedColumnOwner = null;
            IMemoryOwner<int>? rowMaxTouchedColumnOwner = null;
            IMemoryOwner<byte>? rowHasBitsOwner = null;
            IMemoryOwner<byte>? rowTouchedOwner = null;
            IMemoryOwner<int>? touchedRowsOwner = null;

            try
            {
                bitVectorsOwner = allocator.Allocate<nuint>(bitVectorCapacity, AllocationOptions.Clean);
                coverAreaOwner = allocator.Allocate<int>(coverAreaCapacity);
                startCoverOwner = allocator.Allocate<int>(tileCapacity, AllocationOptions.Clean);
                rowMinTouchedColumnOwner = allocator.Allocate<int>(tileCapacity);
                rowMaxTouchedColumnOwner = allocator.Allocate<int>(tileCapacity);
                rowHasBitsOwner = allocator.Allocate<byte>(tileCapacity, AllocationOptions.Clean);
                rowTouchedOwner = allocator.Allocate<byte>(tileCapacity, AllocationOptions.Clean);
                touchedRowsOwner = allocator.Allocate<int>(tileCapacity);
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
            catch
            {
                // A later allocation throwing strands the earlier rentals: the scratch never
                // materialized, so nothing else can return them.
                bitVectorsOwner?.Dispose();
                coverAreaOwner?.Dispose();
                startCoverOwner?.Dispose();
                rowMinTouchedColumnOwner?.Dispose();
                rowMaxTouchedColumnOwner?.Dispose();
                rowHasBitsOwner?.Dispose();
                rowTouchedOwner?.Dispose();
                touchedRowsOwner?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates a context view over a compatible prefix of this scratch for the requested geometry width.
        /// </summary>
        /// <param name="intersectionRule">The fill rule used when converting accumulated winding/coverage into final alpha.</param>
        /// <param name="rasterizationMode">The rasterization mode that selects continuous or centre-sampled coverage.</param>
        /// <param name="lineCount">The retained line count, which is the exact maximum crossing count for one row.</param>
        /// <returns>A <see cref="Context"/> backed by this scratch; the caller must not use two contexts over the same scratch concurrently.</returns>
        public Context CreateContext(
            IntersectionRule intersectionRule,
            RasterizationMode rasterizationMode,
            int lineCount)
            => new(
                this.bitVectorsOwner.Memory.Span,
                this.coverAreaOwner.Memory.Span,
                this.startCoverOwner.Memory.Span,
                this.rowMinTouchedColumnOwner.Memory.Span,
                this.rowMaxTouchedColumnOwner.Memory.Span,
                this.rowHasBitsOwner.Memory.Span,
                this.rowTouchedOwner.Memory.Span,
                this.touchedRowsOwner.Memory.Span,
                rasterizationMode == RasterizationMode.Aliased && lineCount > 0 ? this.GetCrossings(lineCount) : default,
                rasterizationMode == RasterizationMode.Aliased ? this.CrossingCounts : default,
                rasterizationMode == RasterizationMode.Aliased ? this.ColumnCrossings : default,
                rasterizationMode == RasterizationMode.Aliased ? this.ColumnCrossingCounts : default,
                rasterizationMode == RasterizationMode.Aliased ? this.TouchedColumns : default,
                rasterizationMode == RasterizationMode.Aliased ? lineCount : 0,
                intersectionRule,
                rasterizationMode);

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
            this.crossingsOwner?.Dispose();
            this.aliasedScratchOwner?.Dispose();
        }
    }
}
