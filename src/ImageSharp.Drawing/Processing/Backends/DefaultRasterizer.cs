// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
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
internal static class DefaultRasterizer
{
    // Upper bound for temporary scanner buffers (bit vectors + cover/area + start-cover rows).
    // Keeping this bounded prevents pathological full-image allocations on very large interests.
    private const long BandMemoryBudgetBytes = 64L * 1024L * 1024L;

    // Tile height used by the parallel row-tiling pipeline.
    private const int DefaultTileHeight = 16;

    // Cap worker fan-out for coverage emission + composition callbacks.
    // Higher counts increased scheduling overhead for medium geometry workloads.
    private const int MaxParallelWorkerCount = 12;

    private const int FixedShift = 8;
    private const int FixedOne = 1 << FixedShift;
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
    /// Converts one row band of a materialized path into a compact rasterizable payload.
    /// </summary>
    /// <remarks>
    /// The builder emits two complementary data sets:
    /// <list type="bullet">
    /// <item><description><see cref="RasterLineData"/> for visible line segments inside the band.</description></item>
    /// <item><description>Start-cover seeds for segments that begin left of the visible X range.</description></item>
    /// </list>
    /// That split keeps the execution hot path small: the scanner seeds left-of-band winding once,
    /// then only walks visible lines during the actual coverage pass.
    /// </remarks>
    internal static bool TryBuildRasterizableBand(
        MaterializedPath path,
        ReadOnlySpan<int> bandSegmentIndices,
        int translateX,
        int translateY,
        in RasterizerOptions options,
        int bandIndex,
        Span<RasterLineData> lineDestination,
        Span<int> startCoverDestination,
        out RasterizableBandInfo rasterizableBandInfo)
    {
        Rectangle interest = options.Interest;
        int width = interest.Width;
        int height = interest.Height;

        if (width <= 0 || height <= 0 || bandSegmentIndices.Length == 0 || bandIndex < 0)
        {
            rasterizableBandInfo = default;
            return false;
        }

        int tileHeight = DefaultTileHeight;
        int firstBandIndex = FloorDiv(interest.Top, tileHeight);
        int lastBandIndex = FloorDiv(interest.Bottom - 1, tileHeight);
        int tileCount = (lastBandIndex - firstBandIndex) + 1;

        if ((uint)bandIndex >= (uint)tileCount)
        {
            rasterizableBandInfo = default;
            return false;
        }

        int bandTopStart = (firstBandIndex * tileHeight) - interest.Top;
        int bandTop = bandTopStart + (bandIndex * tileHeight);
        int bandHeight = tileHeight;

        if (startCoverDestination.Length < bandHeight || lineDestination.Length < bandSegmentIndices.Length)
        {
            ThrowBandBufferTooSmall();
        }

        int wordsPerRow = BitVectorsForMaxBitCount(width);
        long coverStrideLong = (long)width * 2;

        if (coverStrideLong > int.MaxValue)
        {
            ThrowInterestBoundsTooLarge();
        }

        int coverStride = (int)coverStrideLong;
        bool samplePixelCenter = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter;
        float samplingOffsetX = samplePixelCenter ? 0.5F : 0F;
        float samplingOffsetY = samplePixelCenter ? 0.5F : 0F;
        float clipTop = MathF.Max(0F, bandTop);
        float clipBottom = MathF.Min(height, bandTop + bandHeight);

        if (clipBottom <= clipTop)
        {
            rasterizableBandInfo = default;
            return false;
        }

        Span<int> startCovers = startCoverDestination[..bandHeight];
        startCovers.Clear();
        Span<int> rowMinTouchedColumn = stackalloc int[tileHeight];
        Span<int> rowMaxTouchedColumn = stackalloc int[tileHeight];
        Span<byte> rowHasBits = stackalloc byte[tileHeight];
        Span<byte> rowTouched = stackalloc byte[tileHeight];
        Span<int> touchedRows = stackalloc int[tileHeight];
        Context startCoverContext = new(
            [],
            [],
            startCovers,
            rowMinTouchedColumn,
            rowMaxTouchedColumn,
            rowHasBits,
            rowTouched,
            touchedRows,
            width: 0,
            height: bandHeight,
            wordsPerRow: 0,
            coverStride: 0,
            IntersectionRule.NonZero,
            RasterizationMode.Antialiased,
            antialiasThreshold: 0F);

        int maxVisibleX = width * FixedOne;
        int lineCount = 0;
        int subPathHint = 0;

        for (int i = 0; i < bandSegmentIndices.Length; i++)
        {
            path.GetSegment(bandSegmentIndices[i], out PointF p0, out PointF p1, ref subPathHint);

            float x0 = ((p0.X + translateX) - interest.Left) + samplingOffsetX;
            float y0 = ((p0.Y + translateY) - interest.Top) + samplingOffsetY;
            float x1 = ((p1.X + translateX) - interest.Left) + samplingOffsetX;
            float y1 = ((p1.Y + translateY) - interest.Top) + samplingOffsetY;
            if (!float.IsFinite(x0) || !float.IsFinite(y0) || !float.IsFinite(x1) || !float.IsFinite(y1))
            {
                continue;
            }

            if (!ClipToVerticalBounds(ref x0, ref y0, ref x1, ref y1, clipTop, clipBottom))
            {
                continue;
            }

            int fx0 = FloatToFixed24Dot8(x0);
            int fy0 = FloatToFixed24Dot8(y0 - bandTop);
            int fx1 = FloatToFixed24Dot8(x1);
            int fy1 = FloatToFixed24Dot8(y1 - bandTop);
            if (fy0 == fy1)
            {
                continue;
            }

            int minX = Math.Min(fx0, fx1);
            int maxX = Math.Max(fx0, fx1);

            if (minX < 0)
            {
                startCoverContext.RasterizeLineSegment(fx0, fy0, fx1, fy1);
            }

            if (maxX < 0 || minX > maxVisibleX)
            {
                continue;
            }

            int clippedX0 = fx0;
            int clippedY0 = fy0;
            int clippedX1 = fx1;
            int clippedY1 = fy1;

            if (!ClipToHorizontalBoundsFixed(ref clippedX0, ref clippedY0, ref clippedX1, ref clippedY1, 0, maxVisibleX))
            {
                continue;
            }

            if (clippedY0 == clippedY1)
            {
                continue;
            }

            lineDestination[lineCount++] = new RasterLineData(clippedX0, clippedY0, clippedX1, clippedY1);
        }

        bool hasStartCovers = false;

        for (int i = 0; i < bandHeight; i++)
        {
            if (startCovers[i] != 0)
            {
                hasStartCovers = true;
                break;
            }
        }

        if (lineCount <= 0 && !hasStartCovers)
        {
            rasterizableBandInfo = default;
            return false;
        }

        RasterizationMode rasterizationMode = options.RasterizationMode == RasterizationMode.Antialiased
            ? RasterizationMode.Antialiased
            : RasterizationMode.Aliased;

        rasterizableBandInfo = new RasterizableBandInfo(
            lineCount,
            bandHeight,
            width,
            wordsPerRow,
            coverStride,
            interest.Top + bandTop,
            options.IntersectionRule,
            rasterizationMode,
            options.AntialiasThreshold,
            hasStartCovers);

        return true;
    }

    /// <summary>
    /// Executes one rasterizable band against a reusable scanner context.
    /// </summary>
    internal static void ExecuteRasterizableBand(
        ref Context context,
        in RasterizableBand rasterizableBand,
        Span<float> scanline,
        RasterizerCoverageRowHandler rowHandler)
    {
        context.Reconfigure(
            rasterizableBand.Width,
            rasterizableBand.WordsPerRow,
            rasterizableBand.CoverStride,
            rasterizableBand.BandHeight,
            rasterizableBand.IntersectionRule,
            rasterizableBand.RasterizationMode,
            rasterizableBand.AntialiasThreshold);

        context.SeedStartCovers(rasterizableBand.StartCovers);
        context.RasterizePreparedLines(rasterizableBand.Lines);
        context.EmitCoverageRows(rasterizableBand.DestinationTop, scanline, rowHandler);
        context.ResetTouchedRows();
    }

    /// <summary>
    /// Converts bit count to the number of machine words needed to hold the bitset row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BitVectorsForMaxBitCount(int maxBitCount) => (maxBitCount + WordBitCount - 1) / WordBitCount;

    /// <summary>
    /// Integer floor division for potentially negative values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
        {
            quotient--;
        }

        return quotient;
    }

    /// <summary>
    /// Converts a float coordinate to signed 24.8 fixed-point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FloatToFixed24Dot8(float value) => (int)MathF.Round(value * FixedOne);

    /// <summary>
    /// Clips a fixed-point segment against horizontal bounds.
    /// </summary>
    private static bool ClipToHorizontalBoundsFixed(ref int x0, ref int y0, ref int x1, ref int y1, int minX, int maxX)
    {
        double t0 = 0D;
        double t1 = 1D;
        int originX0 = x0;
        int originY0 = y0;
        long dx = (long)x1 - originX0;
        long dy = (long)y1 - originY0;

        if (!ClipTestFixed(-(double)dx, originX0 - (double)minX, ref t0, ref t1))
        {
            return false;
        }

        if (!ClipTestFixed(dx, maxX - (double)originX0, ref t0, ref t1))
        {
            return false;
        }

        if (t1 < 1D)
        {
            x1 = originX0 + (int)Math.Round(dx * t1);
            y1 = originY0 + (int)Math.Round(dy * t1);
        }

        if (t0 > 0D)
        {
            x0 = originX0 + (int)Math.Round(dx * t0);
            y0 = originY0 + (int)Math.Round(dy * t0);
        }

        return y0 != y1;
    }

    /// <summary>
    /// Clips a segment against vertical bounds using Liang-Barsky style parametric tests.
    /// </summary>
    /// <param name="x0">Segment start X (updated in place).</param>
    /// <param name="y0">Segment start Y (updated in place).</param>
    /// <param name="x1">Segment end X (updated in place).</param>
    /// <param name="y1">Segment end Y (updated in place).</param>
    /// <param name="minY">Minimum Y bound.</param>
    /// <param name="maxY">Maximum Y bound.</param>
    /// <returns><see langword="true"/> when a non-horizontal clipped segment remains.</returns>
    private static bool ClipToVerticalBounds(ref float x0, ref float y0, ref float x1, ref float y1, float minY, float maxY)
    {
        float t0 = 0F;
        float t1 = 1F;
        float dx = x1 - x0;
        float dy = y1 - y0;

        if (!ClipTest(-dy, y0 - minY, ref t0, ref t1))
        {
            return false;
        }

        if (!ClipTest(dy, maxY - y0, ref t0, ref t1))
        {
            return false;
        }

        if (t1 < 1F)
        {
            x1 = x0 + (dx * t1);
            y1 = y0 + (dy * t1);
        }

        if (t0 > 0F)
        {
            x0 += dx * t0;
            y0 += dy * t0;
        }

        return y0 != y1;
    }

    /// <summary>
    /// One Liang-Barsky clip test step.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ClipTest(float p, float q, ref float t0, ref float t1)
    {
        if (p == 0F)
        {
            return q >= 0F;
        }

        float r = q / p;
        if (p < 0F)
        {
            if (r > t1)
            {
                return false;
            }

            if (r > t0)
            {
                t0 = r;
            }
        }
        else
        {
            if (r < t0)
            {
                return false;
            }

            if (r < t1)
            {
                t1 = r;
            }
        }

        return true;
    }

    /// <summary>
    /// One Liang-Barsky clip test step for fixed-point clipping.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ClipTestFixed(double p, double q, ref double t0, ref double t1)
    {
        if (p == 0D)
        {
            return q >= 0D;
        }

        double r = q / p;
        if (p < 0D)
        {
            if (r > t1)
            {
                return false;
            }

            if (r > t0)
            {
                t0 = r;
            }
        }
        else
        {
            if (r < t0)
            {
                return false;
            }

            if (r < t1)
            {
                t1 = r;
            }
        }

        return true;
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
    /// Throws when the caller-provided band buffers are smaller than the requested raster band.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowBandBufferTooSmall()
        => throw new ImageProcessingException("The destination raster band buffer is too small for the requested operation.");

    /// <summary>
    /// Throws when a worker scratch instance is reused for a band taller than it was allocated for.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowBandHeightExceedsScratchCapacity()
        => throw new ImageProcessingException("Requested band height exceeds worker scratch capacity.");

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

        /// <summary>
        /// Creates a lightweight band view over caller-owned line and start-cover buffers.
        /// </summary>
        public RasterizableBand CreateRasterizableBand(ReadOnlySpan<RasterLineData> lines, ReadOnlySpan<int> startCovers)
        {
            if (lines.Length < this.LineCount || startCovers.Length < this.BandHeight)
            {
                ThrowBandBufferTooSmall();
            }

            return new RasterizableBand(
                lines[..this.LineCount],
                startCovers[..this.BandHeight],
                this.Width,
                this.WordsPerRow,
                this.CoverStride,
                this.BandHeight,
                this.DestinationTop,
                this.IntersectionRule,
                this.RasterizationMode,
                this.AntialiasThreshold);
        }
    }

    /// <summary>
    /// Prepared raster payload for one row band.
    /// </summary>
    internal readonly ref struct RasterizableBand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RasterizableBand"/> struct.
        /// </summary>
        public RasterizableBand(
            ReadOnlySpan<RasterLineData> lines,
            ReadOnlySpan<int> startCovers,
            int width,
            int wordsPerRow,
            int coverStride,
            int bandHeight,
            int destinationTop,
            IntersectionRule intersectionRule,
            RasterizationMode rasterizationMode,
            float antialiasThreshold)
        {
            this.Lines = lines;
            this.StartCovers = startCovers;
            this.Width = width;
            this.WordsPerRow = wordsPerRow;
            this.CoverStride = coverStride;
            this.BandHeight = bandHeight;
            this.DestinationTop = destinationTop;
            this.IntersectionRule = intersectionRule;
            this.RasterizationMode = rasterizationMode;
            this.AntialiasThreshold = antialiasThreshold;
        }

        /// <summary>
        /// Gets the clipped line list to rasterize.
        /// </summary>
        public ReadOnlySpan<RasterLineData> Lines { get; }

        /// <summary>
        /// Gets the per-row start-cover seeds for off-screen left coverage.
        /// </summary>
        public ReadOnlySpan<int> StartCovers { get; }

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
        /// Gets the band height in pixels.
        /// </summary>
        public int BandHeight { get; }

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
            if ((uint)height > (uint)this.heightCapacity ||
                (uint)wordsPerRow > (uint)this.wordsPerRowCapacity ||
                (uint)coverStride > (uint)this.coverStrideCapacity ||
                (uint)width > (uint)this.widthCapacity)
            {
                ThrowBandHeightExceedsScratchCapacity();
            }

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

        public void RasterizePreparedLines(ReadOnlySpan<RasterLineData> lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                ref readonly RasterLineData line = ref lines[i];
                this.RasterizeLine(line.X0, line.Y0, line.X1, line.Y1);
            }
        }

        public void RasterizeLineSegment(int x0, int y0, int x1, int y1)
            => this.RasterizeLine(x0, y0, x1, y1);

        /// <summary>
        /// Converts accumulated cover/area tables into non-zero coverage span callbacks.
        /// </summary>
        /// <param name="destinationTop">Absolute destination Y corresponding to row zero in this context.</param>
        /// <param name="scanline">Reusable scanline scratch buffer used to materialize emitted spans.</param>
        /// <param name="rowHandler">Coverage callback invoked for each emitted non-zero span.</param>
        public readonly void EmitCoverageRows(int destinationTop, Span<float> scanline, RasterizerCoverageRowHandler rowHandler)
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
                        rowHandler(destinationTop + row, 0, scanline[..this.width]);
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
                    destinationTop + row,
                    scanline,
                    rowHandler);
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
        /// <param name="destinationY">Absolute destination y for this row.</param>
        /// <param name="scanline">Reusable scanline coverage buffer used for per-span materialization.</param>
        /// <param name="rowHandler">Coverage callback invoked for each emitted non-zero span.</param>
        private readonly void EmitRowCoverage(
            ReadOnlySpan<nuint> rowBitVectors,
            int row,
            int cover,
            int minTouchedColumn,
            int maxTouchedColumn,
            int destinationY,
            Span<float> scanline,
            RasterizerCoverageRowHandler rowHandler)
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
                            EmitRun(rowHandler, destinationY, scanline, ref runStart, ref runEnd);
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
                            EmitRun(rowHandler, destinationY, scanline, ref runStart, ref runEnd);
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
                                EmitRun(rowHandler, destinationY, scanline, ref runStart, ref runEnd);
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

            EmitRun(rowHandler, destinationY, scanline, ref runStart, ref runEnd);
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

            scanline[start..end].Fill(coverage);

            if (runStart < 0)
            {
                runStart = start;
                runEnd = end;
                return;
            }

            if (end > runEnd)
            {
                runEnd = end;
            }
        }

        /// <summary>
        /// Emits the currently accumulated non-zero run, if any.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EmitRun(
            RasterizerCoverageRowHandler rowHandler,
            int destinationY,
            Span<float> scanline,
            ref int runStart,
            ref int runEnd)
        {
            if (runStart < 0)
            {
                return;
            }

            rowHandler(destinationY, runStart, scanline[runStart..runEnd]);
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
    internal readonly struct RasterLineData
    {
        public readonly int X0;

        public readonly int Y0;

        public readonly int X1;

        public readonly int Y1;

        public RasterLineData(int x0, int y0, int x1, int y1)
        {
            this.X0 = x0;
            this.Y0 = y0;
            this.X1 = x1;
            this.Y1 = y1;
        }
    }

    /// <summary>
    /// Stroke centerline edge descriptor used for per-band parallel stroke expansion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each descriptor represents one centerline edge with associated join/cap metadata.
    /// During rasterization, each descriptor is expanded into outline polygon edges that
    /// are rasterized directly via <see cref="Context.RasterizeLine"/>.
    /// </para>
    /// <para>
    /// The layout mirrors the GPU <c>StrokeExpandComputeShader</c> edge format:
    /// <list type="bullet">
    /// <item><description>Side edge (flags=0): <c>(X0,Y0)→(X1,Y1)</c> is the centerline segment.</description></item>
    /// <item><description>Join edge (<see cref="StrokeEdgeFlags.Join"/>): <c>(X0,Y0)</c> is the vertex, <c>(X1,Y1)</c> is the previous endpoint, <c>(AdjX,AdjY)</c> is the next endpoint.</description></item>
    /// <item><description>Cap edge (<see cref="StrokeEdgeFlags.CapStart"/>/<see cref="StrokeEdgeFlags.CapEnd"/>): <c>(X0,Y0)</c> is the cap vertex, <c>(X1,Y1)</c> is the adjacent endpoint.</description></item>
    /// </list>
    /// </para>
    /// <para>All coordinates are in scanner-local float space (relative to interest top-left with sampling offset).</para>
    /// </remarks>
    internal readonly struct StrokeEdgeData
    {
        public readonly float X0;
        public readonly float Y0;
        public readonly float X1;
        public readonly float Y1;
        public readonly float AdjX;
        public readonly float AdjY;
        public readonly StrokeEdgeFlags Flags;

        public StrokeEdgeData(float x0, float y0, float x1, float y1, StrokeEdgeFlags flags, float adjX = 0, float adjY = 0)
        {
            this.X0 = x0;
            this.Y0 = y0;
            this.X1 = x1;
            this.Y1 = y1;
            this.Flags = flags;
            this.AdjX = adjX;
            this.AdjY = adjY;
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
        {
            if ((uint)bandHeight > (uint)this.tileCapacity ||
                (uint)wordsPerRow > (uint)this.wordsPerRow ||
                (uint)coverStride > (uint)this.coverStride ||
                (uint)width > (uint)this.width)
            {
                ThrowBandHeightExceedsScratchCapacity();
            }

            int bitVectorCount = checked(wordsPerRow * bandHeight);
            int coverAreaCount = checked(coverStride * bandHeight);
            return new Context(
                this.bitVectorsOwner.Memory.Span[..bitVectorCount],
                this.coverAreaOwner.Memory.Span[..coverAreaCount],
                this.startCoverOwner.Memory.Span[..bandHeight],
                this.rowMinTouchedColumnOwner.Memory.Span[..bandHeight],
                this.rowMaxTouchedColumnOwner.Memory.Span[..bandHeight],
                this.rowHasBitsOwner.Memory.Span[..bandHeight],
                this.rowTouchedOwner.Memory.Span[..bandHeight],
                this.touchedRowsOwner.Memory.Span[..bandHeight],
                width,
                bandHeight,
                wordsPerRow,
                coverStride,
                intersectionRule,
                rasterizationMode,
                antialiasThreshold);
        }

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
