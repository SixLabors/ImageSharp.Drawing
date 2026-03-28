// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Represents a flush-ready CPU scene built from retained row-local raster payload.
/// </summary>
internal sealed partial class FlushScene
{
    /// <summary>
    /// Holds one retained row operation.
    /// </summary>
    internal readonly struct SceneOperation
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SceneOperation"/> struct for a fill item.
        /// </summary>
        /// <param name="itemIndex">The retained scene item index.</param>
        /// <param name="localRowIndex">The retained rasterizable row index.</param>
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
        /// <param name="kind">The layer operation kind.</param>
        /// <param name="commandIndex">The source command index.</param>
        /// <param name="layerBounds">The retained row-local layer bounds.</param>
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
        /// <param name="firstBlock">The first retained row-item block.</param>
        /// <param name="lastBlock">The last retained row-item block.</param>
        /// <param name="rowBandIndex">The absolute row-band index represented by the row.</param>
        /// <param name="count">The number of retained operations in the row.</param>
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
        /// <param name="allocator">The allocator used for row-block storage.</param>
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
        /// <param name="operation">The retained operation to append.</param>
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

            // Once a row block fills, link a fresh fixed-capacity block instead of reallocating
            // and copying existing operations. This keeps the retained row builder append-only.
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
        /// <param name="rowBandIndex">The absolute row-band index represented by the row.</param>
        /// <returns>The finalized retained row.</returns>
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
    /// </summary>
    /// <remarks>
    /// This mirrors Blaze's <c>RowItemList&lt;T&gt;::Block</c> shape: append into the current block,
    /// allocate a fresh block only when that block fills, and never reallocate or copy existing blocks.
    /// </remarks>
    internal sealed class SceneOperationBlock : IDisposable
    {
        private readonly IMemoryOwner<SceneOperation> owner;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneOperationBlock"/> class.
        /// </summary>
        /// <param name="allocator">The allocator used for block storage.</param>
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
        /// <param name="operation">The retained operation to append.</param>
        public void Append(SceneOperation operation) => this.owner.Memory.Span[this.Count++] = operation;

        /// <summary>
        /// Releases the block storage.
        /// </summary>
        public void Dispose() => this.owner.Dispose();
    }

    /// <summary>
    /// Holds one retained scene item.
    /// </summary>
    internal sealed class SceneItem : IDisposable
    {
        private object? renderer;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneItem"/> class.
        /// </summary>
        /// <param name="commandIndex">The source command index.</param>
        /// <param name="command">The retained composition command.</param>
        /// <param name="rasterizable">The retained rasterizable geometry.</param>
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
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        /// <param name="configuration">The active processing configuration.</param>
        /// <param name="canvasWidth">The destination canvas width.</param>
        /// <returns>The memoized renderer for the scene item.</returns>
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

        /// <inheritdoc />
        public void Dispose() => this.Rasterizable.Dispose();
    }
}
