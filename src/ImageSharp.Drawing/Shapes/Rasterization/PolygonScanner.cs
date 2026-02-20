// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

/// <summary>
/// Fixed-point polygon scanner that converts polygon edges into per-row coverage runs.
/// </summary>
/// <remarks>
/// The scanner has two execution modes:
/// 1. Parallel tiled execution (default): build an edge table once, bucket edges by tile rows,
///    rasterize tiles in parallel with worker-local scratch, then emit in deterministic Y order.
/// 2. Sequential execution: reuse the same edge table and process band buckets on one thread.
///
/// Both modes share the same coverage math and fill-rule handling, ensuring predictable output
/// regardless of scheduling strategy.
/// </remarks>
internal static class PolygonScanner
{
    // Upper bound for temporary scanner buffers (bit vectors + cover/area + start-cover rows).
    // Keeping this bounded prevents pathological full-image allocations on very large interests.
    private const long BandMemoryBudgetBytes = 64L * 1024L * 1024L;

    // Blaze-style tile height used by the parallel row-tiling pipeline.
    private const int DefaultTileHeight = 16;

    // Cap for buffered output coverage in the parallel path. We buffer one float per destination
    // pixel plus one dirty-row byte per tile row before deterministic ordered emission.
    private const long ParallelOutputPixelBudget = 16L * 1024L * 1024L; // 4096 x 4096

    private const int FixedShift = 8;
    private const int FixedOne = 1 << FixedShift;
    private static readonly int WordBitCount = nint.Size * 8;
    private const int AreaToCoverageShift = 9;
    private const int CoverageStepCount = 256;
    private const int EvenOddMask = (CoverageStepCount * 2) - 1;
    private const int EvenOddPeriod = CoverageStepCount * 2;
    private const float CoverageScale = 1F / CoverageStepCount;

    /// <summary>
    /// Rasterizes the path using the default execution policy.
    /// </summary>
    /// <typeparam name="TState">The caller-owned mutable state type.</typeparam>
    /// <param name="path">Path to rasterize.</param>
    /// <param name="options">Rasterization options.</param>
    /// <param name="allocator">Temporary buffer allocator.</param>
    /// <param name="state">Caller-owned mutable state.</param>
    /// <param name="scanlineHandler">Scanline callback invoked in ascending Y order.</param>
    public static void Rasterize<TState>(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct
        => RasterizeCore(path, options, allocator, ref state, scanlineHandler, allowParallel: true);

    /// <summary>
    /// Rasterizes the path using the forced sequential policy.
    /// </summary>
    /// <typeparam name="TState">The caller-owned mutable state type.</typeparam>
    /// <param name="path">Path to rasterize.</param>
    /// <param name="options">Rasterization options.</param>
    /// <param name="allocator">Temporary buffer allocator.</param>
    /// <param name="state">Caller-owned mutable state.</param>
    /// <param name="scanlineHandler">Scanline callback invoked in ascending Y order.</param>
    public static void RasterizeSequential<TState>(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct
        => RasterizeCore(path, options, allocator, ref state, scanlineHandler, allowParallel: false);

    /// <summary>
    /// Shared entry point used by both public execution policies.
    /// </summary>
    /// <typeparam name="TState">The caller-owned mutable state type.</typeparam>
    /// <param name="path">Path to rasterize.</param>
    /// <param name="options">Rasterization options.</param>
    /// <param name="allocator">Temporary buffer allocator.</param>
    /// <param name="state">Caller-owned mutable state.</param>
    /// <param name="scanlineHandler">Scanline callback invoked in ascending Y order.</param>
    /// <param name="allowParallel">
    /// If <see langword="true"/>, the scanner may use parallel tiled execution when profitable.
    /// </param>
    private static void RasterizeCore<TState>(
        IPath path,
        in RasterizerOptions options,
        MemoryAllocator allocator,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler,
        bool allowParallel)
        where TState : struct
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
        if (coverStride > int.MaxValue ||
            !TryGetBandHeight(width, height, wordsPerRow, coverStride, out maxBandRows))
        {
            ThrowInterestBoundsTooLarge();
        }

        int coverStrideInt = (int)coverStride;
        bool samplePixelCenter = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter;
        float samplingOffsetX = samplePixelCenter ? 0.5F : 0F;
        float samplingOffsetY = samplePixelCenter ? 0.5F : 0F;

        // Create tessellated rings once. Both sequential and parallel paths consume this single
        // canonical representation so path flattening/orientation work is never repeated.
        using TessellatedMultipolygon multipolygon = TessellatedMultipolygon.Create(path, allocator);
        using IMemoryOwner<EdgeData> edgeDataOwner = allocator.Allocate<EdgeData>(multipolygon.TotalVertexCount);
        int edgeCount = BuildEdgeTable(
            multipolygon,
            interest.Left,
            interest.Top,
            height,
            samplingOffsetX,
            samplingOffsetY,
            edgeDataOwner.Memory.Span);
        if (edgeCount <= 0)
        {
            return;
        }

        if (allowParallel &&
            TryRasterizeParallel(
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
                allocator,
                ref state,
                scanlineHandler))
        {
            return;
        }

        RasterizeSequentialBands(
            edgeDataOwner.Memory.Span[..edgeCount],
            width,
            height,
            interest.Top,
            wordsPerRow,
            coverStrideInt,
            maxBandRows,
            options.IntersectionRule,
            options.RasterizationMode,
            allocator,
            ref state,
            scanlineHandler);
    }

    /// <summary>
    /// Sequential implementation using band buckets over the prebuilt edge table.
    /// </summary>
    /// <typeparam name="TState">The caller-owned mutable state type.</typeparam>
    /// <param name="edges">Prebuilt edges in scanner-local coordinates.</param>
    /// <param name="width">Destination width in pixels.</param>
    /// <param name="height">Destination height in pixels.</param>
    /// <param name="interestTop">Absolute top Y of the interest rectangle.</param>
    /// <param name="wordsPerRow">Bit-vector words per row.</param>
    /// <param name="coverStrideInt">Cover-area stride in ints.</param>
    /// <param name="maxBandRows">Maximum rows per reusable scratch band.</param>
    /// <param name="intersectionRule">Fill rule.</param>
    /// <param name="rasterizationMode">Coverage mode (AA or aliased).</param>
    /// <param name="allocator">Temporary buffer allocator.</param>
    /// <param name="state">Caller-owned mutable state.</param>
    /// <param name="scanlineHandler">Scanline callback invoked in ascending Y order.</param>
    private static void RasterizeSequentialBands<TState>(
        ReadOnlySpan<EdgeData> edges,
        int width,
        int height,
        int interestTop,
        int wordsPerRow,
        int coverStrideInt,
        int maxBandRows,
        IntersectionRule intersectionRule,
        RasterizationMode rasterizationMode,
        MemoryAllocator allocator,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct
    {
        int bandHeight = maxBandRows;
        int bandCount = (height + bandHeight - 1) / bandHeight;
        if (bandCount < 1)
        {
            return;
        }

        using IMemoryOwner<int> bandCountsOwner = allocator.Allocate<int>(bandCount, AllocationOptions.Clean);
        Span<int> bandCounts = bandCountsOwner.Memory.Span;
        long totalBandEdgeReferences = 0;
        for (int i = 0; i < edges.Length; i++)
        {
            // Each edge can overlap multiple bands. We first count references so we can build
            // a compact contiguous index list (CSR-style) without per-band allocations.
            int startBand = edges[i].MinRow / bandHeight;
            int endBand = edges[i].MaxRow / bandHeight;
            totalBandEdgeReferences += (endBand - startBand) + 1;
            if (totalBandEdgeReferences > int.MaxValue)
            {
                ThrowInterestBoundsTooLarge();
            }

            for (int b = startBand; b <= endBand; b++)
            {
                bandCounts[b]++;
            }
        }

        int totalReferences = (int)totalBandEdgeReferences;
        using IMemoryOwner<int> bandOffsetsOwner = allocator.Allocate<int>(bandCount + 1);
        Span<int> bandOffsets = bandOffsetsOwner.Memory.Span;
        int offset = 0;
        for (int b = 0; b < bandCount; b++)
        {
            // Prefix sum: bandOffsets[b] is the start index of band b inside bandEdgeReferences.
            bandOffsets[b] = offset;
            offset += bandCounts[b];
        }

        bandOffsets[bandCount] = offset;
        using IMemoryOwner<int> bandWriteCursorOwner = allocator.Allocate<int>(bandCount);
        Span<int> bandWriteCursor = bandWriteCursorOwner.Memory.Span;
        bandOffsets[..bandCount].CopyTo(bandWriteCursor);

        using IMemoryOwner<int> bandEdgeReferencesOwner = allocator.Allocate<int>(totalReferences);
        Span<int> bandEdgeReferences = bandEdgeReferencesOwner.Memory.Span;
        for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
        {
            // Scatter each edge index to all bands touched by its row range.
            int startBand = edges[edgeIndex].MinRow / bandHeight;
            int endBand = edges[edgeIndex].MaxRow / bandHeight;
            for (int b = startBand; b <= endBand; b++)
            {
                bandEdgeReferences[bandWriteCursor[b]++] = edgeIndex;
            }
        }

        using WorkerScratch scratch = WorkerScratch.Create(allocator, wordsPerRow, coverStrideInt, width, bandHeight);
        for (int bandIndex = 0; bandIndex < bandCount; bandIndex++)
        {
            int bandTop = bandIndex * bandHeight;
            int currentBandHeight = Math.Min(bandHeight, height - bandTop);
            int start = bandOffsets[bandIndex];
            int length = bandOffsets[bandIndex + 1] - start;
            if (length == 0)
            {
                // No edge crosses this band, so there is nothing to rasterize or clear.
                continue;
            }

            Context context = scratch.CreateContext(currentBandHeight, intersectionRule, rasterizationMode);
            ReadOnlySpan<int> bandEdges = bandEdgeReferences.Slice(start, length);
            context.RasterizeEdgeTable(edges, bandEdges, bandTop);
            context.EmitScanlines(interestTop + bandTop, scratch.Scanline, ref state, scanlineHandler);
            context.ResetTouchedRows();
        }
    }

    /// <summary>
    /// Attempts to execute the tiled parallel scanner.
    /// </summary>
    /// <typeparam name="TState">The caller-owned mutable state type.</typeparam>
    /// <param name="edgeMemory">Memory block containing prebuilt edges.</param>
    /// <param name="edgeCount">Number of valid edges in <paramref name="edgeMemory"/>.</param>
    /// <param name="width">Destination width in pixels.</param>
    /// <param name="height">Destination height in pixels.</param>
    /// <param name="interestTop">Absolute top Y of the interest rectangle.</param>
    /// <param name="wordsPerRow">Bit-vector words per row.</param>
    /// <param name="coverStride">Cover-area stride in ints.</param>
    /// <param name="maxBandRows">Maximum rows per worker scratch context.</param>
    /// <param name="intersectionRule">Fill rule.</param>
    /// <param name="rasterizationMode">Coverage mode (AA or aliased).</param>
    /// <param name="allocator">Temporary buffer allocator.</param>
    /// <param name="state">Caller-owned mutable state.</param>
    /// <param name="scanlineHandler">Scanline callback invoked in ascending Y order.</param>
    /// <returns>
    /// <see langword="true"/> when the tiled path executed successfully;
    /// <see langword="false"/> when the caller should run sequential fallback.
    /// </returns>
    private static bool TryRasterizeParallel<TState>(
        Memory<EdgeData> edgeMemory,
        int edgeCount,
        int width,
        int height,
        int interestTop,
        int wordsPerRow,
        int coverStride,
        int maxBandRows,
        IntersectionRule intersectionRule,
        RasterizationMode rasterizationMode,
        MemoryAllocator allocator,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct
    {
        int tileHeight = Math.Min(DefaultTileHeight, maxBandRows);
        if (tileHeight < 1)
        {
            return false;
        }

        int tileCount = (height + tileHeight - 1) / tileHeight;
        if (tileCount == 1)
        {
            // Tiny workload fast path: avoid bucket construction, worker scheduling, and
            // tile-output buffering when everything fits in a single tile.
            RasterizeSingleTileDirect(
                edgeMemory.Span[..edgeCount],
                width,
                height,
                interestTop,
                wordsPerRow,
                coverStride,
                intersectionRule,
                rasterizationMode,
                allocator,
                ref state,
                scanlineHandler);
            return true;
        }

        if (Environment.ProcessorCount < 2)
        {
            return false;
        }

        long totalPixels = (long)width * height;
        if (totalPixels > ParallelOutputPixelBudget)
        {
            // Parallel mode buffers tile coverage before ordered emission. Skip when the
            // buffered output footprint would exceed our safety budget.
            return false;
        }

        using IMemoryOwner<int> tileCountsOwner = allocator.Allocate<int>(tileCount, AllocationOptions.Clean);
        Span<int> tileCounts = tileCountsOwner.Memory.Span;

        long totalTileEdgeReferences = 0;
        Span<EdgeData> edgeBuffer = edgeMemory.Span;
        for (int i = 0; i < edgeCount; i++)
        {
            // Same CSR construction as sequential mode, now keyed by tile instead of band.
            int startTile = edgeBuffer[i].MinRow / tileHeight;
            int endTile = edgeBuffer[i].MaxRow / tileHeight;
            int tileSpan = (endTile - startTile) + 1;
            totalTileEdgeReferences += tileSpan;

            if (totalTileEdgeReferences > int.MaxValue)
            {
                return false;
            }

            for (int t = startTile; t <= endTile; t++)
            {
                tileCounts[t]++;
            }
        }

        int totalReferences = (int)totalTileEdgeReferences;
        using IMemoryOwner<int> tileOffsetsOwner = allocator.Allocate<int>(tileCount + 1);
        Memory<int> tileOffsetsMemory = tileOffsetsOwner.Memory;
        Span<int> tileOffsets = tileOffsetsMemory.Span;

        int offset = 0;
        for (int t = 0; t < tileCount; t++)
        {
            // Prefix sum over tile counts so each tile gets one contiguous slice.
            tileOffsets[t] = offset;
            offset += tileCounts[t];
        }

        tileOffsets[tileCount] = offset;
        using IMemoryOwner<int> tileWriteCursorOwner = allocator.Allocate<int>(tileCount);
        Span<int> tileWriteCursor = tileWriteCursorOwner.Memory.Span;
        tileOffsets[..tileCount].CopyTo(tileWriteCursor);

        using IMemoryOwner<int> tileEdgeReferencesOwner = allocator.Allocate<int>(totalReferences);
        Memory<int> tileEdgeReferencesMemory = tileEdgeReferencesOwner.Memory;
        Span<int> tileEdgeReferences = tileEdgeReferencesMemory.Span;

        for (int edgeIndex = 0; edgeIndex < edgeCount; edgeIndex++)
        {
            int startTile = edgeBuffer[edgeIndex].MinRow / tileHeight;
            int endTile = edgeBuffer[edgeIndex].MaxRow / tileHeight;
            for (int t = startTile; t <= endTile; t++)
            {
                // Scatter edge indices into each tile's contiguous bucket.
                tileEdgeReferences[tileWriteCursor[t]++] = edgeIndex;
            }
        }

        TileOutput[] tileOutputs = new TileOutput[tileCount];
        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, tileCount)
        };

        try
        {
            _ = Parallel.For(
                0,
                tileCount,
                parallelOptions,
                () => WorkerScratch.Create(allocator, wordsPerRow, coverStride, width, tileHeight),
                (tileIndex, _, scratch) =>
                {
                    ReadOnlySpan<EdgeData> edges = edgeMemory.Span[..edgeCount];
                    Span<int> tileOffsets = tileOffsetsMemory.Span;
                    Span<int> tileEdgeReferences = tileEdgeReferencesMemory.Span;
                    int bandTop = tileIndex * tileHeight;
                    int bandHeight = Math.Min(tileHeight, height - bandTop);
                    int start = tileOffsets[tileIndex];
                    int length = tileOffsets[tileIndex + 1] - start;
                    ReadOnlySpan<int> tileEdges = tileEdgeReferences.Slice(start, length);

                    // Each tile rasterizes fully independently into worker-local scratch.
                    RasterizeTile(
                        scratch,
                        edges,
                        tileEdges,
                        bandTop,
                        bandHeight,
                        width,
                        intersectionRule,
                        rasterizationMode,
                        allocator,
                        tileOutputs,
                        tileIndex);

                    return scratch;
                },
                static scratch => scratch.Dispose());

            EmitTileOutputs(tileOutputs, width, interestTop, ref state, scanlineHandler);
            return true;
        }
        finally
        {
            foreach (TileOutput output in tileOutputs)
            {
                output?.Dispose();
            }
        }
    }

    /// <summary>
    /// Rasterizes a single tile directly into the caller callback.
    /// </summary>
    /// <remarks>
    /// This avoids parallel setup and tile-output buffering for tiny workloads while preserving
    /// the same scan-conversion math and callback ordering as the general tiled path.
    /// </remarks>
    /// <typeparam name="TState">The caller-owned mutable state type.</typeparam>
    /// <param name="edges">Prebuilt edge table.</param>
    /// <param name="width">Destination width in pixels.</param>
    /// <param name="height">Destination height in pixels.</param>
    /// <param name="interestTop">Absolute top Y of the interest rectangle.</param>
    /// <param name="wordsPerRow">Bit-vector words per row.</param>
    /// <param name="coverStride">Cover-area stride in ints.</param>
    /// <param name="intersectionRule">Fill rule.</param>
    /// <param name="rasterizationMode">Coverage mode (AA or aliased).</param>
    /// <param name="allocator">Temporary buffer allocator.</param>
    /// <param name="state">Caller-owned mutable state.</param>
    /// <param name="scanlineHandler">Scanline callback invoked in ascending Y order.</param>
    private static void RasterizeSingleTileDirect<TState>(
        ReadOnlySpan<EdgeData> edges,
        int width,
        int height,
        int interestTop,
        int wordsPerRow,
        int coverStride,
        IntersectionRule intersectionRule,
        RasterizationMode rasterizationMode,
        MemoryAllocator allocator,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct
    {
        using WorkerScratch scratch = WorkerScratch.Create(allocator, wordsPerRow, coverStride, width, height);
        Context context = scratch.CreateContext(height, intersectionRule, rasterizationMode);
        context.RasterizeEdgeTable(edges, bandTop: 0);
        context.EmitScanlines(interestTop, scratch.Scanline, ref state, scanlineHandler);
        context.ResetTouchedRows();
    }

    /// <summary>
    /// Rasterizes one tile/band edge subset into temporary coverage buffers.
    /// </summary>
    /// <param name="scratch">Worker-local scratch buffers.</param>
    /// <param name="edges">Shared edge table.</param>
    /// <param name="tileEdgeIndices">Indices of edges intersecting this tile.</param>
    /// <param name="bandTop">Tile top row in scanner-local coordinates.</param>
    /// <param name="bandHeight">Tile height in rows.</param>
    /// <param name="width">Destination width in pixels.</param>
    /// <param name="intersectionRule">Fill rule.</param>
    /// <param name="rasterizationMode">Coverage mode (AA or aliased).</param>
    /// <param name="allocator">Temporary buffer allocator.</param>
    /// <param name="outputs">Output slot array indexed by tile ID.</param>
    /// <param name="tileIndex">Current tile index.</param>
    private static void RasterizeTile(
        WorkerScratch scratch,
        ReadOnlySpan<EdgeData> edges,
        ReadOnlySpan<int> tileEdgeIndices,
        int bandTop,
        int bandHeight,
        int width,
        IntersectionRule intersectionRule,
        RasterizationMode rasterizationMode,
        MemoryAllocator allocator,
        TileOutput[] outputs,
        int tileIndex)
    {
        if (tileEdgeIndices.Length == 0)
        {
            return;
        }

        Context context = scratch.CreateContext(bandHeight, intersectionRule, rasterizationMode);
        context.RasterizeEdgeTable(edges, tileEdgeIndices, bandTop);

        int coverageLength = checked(width * bandHeight);
        IMemoryOwner<float> coverageOwner = allocator.Allocate<float>(coverageLength, AllocationOptions.Clean);
        IMemoryOwner<byte> dirtyRowsOwner = allocator.Allocate<byte>(bandHeight, AllocationOptions.Clean);
        bool committed = false;

        try
        {
            TileCaptureState captureState = new(width, coverageOwner.Memory, dirtyRowsOwner.Memory);

            // Emit with destinationTop=0 into tile-local storage; global Y is restored later.
            context.EmitScanlines(0, scratch.Scanline, ref captureState, CaptureTileScanline);
            outputs[tileIndex] = new TileOutput(bandTop, bandHeight, coverageOwner, dirtyRowsOwner);
            committed = true;
        }
        finally
        {
            context.ResetTouchedRows();

            if (!committed)
            {
                coverageOwner.Dispose();
                dirtyRowsOwner.Dispose();
            }
        }
    }

    /// <summary>
    /// Emits buffered tile outputs in deterministic top-to-bottom order.
    /// </summary>
    /// <typeparam name="TState">The caller-owned mutable state type.</typeparam>
    /// <param name="outputs">Tile outputs captured by workers.</param>
    /// <param name="width">Destination width in pixels.</param>
    /// <param name="destinationTop">Absolute top Y of the interest rectangle.</param>
    /// <param name="state">Caller-owned mutable state.</param>
    /// <param name="scanlineHandler">Scanline callback invoked in ascending Y order.</param>
    private static void EmitTileOutputs<TState>(
        TileOutput[] outputs,
        int width,
        int destinationTop,
        ref TState state,
        RasterizerScanlineHandler<TState> scanlineHandler)
        where TState : struct
    {
        foreach (TileOutput output in outputs)
        {
            if (output is null)
            {
                continue;
            }

            Span<float> coverage = output.CoverageOwner.Memory.Span;
            Span<byte> dirtyRows = output.DirtyRowsOwner.Memory.Span;
            for (int row = 0; row < output.Height; row++)
            {
                if (dirtyRows[row] == 0)
                {
                    // Rows are sparse; untouched rows were never emitted by the tile worker.
                    continue;
                }

                // Stable top-to-bottom emission keeps observable callback order deterministic.
                Span<float> scanline = coverage.Slice(row * width, width);
                scanlineHandler(destinationTop + output.Top + row, scanline, ref state);
            }
        }
    }

    /// <summary>
    /// Captures one emitted scanline into a tile-local output buffer.
    /// </summary>
    /// <param name="y">Row index relative to tile-local coordinates.</param>
    /// <param name="scanline">Coverage scanline.</param>
    /// <param name="state">Tile capture state.</param>
    private static void CaptureTileScanline(int y, Span<float> scanline, ref TileCaptureState state)
    {
        // y is tile-local (destinationTop was 0 in RasterizeTile).
        int row = y - state.Top;
        scanline.CopyTo(state.Coverage.Span.Slice(row * state.Width, state.Width));
        state.DirtyRows.Span[row] = 1;
    }

    /// <summary>
    /// Builds an edge table in scanner-local coordinates.
    /// </summary>
    /// <param name="multipolygon">Input tessellated rings.</param>
    /// <param name="minX">Interest left in absolute coordinates.</param>
    /// <param name="minY">Interest top in absolute coordinates.</param>
    /// <param name="height">Interest height in pixels.</param>
    /// <param name="samplingOffsetX">Horizontal sampling offset.</param>
    /// <param name="samplingOffsetY">Vertical sampling offset.</param>
    /// <param name="destination">Destination span for edge records.</param>
    /// <returns>Number of valid edge records written.</returns>
    private static int BuildEdgeTable(
        TessellatedMultipolygon multipolygon,
        int minX,
        int minY,
        int height,
        float samplingOffsetX,
        float samplingOffsetY,
        Span<EdgeData> destination)
    {
        int count = 0;
        foreach (TessellatedMultipolygon.Ring ring in multipolygon)
        {
            ReadOnlySpan<PointF> vertices = ring.Vertices;
            for (int i = 0; i < ring.VertexCount; i++)
            {
                PointF p0 = vertices[i];
                PointF p1 = vertices[i + 1];

                float x0 = (p0.X - minX) + samplingOffsetX;
                float y0 = (p0.Y - minY) + samplingOffsetY;
                float x1 = (p1.X - minX) + samplingOffsetX;
                float y1 = (p1.Y - minY) + samplingOffsetY;

                if (!float.IsFinite(x0) || !float.IsFinite(y0) || !float.IsFinite(x1) || !float.IsFinite(y1))
                {
                    continue;
                }

                if (!ClipToVerticalBounds(ref x0, ref y0, ref x1, ref y1, 0F, height))
                {
                    continue;
                }

                int fx0 = FloatToFixed24Dot8(x0);
                int fy0 = FloatToFixed24Dot8(y0);
                int fx1 = FloatToFixed24Dot8(x1);
                int fy1 = FloatToFixed24Dot8(y1);
                if (fy0 == fy1)
                {
                    continue;
                }

                ComputeEdgeRowBounds(fy0, fy1, out int minRow, out int maxRow);
                destination[count++] = new EdgeData(fx0, fy0, fx1, fy1, minRow, maxRow);
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
    /// Computes the inclusive row range affected by a clipped non-horizontal edge.
    /// </summary>
    /// <param name="y0">Edge start Y in 24.8 fixed-point.</param>
    /// <param name="y1">Edge end Y in 24.8 fixed-point.</param>
    /// <param name="minRow">First affected integer scan row.</param>
    /// <param name="maxRow">Last affected integer scan row.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ComputeEdgeRowBounds(int y0, int y1, out int minRow, out int maxRow)
    {
        int y0Row = y0 >> FixedShift;
        int y1Row = y1 >> FixedShift;

        // First touched row is floor(min(y0, y1)).
        minRow = y0Row < y1Row ? y0Row : y1Row;

        int y0Fraction = y0 & (FixedOne - 1);
        int y1Fraction = y1 & (FixedOne - 1);

        // Last touched row is ceil(max(y)) - 1:
        // - when fractional part is non-zero, row is unchanged;
        // - when exactly on a row boundary, subtract 1 (edge ownership rule).
        int y0Candidate = y0Row - (((y0Fraction - 1) >> 31) & 1);
        int y1Candidate = y1Row - (((y1Fraction - 1) >> 31) & 1);
        maxRow = y0Candidate > y1Candidate ? y0Candidate : y1Candidate;
    }

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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInterestBoundsTooLarge()
        => throw new ImageProcessingException("The rasterizer interest bounds are too large for PolygonScanner buffers.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowBandHeightExceedsScratchCapacity()
        => throw new ImageProcessingException("Requested band height exceeds worker scratch capacity.");

    /// <summary>
    /// Band/tile-local scanner context that owns mutable coverage accumulation state.
    /// </summary>
    /// <remarks>
    /// Instances are intentionally stack-bound to keep hot-path data in spans and avoid heap churn.
    /// </remarks>
    private ref struct Context
    {
        private readonly Span<nuint> bitVectors;
        private readonly Span<int> coverArea;
        private readonly Span<int> startCover;
        private readonly Span<byte> rowHasBits;
        private readonly Span<byte> rowTouched;
        private readonly Span<int> touchedRows;
        private readonly int width;
        private readonly int height;
        private readonly int wordsPerRow;
        private readonly int coverStride;
        private readonly IntersectionRule intersectionRule;
        private readonly RasterizationMode rasterizationMode;
        private int touchedRowCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="Context"/> struct.
        /// </summary>
        public Context(
            Span<nuint> bitVectors,
            Span<int> coverArea,
            Span<int> startCover,
            Span<byte> rowHasBits,
            Span<byte> rowTouched,
            Span<int> touchedRows,
            int width,
            int height,
            int wordsPerRow,
            int coverStride,
            IntersectionRule intersectionRule,
            RasterizationMode rasterizationMode)
        {
            this.bitVectors = bitVectors;
            this.coverArea = coverArea;
            this.startCover = startCover;
            this.rowHasBits = rowHasBits;
            this.rowTouched = rowTouched;
            this.touchedRows = touchedRows;
            this.width = width;
            this.height = height;
            this.wordsPerRow = wordsPerRow;
            this.coverStride = coverStride;
            this.intersectionRule = intersectionRule;
            this.rasterizationMode = rasterizationMode;
            this.touchedRowCount = 0;
        }

        /// <summary>
        /// Rasterizes all edges in a tessellated multipolygon directly into this context.
        /// </summary>
        /// <param name="multipolygon">Input tessellated rings.</param>
        /// <param name="minX">Absolute left coordinate of the current scanner window.</param>
        /// <param name="minY">Absolute top coordinate of the current scanner window.</param>
        /// <param name="samplingOffsetX">Horizontal sample origin offset.</param>
        /// <param name="samplingOffsetY">Vertical sample origin offset.</param>
        public void RasterizeMultipolygon(
            TessellatedMultipolygon multipolygon,
            int minX,
            int minY,
            float samplingOffsetX,
            float samplingOffsetY)
        {
            foreach (TessellatedMultipolygon.Ring ring in multipolygon)
            {
                ReadOnlySpan<PointF> vertices = ring.Vertices;
                for (int i = 0; i < ring.VertexCount; i++)
                {
                    PointF p0 = vertices[i];
                    PointF p1 = vertices[i + 1];

                    float x0 = (p0.X - minX) + samplingOffsetX;
                    float y0 = (p0.Y - minY) + samplingOffsetY;
                    float x1 = (p1.X - minX) + samplingOffsetX;
                    float y1 = (p1.Y - minY) + samplingOffsetY;

                    if (!float.IsFinite(x0) || !float.IsFinite(y0) || !float.IsFinite(x1) || !float.IsFinite(y1))
                    {
                        continue;
                    }

                    if (!ClipToVerticalBounds(ref x0, ref y0, ref x1, ref y1, 0F, this.height))
                    {
                        continue;
                    }

                    int fx0 = FloatToFixed24Dot8(x0);
                    int fy0 = FloatToFixed24Dot8(y0);
                    int fx1 = FloatToFixed24Dot8(x1);
                    int fy1 = FloatToFixed24Dot8(y1);
                    if (fy0 == fy1)
                    {
                        continue;
                    }

                    this.RasterizeLine(fx0, fy0, fx1, fy1);
                }
            }
        }

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
        /// Rasterizes a subset of prebuilt edges that intersect this context's vertical range.
        /// </summary>
        /// <param name="edges">Shared edge table.</param>
        /// <param name="edgeIndices">Indices into <paramref name="edges"/> for this band/tile.</param>
        /// <param name="bandTop">Top row of this context in global scanner-local coordinates.</param>
        public void RasterizeEdgeTable(ReadOnlySpan<EdgeData> edges, ReadOnlySpan<int> edgeIndices, int bandTop)
        {
            int bandTopFixed = bandTop * FixedOne;
            int bandBottomFixed = bandTopFixed + (this.height * FixedOne);

            for (int i = 0; i < edgeIndices.Length; i++)
            {
                EdgeData edge = edges[edgeIndices[i]];
                int x0 = edge.X0;
                int y0 = edge.Y0;
                int x1 = edge.X1;
                int y1 = edge.Y1;

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
        /// Converts accumulated cover/area tables into scanline coverage callbacks.
        /// </summary>
        /// <typeparam name="TState">The caller-owned mutable state type.</typeparam>
        /// <param name="destinationTop">Absolute destination Y corresponding to row zero in this context.</param>
        /// <param name="scanline">Reusable scanline scratch buffer.</param>
        /// <param name="state">Caller-owned mutable state.</param>
        /// <param name="scanlineHandler">Scanline callback invoked in ascending Y order.</param>
        public readonly void EmitScanlines<TState>(int destinationTop, Span<float> scanline, ref TState state, RasterizerScanlineHandler<TState> scanlineHandler)
            where TState : struct
        {
            for (int row = 0; row < this.height; row++)
            {
                int rowCover = this.startCover[row];
                if (rowCover == 0 && this.rowHasBits[row] == 0)
                {
                    // Nothing contributed to this row.
                    continue;
                }

                Span<nuint> rowBitVectors = this.bitVectors.Slice(row * this.wordsPerRow, this.wordsPerRow);
                scanline.Clear();
                bool scanlineDirty = this.EmitRowCoverage(rowBitVectors, row, rowCover, scanline);
                if (scanlineDirty)
                {
                    scanlineHandler(destinationTop + row, scanline, ref state);
                }
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
                this.bitVectors.Slice(row * this.wordsPerRow, this.wordsPerRow).Clear();
            }

            this.touchedRowCount = 0;
        }

        /// <summary>
        /// Emits one row by iterating touched columns and coalescing equal-coverage spans.
        /// </summary>
        /// <param name="rowBitVectors">Bitset words indicating touched columns in this row.</param>
        /// <param name="row">Row index inside the context.</param>
        /// <param name="cover">Initial carry cover value from x less than zero contributions.</param>
        /// <param name="scanline">Destination scanline coverage buffer.</param>
        /// <returns><see langword="true"/> when at least one non-zero span was emitted.</returns>
        private readonly bool EmitRowCoverage(ReadOnlySpan<nuint> rowBitVectors, int row, int cover, Span<float> scanline)
        {
            int rowOffset = row * this.coverStride;
            int spanStart = 0;
            int spanEnd = 0;
            float spanCoverage = 0F;
            bool hasCoverage = false;

            for (int wordIndex = 0; wordIndex < rowBitVectors.Length; wordIndex++)
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
                            hasCoverage |= FlushSpan(scanline, spanStart, spanEnd, spanCoverage);
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
                            hasCoverage |= FlushSpan(scanline, spanStart, spanEnd, spanCoverage);
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
                            hasCoverage |= FlushSpan(scanline, spanStart, spanEnd, spanCoverage);
                            spanStart = x;
                            spanEnd = x + 1;
                            spanCoverage = coverage;
                        }
                        else
                        {
                            float gapCoverage = this.AreaToCoverage(cover << AreaToCoverageShift);
                            if (spanCoverage == gapCoverage)
                            {
                                if (coverage == gapCoverage)
                                {
                                    spanEnd = x + 1;
                                }
                                else
                                {
                                    hasCoverage |= FlushSpan(scanline, spanStart, x, spanCoverage);
                                    spanStart = x;
                                    spanEnd = x + 1;
                                    spanCoverage = coverage;
                                }
                            }
                            else
                            {
                                hasCoverage |= FlushSpan(scanline, spanStart, spanEnd, spanCoverage);
                                hasCoverage |= FlushSpan(scanline, spanEnd, x, gapCoverage);
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
            hasCoverage |= FlushSpan(scanline, spanStart, spanEnd, spanCoverage);
            if (cover != 0 && spanEnd < this.width)
            {
                hasCoverage |= FlushSpan(scanline, spanEnd, this.width, this.AreaToCoverage(cover << AreaToCoverageShift));
            }

            return hasCoverage;
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
                // Aliased mode quantizes final coverage to hard 0/1 per pixel.
                return coverage >= 0.5F ? 1F : 0F;
            }

            return coverage;
        }

        /// <summary>
        /// Writes one coverage span into the scanline buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool FlushSpan(Span<float> scanline, int start, int end, float coverage)
        {
            if (coverage <= 0F || end <= start)
            {
                return false;
            }

            scanline[start..end].Fill(coverage);
            return true;
        }

        /// <summary>
        /// Sets a row/column bit and reports whether it was newly set.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly bool ConditionalSetBit(int row, int column)
        {
            int bitIndex = row * this.wordsPerRow;
            int wordIndex = bitIndex + (column / WordBitCount);
            nuint mask = (nuint)1 << (column % WordBitCount);
            ref nuint word = ref this.bitVectors[wordIndex];
            bool newlySet = (word & mask) == 0;
            word |= mask;

            // Fast row-level early-out for EmitScanlines.
            this.rowHasBits[row] = 1;
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
            if (this.ConditionalSetBit(row, column))
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
    /// Immutable scanner-local edge record with precomputed affected-row bounds.
    /// </summary>
    /// <remarks>
    /// All coordinates are stored as signed 24.8 fixed-point integers for predictable hot-path
    /// access without per-read unpacking.
    /// </remarks>
    private readonly struct EdgeData
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
        /// Gets the first scanner row affected by this edge.
        /// </summary>
        public readonly int MinRow;

        /// <summary>
        /// Gets the last scanner row affected by this edge.
        /// </summary>
        public readonly int MaxRow;

        /// <summary>
        /// Initializes a new instance of the <see cref="EdgeData"/> struct.
        /// </summary>
        public EdgeData(int x0, int y0, int x1, int y1, int minRow, int maxRow)
        {
            this.X0 = x0;
            this.Y0 = y0;
            this.X1 = x1;
            this.Y1 = y1;
            this.MinRow = minRow;
            this.MaxRow = maxRow;
        }
    }

    /// <summary>
    /// Mutable state used while capturing one tile's emitted scanlines.
    /// </summary>
    private readonly struct TileCaptureState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TileCaptureState"/> struct.
        /// </summary>
        public TileCaptureState(int width, Memory<float> coverage, Memory<byte> dirtyRows)
        {
            this.Top = 0;
            this.Width = width;
            this.Coverage = coverage;
            this.DirtyRows = dirtyRows;
        }

        /// <summary>
        /// Gets the row origin of this capture buffer.
        /// </summary>
        public int Top { get; }

        /// <summary>
        /// Gets the scanline width.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets contiguous tile coverage storage.
        /// </summary>
        public Memory<float> Coverage { get; }

        /// <summary>
        /// Gets per-row dirty flags for sparse output emission.
        /// </summary>
        public Memory<byte> DirtyRows { get; }
    }

    /// <summary>
    /// Buffered output produced by one rasterized tile.
    /// </summary>
    private sealed class TileOutput : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TileOutput"/> class.
        /// </summary>
        public TileOutput(int top, int height, IMemoryOwner<float> coverageOwner, IMemoryOwner<byte> dirtyRowsOwner)
        {
            this.Top = top;
            this.Height = height;
            this.CoverageOwner = coverageOwner;
            this.DirtyRowsOwner = dirtyRowsOwner;
        }

        /// <summary>
        /// Gets the tile top row relative to interest origin.
        /// </summary>
        public int Top { get; }

        /// <summary>
        /// Gets the number of rows in this tile.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the tile coverage buffer owner.
        /// </summary>
        public IMemoryOwner<float> CoverageOwner { get; private set; }

        /// <summary>
        /// Gets the tile dirty-row buffer owner.
        /// </summary>
        public IMemoryOwner<byte> DirtyRowsOwner { get; private set; }

        /// <summary>
        /// Releases tile output buffers back to the allocator.
        /// </summary>
        public void Dispose()
        {
            this.CoverageOwner?.Dispose();
            this.DirtyRowsOwner?.Dispose();
            this.CoverageOwner = null!;
            this.DirtyRowsOwner = null!;
        }
    }

    /// <summary>
    /// Reusable per-worker scratch buffers used by tiled and sequential band rasterization.
    /// </summary>
    private sealed class WorkerScratch : IDisposable
    {
        private readonly int wordsPerRow;
        private readonly int coverStride;
        private readonly int width;
        private readonly int tileCapacity;
        private readonly IMemoryOwner<nuint> bitVectorsOwner;
        private readonly IMemoryOwner<int> coverAreaOwner;
        private readonly IMemoryOwner<int> startCoverOwner;
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
        /// Allocates worker-local scratch sized for the configured tile/band capacity.
        /// </summary>
        public static WorkerScratch Create(MemoryAllocator allocator, int wordsPerRow, int coverStride, int width, int tileCapacity)
        {
            int bitVectorCapacity = checked(wordsPerRow * tileCapacity);
            int coverAreaCapacity = checked(coverStride * tileCapacity);
            IMemoryOwner<nuint> bitVectorsOwner = allocator.Allocate<nuint>(bitVectorCapacity, AllocationOptions.Clean);
            IMemoryOwner<int> coverAreaOwner = allocator.Allocate<int>(coverAreaCapacity);
            IMemoryOwner<int> startCoverOwner = allocator.Allocate<int>(tileCapacity, AllocationOptions.Clean);
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
                rowHasBitsOwner,
                rowTouchedOwner,
                touchedRowsOwner,
                scanlineOwner);
        }

        /// <summary>
        /// Creates a context view over this scratch for the requested band height.
        /// </summary>
        public Context CreateContext(int bandHeight, IntersectionRule intersectionRule, RasterizationMode rasterizationMode)
        {
            if ((uint)bandHeight > (uint)this.tileCapacity)
            {
                ThrowBandHeightExceedsScratchCapacity();
            }

            int bitVectorCount = checked(this.wordsPerRow * bandHeight);
            int coverAreaCount = checked(this.coverStride * bandHeight);
            return new Context(
                this.bitVectorsOwner.Memory.Span[..bitVectorCount],
                this.coverAreaOwner.Memory.Span[..coverAreaCount],
                this.startCoverOwner.Memory.Span[..bandHeight],
                this.rowHasBitsOwner.Memory.Span[..bandHeight],
                this.rowTouchedOwner.Memory.Span[..bandHeight],
                this.touchedRowsOwner.Memory.Span[..bandHeight],
                this.width,
                bandHeight,
                this.wordsPerRow,
                this.coverStride,
                intersectionRule,
                rasterizationMode);
        }

        /// <summary>
        /// Releases worker-local scratch buffers back to the allocator.
        /// </summary>
        public void Dispose()
        {
            this.bitVectorsOwner.Dispose();
            this.coverAreaOwner.Dispose();
            this.startCoverOwner.Dispose();
            this.rowHasBitsOwner.Dispose();
            this.rowTouchedOwner.Dispose();
            this.touchedRowsOwner.Dispose();
            this.scanlineOwner.Dispose();
        }
    }
}
