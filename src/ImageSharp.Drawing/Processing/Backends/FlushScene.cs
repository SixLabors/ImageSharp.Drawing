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
    {
        Rectangle sceneBounds = targetBounds;
        int rowHeight = DefaultRasterizer.PreferredRowHeight;
        int commandCount = commands.Count;
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
                CompositionCommand command = commands[i];
                if (!command.IsVisible)
                {
                    continue;
                }

                IPath preparedPath = command.PreparedPath
                    ?? throw new InvalidOperationException("Commands must be prepared before building the CPU flush scene.");

                MaterializedPath materializedPath = MaterializedPath.Create(preparedPath);
                if (materializedPath.TotalSegmentCount == 0)
                {
                    continue;
                }

                Rectangle destinationInterest = new(
                    sceneBounds.X + command.DestinationRegion.X,
                    sceneBounds.Y + command.DestinationRegion.Y,
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

                compactedCommands[writeIndex] = commands[i];
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

        // Phase 4: count how many prepared segments each item contributes to each row band.
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

                for (int i = range.Item1; i < range.Item2; i++)
                {
                    localTotal += CountBandSegmentRefs(
                        mpSpan[i],
                        compactedCommands[i].DestinationOffset.Y,
                        in roSpan[i],
                        bcSpan[i],
                        bsoSpan.Slice(offsetSpan[i], bcSpan[i]));
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

        // Phase 5: materialize the dense per-band segment index lists using the offsets computed above.
        _ = Parallel.ForEach(
            Partitioner.Create(0, visibleItemCount),
            () => Array.Empty<int>(),
            (range, _, bandCursorBuffer) =>
            {
                Span<MaterializedPath> mpSpan = pathBuffer.AsSpan();
                Span<RasterizerOptions> roSpan = rasterizerOptionsBuffer.AsSpan();
                Span<int> bcSpan = itemBandCounts.Span;
                Span<int> offsetSpan = itemBandOffsetStartsOwner.Memory.Span;
                Span<int> bsoSpan = bandSegmentOffsetsOwner.Memory.Span;
                Span<int> bsiSpan = bandSegmentIndicesOwner.Memory.Span;

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

                    FillBandSegmentRefs(
                        mpSpan[i],
                        compactedCommands[i].DestinationOffset.Y,
                        in roSpan[i],
                        bandCount,
                        bandCursors,
                        bsiSpan);
                }

                return bandCursorBuffer;
            },
            static _ => { });

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
                    int bandTop = (absoluteBandIndex * rowHeight) - sceneBounds.Y;
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

        // Realize brush renderers once per command before rows begin executing in parallel.
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
                    DefaultRasterizer.WorkerScratch scratch = state.Scratch
                        ?? throw new InvalidOperationException("Raster worker scratch was not initialized.");

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

                    // Commands remain ordered inside a row so composition matches submission order.
                    for (int rowPosition = rowStart; rowPosition < rowEnd; rowPosition++)
                    {
                        ref readonly RowItem rowItem = ref this.rowItems[rowPosition];
                        int itemIndex = rowItem.ItemIndex;
                        DefaultRasterizer.RasterizableBandInfo rasterizableBandInfo = this.rasterizableBands[rowPosition];

                        if (!rasterizableBandInfo.HasCoverage || rowItem.DestinationRegion.Width <= 0 || rowItem.DestinationRegion.Height <= 0)
                        {
                            continue;
                        }

                        ItemRowOperation<TPixel> operation = new(
                            preparedBrushes[itemIndex],
                            destinationFrame,
                            this.interestLefts[itemIndex],
                            state.BrushWorkspace);
                        DefaultRasterizer.RasterizableBand rasterizableBand = rasterizableBandInfo.CreateRasterizableBand(
                            lineData.Span.Slice(rowItem.LineStart, rasterizableBandInfo.LineCount),
                            startCovers.Span.Slice(rowItem.StartCoverStart, rasterizableBandInfo.BandHeight));
                        DefaultRasterizer.ExecuteRasterizableBand(
                            ref context,
                            in rasterizableBand,
                            scratch.Scanline,
                            operation.InvokeCoverageRow);
                    }

                    return state;
                },
                state => state.Dispose());
        }
        finally
        {
            for (int i = 0; i < preparedBrushes.Length; i++)
            {
                preparedBrushes[i]?.Dispose();
            }
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
            0);

    /// <summary>
    /// Counts how many references each band needs for one materialized path.
    /// </summary>
    private static int CountBandSegmentRefs(
        MaterializedPath path,
        int translateY,
        in RasterizerOptions options,
        int bandCount,
        Span<int> bandCounts)
    {
        bandCounts.Clear();

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

        MaterializedPath.SegmentEnumerator enumerator = path.GetSegmentEnumerator();
        while (enumerator.MoveNext())
        {
            if (!TryGetLocalBandSpan(
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
                continue;
            }

            for (int bandIndex = firstBandIndex; bandIndex <= lastBandIndex; bandIndex++)
            {
                bandCounts[bandIndex]++;
                totalCount++;
            }
        }

        return totalCount;
    }

    /// <summary>
    /// Fills the dense per-band segment reference table for one materialized path.
    /// </summary>
    private static void FillBandSegmentRefs(
        MaterializedPath path,
        int translateY,
        in RasterizerOptions options,
        int bandCount,
        Span<int> bandCursors,
        Span<int> bandSegmentIndices)
    {
        if (bandCount <= 0 || path.TotalSegmentCount == 0)
        {
            return;
        }

        Rectangle interest = options.Interest;
        float samplingOffsetY = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        int rowHeight = DefaultRasterizer.PreferredRowHeight;
        int firstSceneBandIndex = FloorDiv(interest.Top, rowHeight);
        int bandTopStart = (firstSceneBandIndex * rowHeight) - interest.Top;

        int segmentIndex = 0;
        MaterializedPath.SegmentEnumerator enumerator = path.GetSegmentEnumerator();
        while (enumerator.MoveNext())
        {
            if (!TryGetLocalBandSpan(
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
                segmentIndex++;
                continue;
            }

            for (int bandIndex = firstBandIndex; bandIndex <= lastBandIndex; bandIndex++)
            {
                bandSegmentIndices[bandCursors[bandIndex]++] = segmentIndex;
            }

            segmentIndex++;
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

    /// <summary>
    /// Maps an absolute destination row request to the local CPU frame slice.
    /// </summary>
    private static Span<TPixel> GetDestinationRow<TPixel>(
        Buffer2DRegion<TPixel> destinationFrame,
        int x,
        int y,
        int length)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int localY = y - destinationFrame.Rectangle.Y;
        int localX = x - destinationFrame.Rectangle.X;
        return destinationFrame.DangerousGetRowSpan(localY).Slice(localX, length);
    }

    /// <summary>
    /// Bridges raster coverage callbacks to brush renderer application.
    /// </summary>
    private readonly struct ItemRowOperation<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly BrushRenderer<TPixel> renderer;
        private readonly Buffer2DRegion<TPixel> destinationFrame;
        private readonly BrushWorkspace<TPixel> workspace;
        private readonly int interestLeft;

        /// <summary>
        /// Initializes a new instance of the <see cref="ItemRowOperation{TPixel}"/> struct.
        /// </summary>
        public ItemRowOperation(
            BrushRenderer<TPixel> renderer,
            Buffer2DRegion<TPixel> destinationFrame,
            int interestLeft,
            BrushWorkspace<TPixel> workspace)
        {
            this.renderer = renderer;
            this.destinationFrame = destinationFrame;
            this.interestLeft = interestLeft;
            this.workspace = workspace;
        }

        /// <summary>
        /// Applies one emitted coverage row to the destination.
        /// </summary>
        public void InvokeCoverageRow(int y, int startX, Span<float> coverage)
        {
            int destinationX = this.interestLeft + startX;
            Span<TPixel> destinationRow = GetDestinationRow(this.destinationFrame, destinationX, y, coverage.Length);
            this.renderer.Apply(destinationRow, coverage, destinationX, y, this.workspace);
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

        public int ItemIndex { get; }

        public int LineStart { get; }

        public int StartCoverStart { get; }

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

        public int ItemIndex { get; }

        public int LocalBandIndex { get; }

        public int SegmentStart { get; }

        public int SegmentCount { get; }

        public Rectangle DestinationRegion { get; }
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
            => this.BrushWorkspace = new BrushWorkspace<TPixel>(allocator, maxWidth);

        /// <summary>
        /// Gets the reusable brush workspace for this worker.
        /// </summary>
        public BrushWorkspace<TPixel> BrushWorkspace { get; }

        /// <summary>
        /// Releases the worker-local brush workspace and raster scratch owned by this state.
        /// </summary>
        public new void Dispose()
        {
            this.BrushWorkspace.Dispose();
            base.Dispose();
        }
    }
}
