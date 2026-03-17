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
    /// Rasterizes the path into trimmed coverage rows.
    /// </summary>
    /// <param name="path">Path to rasterize.</param>
    /// <param name="options">Rasterization options.</param>
    /// <param name="allocator">Temporary buffer allocator.</param>
    /// <param name="rowHandler">Coverage row callback invoked once per emitted row.</param>
    public static void RasterizeRows(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        RasterizerCoverageRowHandler rowHandler)
        => RasterizePreparedFillRows(PreparedGeometry.Create(path), 0, 0, options, allocator, rowHandler);

    /// <summary>
    /// Rasterizes one prepared fill geometry by rebuilding rasterizable row bands on demand.
    /// </summary>
    /// <param name="geometry">The prepared geometry to rasterize.</param>
    /// <param name="translateX">The destination-space X translation.</param>
    /// <param name="translateY">The destination-space Y translation.</param>
    /// <param name="options">The rasterizer options that describe the interest rectangle and fill rule.</param>
    /// <param name="allocator">The allocator used for worker-local temporary buffers.</param>
    /// <param name="rowHandler">The callback that receives emitted coverage rows.</param>
    /// <remarks>
    /// The flush pipeline precomputes row membership for many commands at once. The standalone
    /// rasterizer API only sees a single path, so it keeps the public surface smaller and rebuilds
    /// the row-band view directly from the prepared geometry.
    /// </remarks>
    private static void RasterizePreparedFillRows(
        PreparedGeometry geometry,
        int translateX,
        int translateY,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        RasterizerCoverageRowHandler rowHandler)
    {
        Rectangle interest = options.Interest;
        int width = interest.Width;
        int height = interest.Height;
        int segmentCount = geometry.SegmentCount;
        if (width <= 0 || height <= 0 || segmentCount == 0)
        {
            return;
        }

        long coverStride = (long)width * 2;
        if (coverStride > int.MaxValue)
        {
            ThrowInterestBoundsTooLarge();
        }

        int firstBandIndex = FloorDiv(interest.Top, DefaultTileHeight);
        int lastBandIndex = FloorDiv(interest.Bottom - 1, DefaultTileHeight);
        int bandCount = (lastBandIndex - firstBandIndex) + 1;
        if (bandCount <= 0)
        {
            return;
        }

        RasterizerOptions rasterizerOptions = options;
        int[] segmentIndices = CreateSequentialSegmentIndices(segmentCount);
        if (bandCount == 1 || Environment.ProcessorCount < 2)
        {
            using FillWorkerState worker = new(allocator, segmentCount, width, DefaultTileHeight);
            for (int bandIndex = 0; bandIndex < bandCount; bandIndex++)
            {
                RasterizePreparedFillBand(
                    geometry,
                    segmentIndices,
                    translateX,
                    translateY,
                    rasterizerOptions,
                    bandIndex,
                    worker,
                    rowHandler);
            }

            return;
        }

        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = Math.Min(
                MaxParallelWorkerCount,
                Math.Min(Environment.ProcessorCount, bandCount)),
        };

        _ = Parallel.For(
            0,
            bandCount,
            parallelOptions,
            () => new FillWorkerState(allocator, segmentCount, width, DefaultTileHeight),
            (bandIndex, _, worker) =>
            {
                RasterizePreparedFillBand(
                    geometry,
                    segmentIndices,
                    translateX,
                    translateY,
                    rasterizerOptions,
                    bandIndex,
                    worker,
                    rowHandler);
                return worker;
            },
            static worker => worker.Dispose());
    }

    /// <summary>
    /// Builds and executes a single rasterizable fill band.
    /// </summary>
    private static void RasterizePreparedFillBand(
        PreparedGeometry geometry,
        ReadOnlySpan<int> segmentIndices,
        int translateX,
        int translateY,
        in RasterizerOptions options,
        int bandIndex,
        FillWorkerState worker,
        RasterizerCoverageRowHandler rowHandler)
    {
        if (!TryBuildRasterizableBand(
            geometry,
            segmentIndices,
            translateX,
            translateY,
            in options,
            bandIndex,
            worker.LineBuffer,
            worker.StartCoverBuffer,
            out RasterizableBandInfo rasterizableBandInfo))
        {
            return;
        }

        RasterizableBand rasterizableBand = rasterizableBandInfo.CreateRasterizableBand(
            worker.LineBuffer,
            worker.StartCoverBuffer);
        Context context = worker.Scratch.CreateContext(
            rasterizableBand.Width,
            rasterizableBand.WordsPerRow,
            rasterizableBand.CoverStride,
            rasterizableBand.BandHeight,
            rasterizableBand.IntersectionRule,
            rasterizableBand.RasterizationMode,
            rasterizableBand.AntialiasThreshold);
        ExecuteRasterizableBand(ref context, in rasterizableBand, worker.Scratch.Scanline, rowHandler);
    }

    /// <summary>
    /// Creates a dense 0..N-1 segment index buffer.
    /// </summary>
    private static int[] CreateSequentialSegmentIndices(int segmentCount)
    {
        int[] segmentIndices = new int[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            segmentIndices[i] = i;
        }

        return segmentIndices;
    }

    /// <summary>
    /// Converts one row band of prepared geometry into a compact rasterizable payload.
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
        PreparedGeometry geometry,
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
        ReadOnlySpan<PreparedLineSegment> segments = geometry.Segments;
        for (int i = 0; i < bandSegmentIndices.Length; i++)
        {
            PreparedLineSegment segment = segments[bandSegmentIndices[i]];
            PointF p0 = segment.P0;
            PointF p1 = segment.P1;

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
    /// Rasterizes a stroke path by expanding centerline edges into outline edges per-band in parallel.
    /// </summary>
    /// <param name="path">Centerline path to stroke.</param>
    /// <param name="options">Rasterization options (interest rect should already include stroke expansion).</param>
    /// <param name="allocator">Temporary buffer allocator.</param>
    /// <param name="rowHandler">Coverage row callback invoked once per emitted row.</param>
    /// <param name="strokeWidth">Stroke width in pixels.</param>
    /// <param name="lineJoin">Outer join style.</param>
    /// <param name="lineCap">Cap style for open contour endpoints.</param>
    /// <param name="miterLimit">Miter limit for miter-family joins.</param>
    public static void RasterizeStrokeRows(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        RasterizerCoverageRowHandler rowHandler,
        float strokeWidth,
        LineJoin lineJoin,
        LineCap lineCap,
        float miterLimit)
    {
        WorkerScratch? scratch = null;
        try
        {
            RasterizeStrokeCoreRows(path, options, allocator, rowHandler, allowParallel: true, ref scratch, strokeWidth, lineJoin, lineCap, miterLimit);
        }
        finally
        {
            scratch?.Dispose();
        }
    }

    /// <summary>
    /// Rasterizes a stroke path with caller-managed scratch reuse.
    /// </summary>
    internal static void RasterizeStrokeRows(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        RasterizerCoverageRowHandler rowHandler,
        ref WorkerScratch? reusableScratch,
        float strokeWidth,
        LineJoin lineJoin,
        LineCap lineCap,
        float miterLimit)
        => RasterizeStrokeCoreRows(path, options, allocator, rowHandler, allowParallel: true, ref reusableScratch, strokeWidth, lineJoin, lineCap, miterLimit);

    /// <summary>
    /// Shared entry point for stroke-aware trimmed-row rasterization.
    /// Expands centerline edges into outline edges per-band and rasterizes directly.
    /// </summary>
    private static void RasterizeStrokeCoreRows(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        RasterizerCoverageRowHandler rowHandler,
        bool allowParallel,
        ref WorkerScratch? reusableScratch,
        float strokeWidth,
        LineJoin lineJoin,
        LineCap lineCap,
        float miterLimit)
    {
        Rectangle interest = options.Interest;
        int width = interest.Width;
        int height = interest.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int wordsPerRow = BitVectorsForMaxBitCount(width);
        int maxBandRows = 0;
        long coverStride = (long)width * 2;
        if (coverStride > int.MaxValue || !TryGetBandHeight(width, height, wordsPerRow, coverStride, out maxBandRows))
        {
            ThrowInterestBoundsTooLarge();
        }

        int coverStrideInt = (int)coverStride;
        bool samplePixelCenter = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter;
        float samplingOffsetX = samplePixelCenter ? 0.5F : 0F;
        float samplingOffsetY = samplePixelCenter ? 0.5F : 0F;

        // Flatten path and collect contour info, preserving open/closed state for cap/join generation.
        List<ISimplePath> contours = [];
        int totalVertexCount = 0;
        foreach (ISimplePath sp in path.Flatten())
        {
            if (sp.Points.Length < 2)
            {
                continue;
            }

            contours.Add(sp);
            totalVertexCount += sp.Points.Length;
        }

        if (totalVertexCount == 0)
        {
            return;
        }

        // Max stroke descriptors: closed contours emit 2*N (sides + joins),
        // open contours emit 2*N-1 (sides + interior joins + 2 caps).
        int maxEdgeCount = totalVertexCount * 2;
        using IMemoryOwner<StrokeEdgeData> edgeDataOwner = allocator.Allocate<StrokeEdgeData>(maxEdgeCount);
        int edgeCount = BuildStrokeEdgeTable(
            contours,
            interest.Left,
            interest.Top,
            samplingOffsetX,
            samplingOffsetY,
            edgeDataOwner.Memory.Span);

        if (edgeCount <= 0)
        {
            return;
        }

        float halfWidth = strokeWidth / 2f;
        float expansion = halfWidth * MathF.Max(miterLimit, 1f);

        if (allowParallel &&
            TryRasterizeStrokeParallel(
                edgeDataOwner.Memory,
                edgeCount,
                width,
                height,
                interest.Top,
                wordsPerRow,
                coverStrideInt,
                maxBandRows,
                options.IntersectionRule,
                options.RasterizationMode,
                options.AntialiasThreshold,
                allocator,
                rowHandler,
                ref reusableScratch,
                halfWidth,
                expansion,
                lineJoin,
                lineCap,
                miterLimit))
        {
            return;
        }

        RasterizeStrokeSequentialBands(
            edgeDataOwner.Memory.Span[..edgeCount],
            width,
            height,
            interest.Top,
            wordsPerRow,
            coverStrideInt,
            maxBandRows,
            options.IntersectionRule,
            options.RasterizationMode,
            options.AntialiasThreshold,
            allocator,
            rowHandler,
            ref reusableScratch,
            halfWidth,
            expansion,
            lineJoin,
            lineCap,
            miterLimit);
    }

    /// <summary>
    /// Sequential stroke rasterization using band buckets over the prebuilt stroke edge table.
    /// </summary>
    private static void RasterizeStrokeSequentialBands(
        ReadOnlySpan<StrokeEdgeData> edges,
        int width,
        int height,
        int interestTop,
        int wordsPerRow,
        int coverStrideInt,
        int maxBandRows,
        IntersectionRule intersectionRule,
        RasterizationMode rasterizationMode,
        float antialiasThreshold,
        MemoryAllocator allocator,
        RasterizerCoverageRowHandler rowHandler,
        ref WorkerScratch? reusableScratch,
        float halfWidth,
        float expansion,
        LineJoin lineJoin,
        LineCap lineCap,
        float miterLimit)
    {
        int bandHeight = maxBandRows;
        int bandCount = (height + bandHeight - 1) / bandHeight;
        if (bandCount < 1)
        {
            return;
        }

        if (!TryBuildBandSortedStrokeEdges(
            edges,
            bandCount,
            bandHeight,
            expansion,
            allocator,
            out IMemoryOwner<StrokeEdgeData> sortedEdgesOwner,
            out IMemoryOwner<int> bandOffsetsOwner))
        {
            ThrowInterestBoundsTooLarge();
        }

        using (sortedEdgesOwner)
        using (bandOffsetsOwner)
        {
            Span<StrokeEdgeData> sortedEdges = sortedEdgesOwner.Memory.Span;
            Span<int> bandOffsets = bandOffsetsOwner.Memory.Span;

            if (reusableScratch == null || !reusableScratch.CanReuse(wordsPerRow, coverStrideInt, width, bandHeight))
            {
                reusableScratch?.Dispose();
                reusableScratch = WorkerScratch.Create(allocator, wordsPerRow, coverStrideInt, width, bandHeight);
            }

            WorkerScratch scratch = reusableScratch;
            for (int bandIndex = 0; bandIndex < bandCount; bandIndex++)
            {
                int bandTop = bandIndex * bandHeight;
                int currentBandHeight = Math.Min(bandHeight, height - bandTop);
                int start = bandOffsets[bandIndex];
                int length = bandOffsets[bandIndex + 1] - start;
                if (length == 0)
                {
                    continue;
                }

                Context context = scratch.CreateContext(
                    width,
                    wordsPerRow,
                    coverStrideInt,
                    currentBandHeight,
                    intersectionRule,
                    rasterizationMode,
                    antialiasThreshold);
                context.RasterizeStrokeEdges(sortedEdges.Slice(start, length), bandTop, halfWidth, lineJoin, lineCap, miterLimit);
                context.EmitCoverageRows(interestTop + bandTop, scratch.Scanline, rowHandler);
                context.ResetTouchedRows();
            }
        }
    }

    /// <summary>
    /// Attempts to execute the tiled parallel stroke scanner.
    /// </summary>
    private static bool TryRasterizeStrokeParallel(
        Memory<StrokeEdgeData> edgeMemory,
        int edgeCount,
        int width,
        int height,
        int interestTop,
        int wordsPerRow,
        int coverStride,
        int maxBandRows,
        IntersectionRule intersectionRule,
        RasterizationMode rasterizationMode,
        float antialiasThreshold,
        MemoryAllocator allocator,
        RasterizerCoverageRowHandler rowHandler,
        ref WorkerScratch? reusableScratch,
        float halfWidth,
        float expansion,
        LineJoin lineJoin,
        LineCap lineCap,
        float miterLimit)
    {
        int tileHeight = Math.Min(DefaultTileHeight, maxBandRows);
        if (tileHeight < 1)
        {
            return false;
        }

        int tileCount = (height + tileHeight - 1) / tileHeight;
        if (tileCount == 1 || edgeCount <= 64)
        {
            RasterizeStrokeSingleTileDirect(
                edgeMemory.Span[..edgeCount],
                width,
                height,
                interestTop,
                wordsPerRow,
                coverStride,
                intersectionRule,
                rasterizationMode,
                antialiasThreshold,
                allocator,
                rowHandler,
                ref reusableScratch,
                halfWidth,
                lineJoin,
                lineCap,
                miterLimit);

            return true;
        }

        if (Environment.ProcessorCount < 2)
        {
            return false;
        }

        if (!TryBuildBandSortedStrokeEdges(
            edgeMemory.Span[..edgeCount],
            tileCount,
            tileHeight,
            expansion,
            allocator,
            out IMemoryOwner<StrokeEdgeData> sortedEdgesOwner,
            out IMemoryOwner<int> tileOffsetsOwner))
        {
            return false;
        }

        using (sortedEdgesOwner)
        using (tileOffsetsOwner)
        {
            Memory<StrokeEdgeData> sortedEdgesMemory = sortedEdgesOwner.Memory;
            Memory<int> tileOffsetsMemory = tileOffsetsOwner.Memory;

            ParallelOptions parallelOptions = new()
            {
                MaxDegreeOfParallelism = Math.Min(MaxParallelWorkerCount, Math.Min(Environment.ProcessorCount, tileCount))
            };

            _ = Parallel.For(
                0,
                tileCount,
                parallelOptions,
                () => WorkerScratch.Create(allocator, wordsPerRow, coverStride, width, tileHeight),
                (tileIndex, _, worker) =>
                {
                    Context context = default;
                    bool hasCoverage = false;
                    int bandTop = tileIndex * tileHeight;
                    try
                    {
                        Span<int> tileOffsets = tileOffsetsMemory.Span;
                        int bandHeight = Math.Min(tileHeight, height - bandTop);
                        int start = tileOffsets[tileIndex];
                        int length = tileOffsets[tileIndex + 1] - start;
                        if (length > 0)
                        {
                            ReadOnlySpan<StrokeEdgeData> tileEdges = sortedEdgesMemory.Span.Slice(start, length);
                            context = worker.CreateContext(
                                width,
                                wordsPerRow,
                                coverStride,
                                bandHeight,
                                intersectionRule,
                                rasterizationMode,
                                antialiasThreshold);
                            context.RasterizeStrokeEdges(tileEdges, bandTop, halfWidth, lineJoin, lineCap, miterLimit);
                            hasCoverage = true;
                            context.EmitCoverageRows(interestTop + bandTop, worker.Scanline, rowHandler);
                        }
                    }
                    finally
                    {
                        if (hasCoverage)
                        {
                            context.ResetTouchedRows();
                        }
                    }

                    return worker;
                },
                static worker => worker.Dispose());

            return true;
        }
    }

    /// <summary>
    /// Rasterizes stroke edges in a single tile directly into the caller callback.
    /// </summary>
    private static void RasterizeStrokeSingleTileDirect(
        ReadOnlySpan<StrokeEdgeData> edges,
        int width,
        int height,
        int interestTop,
        int wordsPerRow,
        int coverStride,
        IntersectionRule intersectionRule,
        RasterizationMode rasterizationMode,
        float antialiasThreshold,
        MemoryAllocator allocator,
        RasterizerCoverageRowHandler rowHandler,
        ref WorkerScratch? reusableScratch,
        float halfWidth,
        LineJoin lineJoin,
        LineCap lineCap,
        float miterLimit)
    {
        if (reusableScratch == null || !reusableScratch.CanReuse(wordsPerRow, coverStride, width, height))
        {
            reusableScratch?.Dispose();
            reusableScratch = WorkerScratch.Create(allocator, wordsPerRow, coverStride, width, height);
        }

        WorkerScratch scratch = reusableScratch;
        Context context = scratch.CreateContext(
            width,
            wordsPerRow,
            coverStride,
            height,
            intersectionRule,
            rasterizationMode,
            antialiasThreshold);
        context.RasterizeStrokeEdges(edges, bandTop: 0, halfWidth, lineJoin, lineCap, miterLimit);
        context.EmitCoverageRows(interestTop, scratch.Scanline, rowHandler);
        context.ResetTouchedRows();
    }

    /// <summary>
    /// Builds a band-sorted stroke edge buffer where descriptors are duplicated into each band
    /// their stroke expansion could touch.
    /// </summary>
    private static bool TryBuildBandSortedStrokeEdges(
        ReadOnlySpan<StrokeEdgeData> edges,
        int bucketCount,
        int bucketHeight,
        float expansion,
        MemoryAllocator allocator,
        out IMemoryOwner<StrokeEdgeData> sortedEdgesOwner,
        out IMemoryOwner<int> offsetsOwner)
    {
        using IMemoryOwner<int> countsOwner = allocator.Allocate<int>(bucketCount, AllocationOptions.Clean);
        Span<int> counts = countsOwner.Memory.Span;
        long totalRefs = 0;
        for (int i = 0; i < edges.Length; i++)
        {
            ref readonly StrokeEdgeData edge = ref edges[i];

            // Side edges: outline extends halfWidth from both endpoints.
            // Join/cap edges: geometry is centered on vertex (X0,Y0), extends by expansion.
            float minY, maxY;
            if (edge.Flags == StrokeEdgeFlags.None)
            {
                minY = MathF.Min(edge.Y0, edge.Y1) - expansion;
                maxY = MathF.Max(edge.Y0, edge.Y1) + expansion;
            }
            else
            {
                minY = edge.Y0 - expansion;
                maxY = edge.Y0 + expansion;
            }

            int startBucket = Math.Max(0, (int)MathF.Floor(minY / bucketHeight));
            int endBucket = Math.Min(bucketCount - 1, (int)MathF.Floor(maxY / bucketHeight));
            if (startBucket > endBucket)
            {
                continue;
            }

            totalRefs += (endBucket - startBucket) + 1;
            if (totalRefs > int.MaxValue)
            {
                sortedEdgesOwner = null!;
                offsetsOwner = null!;
                return false;
            }

            for (int b = startBucket; b <= endBucket; b++)
            {
                counts[b]++;
            }
        }

        int totalEdges = (int)totalRefs;
        offsetsOwner = allocator.Allocate<int>(bucketCount + 1);
        Span<int> offsets = offsetsOwner.Memory.Span;
        int offset = 0;
        for (int b = 0; b < bucketCount; b++)
        {
            offsets[b] = offset;
            offset += counts[b];
        }

        offsets[bucketCount] = offset;
        using IMemoryOwner<int> writeCursorOwner = allocator.Allocate<int>(bucketCount);
        Span<int> writeCursor = writeCursorOwner.Memory.Span;
        offsets[..bucketCount].CopyTo(writeCursor);

        sortedEdgesOwner = allocator.Allocate<StrokeEdgeData>(Math.Max(totalEdges, 1));
        Span<StrokeEdgeData> sorted = sortedEdgesOwner.Memory.Span;
        for (int i = 0; i < edges.Length; i++)
        {
            ref readonly StrokeEdgeData edge = ref edges[i];
            float minY, maxY;
            if (edge.Flags == StrokeEdgeFlags.None)
            {
                minY = MathF.Min(edge.Y0, edge.Y1) - expansion;
                maxY = MathF.Max(edge.Y0, edge.Y1) + expansion;
            }
            else
            {
                minY = edge.Y0 - expansion;
                maxY = edge.Y0 + expansion;
            }

            int startBucket = Math.Max(0, (int)MathF.Floor(minY / bucketHeight));
            int endBucket = Math.Min(bucketCount - 1, (int)MathF.Floor(maxY / bucketHeight));
            for (int b = startBucket; b <= endBucket; b++)
            {
                sorted[writeCursor[b]++] = edge;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds a stroke edge table from flattened path contours.
    /// Each contour produces side edge descriptors for segments, join descriptors at interior
    /// vertices, and cap descriptors at endpoints of open contours.
    /// </summary>
    /// <param name="contours">Flattened path contours with open/closed state.</param>
    /// <param name="minX">Interest left in absolute coordinates.</param>
    /// <param name="minY">Interest top in absolute coordinates.</param>
    /// <param name="samplingOffsetX">Horizontal sampling offset.</param>
    /// <param name="samplingOffsetY">Vertical sampling offset.</param>
    /// <param name="destination">Destination span for stroke edge descriptors.</param>
    /// <returns>Number of valid descriptors written.</returns>
    private static int BuildStrokeEdgeTable(
        List<ISimplePath> contours,
        float minX,
        float minY,
        float samplingOffsetX,
        float samplingOffsetY,
        Span<StrokeEdgeData> destination)
    {
        int count = 0;
        for (int c = 0; c < contours.Count; c++)
        {
            ISimplePath sp = contours[c];
            ReadOnlySpan<PointF> pts = sp.Points.Span;
            int n = pts.Length;
            if (n < 2)
            {
                continue;
            }

            bool isClosed = sp.IsClosed;
            float offX = samplingOffsetX - minX;
            float offY = samplingOffsetY - minY;

            if (isClosed)
            {
                // Side edges for all segments, including closing segment back to first vertex.
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    destination[count++] = new StrokeEdgeData(
                        pts[i].X + offX,
                        pts[i].Y + offY,
                        pts[j].X + offX,
                        pts[j].Y + offY,
                        0);
                }

                // Join at each vertex.
                for (int i = 0; i < n; i++)
                {
                    int prev = (i - 1 + n) % n;
                    int next = (i + 1) % n;
                    destination[count++] = new StrokeEdgeData(
                        pts[i].X + offX,
                        pts[i].Y + offY,
                        pts[prev].X + offX,
                        pts[prev].Y + offY,
                        StrokeEdgeFlags.Join,
                        pts[next].X + offX,
                        pts[next].Y + offY);
                }
            }
            else
            {
                // Side edges for all segments.
                for (int i = 0; i < n - 1; i++)
                {
                    destination[count++] = new StrokeEdgeData(
                        pts[i].X + offX,
                        pts[i].Y + offY,
                        pts[i + 1].X + offX,
                        pts[i + 1].Y + offY,
                        0);
                }

                // Interior joins.
                for (int i = 1; i < n - 1; i++)
                {
                    destination[count++] = new StrokeEdgeData(
                        pts[i].X + offX,
                        pts[i].Y + offY,
                        pts[i - 1].X + offX,
                        pts[i - 1].Y + offY,
                        StrokeEdgeFlags.Join,
                        pts[i + 1].X + offX,
                        pts[i + 1].Y + offY);
                }

                // Start cap.
                destination[count++] = new StrokeEdgeData(
                    pts[0].X + offX,
                    pts[0].Y + offY,
                    pts[1].X + offX,
                    pts[1].Y + offY,
                    StrokeEdgeFlags.CapStart);

                // End cap.
                destination[count++] = new StrokeEdgeData(
                    pts[n - 1].X + offX,
                    pts[n - 1].Y + offY,
                    pts[n - 2].X + offX,
                    pts[n - 2].Y + offY,
                    StrokeEdgeFlags.CapEnd);
            }
        }

        return count;
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
    /// Calculates the maximum reusable band height under memory and indexing constraints.
    /// </summary>
    /// <param name="width">Interest width.</param>
    /// <param name="height">Interest height.</param>
    /// <param name="wordsPerRow">Bitset words per row.</param>
    /// <param name="coverStride">Cover-area stride in ints.</param>
    /// <param name="bandHeight">Resulting maximum safe band height.</param>
    /// <returns><see langword="true"/> when a valid band height was produced.</returns>
    private static bool TryGetBandHeight(int width, int height, int wordsPerRow, long coverStride, out int bandHeight)
    {
        bandHeight = 0;
        if (width <= 0 || height <= 0 || wordsPerRow <= 0 || coverStride <= 0)
        {
            return false;
        }

        long bytesPerRow =
            ((long)wordsPerRow * nint.Size) +
            (coverStride * sizeof(int)) +
            sizeof(int);

        long rowsByBudget = BandMemoryBudgetBytes / bytesPerRow;
        if (rowsByBudget < 1)
        {
            rowsByBudget = 1;
        }

        long rowsByBitVectors = int.MaxValue / wordsPerRow;
        long rowsByCoverArea = int.MaxValue / coverStride;
        long maxRows = Math.Min(rowsByBudget, Math.Min(rowsByBitVectors, rowsByCoverArea));
        if (maxRows < 1)
        {
            return false;
        }

        bandHeight = (int)Math.Min(height, maxRows);
        return bandHeight > 0;
    }

    /// <summary>
    /// Converts a float coordinate to signed 24.8 fixed-point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FloatToFixed24Dot8(float value) => (int)MathF.Round(value * FixedOne);

    /// <summary>
    /// Clips a fixed-point segment against vertical bounds.
    /// </summary>
    /// <param name="x0">Segment start X in 24.8 fixed-point (updated in place).</param>
    /// <param name="y0">Segment start Y in 24.8 fixed-point (updated in place).</param>
    /// <param name="x1">Segment end X in 24.8 fixed-point (updated in place).</param>
    /// <param name="y1">Segment end Y in 24.8 fixed-point (updated in place).</param>
    /// <param name="minY">Minimum Y bound in 24.8 fixed-point.</param>
    /// <param name="maxY">Maximum Y bound in 24.8 fixed-point.</param>
    /// <returns><see langword="true"/> when a non-horizontal clipped segment remains.</returns>
    private static bool ClipToVerticalBoundsFixed(ref int x0, ref int y0, ref int x1, ref int y1, int minY, int maxY)
    {
        double t0 = 0D;
        double t1 = 1D;
        int originX0 = x0;
        int originY0 = y0;
        long dx = (long)x1 - originX0;
        long dy = (long)y1 - originY0;
        if (!ClipTestFixed(-(double)dy, originY0 - (double)minY, ref t0, ref t1))
        {
            return false;
        }

        if (!ClipTestFixed(dy, maxY - (double)originY0, ref t0, ref t1))
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
        /// Rasterizes all prebuilt edges that overlap this context.
        /// </summary>
        /// <param name="edges">Shared edge table.</param>
        /// <param name="bandTop">Top row of this context in global scanner-local coordinates.</param>
        public void RasterizeEdgeTable(ReadOnlySpan<EdgeData> edges, int bandTop)
        {
            int bandTopFixed = bandTop * FixedOne;
            int bandBottomFixed = bandTopFixed + (this.height * FixedOne);

            for (int i = 0; i < edges.Length; i++)
            {
                EdgeData edge = edges[i];
                int x0 = edge.X0;
                int y0 = edge.Y0;
                int x1 = edge.X1;
                int y1 = edge.Y1;

                // Fast-path: edge is fully within this band — no clipping needed.
                int minY = Math.Min(y0, y1);
                int maxY = Math.Max(y0, y1);
                if (minY >= bandTopFixed && maxY <= bandBottomFixed)
                {
                    this.RasterizeLine(x0, y0 - bandTopFixed, x1, y1 - bandTopFixed);
                    continue;
                }

                if (!ClipToVerticalBoundsFixed(ref x0, ref y0, ref x1, ref y1, bandTopFixed, bandBottomFixed))
                {
                    continue;
                }

                // Convert global scanner Y to band-local Y after clipping.
                y0 -= bandTopFixed;
                y1 -= bandTopFixed;

                this.RasterizeLine(x0, y0, x1, y1);
            }
        }

        /// <summary>
        /// Expands stroke centerline edge descriptors into outline polygon edges and rasterizes them.
        /// </summary>
        /// <param name="edges">Band-sorted stroke edge descriptors.</param>
        /// <param name="bandTop">Top row of this context in global scanner-local coordinates.</param>
        /// <param name="halfWidth">Half the stroke width in pixels.</param>
        /// <param name="lineJoin">Outer join style.</param>
        /// <param name="lineCap">Cap style for open contour endpoints.</param>
        /// <param name="miterLimit">Miter limit for miter-family joins.</param>
        public void RasterizeStrokeEdges(
            ReadOnlySpan<StrokeEdgeData> edges,
            int bandTop,
            float halfWidth,
            LineJoin lineJoin,
            LineCap lineCap,
            float miterLimit)
        {
            int bandTopFixed = bandTop * FixedOne;
            int bandBottomFixed = bandTopFixed + (this.height * FixedOne);

            for (int i = 0; i < edges.Length; i++)
            {
                ref readonly StrokeEdgeData edge = ref edges[i];
                StrokeEdgeFlags flags = edge.Flags;

                if (flags == StrokeEdgeFlags.None)
                {
                    this.ExpandSideEdge(in edge, halfWidth, bandTopFixed, bandBottomFixed);
                }
                else if ((flags & StrokeEdgeFlags.Join) != 0)
                {
                    this.ExpandJoinEdge(in edge, halfWidth, lineJoin, miterLimit, bandTopFixed, bandBottomFixed);
                }
                else if ((flags & StrokeEdgeFlags.CapStart) != 0)
                {
                    this.ExpandCapEdge(in edge, halfWidth, lineCap, isStart: true, bandTopFixed, bandBottomFixed);
                }
                else
                {
                    this.ExpandCapEdge(in edge, halfWidth, lineCap, isStart: false, bandTopFixed, bandBottomFixed);
                }
            }
        }

        /// <summary>
        /// Emits one outline edge into the rasterizer, converting from float to fixed-point
        /// and clipping to band bounds.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EmitOutlineEdge(
            float ex0,
            float ey0,
            float ex1,
            float ey1,
            int bandTopFixed,
            int bandBottomFixed)
        {
            int fy0 = FloatToFixed24Dot8(ey0);
            int fy1 = FloatToFixed24Dot8(ey1);
            if (fy0 == fy1)
            {
                return;
            }

            int fx0 = FloatToFixed24Dot8(ex0);
            int fx1 = FloatToFixed24Dot8(ex1);

            int minY = Math.Min(fy0, fy1);
            int maxY = Math.Max(fy0, fy1);
            if (minY >= bandBottomFixed || maxY <= bandTopFixed)
            {
                return;
            }

            if (minY >= bandTopFixed && maxY <= bandBottomFixed)
            {
                this.RasterizeLine(fx0, fy0 - bandTopFixed, fx1, fy1 - bandTopFixed);
                return;
            }

            int x0 = fx0, y0 = fy0, x1 = fx1, y1 = fy1;
            if (ClipToVerticalBoundsFixed(ref x0, ref y0, ref x1, ref y1, bandTopFixed, bandBottomFixed))
            {
                this.RasterizeLine(x0, y0 - bandTopFixed, x1, y1 - bandTopFixed);
            }
        }

        /// <summary>
        /// Expands a side (segment) edge into two outline edges offset by the stroke normal.
        /// </summary>
        private void ExpandSideEdge(
            in StrokeEdgeData edge,
            float halfWidth,
            int bandTopFixed,
            int bandBottomFixed)
        {
            float dx = edge.X1 - edge.X0;
            float dy = edge.Y1 - edge.Y0;
            float len = MathF.Sqrt((dx * dx) + (dy * dy));
            if (len < 1e-6f)
            {
                return;
            }

            float nx = (-dy / len) * halfWidth;
            float ny = (dx / len) * halfWidth;

            // Left side.
            this.EmitOutlineEdge(
                edge.X0 + nx,
                edge.Y0 + ny,
                edge.X1 + nx,
                edge.Y1 + ny,
                bandTopFixed,
                bandBottomFixed);

            // Right side (reversed winding).
            this.EmitOutlineEdge(
                edge.X1 - nx,
                edge.Y1 - ny,
                edge.X0 - nx,
                edge.Y0 - ny,
                bandTopFixed,
                bandBottomFixed);
        }

        /// <summary>
        /// Expands a join descriptor into inner bevel + outer join edges.
        /// Ported from the GPU StrokeExpandComputeShader join logic.
        /// </summary>
        private void ExpandJoinEdge(
            in StrokeEdgeData edge,
            float halfWidth,
            LineJoin lineJoin,
            float miterLimit,
            int bandTopFixed,
            int bandBottomFixed)
        {
            float vx = edge.X0, vy = edge.Y0;
            float dx1 = vx - edge.X1, dy1 = vy - edge.Y1;
            float len1 = MathF.Sqrt((dx1 * dx1) + (dy1 * dy1));
            if (len1 < 1e-6f)
            {
                return;
            }

            float dx2 = edge.AdjX - vx, dy2 = edge.AdjY - vy;
            float len2 = MathF.Sqrt((dx2 * dx2) + (dy2 * dy2));
            if (len2 < 1e-6f)
            {
                return;
            }

            float nx1 = -dy1 / len1, ny1 = dx1 / len1;
            float nx2 = -dy2 / len2, ny2 = dx2 / len2;
            float cross = (dx1 * dy2) - (dy1 * dx2);

            float oax, oay, obx, oby, iax, iay, ibx, iby;
            if (cross > 0)
            {
                oax = vx - (nx1 * halfWidth);
                oay = vy - (ny1 * halfWidth);
                obx = vx - (nx2 * halfWidth);
                oby = vy - (ny2 * halfWidth);
                iax = vx + (nx1 * halfWidth);
                iay = vy + (ny1 * halfWidth);
                ibx = vx + (nx2 * halfWidth);
                iby = vy + (ny2 * halfWidth);
            }
            else
            {
                oax = vx + (nx1 * halfWidth);
                oay = vy + (ny1 * halfWidth);
                obx = vx + (nx2 * halfWidth);
                oby = vy + (ny2 * halfWidth);
                iax = vx - (nx1 * halfWidth);
                iay = vy - (ny1 * halfWidth);
                ibx = vx - (nx2 * halfWidth);
                iby = vy - (ny2 * halfWidth);
            }

            float ofx, ofy, otx, oty, ifx, ify, itx, ity;
            if (cross > 0)
            {
                ofx = obx;
                ofy = oby;
                otx = oax;
                oty = oay;
                ifx = iax;
                ify = iay;
                itx = ibx;
                ity = iby;
            }
            else
            {
                ofx = oax;
                ofy = oay;
                otx = obx;
                oty = oby;
                ifx = ibx;
                ify = iby;
                itx = iax;
                ity = iay;
            }

            // Inner join: always bevel.
            this.EmitOutlineEdge(ifx, ify, itx, ity, bandTopFixed, bandBottomFixed);

            // Outer join.
            bool miterHandled = false;
            if (lineJoin is LineJoin.Miter or LineJoin.MiterRevert or LineJoin.MiterRound)
            {
                float ux1 = dx1 / len1;
                float uy1 = dy1 / len1;
                float ux2 = dx2 / len2;
                float uy2 = dy2 / len2;
                float denom = (ux1 * uy2) - (uy1 * ux2);
                if (MathF.Abs(denom) > 1e-4f)
                {
                    float dpx = obx - oax;
                    float dpy = oby - oay;
                    float t = ((dpx * uy2) - (dpy * ux2)) / denom;
                    float mx = oax + (t * ux1);
                    float my = oay + (t * uy1);
                    float mdx = mx - vx;
                    float mdy = my - vy;
                    float miterDist = MathF.Sqrt((mdx * mdx) + (mdy * mdy));
                    float limit = halfWidth * miterLimit;
                    if (miterDist <= limit)
                    {
                        this.EmitOutlineEdge(ofx, ofy, mx, my, bandTopFixed, bandBottomFixed);
                        this.EmitOutlineEdge(mx, my, otx, oty, bandTopFixed, bandBottomFixed);
                        miterHandled = true;
                    }
                    else if (lineJoin == LineJoin.Miter)
                    {
                        // Clipped miter: blend between bevel and full miter at the limit distance.
                        float bdx = ((oax + obx) * 0.5f) - vx;
                        float bdy = ((oay + oby) * 0.5f) - vy;
                        float bdist = MathF.Sqrt((bdx * bdx) + (bdy * bdy));
                        float blend = Math.Clamp((limit - bdist) / (miterDist - bdist), 0f, 1f);
                        float cx1 = ofx + ((mx - ofx) * blend);
                        float cy1 = ofy + ((my - ofy) * blend);
                        float cx2 = otx + ((mx - otx) * blend);
                        float cy2 = oty + ((my - oty) * blend);
                        this.EmitOutlineEdge(ofx, ofy, cx1, cy1, bandTopFixed, bandBottomFixed);
                        this.EmitOutlineEdge(cx1, cy1, cx2, cy2, bandTopFixed, bandBottomFixed);
                        this.EmitOutlineEdge(cx2, cy2, otx, oty, bandTopFixed, bandBottomFixed);
                        miterHandled = true;
                    }
                }
            }

            if (!miterHandled)
            {
                if (lineJoin is LineJoin.Round or LineJoin.MiterRound)
                {
                    float sa = MathF.Atan2(ofy - vy, ofx - vx);
                    float ea = MathF.Atan2(oty - vy, otx - vx);
                    float sweep = ea - sa;
                    if (sweep > MathF.PI)
                    {
                        sweep -= MathF.PI * 2f;
                    }

                    if (sweep < -MathF.PI)
                    {
                        sweep += MathF.PI * 2f;
                    }

                    int steps = Math.Max(4, (int)MathF.Ceiling(MathF.Abs(sweep) * halfWidth * 0.5f));
                    float da = sweep / steps;
                    float pax = ofx;
                    float pay = ofy;
                    for (int s = 1; s <= steps; s++)
                    {
                        float cax, cay;
                        if (s == steps)
                        {
                            cax = otx;
                            cay = oty;
                        }
                        else
                        {
                            float a = sa + (da * s);
                            cax = vx + (MathF.Cos(a) * halfWidth);
                            cay = vy + (MathF.Sin(a) * halfWidth);
                        }

                        this.EmitOutlineEdge(pax, pay, cax, cay, bandTopFixed, bandBottomFixed);
                        pax = cax;
                        pay = cay;
                    }
                }
                else
                {
                    // Bevel.
                    this.EmitOutlineEdge(ofx, ofy, otx, oty, bandTopFixed, bandBottomFixed);
                }
            }
        }

        /// <summary>
        /// Expands a cap descriptor into cap geometry edges (butt, square, or round).
        /// Ported from the GPU StrokeExpandComputeShader cap logic.
        /// </summary>
        private void ExpandCapEdge(
            in StrokeEdgeData edge,
            float halfWidth,
            LineCap lineCap,
            bool isStart,
            int bandTopFixed,
            int bandBottomFixed)
        {
            float cx = edge.X0;
            float cy = edge.Y0;
            float ax = edge.X1;
            float ay = edge.Y1;
            float dx, dy;
            if (isStart)
            {
                dx = ax - cx;
                dy = ay - cy;
            }
            else
            {
                dx = cx - ax;
                dy = cy - ay;
            }

            float len = MathF.Sqrt((dx * dx) + (dy * dy));
            if (len < 1e-6f)
            {
                return;
            }

            float dirX = dx / len;
            float dirY = dy / len;
            float nx = -dirY * halfWidth;
            float ny = dirX * halfWidth;
            float lx = cx + nx;
            float ly = cy + ny;
            float rx = cx - nx;
            float ry = cy - ny;

            if (lineCap == LineCap.Butt)
            {
                if (isStart)
                {
                    this.EmitOutlineEdge(rx, ry, lx, ly, bandTopFixed, bandBottomFixed);
                }
                else
                {
                    this.EmitOutlineEdge(lx, ly, rx, ry, bandTopFixed, bandBottomFixed);
                }
            }
            else if (lineCap == LineCap.Square)
            {
                float ox, oy;
                if (isStart)
                {
                    ox = -dirX * halfWidth;
                    oy = -dirY * halfWidth;
                }
                else
                {
                    ox = dirX * halfWidth;
                    oy = dirY * halfWidth;
                }

                float lxe = lx + ox;
                float lye = ly + oy;
                float rxe = rx + ox;
                float rye = ry + oy;

                if (isStart)
                {
                    this.EmitOutlineEdge(rx, ry, rxe, rye, bandTopFixed, bandBottomFixed);
                    this.EmitOutlineEdge(rxe, rye, lxe, lye, bandTopFixed, bandBottomFixed);
                    this.EmitOutlineEdge(lxe, lye, lx, ly, bandTopFixed, bandBottomFixed);
                }
                else
                {
                    this.EmitOutlineEdge(lx, ly, lxe, lye, bandTopFixed, bandBottomFixed);
                    this.EmitOutlineEdge(lxe, lye, rxe, rye, bandTopFixed, bandBottomFixed);
                    this.EmitOutlineEdge(rxe, rye, rx, ry, bandTopFixed, bandBottomFixed);
                }
            }
            else
            {
                // Round cap.
                float sa, sx, sy, ex, ey;
                if (isStart)
                {
                    sa = MathF.Atan2(ry - cy, rx - cx);
                    sx = rx;
                    sy = ry;
                    ex = lx;
                    ey = ly;
                }
                else
                {
                    sa = MathF.Atan2(ly - cy, lx - cx);
                    sx = lx;
                    sy = ly;
                    ex = rx;
                    ey = ry;
                }

                float sweep = MathF.Atan2(ey - cy, ex - cx) - sa;
                if (sweep > 0f)
                {
                    sweep -= MathF.PI * 2f;
                }

                int steps = Math.Max(4, (int)MathF.Ceiling(MathF.Abs(sweep) * halfWidth * 0.5f));
                float da = sweep / steps;
                float pax = sx;
                float pay = sy;
                for (int s = 1; s <= steps; s++)
                {
                    float cax, cay;
                    if (s == steps)
                    {
                        cax = ex;
                        cay = ey;
                    }
                    else
                    {
                        float a = sa + (da * s);
                        cax = cx + (MathF.Cos(a) * halfWidth);
                        cay = cy + (MathF.Sin(a) * halfWidth);
                    }

                    this.EmitOutlineEdge(pax, pay, cax, cay, bandTopFixed, bandBottomFixed);
                    pax = cax;
                    pay = cay;
                }
            }
        }

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
    /// Per-worker buffers for the standalone fill rasterization API.
    /// </summary>
    /// <remarks>
    /// A single-path rasterization request does not build a flush-wide scene plan, so each worker
    /// owns one reusable line buffer and start-cover buffer that are reused for every band it
    /// processes.
    /// </remarks>
    private sealed class FillWorkerState : IDisposable
    {
        private readonly IMemoryOwner<RasterLineData> lineBufferOwner;
        private readonly IMemoryOwner<int> startCoverOwner;

        /// <summary>
        /// Initializes a new instance of the <see cref="FillWorkerState"/> class.
        /// </summary>
        public FillWorkerState(MemoryAllocator allocator, int segmentCapacity, int width, int bandHeight)
        {
            int wordsPerRow = BitVectorsForMaxBitCount(width);
            int coverStride = checked(width * 2);
            this.Scratch = WorkerScratch.Create(allocator, wordsPerRow, coverStride, width, bandHeight);
            this.lineBufferOwner = allocator.Allocate<RasterLineData>(Math.Max(segmentCapacity, 1));
            this.startCoverOwner = allocator.Allocate<int>(bandHeight);
        }

        /// <summary>
        /// Gets the reusable scanner scratch.
        /// </summary>
        public WorkerScratch Scratch { get; }

        /// <summary>
        /// Gets the reusable line buffer for the current worker.
        /// </summary>
        public Span<RasterLineData> LineBuffer => this.lineBufferOwner.Memory.Span;

        /// <summary>
        /// Gets the reusable start-cover buffer for the current worker.
        /// </summary>
        public Span<int> StartCoverBuffer => this.startCoverOwner.Memory.Span;

        /// <inheritdoc />
        public void Dispose()
        {
            this.startCoverOwner.Dispose();
            this.lineBufferOwner.Dispose();
            this.Scratch.Dispose();
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
        /// Creates a context view over this scratch for the requested band height.
        /// </summary>
        public Context CreateContext(int bandHeight, IntersectionRule intersectionRule, RasterizationMode rasterizationMode, float antialiasThreshold)
            => this.CreateContext(
                this.width,
                this.wordsPerRow,
                this.coverStride,
                bandHeight,
                intersectionRule,
                rasterizationMode,
                antialiasThreshold);

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
