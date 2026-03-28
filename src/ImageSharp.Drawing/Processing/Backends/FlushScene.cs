// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Represents a flush-ready CPU scene built from retained row-local raster payload.
/// </summary>
internal sealed partial class FlushScene : IDisposable
{
    private static readonly FlushScene EmptyScene = new(
        itemCount: 0,
        rowCount: 0,
        rowItemCount: 0,
        totalEdgeCount: 0,
        singleBandItemCount: 0,
        smallEdgeItemCount: 0,
        maxLayerDepth: 0,
        items: [],
        rows: []);

    private readonly SceneItem[] items;
    private readonly SceneRow[] rows;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlushScene"/> class.
    /// </summary>
    private FlushScene(
        int itemCount,
        int rowCount,
        int rowItemCount,
        long totalEdgeCount,
        int singleBandItemCount,
        int smallEdgeItemCount,
        int maxLayerDepth,
        SceneItem[] items,
        SceneRow[] rows)
    {
        this.ItemCount = itemCount;
        this.RowCount = rowCount;
        this.RowItemCount = rowItemCount;
        this.TotalEdgeCount = totalEdgeCount;
        this.SingleBandItemCount = singleBandItemCount;
        this.SmallEdgeItemCount = smallEdgeItemCount;
        this.MaxLayerDepth = maxLayerDepth;
        this.items = items;
        this.rows = rows;
    }

    /// <summary>
    /// Gets the number of visible fill items retained by the scene.
    /// </summary>
    public int ItemCount { get; }

    /// <summary>
    /// Gets the retained visible scene items.
    /// </summary>
    internal SceneItem[] Items => this.items;

    /// <summary>
    /// Gets the number of scene rows containing executable work.
    /// </summary>
    public int RowCount { get; }

    /// <summary>
    /// Gets the retained row lists.
    /// </summary>
    internal SceneRow[] Rows => this.rows;

    /// <summary>
    /// Gets the total number of row items retained by the scene.
    /// </summary>
    public int RowItemCount { get; }

    /// <summary>
    /// Gets the total number of encoded raster edges retained by the scene.
    /// </summary>
    public long TotalEdgeCount { get; }

    /// <summary>
    /// Gets the number of items that occupy a single row band.
    /// </summary>
    public int SingleBandItemCount { get; }

    /// <summary>
    /// Gets the number of items whose retained edge count is small.
    /// </summary>
    public int SmallEdgeItemCount { get; }

    /// <summary>
    /// Gets the maximum retained layer nesting depth in this scene.
    /// </summary>
    public int MaxLayerDepth { get; }

    /// <summary>
    /// Creates a new scene by scheduling visible fill commands directly over retained rasterizable geometry.
    /// </summary>
    /// <param name="commands">The prepared composition commands.</param>
    /// <param name="targetBounds">The destination bounds of the flush.</param>
    /// <param name="allocator">The allocator used for retained row storage.</param>
    /// <returns>A flush-ready scene.</returns>
    public static FlushScene Create(
        IReadOnlyList<CompositionCommand> commands,
        in Rectangle targetBounds,
        MemoryAllocator allocator)
    {
        int commandCount = commands.Count;
        if (commandCount == 0)
        {
            return Empty();
        }

        // The scene builder writes directly into row-owned appenders while the command stream is walked.
        // There is no compaction phase, no row count/prefix sum phase, and no late raster-band construction.
        int firstTargetRowBandIndex = targetBounds.Top / DefaultRasterizer.DefaultTileHeight;
        int lastTargetRowBandIndex = (targetBounds.Bottom - 1) / DefaultRasterizer.DefaultTileHeight;
        Rectangle localTargetBounds = targetBounds;
        SceneItem[] items = new SceneItem[commandCount];
        RowBuilder[] rowBuilders = new RowBuilder[lastTargetRowBandIndex - firstTargetRowBandIndex + 1];
        LinearGeometry?[] preparedGeometries = new LinearGeometry?[commandCount];
        DefaultRasterizer.RasterizableGeometry?[] preparedRasterizables = new DefaultRasterizer.RasterizableGeometry?[commandCount];

        for (int commandIndex = 0; commandIndex < commandCount; commandIndex++)
        {
            CompositionCommand command = commands[commandIndex];
            if (IsSceneFill(command))
            {
                // Lower each fill path once up front so the parallel rasterizable creation stage
                // can consume cached linear geometry without repeating path lowering work.
                preparedGeometries[commandIndex] = command.PreparedPath!.ToLinearGeometry();
            }
        }

        Parallel.For(
            0,
            commandCount,
            commandIndex =>
            {
                CompositionCommand command = commands[commandIndex];
                LinearGeometry? geometry = preparedGeometries[commandIndex];
                if (geometry is null)
                {
                    return;
                }

                preparedRasterizables[commandIndex] = DefaultRasterizer.CreateRasterizableGeometry(
                    geometry,
                    command.DestinationOffset.X,
                    command.DestinationOffset.Y,
                    command.RasterizerOptions,
                    allocator);
            });

        int itemCount = 0;
        long totalEdgeCount = 0;
        int singleBandItemCount = 0;
        int smallEdgeItemCount = 0;
        int currentLayerDepth = 0;
        int maxLayerDepth = 0;
        for (int commandIndex = 0; commandIndex < commandCount; commandIndex++)
        {
            CompositionCommand command = commands[commandIndex];
            if (TryGetLayerOperation(
                command,
                targetBounds,
                firstTargetRowBandIndex,
                out CompositionCommandKind operationKind,
                out Rectangle layerBandBounds,
                out int firstRowSlot,
                out int lastRowSlot))
            {
                if (operationKind == CompositionCommandKind.BeginLayer)
                {
                    currentLayerDepth++;
                    if (currentLayerDepth > maxLayerDepth)
                    {
                        maxLayerDepth = currentLayerDepth;
                    }
                }

                if (operationKind == CompositionCommandKind.EndLayer)
                {
                    currentLayerDepth--;
                }

                continue;
            }

            if (!IsSceneFill(command))
            {
                continue;
            }

            DefaultRasterizer.RasterizableGeometry? rasterizable = preparedRasterizables[commandIndex];

            if (rasterizable is null || rasterizable.RowBandCount == 0)
            {
                rasterizable?.Dispose();
                continue;
            }

            int itemIndex = itemCount++;
            items[itemIndex] = new SceneItem(commandIndex, command, rasterizable);

            for (int localRowIndex = 0; localRowIndex < rasterizable.RowBandCount; localRowIndex++)
            {
                if (!rasterizable.HasCoverage(localRowIndex))
                {
                    continue;
                }

                DefaultRasterizer.RasterizableBandInfo info = rasterizable.GetBandInfo(localRowIndex);
                totalEdgeCount += info.LineCount;

                if (info.LineCount <= 8)
                {
                    smallEdgeItemCount++;
                }
            }

            if (rasterizable.RowBandCount == 1)
            {
                singleBandItemCount++;
            }
        }

        int targetRowCount = rowBuilders.Length;
        if (targetRowCount > 0)
        {
            int iterationCount = Math.Min(Environment.ProcessorCount, targetRowCount);
            Parallel.For(
                0,
                iterationCount,
                partitionIndex =>
                {
                    int rowStart = (partitionIndex * targetRowCount) / iterationCount;
                    int rowEnd = ((partitionIndex + 1) * targetRowCount) / iterationCount;
                    int nextItemIndex = 0;

                    // Each worker owns a contiguous row range and scans commands in original order.
                    // That keeps per-row ordering deterministic while avoiding shared row-builder mutation.
                    for (int commandIndex = 0; commandIndex < commandCount; commandIndex++)
                    {
                        CompositionCommand command = commands[commandIndex];
                        if (TryGetLayerOperation(
                            command,
                            localTargetBounds,
                            firstTargetRowBandIndex,
                            out CompositionCommandKind operationKind,
                            out Rectangle layerBandBounds,
                            out int firstRowSlot,
                            out int lastRowSlot))
                        {
                            int localFirst = Math.Max(firstRowSlot, rowStart);
                            int localLast = Math.Min(lastRowSlot, rowEnd - 1);
                            for (int rowSlot = localFirst; rowSlot <= localLast; rowSlot++)
                            {
                                ref RowBuilder builder = ref rowBuilders[rowSlot];
                                if (!builder.IsInitialized)
                                {
                                    builder = new RowBuilder(allocator);
                                }

                                int rowTop = localTargetBounds.Top + (rowSlot * DefaultRasterizer.DefaultTileHeight);
                                Rectangle rowBounds = new(localTargetBounds.Left, rowTop, localTargetBounds.Width, DefaultRasterizer.DefaultTileHeight);
                                Rectangle rowLayerBounds = Rectangle.Intersect(layerBandBounds, rowBounds);
                                builder.Append(new SceneOperation(operationKind, commandIndex, rowLayerBounds));
                            }

                            continue;
                        }

                        if (nextItemIndex >= itemCount || items[nextItemIndex].CommandIndex != commandIndex)
                        {
                            continue;
                        }

                        int itemIndex = nextItemIndex++;
                        DefaultRasterizer.RasterizableGeometry rasterizable = items[itemIndex].Rasterizable;
                        int localRowStart = Math.Max(0, rowStart - (rasterizable.FirstRowBandIndex - firstTargetRowBandIndex));
                        int localRowEnd = Math.Min(rasterizable.RowBandCount, rowEnd - (rasterizable.FirstRowBandIndex - firstTargetRowBandIndex));

                        for (int localRowIndex = localRowStart; localRowIndex < localRowEnd; localRowIndex++)
                        {
                            if (!rasterizable.HasCoverage(localRowIndex))
                            {
                                continue;
                            }

                            int rowSlot = (rasterizable.FirstRowBandIndex - firstTargetRowBandIndex) + localRowIndex;
                            ref RowBuilder builder = ref rowBuilders[rowSlot];
                            if (!builder.IsInitialized)
                            {
                                builder = new RowBuilder(allocator);
                            }

                            builder.Append(new SceneOperation(itemIndex, localRowIndex));
                        }
                    }
                });
        }

        int rowCount = 0;
        int rowItemCount = 0;
        for (int i = 0; i < rowBuilders.Length; i++)
        {
            if (!rowBuilders[i].IsInitialized)
            {
                continue;
            }

            rowCount++;
            rowItemCount += rowBuilders[i].Count;
        }

        if (itemCount == 0 || rowItemCount == 0)
        {
            DisposeItems(items, itemCount);
            DisposeRows(rowBuilders);
            return Empty();
        }

        SceneRow[] sceneRows = FinalizeRows(rowBuilders, firstTargetRowBandIndex, rowCount);
        return new FlushScene(
            itemCount,
            rowCount,
            rowItemCount,
            totalEdgeCount,
            singleBandItemCount,
            smallEdgeItemCount,
            maxLayerDepth,
            items,
            sceneRows);
    }

    /// <summary>
    /// Releases retained scene storage.
    /// </summary>
    public void Dispose()
    {
        for (int i = 0; i < this.rows.Length; i++)
        {
            this.rows[i].Dispose();
        }

        for (int i = 0; i < this.ItemCount; i++)
        {
            this.items[i].Dispose();
        }
    }

    /// <summary>
    /// Creates an empty scene instance.
    /// </summary>
    private static FlushScene Empty() => EmptyScene;

    /// <summary>
    /// Identifies whether a command contributes executable fill work to the scene.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSceneFill(in CompositionCommand command)
        => command.IsVisible && command.Kind == CompositionCommandKind.FillLayer && command.PreparedPath is not null;

    /// <summary>
    /// Identifies whether a command contributes retained per-row layer control operations.
    /// </summary>
    private static bool TryGetLayerOperation(
        in CompositionCommand command,
        in Rectangle targetBounds,
        int firstTargetRowBandIndex,
        out CompositionCommandKind operationKind,
        out Rectangle layerBounds,
        out int firstRowSlot,
        out int lastRowSlot)
    {
        operationKind = default;
        layerBounds = default;
        firstRowSlot = 0;
        lastRowSlot = -1;

        if (!command.IsVisible)
        {
            return false;
        }

        switch (command.Kind)
        {
            case CompositionCommandKind.BeginLayer:
                operationKind = CompositionCommandKind.BeginLayer;
                break;

            case CompositionCommandKind.EndLayer:
                operationKind = CompositionCommandKind.EndLayer;
                break;

            default:
                return false;
        }

        Rectangle bounds = Rectangle.Intersect(command.LayerBounds, targetBounds);
        if (bounds.Height <= 0 || bounds.Width <= 0)
        {
            return false;
        }

        layerBounds = bounds;
        int firstRowBandIndex = bounds.Top / DefaultRasterizer.DefaultTileHeight;
        int lastRowBandIndex = (bounds.Bottom - 1) / DefaultRasterizer.DefaultTileHeight;
        firstRowSlot = firstRowBandIndex - firstTargetRowBandIndex;
        lastRowSlot = lastRowBandIndex - firstTargetRowBandIndex;
        return firstRowSlot <= lastRowSlot;
    }

    /// <summary>
    /// Finalizes row-owned append builders into immutable scene rows.
    /// </summary>
    private static SceneRow[] FinalizeRows(RowBuilder[] builders, int firstTargetRowBandIndex, int rowCount)
    {
        SceneRow[] rows = new SceneRow[rowCount];
        int writeIndex = 0;
        for (int i = 0; i < builders.Length; i++)
        {
            if (!builders[i].IsInitialized)
            {
                continue;
            }

            rows[writeIndex++] = builders[i].Finalize(firstTargetRowBandIndex + i);
        }

        return rows;
    }

    /// <summary>
    /// Disposes partially created scene items.
    /// </summary>
    private static void DisposeItems(SceneItem[] items, int count)
    {
        for (int i = 0; i < count; i++)
        {
            items[i].Dispose();
        }
    }

    /// <summary>
    /// Disposes partially created row builders.
    /// </summary>
    private static void DisposeRows(RowBuilder[] builders)
    {
        for (int i = 0; i < builders.Length; i++)
        {
            builders[i].Dispose();
        }
    }
}
