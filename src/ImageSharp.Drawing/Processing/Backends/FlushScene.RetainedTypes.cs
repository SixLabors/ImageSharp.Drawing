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
    /// Identifies the retained row operation carried by a <see cref="SceneOperation"/>.
    /// </summary>
    internal enum SceneOperationKind : byte
    {
        /// <summary>
        /// A retained fill item.
        /// </summary>
        FillItem = 0,

        /// <summary>
        /// A retained stroke item.
        /// </summary>
        StrokeItem = 1,

        /// <summary>
        /// Starts an isolated compositing layer.
        /// </summary>
        BeginLayer = 2,

        /// <summary>
        /// Ends the most recently opened layer.
        /// </summary>
        EndLayer = 3,

        /// <summary>
        /// Applies an image processor to the current target.
        /// </summary>
        Apply = 4,
    }

    /// <summary>
    /// Holds one retained row operation.
    /// </summary>
    internal readonly struct SceneOperation
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SceneOperation"/> struct for a draw item.
        /// </summary>
        /// <param name="kind">The retained draw operation kind.</param>
        /// <param name="itemIndex">The retained scene item index.</param>
        /// <param name="localRowIndex">The retained rasterizable row index.</param>
        public SceneOperation(SceneOperationKind kind, int itemIndex, int localRowIndex)
        {
            this.Kind = kind;
            this.ItemIndex = itemIndex;
            this.LocalRowIndex = localRowIndex;
            this.LayerBounds = default;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneOperation"/> struct for a layer control operation.
        /// </summary>
        /// <param name="kind">The layer operation kind.</param>
        /// <param name="layerBounds">The retained row-local layer bounds.</param>
        /// <param name="itemIndex">The retained layer-options index for begin-layer operations.</param>
        public SceneOperation(CompositionCommandKind kind, Rectangle layerBounds, int itemIndex)
        {
            this.Kind = kind == CompositionCommandKind.BeginLayer ? SceneOperationKind.BeginLayer : SceneOperationKind.EndLayer;
            this.ItemIndex = itemIndex;
            this.LocalRowIndex = -1;
            this.LayerBounds = layerBounds;
        }

        /// <summary>
        /// Gets the operation kind.
        /// </summary>
        public SceneOperationKind Kind { get; }

        /// <summary>
        /// Gets the retained scene item index for fill operations.
        /// </summary>
        public int ItemIndex { get; }

        /// <summary>
        /// Gets the retained rasterizable row index for fill operations.
        /// </summary>
        public int LocalRowIndex { get; }

        /// <summary>
        /// Gets the retained row-local layer bounds for layer operations.
        /// </summary>
        public Rectangle LayerBounds { get; }
    }

    /// <summary>
    /// Holds one retained command-indexed layer or apply operation.
    /// </summary>
    internal readonly struct SceneControlItem
    {
        private readonly ApplySceneItem? applyItem;
        private readonly DrawingCanvasLayer? layer;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneControlItem"/> struct for a layer control operation.
        /// </summary>
        /// <param name="kind">The layer operation kind.</param>
        /// <param name="layerBounds">The retained layer bounds.</param>
        /// <param name="layer">The retained layer state.</param>
        public SceneControlItem(CompositionCommandKind kind, Rectangle layerBounds, DrawingCanvasLayer layer)
        {
            this.Kind = kind == CompositionCommandKind.BeginLayer ? SceneOperationKind.BeginLayer : SceneOperationKind.EndLayer;
            this.LayerBounds = layerBounds;
            this.layer = layer;
            this.applyItem = null;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneControlItem"/> struct for an apply operation.
        /// </summary>
        /// <param name="applyItem">The retained apply operation.</param>
        public SceneControlItem(ApplySceneItem applyItem)
        {
            this.Kind = SceneOperationKind.Apply;
            this.LayerBounds = default;
            this.layer = null;
            this.applyItem = applyItem;
        }

        /// <summary>
        /// Gets the operation kind.
        /// </summary>
        public SceneOperationKind Kind { get; }

        /// <summary>
        /// Gets the retained layer bounds for layer operations.
        /// </summary>
        public Rectangle LayerBounds { get; }

        /// <summary>
        /// Gets the layer state for layer operations.
        /// </summary>
        public DrawingCanvasLayer Layer => this.layer ?? throw new InvalidOperationException("Only layer steps carry layer state.");

        /// <summary>
        /// Gets the retained apply operation.
        /// </summary>
        public ApplySceneItem ApplyItem => this.applyItem ?? throw new InvalidOperationException("Only apply steps carry apply state.");
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
        /// Appends the retained blocks owned by <paramref name="source"/> to <paramref name="destination"/>
        /// without copying individual operations.
        /// </summary>
        /// <param name="destination">The builder receiving the appended blocks.</param>
        /// <param name="source">The builder supplying the appended blocks.</param>
        public static void AppendBuilder(ref RowBuilder destination, ref RowBuilder source)
        {
            if (source.firstBlock is null)
            {
                return;
            }

            if (destination.firstBlock is null)
            {
                destination = source;
                source = default;
                return;
            }

            destination.lastBlock!.Next = source.firstBlock;
            source.firstBlock.Previous = destination.lastBlock;
            destination.lastBlock = source.lastBlock;
            destination.count += source.count;
            source = default;
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
    /// Holds retained row work followed by an optional target-wide control operation.
    /// </summary>
    internal sealed class SceneSegment : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SceneSegment"/> class.
        /// </summary>
        /// <param name="rows">The retained rows in this segment.</param>
        /// <param name="rowItemCount">The number of retained row operations in this segment.</param>
        /// <param name="applyItem">The apply operation executed after the rows.</param>
        /// <param name="layerItem">The scoped layer executed after the rows.</param>
        public SceneSegment(
            SceneRow[] rows,
            int rowItemCount,
            ApplySceneItem? applyItem,
            ScopedLayerSceneItem? layerItem)
        {
            this.Rows = rows;
            this.RowItemCount = rowItemCount;
            this.ApplyItem = applyItem;
            this.LayerItem = layerItem;
        }

        /// <summary>
        /// Gets the retained rows in this segment.
        /// </summary>
        public SceneRow[] Rows { get; }

        /// <summary>
        /// Gets the number of retained row operations in this segment.
        /// </summary>
        public int RowItemCount { get; }

        /// <summary>
        /// Gets the apply operation executed after the rows.
        /// </summary>
        public ApplySceneItem? ApplyItem { get; }

        /// <summary>
        /// Gets the scoped layer executed after the rows.
        /// </summary>
        public ScopedLayerSceneItem? LayerItem { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            for (int i = 0; i < this.Rows.Length; i++)
            {
                this.Rows[i].Dispose();
            }

            this.ApplyItem?.Dispose();
            this.LayerItem?.Dispose();
        }
    }

    /// <summary>
    /// Holds a scoped layer whose contents must be rendered into one full target before compositing.
    /// </summary>
    internal sealed class ScopedLayerSceneItem : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScopedLayerSceneItem"/> class.
        /// </summary>
        /// <param name="layer">The retained layer state.</param>
        /// <param name="bounds">The absolute layer target bounds.</param>
        /// <param name="segments">The retained layer contents.</param>
        /// <param name="rowCount">The retained row count inside the layer.</param>
        /// <param name="rowItemCount">The retained row operation count inside the layer.</param>
        public ScopedLayerSceneItem(
            DrawingCanvasLayer layer,
            Rectangle bounds,
            SceneSegment[] segments,
            int rowCount,
            int rowItemCount)
        {
            this.Layer = layer;
            this.Bounds = bounds;
            this.Segments = segments;
            this.RowCount = rowCount;
            this.RowItemCount = rowItemCount;
        }

        /// <summary>
        /// Gets the retained layer state.
        /// </summary>
        public DrawingCanvasLayer Layer { get; }

        /// <summary>
        /// Gets the absolute layer target bounds.
        /// </summary>
        public Rectangle Bounds { get; }

        /// <summary>
        /// Gets the retained layer contents.
        /// </summary>
        public SceneSegment[] Segments { get; }

        /// <summary>
        /// Gets the retained row count inside the layer.
        /// </summary>
        public int RowCount { get; }

        /// <summary>
        /// Gets the retained row operation count inside the layer.
        /// </summary>
        public int RowItemCount { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            for (int i = 0; i < this.Segments.Length; i++)
            {
                this.Segments[i].Dispose();
            }
        }
    }

    /// <summary>
    /// Holds one retained apply scene operation.
    /// </summary>
    internal sealed class ApplySceneItem : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApplySceneItem"/> class.
        /// </summary>
        /// <param name="operation">The image processing operation.</param>
        /// <param name="sourceRect">The canvas-local source rectangle copied from the current target.</param>
        /// <param name="brushOffset">The offset used by the runtime image brush.</param>
        /// <param name="graphicsOptions">The graphics options used by the apply item.</param>
        /// <param name="brushBounds">The brush bounds used for applicator creation.</param>
        /// <param name="rasterizable">The retained rasterizable geometry.</param>
        /// <param name="clipState">The exact clip state captured with the command.</param>
        /// <param name="pathClipState">The retained path clip raster data captured with the command.</param>
        /// <param name="destinationOffset">The destination offset used to place clip descriptors.</param>
        /// <param name="ownerLayer">The layer that owned this item when it was recorded.</param>
        public ApplySceneItem(
            Action<IImageProcessingContext> operation,
            Rectangle sourceRect,
            Point brushOffset,
            GraphicsOptions graphicsOptions,
            Rectangle brushBounds,
            DefaultRasterizer.RasterizableGeometry rasterizable,
            DrawingClipState clipState,
            PreparedPathClipState? pathClipState,
            Point destinationOffset,
            DrawingCanvasLayer? ownerLayer)
        {
            this.Operation = operation;
            this.SourceRect = sourceRect;
            this.BrushOffset = brushOffset;
            this.GraphicsOptions = graphicsOptions;
            this.BrushBounds = brushBounds;
            this.Rasterizable = rasterizable;
            this.ClipState = clipState;
            this.PathClipState = pathClipState;
            this.DestinationOffset = destinationOffset;
            this.OwnerLayer = ownerLayer;
        }

        /// <summary>
        /// Gets the image processing operation.
        /// </summary>
        public Action<IImageProcessingContext> Operation { get; }

        /// <summary>
        /// Gets the canvas-local source rectangle copied from the current target.
        /// </summary>
        public Rectangle SourceRect { get; }

        /// <summary>
        /// Gets the offset used by the runtime image brush.
        /// </summary>
        public Point BrushOffset { get; }

        /// <summary>
        /// Gets the graphics options used by the apply item.
        /// </summary>
        public GraphicsOptions GraphicsOptions { get; }

        /// <summary>
        /// Gets the brush bounds used for applicator creation.
        /// </summary>
        public Rectangle BrushBounds { get; }

        /// <summary>
        /// Gets the retained rasterizable geometry.
        /// </summary>
        public DefaultRasterizer.RasterizableGeometry Rasterizable { get; }

        /// <summary>
        /// Gets the exact clip state captured with the command.
        /// </summary>
        public DrawingClipState ClipState { get; }

        /// <summary>
        /// Gets the retained path clip raster data captured with the command.
        /// </summary>
        public PreparedPathClipState? PathClipState { get; }

        /// <summary>
        /// Gets the destination offset used to place clip descriptors.
        /// </summary>
        public Point DestinationOffset { get; }

        /// <summary>
        /// Gets the layer that owned this item when it was recorded.
        /// </summary>
        public DrawingCanvasLayer? OwnerLayer { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            this.Rasterizable.Dispose();
            this.PathClipState?.Dispose();
        }
    }

    /// <summary>
    /// Holds one retained fill scene item.
    /// </summary>
    internal sealed class FillSceneItem : IDisposable
    {
        private object? renderer;

        /// <summary>
        /// Initializes a new instance of the <see cref="FillSceneItem"/> class.
        /// </summary>
        /// <param name="brush">The brush used by the fill item.</param>
        /// <param name="graphicsOptions">The graphics options used by the fill item.</param>
        /// <param name="brushBounds">The brush bounds used for applicator creation.</param>
        /// <param name="rasterizable">The retained rasterizable geometry.</param>
        /// <param name="clipState">The exact clip state captured with the command.</param>
        /// <param name="pathClipState">The retained path clip raster data captured with the command.</param>
        /// <param name="destinationOffset">The destination offset used to place clip descriptors.</param>
        /// <param name="ownerLayer">The layer that owned this item when it was recorded.</param>
        public FillSceneItem(
            Brush brush,
            GraphicsOptions graphicsOptions,
            Rectangle brushBounds,
            DefaultRasterizer.RasterizableGeometry rasterizable,
            DrawingClipState clipState,
            PreparedPathClipState? pathClipState,
            Point destinationOffset,
            DrawingCanvasLayer? ownerLayer)
        {
            this.Brush = brush;
            this.GraphicsOptions = graphicsOptions;
            this.BrushBounds = brushBounds;
            this.Rasterizable = rasterizable;
            this.ClipState = clipState;
            this.PathClipState = pathClipState;
            this.DestinationOffset = destinationOffset;
            this.OwnerLayer = ownerLayer;
        }

        /// <summary>
        /// Gets the brush used by the fill item.
        /// </summary>
        public Brush Brush { get; }

        /// <summary>
        /// Gets the graphics options used by the fill item.
        /// </summary>
        public GraphicsOptions GraphicsOptions { get; }

        /// <summary>
        /// Gets the brush bounds used for applicator creation.
        /// </summary>
        public Rectangle BrushBounds { get; }

        /// <summary>
        /// Gets the retained rasterizable geometry.
        /// </summary>
        public DefaultRasterizer.RasterizableGeometry Rasterizable { get; }

        /// <summary>
        /// Gets the exact clip state captured with the command.
        /// </summary>
        public DrawingClipState ClipState { get; }

        /// <summary>
        /// Gets the retained path clip raster data captured with the command.
        /// </summary>
        public PreparedPathClipState? PathClipState { get; }

        /// <summary>
        /// Gets the destination offset used to place clip descriptors.
        /// </summary>
        public Point DestinationOffset { get; }

        /// <summary>
        /// Gets the layer that owned this item when it was recorded.
        /// </summary>
        public DrawingCanvasLayer? OwnerLayer { get; }

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

            typed = this.Brush.CreateRenderer<TPixel>(
                configuration,
                this.GraphicsOptions,
                canvasWidth,
                this.BrushBounds);

            this.renderer = typed;
            return typed;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.Rasterizable.Dispose();
            this.PathClipState?.Dispose();
        }
    }

    /// <summary>
    /// Holds one retained stroke scene item.
    /// </summary>
    internal sealed class StrokeSceneItem : IDisposable
    {
        private object? renderer;

        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeSceneItem"/> class.
        /// </summary>
        /// <param name="brush">The prepared brush for the stroke item.</param>
        /// <param name="graphicsOptions">The graphics options for the stroke item.</param>
        /// <param name="brushBounds">The prepared brush bounds.</param>
        /// <param name="rasterizable">The retained stroke rasterizable geometry.</param>
        /// <param name="clipState">The exact clip state captured with the command.</param>
        /// <param name="pathClipState">The retained path clip raster data captured with the command.</param>
        /// <param name="destinationOffset">The destination offset used to place clip descriptors.</param>
        /// <param name="ownerLayer">The layer that owned this item when it was recorded.</param>
        public StrokeSceneItem(
            Brush brush,
            GraphicsOptions graphicsOptions,
            Rectangle brushBounds,
            DefaultRasterizer.StrokeRasterizableGeometry rasterizable,
            DrawingClipState clipState,
            PreparedPathClipState? pathClipState,
            Point destinationOffset,
            DrawingCanvasLayer? ownerLayer)
        {
            this.Brush = brush;
            this.GraphicsOptions = graphicsOptions;
            this.BrushBounds = brushBounds;
            this.Rasterizable = rasterizable;
            this.ClipState = clipState;
            this.PathClipState = pathClipState;
            this.DestinationOffset = destinationOffset;
            this.OwnerLayer = ownerLayer;
        }

        /// <summary>
        /// Gets the prepared brush for the stroke item.
        /// </summary>
        public Brush Brush { get; }

        /// <summary>
        /// Gets the graphics options for the stroke item.
        /// </summary>
        public GraphicsOptions GraphicsOptions { get; }

        /// <summary>
        /// Gets the prepared brush bounds for the stroke item.
        /// </summary>
        public Rectangle BrushBounds { get; }

        /// <summary>
        /// Gets the retained stroke rasterizable geometry.
        /// </summary>
        public DefaultRasterizer.StrokeRasterizableGeometry Rasterizable { get; }

        /// <summary>
        /// Gets the exact clip state captured with the command.
        /// </summary>
        public DrawingClipState ClipState { get; }

        /// <summary>
        /// Gets the retained path clip raster data captured with the command.
        /// </summary>
        public PreparedPathClipState? PathClipState { get; }

        /// <summary>
        /// Gets the destination offset used to place clip descriptors.
        /// </summary>
        public Point DestinationOffset { get; }

        /// <summary>
        /// Gets the layer that owned this item when it was recorded.
        /// </summary>
        public DrawingCanvasLayer? OwnerLayer { get; }

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

            typed = this.Brush.CreateRenderer<TPixel>(
                configuration,
                this.GraphicsOptions,
                canvasWidth,
                this.BrushBounds);

            this.renderer = typed;
            return typed;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.Rasterizable.Dispose();
            this.PathClipState?.Dispose();
        }
    }
}
