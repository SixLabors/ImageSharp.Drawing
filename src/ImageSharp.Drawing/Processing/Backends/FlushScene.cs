// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Collections.Concurrent;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Flush-scoped CPU scene realized in destination coordinates.
/// </summary>
/// <remarks>
/// <para>
/// One flush is converted into a set of destination-row-local raster items with prepared geometry
/// and row membership lists.
/// </para>
/// <para>
/// The scene owns flush-local scheduling data plus row-local raster payloads that are prebuilt in
/// destination coordinates. Scene-row execution can then rasterize directly from those prepared
/// row items without rebuilding coverage state for every command during composition.
/// </para>
/// </remarks>
internal sealed class FlushScene : IDisposable
{
    // Keep row-level parallelism bounded so small scenes do not overpay in scheduling overhead.
    private const int MaxParallelWorkerCount = 12;

    private readonly CompositionCommand[] commands;
    private readonly int[] interestLefts;
    private readonly IMemoryOwner<DefaultRasterizer.RasterLineData>? lineDataOwner;
    private readonly IMemoryOwner<int>? startCoverOwner;
    private readonly DefaultRasterizer.RasterizableBandInfo[] rasterizableBands;
    private readonly int[] rowOffsets;
    private readonly RowItem[] rowItems;
    private readonly int firstSceneBandTop;
    private readonly int maxWidth;
    private readonly int maxWordsPerRow;
    private readonly int maxCoverStride;
    private readonly int maxBandCapacity;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlushScene"/> class.
    /// </summary>
    private FlushScene(
        CompositionCommand[] commands,
        int[] interestLefts,
        IMemoryOwner<DefaultRasterizer.RasterLineData>? lineDataOwner,
        IMemoryOwner<int>? startCoverOwner,
        DefaultRasterizer.RasterizableBandInfo[] rasterizableBands,
        int[] rowOffsets,
        RowItem[] rowItems,
        int firstSceneBandTop,
        int maxWidth,
        int maxWordsPerRow,
        int maxCoverStride,
        int maxBandCapacity,
        long totalEdgeCount,
        int singleBandItemCount,
        int smallEdgeItemCount)
    {
        this.commands = commands;
        this.interestLefts = interestLefts;
        this.lineDataOwner = lineDataOwner;
        this.startCoverOwner = startCoverOwner;
        this.rasterizableBands = rasterizableBands;
        this.rowOffsets = rowOffsets;
        this.rowItems = rowItems;
        this.firstSceneBandTop = firstSceneBandTop;
        this.maxWidth = maxWidth;
        this.maxWordsPerRow = maxWordsPerRow;
        this.maxCoverStride = maxCoverStride;
        this.maxBandCapacity = maxBandCapacity;
        this.TotalEdgeCount = totalEdgeCount;
        this.SingleBandItemCount = singleBandItemCount;
        this.SmallEdgeItemCount = smallEdgeItemCount;
    }

    /// <summary>
    /// Gets the number of visible raster items in the scene.
    /// </summary>
    public int ItemCount => this.commands.Length;

    /// <summary>
    /// Gets the number of destination scene rows in this flush.
    /// </summary>
    public int RowCount => this.rowOffsets.Length == 0 ? 0 : this.rowOffsets.Length - 1;

    /// <summary>
    /// Gets the total number of row-local raster items in this flush.
    /// </summary>
    public int RowItemCount => this.rowItems.Length;

    /// <summary>
    /// Gets the total prepared edge count across all raster items.
    /// </summary>
    public long TotalEdgeCount { get; }

    /// <summary>
    /// Gets the count of items whose realized interest fits in one raster row band.
    /// </summary>
    public int SingleBandItemCount { get; }

    /// <summary>
    /// Gets the count of items with small prepared geometry.
    /// </summary>
    public int SmallEdgeItemCount { get; }

    /// <summary>
    /// Builds a flush-scoped CPU scene from prepared commands.
    /// </summary>
    public static FlushScene Create(
        IReadOnlyList<CompositionCommand> commands,
        in Rectangle targetBounds,
        MemoryAllocator allocator)
        => Create(commands, 0, commands.Count, targetBounds, allocator);

    /// <summary>
    /// Builds a flush-scoped CPU scene from a contiguous command range.
    /// </summary>
    public static FlushScene Create(
        IReadOnlyList<CompositionCommand> commands,
        int start,
        int length,
        in Rectangle targetBounds,
        MemoryAllocator allocator)
    {
        Rectangle sceneBounds = targetBounds;
        int rowHeight = DefaultRasterizer.PreferredRowHeight;
        int commandCount = length;
        if (commandCount == 0)
        {
            return Empty();
        }

        // Phase 1: materialize paths and compute band layout for visible commands.
        // Visibility clipping was already done during Prepare(), so we just skip non-visible commands.
        // All temporary buffers are pooled via the allocator to avoid GC pressure on large command counts.
        IMemoryOwner<int> interestLeftBufferOwner = allocator.Allocate<int>(commandCount);
        IMemoryOwner<int> firstBandIndexBufferOwner = allocator.Allocate<int>(commandCount);
        IMemoryOwner<int> bandCountBufferOwner = allocator.Allocate<int>(commandCount);
        IMemoryOwner<byte> visibleItemFlagsOwner = allocator.Allocate<byte>(commandCount, AllocationOptions.Clean);
        MaterializedPath[] pathBuffer = new MaterializedPath[commandCount];
        RasterizerOptions[] rasterizerOptionsBuffer = new RasterizerOptions[commandCount];

        Memory<int> interestLeftBuffer = interestLeftBufferOwner.Memory;
        Memory<int> firstBandIndexBuffer = firstBandIndexBufferOwner.Memory;
        Memory<int> bandCountBuffer = bandCountBufferOwner.Memory;
        Memory<byte> visibleItemFlags = visibleItemFlagsOwner.Memory;

        _ = Parallel.ForEach(Partitioner.Create(0, commandCount), range =>
        {
            Span<int> ilSpan = interestLeftBuffer.Span;
            Span<int> fbSpan = firstBandIndexBuffer.Span;
            Span<int> bcSpan = bandCountBuffer.Span;
            Span<byte> vfSpan = visibleItemFlags.Span;

            for (int i = range.Item1; i < range.Item2; i++)
            {
                CompositionCommand command = commands[start + i];
                if (command.Kind is not CompositionCommandKind.FillLayer || !command.IsVisible)
                {
                    continue;
                }

                IPath preparedPath = command.PreparedPath!;

                MaterializedPath materializedPath = MaterializedPath.Create(preparedPath);
                if (materializedPath.TotalSegmentCount == 0)
                {
                    continue;
                }

                Rectangle destinationInterest = new(
                    command.TargetBounds.X + command.DestinationRegion.X,
                    command.TargetBounds.Y + command.DestinationRegion.Y,
                    command.DestinationRegion.Width,
                    command.DestinationRegion.Height);

                RasterizerOptions sourceOptions = command.RasterizerOptions;
                RasterizerOptions itemOptions = new(
                    destinationInterest,
                    sourceOptions.IntersectionRule,
                    sourceOptions.RasterizationMode,
                    sourceOptions.SamplingOrigin,
                    sourceOptions.AntialiasThreshold);

                int firstBandIndex = FloorDiv(destinationInterest.Top, rowHeight);
                int lastBandIndex = FloorDiv(destinationInterest.Bottom - 1, rowHeight);
                int bandCount = (lastBandIndex - firstBandIndex) + 1;

                if (bandCount <= 0)
                {
                    continue;
                }

                pathBuffer[i] = materializedPath;
                rasterizerOptionsBuffer[i] = itemOptions;
                ilSpan[i] = destinationInterest.Left;
                fbSpan[i] = firstBandIndex;
                bcSpan[i] = bandCount;
                vfSpan[i] = 1;
            }
        });

        int visibleItemCount = 0;

        // TODO: SIMD sum over the byte span.
        Span<byte> flagSpan = visibleItemFlags.Span;

        for (int i = 0; i < flagSpan.Length; i++)
        {
            visibleItemCount += flagSpan[i];
        }

        if (visibleItemCount == 0)
        {
            return Empty();
        }

        // Phase 2: compact in-place — move visible entries to the front of the Phase 1 arrays.
        // This avoids a second allocation for MaterializedPath[] and RasterizerOptions[].
        CompositionCommand[] compactedCommands = new CompositionCommand[visibleItemCount];
        int[] interestLefts = new int[visibleItemCount];
        IMemoryOwner<int> itemFirstBandIndicesOwner = allocator.Allocate<int>(visibleItemCount);
        IMemoryOwner<int> itemBandCountsOwner = allocator.Allocate<int>(visibleItemCount);
        Memory<int> itemFirstBandIndices = itemFirstBandIndicesOwner.Memory;
        Memory<int> itemBandCounts = itemBandCountsOwner.Memory;

        int writeIndex = 0;
        {
            Span<MaterializedPath> mpSpan = pathBuffer.AsSpan();
            Span<RasterizerOptions> roSpan = rasterizerOptionsBuffer.AsSpan();
            Span<int> ilSpan = interestLeftBuffer.Span;
            Span<int> fbSrc = firstBandIndexBuffer.Span;
            Span<int> bcSrc = bandCountBuffer.Span;
            Span<int> fbDst = itemFirstBandIndices.Span;
            Span<int> bcDst = itemBandCounts.Span;

            for (int i = 0; i < commandCount; i++)
            {
                if (flagSpan[i] == 0)
                {
                    continue;
                }

                compactedCommands[writeIndex] = commands[start + i];
                mpSpan[writeIndex] = mpSpan[i];
                roSpan[writeIndex] = roSpan[i];
                interestLefts[writeIndex] = ilSpan[i];
                fbDst[writeIndex] = fbSrc[i];
                bcDst[writeIndex] = bcSrc[i];
                writeIndex++;
            }
        }

        // Phase 1 buffers for int/byte are no longer needed — release them now.
        bandCountBufferOwner.Dispose();
        firstBandIndexBufferOwner.Dispose();
        interestLeftBufferOwner.Dispose();
        visibleItemFlagsOwner.Dispose();

        int minBandIndex = int.MaxValue;
        int maxBandIndex = int.MinValue;
        int maxWidth = 0;
        int maxWordsPerRow = 0;
        int maxCoverStride = 0;
        int maxBandCapacity = rowHeight;
        long totalEdgeCount = 0;
        int singleBandItemCount = 0;
        int smallEdgeItemCount = 0;

        // Segment start offsets into the band assignment cache — one entry per visible item.
        // Used in Phases 4 and 5 to slice each item's portion of the cache.
        int[] itemSegmentStarts = new int[visibleItemCount];

        // Phase 3: derive scene-wide maxima once so every worker can allocate one reusable scratch set.
        {
            Span<RasterizerOptions> roSpan = rasterizerOptionsBuffer.AsSpan();
            Span<int> fbSpan = itemFirstBandIndices.Span;
            Span<int> bcSpan = itemBandCounts.Span;
            Span<MaterializedPath> mpSpan = pathBuffer.AsSpan();

            for (int i = 0; i < visibleItemCount; i++)
            {
                RasterizerOptions itemOptions = roSpan[i];
                Rectangle interest = itemOptions.Interest;
                int width = interest.Width;
                long coverStride = (long)width * 2;

                if (coverStride > int.MaxValue)
                {
                    throw new InvalidOperationException("Interest bounds exceed rasterizer limits.");
                }

                minBandIndex = Math.Min(minBandIndex, fbSpan[i]);
                maxBandIndex = Math.Max(maxBandIndex, fbSpan[i] + bcSpan[i] - 1);
                maxWidth = Math.Max(maxWidth, width);
                maxWordsPerRow = Math.Max(maxWordsPerRow, BitVectorsForMaxBitCount(width));
                maxCoverStride = Math.Max(maxCoverStride, (int)coverStride);
                itemSegmentStarts[i] = (int)totalEdgeCount;
                totalEdgeCount += mpSpan[i].TotalSegmentCount;

                if (bcSpan[i] == 1)
                {
                    singleBandItemCount++;
                }

                if (mpSpan[i].TotalSegmentCount <= 64)
                {
                    smallEdgeItemCount++;
                }
            }
        }

        // Band assignment cache: stores the packed (firstBand | lastBand << 16) for each segment,
        // or -1 if the segment falls outside the item's interest rectangle.
        // Built during Phase 4 so Phase 5 can scatter from integers instead of re-enumerating path geometry.
        IMemoryOwner<int> bandAssignmentCacheOwner = allocator.Allocate<int>(Math.Max((int)totalEdgeCount, 1));
        Memory<int> bandAssignmentCache = bandAssignmentCacheOwner.Memory;

        IMemoryOwner<int> itemBandOffsetStartsOwner = allocator.Allocate<int>(visibleItemCount + 1);
        int totalBandOffsetCount = 0;
        {
            Span<int> offsetSpan = itemBandOffsetStartsOwner.Memory.Span;
            Span<int> bcSpan = itemBandCounts.Span;
            for (int i = 0; i < visibleItemCount; i++)
            {
                offsetSpan[i] = totalBandOffsetCount;
                totalBandOffsetCount += bcSpan[i] + 1;
            }

            offsetSpan[visibleItemCount] = totalBandOffsetCount;
        }

        IMemoryOwner<int> bandSegmentOffsetsOwner = allocator.Allocate<int>(totalBandOffsetCount);
        long totalBandSegmentRefs = 0;

        // Phase 4: count how many prepared segments each item contributes to each row band,
        // and record the packed band assignment for each segment in the cache so Phase 5
        // can scatter from integers rather than re-enumerating path geometry.
        _ = Parallel.ForEach(
            Partitioner.Create(0, visibleItemCount),
            () => 0L,
            (range, _, localTotal) =>
            {
                Span<MaterializedPath> mpSpan = pathBuffer.AsSpan();
                Span<RasterizerOptions> roSpan = rasterizerOptionsBuffer.AsSpan();
                Span<int> bcSpan = itemBandCounts.Span;
                Span<int> offsetSpan = itemBandOffsetStartsOwner.Memory.Span;
                Span<int> bsoSpan = bandSegmentOffsetsOwner.Memory.Span;
                Span<int> bacSpan = bandAssignmentCache.Span;

                for (int i = range.Item1; i < range.Item2; i++)
                {
                    localTotal += CountAndStoreBandSegmentRefs(
                        mpSpan[i],
                        compactedCommands[i].DestinationOffset.Y,
                        in roSpan[i],
                        bcSpan[i],
                        bsoSpan.Slice(offsetSpan[i], bcSpan[i]),
                        bacSpan.Slice(itemSegmentStarts[i], mpSpan[i].TotalSegmentCount));
                }

                return localTotal;
            },
            localTotal => Interlocked.Add(ref totalBandSegmentRefs, localTotal));

        if (totalBandSegmentRefs > int.MaxValue)
        {
            throw new InvalidOperationException("Flush scene exceeds row-local segment indexing limits.");
        }

        int runningSegmentOffset = 0;
        {
            Span<int> offsetSpan = itemBandOffsetStartsOwner.Memory.Span;
            Span<int> bcSpan = itemBandCounts.Span;
            Span<int> bsoSpan = bandSegmentOffsetsOwner.Memory.Span;
            for (int i = 0; i < visibleItemCount; i++)
            {
                int bandOffsetStart = offsetSpan[i];
                int bandCount = bcSpan[i];
                for (int bandIndex = 0; bandIndex < bandCount; bandIndex++)
                {
                    int segmentCount = bsoSpan[bandOffsetStart + bandIndex];
                    bsoSpan[bandOffsetStart + bandIndex] = runningSegmentOffset;
                    runningSegmentOffset += segmentCount;
                }

                bsoSpan[bandOffsetStart + bandCount] = runningSegmentOffset;
            }
        }

        IMemoryOwner<int> bandSegmentIndicesOwner = allocator.Allocate<int>(Math.Max(runningSegmentOffset, 1));

        // Phase 5: scatter segment indices into the dense per-band lists using offsets from the prefix sum.
        // Reads the compact band assignment cache built in Phase 4 — no path geometry re-enumeration.
        _ = Parallel.ForEach(
            Partitioner.Create(0, visibleItemCount),
            () => Array.Empty<int>(),
            (range, _, bandCursorBuffer) =>
            {
                Span<MaterializedPath> mpSpan = pathBuffer.AsSpan();
                Span<int> bcSpan = itemBandCounts.Span;
                Span<int> offsetSpan = itemBandOffsetStartsOwner.Memory.Span;
                Span<int> bsoSpan = bandSegmentOffsetsOwner.Memory.Span;
                Span<int> bsiSpan = bandSegmentIndicesOwner.Memory.Span;
                Span<int> bacSpan = bandAssignmentCache.Span;

                for (int i = range.Item1; i < range.Item2; i++)
                {
                    int bandCount = bcSpan[i];
                    if (bandCount <= 0)
                    {
                        continue;
                    }

                    if (bandCursorBuffer.Length < bandCount)
                    {
                        bandCursorBuffer = new int[bandCount];
                    }

                    ReadOnlySpan<int> bandOffsets = bsoSpan.Slice(offsetSpan[i], bandCount + 1);
                    Span<int> bandCursors = bandCursorBuffer.AsSpan(0, bandCount);
                    bandOffsets[..bandCount].CopyTo(bandCursors);

                    FillBandSegmentRefsFromCache(
                        bacSpan.Slice(itemSegmentStarts[i], mpSpan[i].TotalSegmentCount),
                        bandCursors,
                        bsiSpan);
                }

                return bandCursorBuffer;
            },
            static _ => { });

        bandAssignmentCacheOwner.Dispose();

        int rowCount = (maxBandIndex - minBandIndex) + 1;
        int[] rowCounts = new int[rowCount];
        int totalRefs = 0;

        // Phase 6: convert item-local bands into scene-row membership counts.
        {
            Span<int> fbSpan = itemFirstBandIndices.Span;
            Span<int> bcSpan = itemBandCounts.Span;
            Span<int> offsetSpan = itemBandOffsetStartsOwner.Memory.Span;
            Span<int> bsoSpan = bandSegmentOffsetsOwner.Memory.Span;

            for (int i = 0; i < visibleItemCount; i++)
            {
                int itemFirstActiveRow = fbSpan[i] - minBandIndex;
                int bandOffsetStart = offsetSpan[i];
                for (int localBandIndex = 0; localBandIndex < bcSpan[i]; localBandIndex++)
                {
                    if (bsoSpan[bandOffsetStart + localBandIndex + 1] <= bsoSpan[bandOffsetStart + localBandIndex])
                    {
                        continue;
                    }

                    int sceneRow = itemFirstActiveRow + localBandIndex;
                    rowCounts[sceneRow]++;
                    totalRefs++;
                }
            }
        }

        if (totalRefs == 0)
        {
            itemFirstBandIndicesOwner.Dispose();
            itemBandCountsOwner.Dispose();
            itemBandOffsetStartsOwner.Dispose();
            bandSegmentOffsetsOwner.Dispose();
            bandSegmentIndicesOwner.Dispose();
            return Empty();
        }

        int[] rowOffsets = new int[rowCount + 1];
        int runningOffset = 0;
        for (int i = 0; i < rowCount; i++)
        {
            rowOffsets[i] = runningOffset;
            runningOffset += rowCounts[i];
        }

        rowOffsets[rowCount] = runningOffset;

        PendingRowItem[] pendingRowItems = new PendingRowItem[totalRefs];
        int[] rowCursors = new int[rowCount];
        Array.Copy(rowOffsets, rowCursors, rowCount);

        // Phase 7: build the row-major execution order while preserving command submission order per row.
        {
            Span<int> fbSpan = itemFirstBandIndices.Span;
            Span<int> bcSpan = itemBandCounts.Span;
            Span<int> offsetSpan = itemBandOffsetStartsOwner.Memory.Span;
            Span<int> bsoSpan = bandSegmentOffsetsOwner.Memory.Span;

            for (int i = 0; i < visibleItemCount; i++)
            {
                int itemFirstActiveRow = fbSpan[i] - minBandIndex;
                int bandOffsetStart = offsetSpan[i];
                CompositionCommand command = compactedCommands[i];
                for (int localBandIndex = 0; localBandIndex < bcSpan[i]; localBandIndex++)
                {
                    int segmentStart = bsoSpan[bandOffsetStart + localBandIndex];
                    int segmentEnd = bsoSpan[bandOffsetStart + localBandIndex + 1];
                    int segmentCount = segmentEnd - segmentStart;
                    if (segmentCount <= 0)
                    {
                        continue;
                    }

                    int sceneRow = itemFirstActiveRow + localBandIndex;
                    int absoluteBandIndex = fbSpan[i] + localBandIndex;
                    int bandTop = (absoluteBandIndex * rowHeight) - command.TargetBounds.Y;
                    Rectangle rowDestinationRegion = Rectangle.Intersect(
                        command.DestinationRegion,
                        new Rectangle(
                            command.DestinationRegion.X,
                            bandTop,
                            command.DestinationRegion.Width,
                            rowHeight));
                    pendingRowItems[rowCursors[sceneRow]++] = new PendingRowItem(
                        i,
                        localBandIndex,
                        segmentStart,
                        segmentCount,
                        rowDestinationRegion);
                }
            }
        }

        // Dispose band layout temporaries — no longer needed after Phase 7.
        itemFirstBandIndicesOwner.Dispose();
        itemBandCountsOwner.Dispose();
        itemBandOffsetStartsOwner.Dispose();
        bandSegmentOffsetsOwner.Dispose();

        IMemoryOwner<DefaultRasterizer.RasterLineData>? lineDataOwner =
            runningSegmentOffset > 0 ? allocator.Allocate<DefaultRasterizer.RasterLineData>(runningSegmentOffset) : null;
        IMemoryOwner<int>? startCoverOwner = totalRefs > 0 ? allocator.Allocate<int>(totalRefs * rowHeight) : null;

        DefaultRasterizer.RasterizableBandInfo[] rasterizableBands = new DefaultRasterizer.RasterizableBandInfo[totalRefs];
        RowItem[] rowItems = new RowItem[totalRefs];
        long totalLineCount = 0;

        if (lineDataOwner is not null && startCoverOwner is not null)
        {
            Memory<DefaultRasterizer.RasterLineData> lineData = lineDataOwner.Memory;
            Memory<int> startCoverData = startCoverOwner.Memory;

            // Phase 8: prebuild each row-local raster band once so execution only performs scan conversion.
            _ = Parallel.ForEach(Partitioner.Create(0, totalRefs), range =>
            {
                Span<MaterializedPath> mpSpan = pathBuffer.AsSpan();
                Span<RasterizerOptions> roSpan = rasterizerOptionsBuffer.AsSpan();
                Span<int> bsiSpan = bandSegmentIndicesOwner.Memory.Span;

                long localLineCount = 0;
                for (int rowPosition = range.Item1; rowPosition < range.Item2; rowPosition++)
                {
                    PendingRowItem pendingRowItem = pendingRowItems[rowPosition];
                    int itemIndex = pendingRowItem.ItemIndex;
                    rowItems[rowPosition] = new RowItem(
                        itemIndex,
                        pendingRowItem.SegmentStart,
                        rowPosition * rowHeight,
                        pendingRowItem.DestinationRegion);

                    _ = DefaultRasterizer.TryBuildRasterizableBand(
                        mpSpan[itemIndex],
                        bsiSpan.Slice(pendingRowItem.SegmentStart, pendingRowItem.SegmentCount),
                        compactedCommands[itemIndex].DestinationOffset.X,
                        compactedCommands[itemIndex].DestinationOffset.Y,
                        in roSpan[itemIndex],
                        pendingRowItem.LocalBandIndex,
                        lineData.Span.Slice(pendingRowItem.SegmentStart, pendingRowItem.SegmentCount),
                        startCoverData.Span.Slice(rowPosition * rowHeight, rowHeight),
                        out rasterizableBands[rowPosition]);
                    localLineCount += rasterizableBands[rowPosition].LineCount;
                }

                _ = Interlocked.Add(ref totalLineCount, localLineCount);
            });
        }

        // Dispose remaining temporary.
        bandSegmentIndicesOwner.Dispose();

        return new FlushScene(
            compactedCommands,
            interestLefts,
            lineDataOwner,
            startCoverOwner,
            rasterizableBands,
            rowOffsets,
            rowItems,
            minBandIndex * rowHeight,
            maxWidth,
            maxWordsPerRow,
            maxCoverStride,
            maxBandCapacity,
            totalLineCount,
            singleBandItemCount,
            smallEdgeItemCount);
    }

    /// <summary>
    /// Executes the scene against a CPU destination region.
    /// </summary>
    /// <param name="configuration">The configuration that supplies memory allocation and pixel operations.</param>
    /// <param name="destinationFrame">The CPU destination region that receives the composed pixels.</param>
    public void Execute<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> destinationFrame)
        where TPixel : unmanaged, IPixel<TPixel> => this.ExecuteCore(configuration, destinationFrame, this.commands);

    /// <summary>
    /// Executes the scene against a CPU destination region while honoring inline layer commands.
    /// </summary>
    /// <param name="configuration">The configuration that supplies memory allocation and pixel operations.</param>
    /// <param name="destinationFrame">The CPU destination region that receives the composed pixels.</param>
    /// <param name="sourceCommands">The full ordered command stream, including <c>BeginLayer</c> and <c>EndLayer</c>.</param>
    public void ExecuteLayered<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> destinationFrame,
        IReadOnlyList<CompositionCommand> sourceCommands)
        where TPixel : unmanaged, IPixel<TPixel> => this.ExecuteCore(configuration, destinationFrame, sourceCommands);

    /// <summary>
    /// Executes the scene against a CPU destination region using either the flat fill path or the inline layer path.
    /// </summary>
    private void ExecuteCore<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> destinationFrame,
        IReadOnlyList<CompositionCommand> sourceCommands)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (this.RowCount == 0)
        {
            return;
        }

        MemoryAllocator allocator = configuration.MemoryAllocator;
        ReadOnlyMemory<DefaultRasterizer.RasterLineData> lineData =
            this.lineDataOwner?.Memory ?? Memory<DefaultRasterizer.RasterLineData>.Empty;

        ReadOnlyMemory<int> startCovers =
            this.startCoverOwner?.Memory ?? Memory<int>.Empty;

        ParallelOptions options = new()
        {
            MaxDegreeOfParallelism = Math.Min(
                MaxParallelWorkerCount,
                Math.Min(Environment.ProcessorCount, Math.Max(1, this.RowCount))),
        };

        BrushRenderer<TPixel>[] preparedBrushes = new BrushRenderer<TPixel>[this.commands.Length];
        _ = Parallel.ForEach(Partitioner.Create(0, this.commands.Length), range =>
        {
            for (int i = range.Item1; i < range.Item2; i++)
            {
                CompositionCommand command = this.commands[i];
                preparedBrushes[i] = command.Brush.CreateRenderer<TPixel>(
                    configuration,
                    command.GraphicsOptions,
                    destinationFrame.Width,
                    command.BrushBounds);
            }
        });

        try
        {
            _ = Parallel.For(
                0,
                this.RowCount,
                options,
                () => new ExecuteWorkerState<TPixel>(
                    allocator,
                    this.maxWordsPerRow,
                    this.maxCoverStride,
                    this.maxWidth,
                    this.maxBandCapacity),
                (sceneRow, _, state) =>
                {
                    DefaultRasterizer.WorkerScratch scratch = state.Scratch!;
                    DefaultRasterizer.Context context = scratch.CreateContext(
                        this.maxWidth,
                        this.maxWordsPerRow,
                        this.maxCoverStride,
                        this.maxBandCapacity,
                        IntersectionRule.NonZero,
                        RasterizationMode.Antialiased,
                        antialiasThreshold: 0F);

                    int rowStart = this.rowOffsets[sceneRow];
                    int rowEnd = this.rowOffsets[sceneRow + 1];
                    if (rowStart >= rowEnd)
                    {
                        return state;
                    }

                    BandTarget<TPixel> rootTarget = BandTarget<TPixel>.CreateRoot(destinationFrame);
                    this.ExecuteBand(
                        configuration,
                        sourceCommands,
                        preparedBrushes,
                        lineData.Span,
                        startCovers.Span,
                        sceneRow,
                        rowStart,
                        rowEnd,
                        ref context,
                        state,
                        rootTarget);
                    return state;
                },
                state => state.Dispose());
        }
        finally
        {
            for (int i = 0; i < preparedBrushes.Length; i++)
            {
                preparedBrushes[i].Dispose();
            }
        }
    }

    /// <summary>
    /// Executes one band of the command stream while honoring inline layer boundaries.
    /// </summary>
    private void ExecuteBand<TPixel>(
        Configuration configuration,
        IReadOnlyList<CompositionCommand> sourceCommands,
        BrushRenderer<TPixel>[] preparedBrushes,
        ReadOnlySpan<DefaultRasterizer.RasterLineData> lineData,
        ReadOnlySpan<int> startCovers,
        int sceneRow,
        int rowStart,
        int rowEnd,
        ref DefaultRasterizer.Context context,
        ExecuteWorkerState<TPixel> state,
        BandTarget<TPixel> rootTarget)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int bandTop = this.firstSceneBandTop + (sceneRow * DefaultRasterizer.PreferredRowHeight);

        // Mirror the GPU control flow: one structural layer stack driven by BeginLayer/EndLayer.
        // CPU-only backdrop state is stored separately by depth.
        Span<Rectangle> layerStack = state.GetLayerStack();
        int layerCount = 0;

        BandTarget<TPixel> target = rootTarget;
        int rowCursor = rowStart;
        int visibleFillIndex = 0;

        for (int commandIndex = 0; commandIndex < sourceCommands.Count; commandIndex++)
        {
            CompositionCommand command = sourceCommands[commandIndex];
            switch (command.Kind)
            {
                case CompositionCommandKind.FillLayer:
                    if (command.IsVisible)
                    {
                        if (rowCursor < rowEnd && this.rowItems[rowCursor].ItemIndex == visibleFillIndex)
                        {
                            this.ExecuteRowItem(
                                preparedBrushes,
                                lineData,
                                startCovers,
                                rowCursor,
                                ref context,
                                state,
                                target);
                            rowCursor++;
                        }

                        visibleFillIndex++;
                    }

                    break;

                case CompositionCommandKind.BeginLayer:
                    BandTarget<TPixel> layerTarget = BandTarget<TPixel>.CreateLayer(
                        target,
                        command.LayerBounds,
                        bandTop,
                        DefaultRasterizer.PreferredRowHeight);

                    int layerDepth = layerCount;
                    layerStack = state.EnsureLayerDepth(layerDepth);
                    layerStack[layerDepth] = layerTarget.ActiveBounds;
                    layerCount++;

                    if (layerTarget.ActiveBounds.Width > 0 && layerTarget.ActiveBounds.Height > 0)
                    {
                        // CPU execution still needs preserved pixels so EndLayer can composite back correctly.
                        Buffer2DRegion<TPixel> backdrop = state.GetLayerBackdropRegion(
                            layerDepth,
                            layerTarget.ActiveBounds.Width,
                            layerTarget.ActiveBounds.Height);
                        SaveBackdropAndClear(layerTarget, backdrop);
                        state.SetLayerCompositeState(layerDepth, backdrop, command.GraphicsOptions, hasBackdrop: true);
                    }
                    else
                    {
                        state.SetLayerCompositeState(layerDepth, default, command.GraphicsOptions, hasBackdrop: false);
                    }

                    target = layerTarget;
                    break;

                case CompositionCommandKind.EndLayer:
                    if (layerCount == 0)
                    {
                        continue;
                    }

                    int layerIndex = layerCount - 1;
                    Rectangle layerBounds = layerStack[layerIndex];
                    layerCount--;

                    LayerCompositeState<TPixel> layer = state.GetLayerCompositeState(layerIndex);
                    BandTarget<TPixel> compositeTarget = new(
                        rootTarget.Region,
                        rootTarget.OriginX,
                        rootTarget.OriginY,
                        layerBounds);

                    if (layer.HasBackdrop)
                    {
                        ComposeLayerBand(configuration, layer.Backdrop, compositeTarget, layer.Options, state.BrushWorkspace);
                    }

                    // The structural stack determines the current target after the pop.
                    target = layerCount == 0
                        ? rootTarget
                        : new BandTarget<TPixel>(
                            rootTarget.Region,
                            rootTarget.OriginX,
                            rootTarget.OriginY,
                            layerStack[layerCount - 1]);
                    break;
            }
        }
    }

    /// <summary>
    /// Rasterizes and composites one prepared row item into the current band target.
    /// </summary>
    private void ExecuteRowItem<TPixel>(
        BrushRenderer<TPixel>[] preparedBrushes,
        ReadOnlySpan<DefaultRasterizer.RasterLineData> lineData,
        ReadOnlySpan<int> startCovers,
        int rowPosition,
        ref DefaultRasterizer.Context context,
        ExecuteWorkerState<TPixel> state,
        BandTarget<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ref readonly RowItem rowItem = ref this.rowItems[rowPosition];
        int itemIndex = rowItem.ItemIndex;
        DefaultRasterizer.RasterizableBandInfo rasterizableBandInfo = this.rasterizableBands[rowPosition];

        if (!rasterizableBandInfo.HasCoverage || rowItem.DestinationRegion.Width <= 0 || rowItem.DestinationRegion.Height <= 0)
        {
            return;
        }

        BandRowOperation<TPixel> operation = new(
            preparedBrushes[itemIndex],
            target,
            this.interestLefts[itemIndex],
            state.BrushWorkspace);
        DefaultRasterizer.RasterizableBand rasterizableBand = rasterizableBandInfo.CreateRasterizableBand(
            lineData.Slice(rowItem.LineStart, rasterizableBandInfo.LineCount),
            startCovers.Slice(rowItem.StartCoverStart, rasterizableBandInfo.BandHeight));
        DefaultRasterizer.ExecuteRasterizableBand(
            ref context,
            in rasterizableBand,
            state.Scratch!.Scanline,
            operation.InvokeCoverageRow);
    }

    /// <summary>
    /// Blends an isolated layer result back over the saved backdrop for the current band.
    /// </summary>
    private static void ComposeLayerBand<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> backdrop,
        BandTarget<TPixel> destination,
        GraphicsOptions options,
        BrushWorkspace<TPixel> workspace)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Rectangle activeBounds = destination.ActiveBounds;
        if (activeBounds.Width <= 0 || activeBounds.Height <= 0)
        {
            return;
        }

        PixelBlender<TPixel> blender = PixelOperations<TPixel>.Instance.GetPixelBlender(options);
        Span<float> amounts = workspace.GetAmounts(activeBounds.Width);
        amounts.Fill(options.BlendPercentage);
        Span<TPixel> sourceRowCopy = workspace.GetOverlays(activeBounds.Width);

        for (int y = activeBounds.Top; y < activeBounds.Bottom; y++)
        {
            int localY = y - activeBounds.Top;
            Span<TPixel> dstRow = destination.GetRow(y, activeBounds.Left, activeBounds.Width);

            // Preserve the isolated layer result, restore the saved backdrop, then blend the layer over it.
            dstRow.CopyTo(sourceRowCopy);
            backdrop.DangerousGetRowSpan(localY)[..activeBounds.Width].CopyTo(dstRow);
            blender.Blend(configuration, dstRow, dstRow, sourceRowCopy, amounts);
        }
    }

    /// <summary>
    /// Saves the pixels covered by the active layer band and clears that region for isolated drawing.
    /// </summary>
    private static void SaveBackdropAndClear<TPixel>(BandTarget<TPixel> target, Buffer2DRegion<TPixel> backdrop)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Rectangle activeBounds = target.ActiveBounds;
        for (int y = activeBounds.Top; y < activeBounds.Bottom; y++)
        {
            int localY = y - activeBounds.Top;
            Span<TPixel> dstRow = target.GetRow(y, activeBounds.Left, activeBounds.Width);

            // Layer draws start from transparent black; the old pixels are restored during composition.
            dstRow.CopyTo(backdrop.DangerousGetRowSpan(localY)[..activeBounds.Width]);
            dstRow.Clear();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.startCoverOwner?.Dispose();
        this.lineDataOwner?.Dispose();
    }

    /// <summary>
    /// Creates an empty scene instance.
    /// </summary>
    private static FlushScene Empty()
        => new(
            [],
            [],
            null,
            null,
            [],
            [],
            [],
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0);

    /// <summary>
    /// Counts how many references each band needs for one materialized path and records the packed
    /// band assignment <c>(firstBand | lastBand &lt;&lt; 16)</c> for each segment in <paramref name="bandAssignments"/>,
    /// or <c>-1</c> for segments that fall outside the item's interest rectangle.
    /// The cache is consumed by <see cref="FillBandSegmentRefsFromCache"/> in Phase 5, eliminating a
    /// second enumeration of path geometry.
    /// </summary>
    private static int CountAndStoreBandSegmentRefs(
        MaterializedPath path,
        int translateY,
        in RasterizerOptions options,
        int bandCount,
        Span<int> bandCounts,
        Span<int> bandAssignments)
    {
        bandCounts.Clear();
        bandAssignments.Fill(-1);

        if (bandCount <= 0 || path.TotalSegmentCount == 0)
        {
            return 0;
        }

        Rectangle interest = options.Interest;
        float samplingOffsetY = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        int rowHeight = DefaultRasterizer.PreferredRowHeight;
        int firstSceneBandIndex = FloorDiv(interest.Top, rowHeight);
        int bandTopStart = (firstSceneBandIndex * rowHeight) - interest.Top;
        int totalCount = 0;

        int segmentIndex = 0;
        MaterializedPath.SegmentEnumerator enumerator = path.GetSegmentEnumerator();
        while (enumerator.MoveNext())
        {
            if (TryGetLocalBandSpan(
                enumerator.CurrentMinY,
                enumerator.CurrentMaxY,
                translateY,
                interest.Top,
                interest.Height,
                samplingOffsetY,
                bandTopStart,
                rowHeight,
                bandCount,
                out int firstBandIndex,
                out int lastBandIndex))
            {
                bandAssignments[segmentIndex] = firstBandIndex | (lastBandIndex << 16);
                for (int bandIndex = firstBandIndex; bandIndex <= lastBandIndex; bandIndex++)
                {
                    bandCounts[bandIndex]++;
                    totalCount++;
                }
            }

            segmentIndex++;
        }

        return totalCount;
    }

    /// <summary>
    /// Scatters segment indices into the dense per-band reference table by reading the compact
    /// band assignment cache built during Phase 4. No path geometry is accessed.
    /// </summary>
    private static void FillBandSegmentRefsFromCache(
        ReadOnlySpan<int> bandAssignments,
        Span<int> bandCursors,
        Span<int> bandSegmentIndices)
    {
        for (int segmentIndex = 0; segmentIndex < bandAssignments.Length; segmentIndex++)
        {
            int packed = bandAssignments[segmentIndex];
            if (packed < 0)
            {
                continue;
            }

            int firstBandIndex = packed & 0xFFFF;
            int lastBandIndex = (packed >> 16) & 0xFFFF;
            for (int bandIndex = firstBandIndex; bandIndex <= lastBandIndex; bandIndex++)
            {
                bandSegmentIndices[bandCursors[bandIndex]++] = segmentIndex;
            }
        }
    }

    /// <summary>
    /// Computes the inclusive row-band span touched by a segment with the given Y extents.
    /// </summary>
    private static bool TryGetLocalBandSpan(
        float segmentMinY,
        float segmentMaxY,
        int translateY,
        int interestTop,
        int interestHeight,
        float samplingOffsetY,
        int bandTopStart,
        int rowHeight,
        int bandCount,
        out int firstBandIndex,
        out int lastBandIndex)
    {
        float localMinY = ((segmentMinY + translateY) - interestTop) + samplingOffsetY;
        float localMaxY = ((segmentMaxY + translateY) - interestTop) + samplingOffsetY;
        float clipMinY = MathF.Max(0F, localMinY);
        float clipMaxY = MathF.Min(interestHeight, localMaxY);

        if (clipMaxY <= clipMinY)
        {
            firstBandIndex = 0;
            lastBandIndex = -1;
            return false;
        }

        firstBandIndex = FloorDiv((int)MathF.Floor(clipMinY - bandTopStart), rowHeight);
        lastBandIndex = FloorDiv((int)MathF.Ceiling(clipMaxY - bandTopStart) - 1, rowHeight);
        if (lastBandIndex < 0 || firstBandIndex >= bandCount)
        {
            return false;
        }

        firstBandIndex = Math.Max(0, firstBandIndex);
        lastBandIndex = Math.Min(bandCount - 1, lastBandIndex);
        return lastBandIndex >= firstBandIndex;
    }

    /// <summary>
    /// Converts a pixel width to the number of machine-word bit vectors required per row.
    /// </summary>
    private static int BitVectorsForMaxBitCount(int maxBitCount)
        => (maxBitCount + (nint.Size * 8) - 1) / (nint.Size * 8);

    /// <summary>
    /// Performs mathematical floor division for potentially negative coordinates.
    /// </summary>
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

    private readonly struct BandRowOperation<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly BrushRenderer<TPixel> renderer;
        private readonly BandTarget<TPixel> target;
        private readonly BrushWorkspace<TPixel> workspace;
        private readonly int interestLeft;

        /// <summary>
        /// Initializes a new instance of the <see cref="BandRowOperation{TPixel}"/> struct.
        /// </summary>
        public BandRowOperation(
            BrushRenderer<TPixel> renderer,
            BandTarget<TPixel> target,
            int interestLeft,
            BrushWorkspace<TPixel> workspace)
        {
            this.renderer = renderer;
            this.target = target;
            this.interestLeft = interestLeft;
            this.workspace = workspace;
        }

        /// <summary>
        /// Applies one emitted coverage row to the current band target.
        /// </summary>
        public void InvokeCoverageRow(int y, int startX, Span<float> coverage)
        {
            int destinationX = this.interestLeft + startX;
            Span<TPixel> destinationRow = this.target.GetRow(y, destinationX, coverage.Length);
            this.renderer.Apply(destinationRow, coverage, destinationX, y, this.workspace);
        }
    }

    private readonly struct BandTarget<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BandTarget{TPixel}"/> struct.
        /// </summary>
        public BandTarget(Buffer2DRegion<TPixel> region, int originX, int originY, Rectangle activeBounds)
        {
            this.Region = region;
            this.OriginX = originX;
            this.OriginY = originY;
            this.ActiveBounds = activeBounds;
        }

        /// <summary>
        /// Gets the backing destination region for this band target.
        /// </summary>
        public Buffer2DRegion<TPixel> Region { get; }

        /// <summary>
        /// Gets the absolute X origin of <see cref="Region"/>.
        /// </summary>
        public int OriginX { get; }

        /// <summary>
        /// Gets the absolute Y origin of <see cref="Region"/>.
        /// </summary>
        public int OriginY { get; }

        /// <summary>
        /// Gets the active absolute bounds within <see cref="Region"/> for the current band.
        /// </summary>
        public Rectangle ActiveBounds { get; }

        /// <summary>
        /// Creates the root band target for the destination frame.
        /// </summary>
        public static BandTarget<TPixel> CreateRoot(Buffer2DRegion<TPixel> destinationFrame)
            => new(
                destinationFrame,
                destinationFrame.Rectangle.X,
                destinationFrame.Rectangle.Y,
                destinationFrame.Rectangle);

        /// <summary>
        /// Creates a child band target clipped to the current layer bounds and band span.
        /// </summary>
        public static BandTarget<TPixel> CreateLayer(
            BandTarget<TPixel> parent,
            Rectangle layerBounds,
            int bandTop,
            int bandHeight)
        {
            Rectangle activeBounds = Rectangle.Intersect(
                parent.ActiveBounds,
                new Rectangle(layerBounds.X, bandTop, layerBounds.Width, bandHeight));
            activeBounds = Rectangle.Intersect(activeBounds, layerBounds);
            return new(parent.Region, parent.OriginX, parent.OriginY, activeBounds);
        }

        /// <summary>
        /// Gets a writable row slice in absolute destination coordinates.
        /// </summary>
        public Span<TPixel> GetRow(int y, int x, int length)
        {
            int localY = y - this.OriginY;
            int localX = x - this.OriginX;
            return this.Region.DangerousGetRowSpan(localY).Slice(localX, length);
        }
    }

    /// <summary>
    /// Row-major execution record for one prebuilt rasterizable band.
    /// </summary>
    private readonly struct RowItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RowItem"/> struct.
        /// </summary>
        public RowItem(
            int itemIndex,
            int lineStart,
            int startCoverStart,
            Rectangle destinationRegion)
        {
            this.ItemIndex = itemIndex;
            this.LineStart = lineStart;
            this.StartCoverStart = startCoverStart;
            this.DestinationRegion = destinationRegion;
        }

        /// <summary>
        /// Gets the visible command index referenced by this row item.
        /// </summary>
        public int ItemIndex { get; }

        /// <summary>
        /// Gets the starting line-data index for this row item.
        /// </summary>
        public int LineStart { get; }

        /// <summary>
        /// Gets the starting start-cover index for this row item.
        /// </summary>
        public int StartCoverStart { get; }

        /// <summary>
        /// Gets the target-local destination region covered by this row item.
        /// </summary>
        public Rectangle DestinationRegion { get; }
    }

    /// <summary>
    /// Intermediate row item used while the scene is being built.
    /// </summary>
    private readonly struct PendingRowItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PendingRowItem"/> struct.
        /// </summary>
        public PendingRowItem(
            int itemIndex,
            int localBandIndex,
            int segmentStart,
            int segmentCount,
            Rectangle destinationRegion)
        {
            this.ItemIndex = itemIndex;
            this.LocalBandIndex = localBandIndex;
            this.SegmentStart = segmentStart;
            this.SegmentCount = segmentCount;
            this.DestinationRegion = destinationRegion;
        }

        /// <summary>
        /// Gets the visible command index referenced by this pending row item.
        /// </summary>
        public int ItemIndex { get; }

        /// <summary>
        /// Gets the local band index within the owning item.
        /// </summary>
        public int LocalBandIndex { get; }

        /// <summary>
        /// Gets the starting segment-reference index for this pending row item.
        /// </summary>
        public int SegmentStart { get; }

        /// <summary>
        /// Gets the number of segment references for this pending row item.
        /// </summary>
        public int SegmentCount { get; }

        /// <summary>
        /// Gets the target-local destination region covered by this pending row item.
        /// </summary>
        public Rectangle DestinationRegion { get; }
    }

    /// <summary>
    /// CPU-only compositing state associated with one active layer depth.
    /// </summary>
    private readonly struct LayerCompositeState<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LayerCompositeState{TPixel}"/> struct.
        /// </summary>
        public LayerCompositeState(Buffer2DRegion<TPixel> backdrop, GraphicsOptions options, bool hasBackdrop)
        {
            this.Backdrop = backdrop;
            this.Options = options;
            this.HasBackdrop = hasBackdrop;
        }

        /// <summary>
        /// Gets the saved backdrop for this layer scope.
        /// </summary>
        public Buffer2DRegion<TPixel> Backdrop { get; }

        /// <summary>
        /// Gets the compositing options used when this layer closes.
        /// </summary>
        public GraphicsOptions Options { get; }

        /// <summary>
        /// Gets a value indicating whether this layer intersects the current band and owns a backdrop.
        /// </summary>
        public bool HasBackdrop { get; }
    }

    /// <summary>
    /// Base raster worker state shared by execution workers.
    /// </summary>
    private class RasterWorkerState : IDisposable
    {
        private DefaultRasterizer.WorkerScratch? scratch;

        /// <summary>
        /// Initializes a new instance of the <see cref="RasterWorkerState"/> class.
        /// </summary>
        public RasterWorkerState(
            MemoryAllocator allocator,
            int maxWordsPerRow,
            int maxCoverStride,
            int maxWidth,
            int maxBandCapacity) =>
            this.scratch = DefaultRasterizer.WorkerScratch.Create(
                allocator,
                maxWordsPerRow,
                maxCoverStride,
                maxWidth,
                maxBandCapacity);

        /// <summary>
        /// Gets the reusable raster scratch for this worker.
        /// </summary>
        public ref DefaultRasterizer.WorkerScratch? Scratch => ref this.scratch;

        /// <inheritdoc />
        public void Dispose()
        {
            this.scratch?.Dispose();
            this.scratch = null;
        }
    }

    /// <summary>
    /// Execution worker state that combines raster scratch with brush workspace scratch.
    /// </summary>
    private sealed class ExecuteWorkerState<TPixel> : RasterWorkerState
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly MemoryAllocator allocator;
        private Buffer2D<TPixel>?[] layerBackdropBuffers = [];
        private LayerCompositeState<TPixel>[] layerCompositeStates = [];
        private Rectangle[] layerStack = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="ExecuteWorkerState{TPixel}"/> class.
        /// </summary>
        public ExecuteWorkerState(
            MemoryAllocator allocator,
            int maxWordsPerRow,
            int maxCoverStride,
            int maxWidth,
            int maxBandCapacity)
            : base(allocator, maxWordsPerRow, maxCoverStride, maxWidth, maxBandCapacity)
        {
            this.allocator = allocator;
            this.BrushWorkspace = new BrushWorkspace<TPixel>(allocator, maxWidth);
        }

        /// <summary>
        /// Gets the reusable brush workspace for this worker.
        /// </summary>
        public BrushWorkspace<TPixel> BrushWorkspace { get; }

        /// <summary>
        /// Gets writable layer-stack storage sized for the current execution depth.
        /// </summary>
        public Span<Rectangle> GetLayerStack() => this.layerStack;

        /// <summary>
        /// Ensures the structural layer stack can address the requested depth.
        /// </summary>
        public Span<Rectangle> EnsureLayerDepth(int depth)
        {
            int requiredLength = depth + 1;
            if (requiredLength > this.layerStack.Length)
            {
                Array.Resize(ref this.layerStack, Math.Max(requiredLength, this.layerStack.Length == 0 ? 4 : this.layerStack.Length * 2));
            }

            return this.layerStack;
        }

        /// <summary>
        /// Stores the CPU-only compositing state for one active layer depth.
        /// </summary>
        public void SetLayerCompositeState(int depth, Buffer2DRegion<TPixel> backdrop, GraphicsOptions options, bool hasBackdrop)
        {
            int requiredLength = depth + 1;
            if (requiredLength > this.layerCompositeStates.Length)
            {
                Array.Resize(ref this.layerCompositeStates, Math.Max(requiredLength, this.layerCompositeStates.Length == 0 ? 4 : this.layerCompositeStates.Length * 2));
            }

            this.layerCompositeStates[depth] = new(backdrop, options, hasBackdrop);
        }

        /// <summary>
        /// Gets the CPU-only compositing state for one active layer depth.
        /// </summary>
        public LayerCompositeState<TPixel> GetLayerCompositeState(int depth)
            => this.layerCompositeStates[depth];

        /// <summary>
        /// Ensures the backdrop buffer table can address the requested depth.
        /// </summary>
        private void EnsureBackdropBufferDepth(int depth)
        {
            int requiredLength = depth + 1;
            if (requiredLength > this.layerBackdropBuffers.Length)
            {
                Array.Resize(ref this.layerBackdropBuffers, Math.Max(requiredLength, this.layerBackdropBuffers.Length == 0 ? 4 : this.layerBackdropBuffers.Length * 2));
            }
        }

        /// <summary>
        /// Gets scratch storage for the backdrop covered by one active layer depth.
        /// The buffer is reused across bands and only grows when a later band needs more space.
        /// </summary>
        /// <param name="depth">The current nested layer depth within this worker.</param>
        /// <param name="width">The active band width that must be preserved.</param>
        /// <param name="height">The active band height that must be preserved.</param>
        /// <returns>A temporary backdrop region sized for the current layer band.</returns>
        public Buffer2DRegion<TPixel> GetLayerBackdropRegion(int depth, int width, int height)
        {
            this.EnsureBackdropBufferDepth(depth);

            Buffer2D<TPixel>? buffer = this.layerBackdropBuffers[depth];
            if (buffer is null || buffer.Width < width || buffer.Height < height)
            {
                buffer?.Dispose();
                buffer = this.allocator.Allocate2D<TPixel>(width, height, AllocationOptions.None);
                this.layerBackdropBuffers[depth] = buffer;
            }

            return new Buffer2DRegion<TPixel>(buffer, new Rectangle(0, 0, width, height));
        }

        /// <summary>
        /// Releases the worker-local brush workspace and raster scratch owned by this state.
        /// </summary>
        public new void Dispose()
        {
            for (int i = 0; i < this.layerBackdropBuffers.Length; i++)
            {
                this.layerBackdropBuffers[i]?.Dispose();
            }

            this.BrushWorkspace.Dispose();
            base.Dispose();
        }
    }
}
