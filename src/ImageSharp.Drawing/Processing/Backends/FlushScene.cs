// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Flush-scoped CPU scene realized in destination coordinates.
/// </summary>
/// <remarks>
/// <para>
/// This is the CPU backend's SharpBlaze-style execution unit: one flush is converted into a set of
/// destination-row-local raster items with prepared geometry and row membership lists.
/// </para>
/// <para>
/// The scene owns flush-local scheduling data plus SharpBlaze-style row-local raster payloads
/// prebuilt in destination coordinates. Scene-row execution can then rasterize directly from those
/// prelinearized row items.
/// </para>
/// </remarks>
internal sealed class FlushScene : IDisposable
{
    private const int MaxParallelWorkerCount = 12;

    private readonly PreparedCompositionCommand[] commands;
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

    private FlushScene(
        PreparedCompositionCommand[] commands,
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

        PreparedCompositionCommand[] preparedCommandBuffer = new PreparedCompositionCommand[commandCount];
        PreparedGeometry?[] geometryBuffer = new PreparedGeometry[commandCount];
        RasterizerOptions[] rasterizerOptionsBuffer = new RasterizerOptions[commandCount];
        Point[] destinationOffsetBuffer = new Point[commandCount];
        int[] interestLeftBuffer = new int[commandCount];
        int[] firstBandIndexBuffer = new int[commandCount];
        int[] bandCountBuffer = new int[commandCount];
        byte[] visibleItemFlags = new byte[commandCount];

        Parallel.ForEach(Partitioner.Create(0, commandCount), range =>
        {
            for (int i = range.Item1; i < range.Item2; i++)
            {
                CompositionCommand command = commands[i];
                if (!CompositionScenePlanner.TryPrepareCommand(in command, in sceneBounds, out PreparedCompositionCommand prepared))
                {
                    continue;
                }

                PreparedGeometry geometry = command.Geometry
                    ?? throw new InvalidOperationException("Commands must be prepared before building the CPU flush scene.");

                if (geometry.SegmentCount == 0)
                {
                    continue;
                }

                Rectangle destinationInterest = new(
                    sceneBounds.X + prepared.DestinationRegion.X,
                    sceneBounds.Y + prepared.DestinationRegion.Y,
                    prepared.DestinationRegion.Width,
                    prepared.DestinationRegion.Height);

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

                preparedCommandBuffer[i] = prepared;
                geometryBuffer[i] = geometry;
                rasterizerOptionsBuffer[i] = itemOptions;
                destinationOffsetBuffer[i] = command.DestinationOffset;
                interestLeftBuffer[i] = destinationInterest.Left;
                firstBandIndexBuffer[i] = firstBandIndex;
                bandCountBuffer[i] = bandCount;
                visibleItemFlags[i] = 1;
            }
        });

        int visibleItemCount = 0;
        for (int i = 0; i < visibleItemFlags.Length; i++)
        {
            visibleItemCount += visibleItemFlags[i];
        }

        if (visibleItemCount == 0)
        {
            return Empty();
        }

        PreparedCompositionCommand[] preparedCommands = new PreparedCompositionCommand[visibleItemCount];
        PreparedGeometry[] geometries = new PreparedGeometry[visibleItemCount];
        RasterizerOptions[] rasterizerOptions = new RasterizerOptions[visibleItemCount];
        Point[] destinationOffsets = new Point[visibleItemCount];
        int[] interestLefts = new int[visibleItemCount];
        int[] itemFirstBandIndices = new int[visibleItemCount];
        int[] itemBandCounts = new int[visibleItemCount];

        int writeIndex = 0;
        for (int i = 0; i < commandCount; i++)
        {
            if (visibleItemFlags[i] == 0)
            {
                continue;
            }

            preparedCommands[writeIndex] = preparedCommandBuffer[i];
            geometries[writeIndex] = geometryBuffer[i]!;
            rasterizerOptions[writeIndex] = rasterizerOptionsBuffer[i];
            destinationOffsets[writeIndex] = destinationOffsetBuffer[i];
            interestLefts[writeIndex] = interestLeftBuffer[i];
            itemFirstBandIndices[writeIndex] = firstBandIndexBuffer[i];
            itemBandCounts[writeIndex] = bandCountBuffer[i];
            writeIndex++;
        }

        int minBandIndex = int.MaxValue;
        int maxBandIndex = int.MinValue;
        int maxWidth = 0;
        int maxWordsPerRow = 0;
        int maxCoverStride = 0;
        int maxBandCapacity = rowHeight;
        long totalEdgeCount = 0;
        int singleBandItemCount = 0;
        int smallEdgeItemCount = 0;

        for (int i = 0; i < visibleItemCount; i++)
        {
            RasterizerOptions itemOptions = rasterizerOptions[i];
            Rectangle interest = itemOptions.Interest;
            int width = interest.Width;
            long coverStride = (long)width * 2;
            if (coverStride > int.MaxValue)
            {
                throw new InvalidOperationException("Interest bounds exceed rasterizer limits.");
            }

            minBandIndex = Math.Min(minBandIndex, itemFirstBandIndices[i]);
            maxBandIndex = Math.Max(maxBandIndex, itemFirstBandIndices[i] + itemBandCounts[i] - 1);
            maxWidth = Math.Max(maxWidth, width);
            maxWordsPerRow = Math.Max(maxWordsPerRow, BitVectorsForMaxBitCount(width));
            maxCoverStride = Math.Max(maxCoverStride, (int)coverStride);
            totalEdgeCount += geometries[i].SegmentCount;

            if (itemBandCounts[i] == 1)
            {
                singleBandItemCount++;
            }

            if (geometries[i].SegmentCount <= 64)
            {
                smallEdgeItemCount++;
            }
        }

        int[] itemBandOffsetStarts = new int[visibleItemCount + 1];
        int totalBandOffsetCount = 0;
        for (int i = 0; i < visibleItemCount; i++)
        {
            itemBandOffsetStarts[i] = totalBandOffsetCount;
            totalBandOffsetCount += itemBandCounts[i] + 1;
        }

        itemBandOffsetStarts[visibleItemCount] = totalBandOffsetCount;

        int[] bandSegmentOffsets = new int[totalBandOffsetCount];
        long totalBandSegmentRefs = 0;
        Parallel.ForEach(
            Partitioner.Create(0, visibleItemCount),
            () => 0L,
            (range, _, localTotal) =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    localTotal += CountBandSegmentRefs(
                        geometries[i],
                        destinationOffsets[i].Y,
                        in rasterizerOptions[i],
                        itemBandCounts[i],
                        bandSegmentOffsets.AsSpan(itemBandOffsetStarts[i], itemBandCounts[i]));
                }

                return localTotal;
            },
            localTotal => Interlocked.Add(ref totalBandSegmentRefs, localTotal));

        if (totalBandSegmentRefs > int.MaxValue)
        {
            throw new InvalidOperationException("Flush scene exceeds row-local segment indexing limits.");
        }

        int runningSegmentOffset = 0;
        for (int i = 0; i < visibleItemCount; i++)
        {
            int bandOffsetStart = itemBandOffsetStarts[i];
            int bandCount = itemBandCounts[i];
            for (int bandIndex = 0; bandIndex < bandCount; bandIndex++)
            {
                int segmentCount = bandSegmentOffsets[bandOffsetStart + bandIndex];
                bandSegmentOffsets[bandOffsetStart + bandIndex] = runningSegmentOffset;
                runningSegmentOffset += segmentCount;
            }

            bandSegmentOffsets[bandOffsetStart + bandCount] = runningSegmentOffset;
        }

        int[] bandSegmentIndices = new int[runningSegmentOffset];
        Parallel.ForEach(
            Partitioner.Create(0, visibleItemCount),
            () => Array.Empty<int>(),
            (range, _, bandCursorBuffer) =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    int bandCount = itemBandCounts[i];
                    if (bandCount <= 0)
                    {
                        continue;
                    }

                    if (bandCursorBuffer.Length < bandCount)
                    {
                        bandCursorBuffer = new int[bandCount];
                    }

                    ReadOnlySpan<int> bandOffsets = bandSegmentOffsets.AsSpan(itemBandOffsetStarts[i], bandCount + 1);
                    Span<int> bandCursors = bandCursorBuffer.AsSpan(0, bandCount);
                    bandOffsets[..bandCount].CopyTo(bandCursors);

                    FillBandSegmentRefs(
                        geometries[i],
                        destinationOffsets[i].Y,
                        in rasterizerOptions[i],
                        bandCount,
                        bandCursors,
                        bandSegmentIndices);
                }

                return bandCursorBuffer;
            },
            static _ => { });

        int rowCount = (maxBandIndex - minBandIndex) + 1;
        int[] rowCounts = new int[rowCount];
        int totalRefs = 0;

        for (int i = 0; i < visibleItemCount; i++)
        {
            int itemFirstActiveRow = itemFirstBandIndices[i] - minBandIndex;
            int bandOffsetStart = itemBandOffsetStarts[i];
            for (int localBandIndex = 0; localBandIndex < itemBandCounts[i]; localBandIndex++)
            {
                if (bandSegmentOffsets[bandOffsetStart + localBandIndex + 1] <= bandSegmentOffsets[bandOffsetStart + localBandIndex])
                {
                    continue;
                }

                int sceneRow = itemFirstActiveRow + localBandIndex;
                rowCounts[sceneRow]++;
                totalRefs++;
            }
        }

        if (totalRefs == 0)
        {
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
        for (int i = 0; i < visibleItemCount; i++)
        {
            int itemFirstActiveRow = itemFirstBandIndices[i] - minBandIndex;
            int bandOffsetStart = itemBandOffsetStarts[i];
            PreparedCompositionCommand command = preparedCommands[i];
            for (int localBandIndex = 0; localBandIndex < itemBandCounts[i]; localBandIndex++)
            {
                int segmentStart = bandSegmentOffsets[bandOffsetStart + localBandIndex];
                int segmentEnd = bandSegmentOffsets[bandOffsetStart + localBandIndex + 1];
                int segmentCount = segmentEnd - segmentStart;
                if (segmentCount <= 0)
                {
                    continue;
                }

                int sceneRow = itemFirstActiveRow + localBandIndex;
                int absoluteBandIndex = itemFirstBandIndices[i] + localBandIndex;
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
            Parallel.ForEach(Partitioner.Create(0, totalRefs), range =>
            {
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
                        geometries[itemIndex],
                        bandSegmentIndices.AsSpan(pendingRowItem.SegmentStart, pendingRowItem.SegmentCount),
                        destinationOffsets[itemIndex].X,
                        destinationOffsets[itemIndex].Y,
                        in rasterizerOptions[itemIndex],
                        pendingRowItem.LocalBandIndex,
                        lineData.Span.Slice(pendingRowItem.SegmentStart, pendingRowItem.SegmentCount),
                        startCoverData.Span.Slice(rowPosition * rowHeight, rowHeight),
                        out rasterizableBands[rowPosition]);
                    localLineCount += rasterizableBands[rowPosition].LineCount;
                }

                Interlocked.Add(ref totalLineCount, localLineCount);
            });
        }

        return new FlushScene(
            preparedCommands,
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
    /// Rasterizes the scene without composition for profiling.
    /// </summary>
    public void RasterizeNoOp(MemoryAllocator allocator)
    {
        if (this.RowCount == 0)
        {
            return;
        }

        ReadOnlyMemory<DefaultRasterizer.RasterLineData> lineData =
            this.lineDataOwner?.Memory ?? Memory<DefaultRasterizer.RasterLineData>.Empty;
        ReadOnlyMemory<int> startCovers =
            this.startCoverOwner?.Memory ?? Memory<int>.Empty;
        ParallelOptions options = CreateParallelOptions(this.RowCount);
        _ = Parallel.For(
            0,
            this.RowCount,
            options,
            () => new RasterWorkerState(
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
                for (int rowPosition = this.rowOffsets[sceneRow]; rowPosition < this.rowOffsets[sceneRow + 1]; rowPosition++)
                {
                    ref readonly RowItem rowItem = ref this.rowItems[rowPosition];
                    DefaultRasterizer.RasterizableBandInfo rasterizableBandInfo = this.rasterizableBands[rowPosition];
                    if (!rasterizableBandInfo.HasCoverage)
                    {
                        continue;
                    }

                    DefaultRasterizer.RasterizableBand rasterizableBand = rasterizableBandInfo.CreateRasterizableBand(
                        lineData.Span.Slice(rowItem.LineStart, rasterizableBandInfo.LineCount),
                        startCovers.Span.Slice(rowItem.StartCoverStart, rasterizableBandInfo.BandHeight));
                    DefaultRasterizer.ExecuteRasterizableBand(
                        ref context,
                        in rasterizableBand,
                        scratch.Scanline,
                        static (int y, int startX, Span<float> coverage) => { });
                }

                return state;
            },
            static state => state.Dispose());
    }

    /// <summary>
    /// Executes the scene against a CPU destination region.
    /// </summary>
    public ExecutionProfile Execute<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> destinationFrame)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (this.RowCount == 0)
        {
            return default;
        }

        MemoryAllocator allocator = configuration.MemoryAllocator;
        long createApplicatorTicks;
        long rasterizeComposeTicks = 0;
        long disposeApplicatorTicks;
        ReadOnlyMemory<DefaultRasterizer.RasterLineData> lineData =
            this.lineDataOwner?.Memory ?? Memory<DefaultRasterizer.RasterLineData>.Empty;
        ReadOnlyMemory<int> startCovers =
            this.startCoverOwner?.Memory ?? Memory<int>.Empty;
        ParallelOptions options = CreateParallelOptions(this.RowCount);
        BrushRenderer<TPixel>[] preparedBrushes = new BrushRenderer<TPixel>[this.commands.Length];

        long startTimestamp = Stopwatch.GetTimestamp();
        Parallel.ForEach(Partitioner.Create(0, this.commands.Length), range =>
        {
            for (int i = range.Item1; i < range.Item2; i++)
            {
                PreparedCompositionCommand command = this.commands[i];
                preparedBrushes[i] = command.Brush.CreateRenderer<TPixel>(
                    configuration,
                    command.GraphicsOptions,
                    destinationFrame.Width,
                    command.BrushBounds);
            }
        });
        createApplicatorTicks = Stopwatch.GetTimestamp() - startTimestamp;

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
                        long rowTimestamp = Stopwatch.GetTimestamp();
                        DefaultRasterizer.ExecuteRasterizableBand(
                            ref context,
                            in rasterizableBand,
                            scratch.Scanline,
                            operation.InvokeCoverageRow);
                        state.RasterizeAndComposeTicks += Stopwatch.GetTimestamp() - rowTimestamp;
                    }

                    return state;
                },
                state =>
                {
                    Interlocked.Add(ref rasterizeComposeTicks, state.RasterizeAndComposeTicks);
                    state.Dispose();
                });
        }
        finally
        {
            startTimestamp = Stopwatch.GetTimestamp();
            for (int i = 0; i < preparedBrushes.Length; i++)
            {
                preparedBrushes[i]?.Dispose();
            }

            disposeApplicatorTicks = Stopwatch.GetTimestamp() - startTimestamp;
        }

        return new ExecutionProfile(
            TicksToMilliseconds(createApplicatorTicks),
            TicksToMilliseconds(rasterizeComposeTicks),
            TicksToMilliseconds(disposeApplicatorTicks));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.startCoverOwner?.Dispose();
        this.lineDataOwner?.Dispose();
    }

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

    private static int CountBandSegmentRefs(
        PreparedGeometry geometry,
        int translateY,
        in RasterizerOptions options,
        int bandCount,
        Span<int> bandCounts)
    {
        bandCounts.Clear();
        if (bandCount <= 0 || geometry.SegmentCount == 0)
        {
            return 0;
        }

        Rectangle interest = options.Interest;
        float samplingOffsetY = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        int rowHeight = DefaultRasterizer.PreferredRowHeight;
        int firstSceneBandIndex = FloorDiv(interest.Top, rowHeight);
        int bandTopStart = (firstSceneBandIndex * rowHeight) - interest.Top;
        int totalCount = 0;
        foreach (PreparedLineSegment segment in geometry.Segments)
        {
            if (!TryGetLocalBandSpan(
                segment,
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

    private static void FillBandSegmentRefs(
        PreparedGeometry geometry,
        int translateY,
        in RasterizerOptions options,
        int bandCount,
        Span<int> bandCursors,
        int[] bandSegmentIndices)
    {
        if (bandCount <= 0 || geometry.SegmentCount == 0)
        {
            return;
        }

        Rectangle interest = options.Interest;
        float samplingOffsetY = options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        int rowHeight = DefaultRasterizer.PreferredRowHeight;
        int firstSceneBandIndex = FloorDiv(interest.Top, rowHeight);
        int bandTopStart = (firstSceneBandIndex * rowHeight) - interest.Top;
        ReadOnlySpan<PreparedLineSegment> segments = geometry.Segments;
        for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            if (!TryGetLocalBandSpan(
                segments[segmentIndex],
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
                bandSegmentIndices[bandCursors[bandIndex]++] = segmentIndex;
            }
        }
    }

    private static bool TryGetLocalBandSpan(
        PreparedLineSegment segment,
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
        float localMinY = ((segment.MinY + translateY) - interestTop) + samplingOffsetY;
        float localMaxY = ((segment.MaxY + translateY) - interestTop) + samplingOffsetY;
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

    private static int BitVectorsForMaxBitCount(int maxBitCount)
        => (maxBitCount + (nint.Size * 8) - 1) / (nint.Size * 8);

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

    private static ParallelOptions CreateParallelOptions(int rowCount)
        => new()
        {
            MaxDegreeOfParallelism = Math.Min(
                MaxParallelWorkerCount,
                Math.Min(Environment.ProcessorCount, Math.Max(1, rowCount))),
        };

    private static double TicksToMilliseconds(long ticks)
        => (ticks * 1000D) / Stopwatch.Frequency;

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

    internal readonly record struct ExecutionProfile(
        double CreateApplicatorsMilliseconds,
        double RasterizeAndComposeMilliseconds,
        double DisposeApplicatorsMilliseconds);

    private readonly struct ItemRowOperation<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly BrushRenderer<TPixel> applicator;
        private readonly Buffer2DRegion<TPixel> destinationFrame;
        private readonly BrushWorkspace<TPixel> workspace;
        private readonly int interestLeft;

        public ItemRowOperation(
            BrushRenderer<TPixel> applicator,
            Buffer2DRegion<TPixel> destinationFrame,
            int interestLeft,
            BrushWorkspace<TPixel> workspace)
        {
            this.applicator = applicator;
            this.destinationFrame = destinationFrame;
            this.interestLeft = interestLeft;
            this.workspace = workspace;
        }

        public void InvokeCoverageRow(int y, int startX, Span<float> coverage)
        {
            int destinationX = this.interestLeft + startX;
            Span<TPixel> destinationRow = GetDestinationRow(this.destinationFrame, destinationX, y, coverage.Length);
            this.applicator.Apply(destinationRow, coverage, destinationX, y, this.workspace);
        }
    }

    private readonly struct RowItem
    {
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

    private readonly struct PendingRowItem
    {
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

    private class RasterWorkerState : IDisposable
    {
        private DefaultRasterizer.WorkerScratch? scratch;

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

        public ref DefaultRasterizer.WorkerScratch? Scratch => ref this.scratch;

        public void Dispose()
        {
            this.scratch?.Dispose();
            this.scratch = null;
        }
    }

    private sealed class ExecuteWorkerState<TPixel> : RasterWorkerState
        where TPixel : unmanaged, IPixel<TPixel>
    {
        public ExecuteWorkerState(
            MemoryAllocator allocator,
            int maxWordsPerRow,
            int maxCoverStride,
            int maxWidth,
            int maxBandCapacity)
            : base(allocator, maxWordsPerRow, maxCoverStride, maxWidth, maxBandCapacity)
            => this.BrushWorkspace = new BrushWorkspace<TPixel>(allocator, maxWidth);

        public BrushWorkspace<TPixel> BrushWorkspace { get; }

        public long RasterizeAndComposeTicks { get; set; }

        public new void Dispose()
        {
            this.BrushWorkspace.Dispose();
            base.Dispose();
        }
    }
}
