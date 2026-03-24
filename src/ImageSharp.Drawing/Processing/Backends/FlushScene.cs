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
internal sealed class FlushScene : IDisposable
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

    /// <summary>
    /// Holds one retained scene item.
    /// </summary>
    internal struct SceneItem
    {
        private object? renderer;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneItem"/> struct.
        /// </summary>
        public SceneItem(int commandIndex, CompositionCommand command, DefaultRasterizer.RasterizableGeometry rasterizable)
        {
            this.CommandIndex = commandIndex;
            this.Command = command;
            this.Rasterizable = rasterizable;
        }

        /// <summary>
        /// Gets the source command index.
        /// </summary>
        public int CommandIndex { get; }

        /// <summary>
        /// Gets the retained composition command.
        /// </summary>
        public CompositionCommand Command { get; }

        /// <summary>
        /// Gets the retained rasterizable geometry.
        /// </summary>
        public DefaultRasterizer.RasterizableGeometry Rasterizable { get; }

        /// <summary>
        /// Gets the memoized renderer for this scene item, creating it on first use.
        /// </summary>
        public BrushRenderer<TPixel> GetRenderer<TPixel>(Configuration configuration, int canvasWidth)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            if (this.renderer is BrushRenderer<TPixel> typed)
            {
                return typed;
            }

            typed = this.Command.Brush.CreateRenderer<TPixel>(
                configuration,
                this.Command.GraphicsOptions,
                canvasWidth,
                this.Command.BrushBounds);

            this.renderer = typed;
            return typed;
        }

        /// <summary>
        /// Disposes the retained rasterizable geometry.
        /// </summary>
        public void Dispose()
        {
            (this.renderer as IDisposable)?.Dispose();
            this.Rasterizable.Dispose();
        }
    }

    #pragma warning disable SA1201 // Keep the retained row operation adjacent to the retained scene item it references.
    /// <summary>
    /// Holds one retained row operation.
    /// </summary>
    internal readonly struct SceneOperation
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SceneOperation"/> struct for a fill item.
        /// </summary>
        public SceneOperation(int itemIndex, int localRowIndex)
        {
            this.Kind = CompositionCommandKind.FillLayer;
            this.ItemIndex = itemIndex;
            this.LocalRowIndex = localRowIndex;
            this.CommandIndex = -1;
            this.LayerBounds = default;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneOperation"/> struct for a layer control operation.
        /// </summary>
        public SceneOperation(CompositionCommandKind kind, int commandIndex, Rectangle layerBounds)
        {
            this.Kind = kind;
            this.ItemIndex = -1;
            this.LocalRowIndex = -1;
            this.CommandIndex = commandIndex;
            this.LayerBounds = layerBounds;
        }

        /// <summary>
        /// Gets the operation kind.
        /// </summary>
        public CompositionCommandKind Kind { get; }

        /// <summary>
        /// Gets the retained scene item index for fill operations.
        /// </summary>
        public int ItemIndex { get; }

        /// <summary>
        /// Gets the retained rasterizable row index for fill operations.
        /// </summary>
        public int LocalRowIndex { get; }

        /// <summary>
        /// Gets the source command index for layer operations.
        /// </summary>
        public int CommandIndex { get; }

        /// <summary>
        /// Gets the retained row-local layer bounds for layer operations.
        /// </summary>
        public Rectangle LayerBounds { get; }
    }
    #pragma warning restore SA1201

    /// <summary>
    /// Holds one retained scene row.
    /// </summary>
    internal readonly struct SceneRow : IDisposable
    {
        private readonly SceneOperationBlock? firstBlock;
        private readonly SceneOperationBlock? lastBlock;
        private readonly int rowBandIndex;
        private readonly int count;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneRow"/> struct.
        /// </summary>
        public SceneRow(SceneOperationBlock? firstBlock, SceneOperationBlock? lastBlock, int rowBandIndex, int count)
        {
            this.firstBlock = firstBlock;
            this.lastBlock = lastBlock;
            this.rowBandIndex = rowBandIndex;
            this.count = count;
        }

        /// <summary>
        /// Gets the absolute row-band index represented by this scene row.
        /// </summary>
        public int RowBandIndex => this.rowBandIndex;

        /// <summary>
        /// Gets the number of row items in this scene row.
        /// </summary>
        public int Count => this.count;

        /// <summary>
        /// Gets the first retained row-item block.
        /// </summary>
        public SceneOperationBlock? FirstBlock => this.firstBlock;

        /// <summary>
        /// Gets the last retained row-item block.
        /// </summary>
        public SceneOperationBlock? LastBlock => this.lastBlock;

        /// <summary>
        /// Releases the row storage.
        /// </summary>
        public void Dispose()
        {
            SceneOperationBlock? block = this.firstBlock;
            while (block is not null)
            {
                SceneOperationBlock? next = block.Next;
                block.Dispose();
                block = next;
            }
        }
    }

    /// <summary>
    /// Appends row items directly into allocator-backed row storage.
    /// </summary>
    private struct RowBuilder : IDisposable
    {
        private readonly MemoryAllocator allocator;
        private SceneOperationBlock? firstBlock;
        private SceneOperationBlock? lastBlock;
        private int count;

        /// <summary>
        /// Initializes a new instance of the <see cref="RowBuilder"/> struct.
        /// </summary>
        public RowBuilder(MemoryAllocator allocator)
        {
            this.allocator = allocator;
            this.firstBlock = null;
            this.lastBlock = null;
            this.count = 0;
        }

        /// <summary>
        /// Gets a value indicating whether the builder has been initialized.
        /// </summary>
        public readonly bool IsInitialized => this.allocator is not null;

        /// <summary>
        /// Gets the number of operations appended to this builder.
        /// </summary>
        public readonly int Count => this.count;

        /// <summary>
        /// Appends a row item.
        /// </summary>
        public void Append(SceneOperation operation)
        {
            if (this.lastBlock is null)
            {
                SceneOperationBlock block = new(this.allocator);
                block.Append(operation);
                this.firstBlock = block;
                this.lastBlock = block;
                this.count++;
                return;
            }

            SceneOperationBlock current = this.lastBlock;
            if (current.Count < SceneOperationBlock.ItemsPerBlock)
            {
                current.Append(operation);
                this.count++;
                return;
            }

            SceneOperationBlock next = new(this.allocator);
            next.Append(operation);
            current.Next = next;
            next.Previous = current;
            this.lastBlock = next;
            this.count++;
        }

        /// <summary>
        /// Finalizes the builder into retained scene storage.
        /// </summary>
        public readonly SceneRow Finalize(int rowBandIndex) => new(this.firstBlock, this.lastBlock, rowBandIndex, this.count);

        /// <summary>
        /// Disposes unfinalized storage.
        /// </summary>
        public readonly void Dispose()
        {
            SceneOperationBlock? block = this.firstBlock;
            while (block is not null)
            {
                SceneOperationBlock? next = block.Next;
                block.Dispose();
                block = next;
            }
        }
    }

    /// <summary>
    /// Represents one fixed-capacity row-item block.
    /// This mirrors Blaze's <c>RowItemList&lt;T&gt;::Block</c> shape: append into the current block,
    /// allocate a fresh block only when that block fills, and never reallocate/copy existing blocks.
    /// </summary>
    internal sealed class SceneOperationBlock : IDisposable
    {
        private readonly IMemoryOwner<SceneOperation> owner;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneOperationBlock"/> class.
        /// </summary>
        public SceneOperationBlock(MemoryAllocator allocator)
            => this.owner = allocator.Allocate<SceneOperation>(ItemsPerBlock);

        /// <summary>
        /// Gets the fixed item capacity per block.
        /// </summary>
        public static int ItemsPerBlock => 32;

        /// <summary>
        /// Gets or sets the previous block in the row list.
        /// </summary>
        public SceneOperationBlock? Previous { get; set; }

        /// <summary>
        /// Gets or sets the next block in the row list.
        /// </summary>
        public SceneOperationBlock? Next { get; set; }

        /// <summary>
        /// Gets the number of items written into this block.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Gets the items written into this block.
        /// </summary>
        public Span<SceneOperation> Items => this.owner.Memory.Span[..this.Count];

        /// <summary>
        /// Appends an item into this block.
        /// </summary>
        public void Append(SceneOperation operation) => this.owner.Memory.Span[this.Count++] = operation;

        /// <summary>
        /// Releases the block storage.
        /// </summary>
        public void Dispose() => this.owner.Dispose();
    }
}
