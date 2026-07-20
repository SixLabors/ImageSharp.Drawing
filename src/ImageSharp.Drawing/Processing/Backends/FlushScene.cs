// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Represents a flush-ready CPU scene built from retained row-local raster payload.
/// </summary>
internal sealed partial class FlushScene : IDisposable
{
    private static readonly FlushScene EmptyScene = new(
        fillItemCount: 0,
        strokeItemCount: 0,
        rowCount: 0,
        rowItemCount: 0,
        totalEdgeCount: 0,
        singleBandItemCount: 0,
        smallEdgeItemCount: 0,
        maxLayerDepth: 0,
        fillItems: [],
        strokeItems: [],
        layers: [],
        controlItems: [],
        hasApply: false,
        rows: [],
        segments: []);

    /// <summary>
    /// Initializes a new instance of the <see cref="FlushScene"/> class.
    /// </summary>
    /// <param name="fillItemCount">The number of visible fill items retained by the scene.</param>
    /// <param name="strokeItemCount">The number of visible stroke items retained by the scene.</param>
    /// <param name="rowCount">The number of scene rows containing executable work.</param>
    /// <param name="rowItemCount">The total number of row items retained by the scene.</param>
    /// <param name="totalEdgeCount">The total number of encoded raster edges retained by the scene.</param>
    /// <param name="singleBandItemCount">The number of items that occupy a single row band.</param>
    /// <param name="smallEdgeItemCount">The number of items whose retained edge count is small.</param>
    /// <param name="maxLayerDepth">The maximum retained layer nesting depth.</param>
    /// <param name="fillItems">The retained fill items indexed by original command index.</param>
    /// <param name="strokeItems">The retained stroke items indexed by original command index.</param>
    /// <param name="layers">The retained layer state indexed by begin-layer command index.</param>
    /// <param name="controlItems">The layer and apply control operations indexed by original command index.</param>
    /// <param name="hasApply">Whether the scene contains apply barriers.</param>
    /// <param name="rows">The retained row lists.</param>
    /// <param name="segments">The retained target-wide execution segments for scenes containing apply barriers.</param>
    private FlushScene(
        int fillItemCount,
        int strokeItemCount,
        int rowCount,
        int rowItemCount,
        long totalEdgeCount,
        int singleBandItemCount,
        int smallEdgeItemCount,
        int maxLayerDepth,
        FillSceneItem?[] fillItems,
        StrokeSceneItem?[] strokeItems,
        DrawingCanvasLayer?[] layers,
        SceneControlItem?[] controlItems,
        bool hasApply,
        SceneRow[] rows,
        SceneSegment[] segments)
    {
        this.FillItemCount = fillItemCount;
        this.StrokeItemCount = strokeItemCount;
        this.RowCount = rowCount;
        this.RowItemCount = rowItemCount;
        this.TotalEdgeCount = totalEdgeCount;
        this.SingleBandItemCount = singleBandItemCount;
        this.SmallEdgeItemCount = smallEdgeItemCount;
        this.MaxLayerDepth = maxLayerDepth;
        this.FillItems = fillItems;
        this.StrokeItems = strokeItems;
        this.Layers = layers;
        this.ControlItems = controlItems;
        this.HasApply = hasApply;
        this.Rows = rows;
        this.Segments = segments;
    }

    /// <summary>
    /// Gets the number of visible draw items retained by the scene.
    /// </summary>
    public int ItemCount => this.FillItemCount + this.StrokeItemCount;

    /// <summary>
    /// Gets the number of visible fill items retained by the scene.
    /// </summary>
    public int FillItemCount { get; }

    /// <summary>
    /// Gets the number of visible stroke items retained by the scene.
    /// </summary>
    public int StrokeItemCount { get; }

    /// <summary>
    /// Gets the retained visible scene items.
    /// </summary>
    public FillSceneItem?[] FillItems { get; }

    /// <summary>
    /// Gets the retained visible stroke scene items.
    /// </summary>
    public StrokeSceneItem?[] StrokeItems { get; }

    /// <summary>
    /// Gets the retained layer state indexed by begin-layer command index.
    /// </summary>
    public DrawingCanvasLayer?[] Layers { get; }

    /// <summary>
    /// Gets layer and apply control operations indexed by original command index.
    /// </summary>
    private SceneControlItem?[] ControlItems { get; }

    /// <summary>
    /// Gets a value indicating whether the scene contains apply barriers.
    /// </summary>
    public bool HasApply { get; }

    /// <summary>
    /// Gets the number of scene rows containing executable work.
    /// </summary>
    public int RowCount { get; }

    /// <summary>
    /// Gets the retained row lists.
    /// </summary>
    public SceneRow[] Rows { get; }

    /// <summary>
    /// Gets retained target-wide execution segments for scenes containing apply barriers.
    /// </summary>
    public SceneSegment[] Segments { get; }

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
    /// Creates a new scene by scheduling visible draw operations directly over retained rasterizable geometry.
    /// </summary>
    /// <param name="scene">The prepared composition scene.</param>
    /// <param name="targetBounds">The destination bounds of the flush.</param>
    /// <param name="allocator">The allocator used for retained row storage.</param>
    /// <param name="maxDegreeOfParallelism">
    /// The maximum degree of parallelism to use when building the scene, or <c>-1</c> to pass
    /// through the runtime's unlimited sentinel for <see cref="ParallelOptions.MaxDegreeOfParallelism"/>.
    /// </param>
    /// <returns>A flush-ready scene.</returns>
    public static FlushScene Create(
        DrawingCommandBatch scene,
        in Rectangle targetBounds,
        MemoryAllocator allocator,
        int maxDegreeOfParallelism)
    {
        int commandCount = scene.CommandCount;
        if (commandCount == 0)
        {
            return Empty();
        }

        int firstTargetRowBandIndex = targetBounds.Top / DefaultRasterizer.DefaultTileHeight;
        int lastTargetRowBandIndex = (targetBounds.Bottom - 1) / DefaultRasterizer.DefaultTileHeight;
        int targetRowCount = (lastTargetRowBandIndex - firstTargetRowBandIndex) + 1;
        Rectangle targetRectangle = targetBounds;

        if (targetRowCount <= 0)
        {
            return Empty();
        }

        FillSceneItem?[] fillItems = new FillSceneItem?[commandCount];
        StrokeSceneItem?[] strokeItems = new StrokeSceneItem?[commandCount];
        DrawingCanvasLayer?[] layers = new DrawingCanvasLayer?[commandCount];
        SceneControlItem?[] controlItems = scene.HasApply ? new SceneControlItem?[commandCount] : [];
        int partitionCount = ParallelExecutionHelper.GetPartitionCount(maxDegreeOfParallelism, commandCount, targetRowCount);
        PartitionState[] partitions = new PartitionState[partitionCount];

        // The ordered begin/end-clip command stream is the single source of truth for clipping.
        // A single sequential prescan resolves the clip scopes open at each partition boundary so
        // partitions can track the stream independently while processing commands in parallel.
        (DrawingClipDescriptor Descriptor, Point Anchor)[][]? partitionClipSeeds = scene.HasClipControls
            ? CreatePartitionClipSeeds(scene.Commands, commandCount, partitionCount)
            : null;

        _ = Parallel.For(
            0,
            partitionCount,
            ParallelExecutionHelper.CreateParallelOptions(maxDegreeOfParallelism, partitionCount),
            partitionIndex =>
            {
                // Integer division splits the commands into contiguous half-open ranges,
                // keeping the partitions balanced while assigning each command exactly once.
                int partitionCommandStart = (partitionIndex * commandCount) / partitionCount;
                int partitionCommandEnd = ((partitionIndex + 1) * commandCount) / partitionCount;

                partitions[partitionIndex] = ProcessPartition(
                    scene.Commands,
                    partitionCommandStart,
                    partitionCommandEnd,
                    partitionClipSeeds?[partitionIndex],
                    targetRectangle,
                    firstTargetRowBandIndex,
                    targetRowCount,
                    allocator,
                    fillItems,
                    strokeItems,
                    layers,
                    controlItems);
            });

        RowBuilder[] rowBuilders = new RowBuilder[targetRowCount];
        int fillItemCount = 0;
        int strokeItemCount = 0;
        long totalEdgeCount = 0;
        int singleBandItemCount = 0;
        int smallEdgeItemCount = 0;
        int currentLayerDepth = 0;
        int maxLayerDepth = 0;

        // Merge partitions in ascending index order. Partitions cover contiguous ascending command
        // ranges, so appending their row builders in this order preserves painter's order per row.
        for (int i = 0; i < partitionCount; i++)
        {
            PartitionState partition = partitions[i];
            fillItemCount += partition.FillItemCount;
            strokeItemCount += partition.StrokeItemCount;
            totalEdgeCount += partition.TotalEdgeCount;
            singleBandItemCount += partition.SingleBandItemCount;
            smallEdgeItemCount += partition.SmallEdgeItemCount;

            // Partition layer depths are relative to their own command start; offsetting by the
            // depth accumulated across earlier partitions recovers the absolute nesting depth.
            maxLayerDepth = Math.Max(maxLayerDepth, currentLayerDepth + partition.MaxLayerDepth);
            currentLayerDepth += partition.LayerDepthDelta;

            for (int rowSlot = 0; rowSlot < targetRowCount; rowSlot++)
            {
                RowBuilder.AppendBuilder(ref rowBuilders[rowSlot], ref partition.RowBuilders[rowSlot]);
            }
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

        // Apply barriers execute even when no draw work survived clipping, so a scene that
        // contains them can never collapse to the empty singleton.
        if (((fillItemCount + strokeItemCount) == 0 || rowItemCount == 0) && !scene.HasApply)
        {
            DisposeRows(rowBuilders);
            return Empty();
        }

        SceneRow[] sceneRows = rowCount == 0
            ? []
            : FinalizeRows(rowBuilders, firstTargetRowBandIndex, rowCount);
        SceneSegment[] segments = [];

        if (scene.HasApply)
        {
            segments = CreateApplySegments(
                commandCount,
                sceneRows,
                controlItems,
                allocator,
                firstTargetRowBandIndex,
                targetRowCount,
                out rowCount,
                out rowItemCount);

            // Segment building copied every row operation into per-segment storage, so the merged
            // rows are redundant. Apply item ownership also moved into the segments; clearing the
            // control items prevents Dispose from double-disposing those apply items.
            for (int i = 0; i < sceneRows.Length; i++)
            {
                sceneRows[i].Dispose();
            }

            sceneRows = [];
            controlItems = [];
        }

        return new FlushScene(
            fillItemCount,
            strokeItemCount,
            rowCount,
            rowItemCount,
            totalEdgeCount,
            singleBandItemCount,
            smallEdgeItemCount,
            maxLayerDepth,
            fillItems,
            strokeItems,
            layers,
            controlItems,
            scene.HasApply,
            sceneRows,
            segments);
    }

    /// <summary>
    /// Releases retained scene storage.
    /// </summary>
    public void Dispose()
    {
        for (int i = 0; i < this.Rows.Length; i++)
        {
            this.Rows[i].Dispose();
        }

        for (int i = 0; i < this.Segments.Length; i++)
        {
            this.Segments[i].Dispose();
        }

        for (int i = 0; i < this.FillItems.Length; i++)
        {
            this.FillItems[i]?.Dispose();
        }

        for (int i = 0; i < this.StrokeItems.Length; i++)
        {
            this.StrokeItems[i]?.Dispose();
        }

        for (int i = 0; i < this.ControlItems.Length; i++)
        {
            if (this.ControlItems[i] is SceneControlItem controlItem &&
                controlItem.Kind == SceneOperationKind.Apply)
            {
                controlItem.ApplyItem.Dispose();
            }
        }
    }

    /// <summary>
    /// Gets the shared empty scene instance.
    /// </summary>
    /// <returns>The cached empty scene singleton.</returns>
    private static FlushScene Empty() => EmptyScene;

    /// <summary>
    /// Identifies whether a path-backed command contributes executable retained raster work to the scene.
    /// </summary>
    /// <param name="command">The command to inspect.</param>
    /// <returns><see langword="true"/> when the command retains raster work; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSceneDrawable(in CompositionCommand command)
        => command.Kind == CompositionCommandKind.FillLayer;

    /// <summary>
    /// Accumulates retained fill statistics used for scene heuristics.
    /// </summary>
    /// <param name="rasterizable">The retained rasterizable fill geometry to measure.</param>
    /// <param name="totalEdgeCount">The running total of encoded raster edges.</param>
    /// <param name="smallEdgeItemCount">The running count of covered bands with a small edge count.</param>
    /// <param name="singleBandItemCount">The running count of items that occupy a single row band.</param>
    private static void AccumulateFillItemStats(
        DefaultRasterizer.RasterizableGeometry rasterizable,
        ref long totalEdgeCount,
        ref int smallEdgeItemCount,
        ref int singleBandItemCount)
    {
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

    /// <summary>
    /// Accumulates retained stroke statistics used for scene heuristics.
    /// </summary>
    /// <param name="rasterizable">The retained rasterizable stroke geometry to measure.</param>
    /// <param name="totalEdgeCount">The running total of encoded raster edges.</param>
    /// <param name="smallEdgeItemCount">The running count of covered bands with a small edge count.</param>
    /// <param name="singleBandItemCount">The running count of items that occupy a single row band.</param>
    private static void AccumulateStrokeItemStats(
        DefaultRasterizer.StrokeRasterizableGeometry rasterizable,
        ref long totalEdgeCount,
        ref int smallEdgeItemCount,
        ref int singleBandItemCount)
    {
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

    /// <summary>
    /// Appends retained fill row operations for one item into the row builders owned by the current partition.
    /// </summary>
    /// <param name="rowBuilders">The partition-owned row builders, one slot per target row band.</param>
    /// <param name="rowStart">The first row slot the item may write to.</param>
    /// <param name="rowEnd">The exclusive end row slot the item may write to.</param>
    /// <param name="firstTargetRowBandIndex">The first row-band index covered by the target.</param>
    /// <param name="itemIndex">The retained scene item index; equals the original command index.</param>
    /// <param name="rasterizable">The retained rasterizable fill geometry.</param>
    /// <param name="allocator">The allocator used for row-block storage.</param>
    private static void AppendFillRowOperations(
        RowBuilder[] rowBuilders,
        int rowStart,
        int rowEnd,
        int firstTargetRowBandIndex,
        int itemIndex,
        DefaultRasterizer.RasterizableGeometry rasterizable,
        MemoryAllocator allocator)
    {
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

            builder.Append(new SceneOperation(SceneOperationKind.FillItem, itemIndex, localRowIndex));
        }
    }

    /// <summary>
    /// Appends retained stroke row operations for one item into the row builders owned by the current partition.
    /// </summary>
    /// <param name="rowBuilders">The partition-owned row builders, one slot per target row band.</param>
    /// <param name="rowStart">The first row slot the item may write to.</param>
    /// <param name="rowEnd">The exclusive end row slot the item may write to.</param>
    /// <param name="firstTargetRowBandIndex">The first row-band index covered by the target.</param>
    /// <param name="itemIndex">The retained scene item index; equals the original command index.</param>
    /// <param name="rasterizable">The retained rasterizable stroke geometry.</param>
    /// <param name="allocator">The allocator used for row-block storage.</param>
    private static void AppendStrokeRowOperations(
        RowBuilder[] rowBuilders,
        int rowStart,
        int rowEnd,
        int firstTargetRowBandIndex,
        int itemIndex,
        DefaultRasterizer.StrokeRasterizableGeometry rasterizable,
        MemoryAllocator allocator)
    {
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

            builder.Append(new SceneOperation(SceneOperationKind.StrokeItem, itemIndex, localRowIndex));
        }
    }

    /// <summary>
    /// Computes the row-slot range a fill or stroke command may write to. When the command was
    /// recorded inside a SaveLayer the row distribution is confined to the layer's row bands so
    /// a command's geometry cannot leak into rows that lie above or below the layer's
    /// <see cref="CompositionCommand.TargetBounds"/>. Outside any layer (root or region scope)
    /// the command is allowed to address every row; constraining row distribution by the
    /// region's bounds would change long-standing rendering behaviour for region-only paths.
    /// </summary>
    /// <param name="commandTargetBounds">The command's absolute target bounds.</param>
    /// <param name="firstTargetRowBandIndex">The first row-band index covered by the target.</param>
    /// <param name="totalRowSlots">The total number of target row slots.</param>
    /// <param name="isInsideLayer">True if the command was recorded inside a SaveLayer scope.</param>
    /// <param name="rowStart">The first row slot the command may write to.</param>
    /// <param name="rowEnd">The exclusive end row slot the command may write to.</param>
    private static void GetEffectiveRowSlotRange(
        Rectangle commandTargetBounds,
        int firstTargetRowBandIndex,
        int totalRowSlots,
        bool isInsideLayer,
        out int rowStart,
        out int rowEnd)
    {
        if (!isInsideLayer)
        {
            rowStart = 0;
            rowEnd = totalRowSlots;
            return;
        }

        int firstRowBand = commandTargetBounds.Top / DefaultRasterizer.DefaultTileHeight;
        int lastRowBand = (commandTargetBounds.Bottom - 1) / DefaultRasterizer.DefaultTileHeight;
        rowStart = Math.Max(0, firstRowBand - firstTargetRowBandIndex);
        rowEnd = Math.Min(totalRowSlots, lastRowBand - firstTargetRowBandIndex + 1);
    }

    /// <summary>
    /// Identifies whether a command contributes retained per-row layer control operations.
    /// </summary>
    /// <param name="command">The command to inspect.</param>
    /// <param name="targetBounds">The destination bounds of the flush.</param>
    /// <param name="firstTargetRowBandIndex">The first row-band index covered by the target.</param>
    /// <param name="operationKind">The resolved begin or end layer operation kind.</param>
    /// <param name="layerBounds">The layer bounds intersected with the target bounds.</param>
    /// <param name="firstRowSlot">The first row slot touched by the layer.</param>
    /// <param name="lastRowSlot">The last row slot touched by the layer, inclusive.</param>
    /// <returns><see langword="true"/> when the command is a layer operation visible within the target; otherwise <see langword="false"/>.</returns>
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
    /// <param name="builders">The row builders to finalize, one slot per target row band.</param>
    /// <param name="firstTargetRowBandIndex">The first row-band index covered by the target.</param>
    /// <param name="rowCount">The number of initialized builders; sizes the resulting array.</param>
    /// <returns>The finalized scene rows in ascending row-band order.</returns>
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
    /// Disposes partially created row builders.
    /// </summary>
    /// <param name="builders">The row builders to dispose.</param>
    private static void DisposeRows(RowBuilder[] builders)
    {
        for (int i = 0; i < builders.Length; i++)
        {
            builders[i].Dispose();
        }
    }

    /// <summary>
    /// Builds target-wide execution segments for a scene containing apply barriers.
    /// </summary>
    /// <param name="commandCount">The number of commands in the source batch.</param>
    /// <param name="rows">The merged scene rows whose operations are redistributed into segments.</param>
    /// <param name="controlItems">The layer and apply control operations indexed by original command index.</param>
    /// <param name="allocator">The allocator used for retained row storage.</param>
    /// <param name="firstTargetRowBandIndex">The first row-band index covered by the target.</param>
    /// <param name="targetRowCount">The number of row bands covered by the target.</param>
    /// <param name="rowCount">The total retained row count across all segments.</param>
    /// <param name="rowItemCount">The total retained row operation count across all segments.</param>
    /// <returns>The finalized target-wide execution segments.</returns>
    private static SceneSegment[] CreateApplySegments(
        int commandCount,
        SceneRow[] rows,
        SceneControlItem?[] controlItems,
        MemoryAllocator allocator,
        int firstTargetRowBandIndex,
        int targetRowCount,
        out int rowCount,
        out int rowItemCount)
    {
        SceneSequenceBuilder root = new(allocator, firstTargetRowBandIndex, targetRowCount);
        SceneSegmentBuilder?[] commandSegments = new SceneSegmentBuilder?[commandCount];
        List<ScopedLayerBuildFrame> scopedLayers = [];
        SceneSequenceBuilder current = root;

        for (int commandIndex = 0; commandIndex < commandCount; commandIndex++)
        {
            if (controlItems[commandIndex] is not SceneControlItem controlItem)
            {
                commandSegments[commandIndex] = current.CurrentSegment;
                continue;
            }

            switch (controlItem.Kind)
            {
                case SceneOperationKind.BeginLayer:
                    if (controlItem.Layer.RequiresScopedApply)
                    {
                        SceneSequenceBuilder child = new(allocator, firstTargetRowBandIndex, targetRowCount);
                        scopedLayers.Add(new ScopedLayerBuildFrame(current, controlItem.Layer, controlItem.LayerBounds, child));
                        current = child;
                    }
                    else
                    {
                        commandSegments[commandIndex] = current.CurrentSegment;
                    }

                    break;

                case SceneOperationKind.EndLayer:
                    if (scopedLayers.Count != 0 &&
                        ReferenceEquals(scopedLayers[^1].Layer, controlItem.Layer))
                    {
                        ScopedLayerBuildFrame frame = scopedLayers[^1];
                        scopedLayers.RemoveAt(scopedLayers.Count - 1);
                        current = frame.Parent;
                        current.AddLayer(new ScopedLayerSceneBuilder(frame.Layer, frame.Bounds, frame.Content));
                    }
                    else
                    {
                        commandSegments[commandIndex] = current.CurrentSegment;
                    }

                    break;

                case SceneOperationKind.Apply:
                    current.AddApply(controlItem.ApplyItem);
                    break;
            }
        }

        // Apply barriers are whole-target operations. Split retained rows once here so render
        // can replay each segment directly instead of filtering every row by command index.
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            SceneRow row = rows[rowIndex];
            int rowSlot = row.RowBandIndex - firstTargetRowBandIndex;

            for (SceneOperationBlock? block = row.FirstBlock; block is not null; block = block.Next)
            {
                foreach (SceneOperation operation in block.Items)
                {
                    // ItemIndex always equals the original command index, so it selects the segment
                    // that was current when the command was recorded. Scoped-layer begin/end markers
                    // have no segment and are dropped; the scoped layer item composites its content
                    // as a whole instead of replaying inline layer markers.
                    commandSegments[operation.ItemIndex]?.Append(rowSlot, operation);
                }
            }
        }

        return root.FinalizeSegments(out rowCount, out rowItemCount);
    }

    /// <summary>
    /// Computes, for each partition's command start, the clip scopes opened by earlier partitions,
    /// outermost first, resolved from the ordered begin/end-clip command stream.
    /// </summary>
    /// <param name="commands">The ordered retained command stream.</param>
    /// <param name="commandCount">The number of commands in the stream.</param>
    /// <param name="partitionCount">The number of partitions the commands are split into.</param>
    /// <returns>Per-partition arrays of the clip scopes open at each partition boundary, outermost first.</returns>
    private static (DrawingClipDescriptor Descriptor, Point Anchor)[][] CreatePartitionClipSeeds(
        IReadOnlyList<CompositionSceneCommand> commands,
        int commandCount,
        int partitionCount)
    {
        (DrawingClipDescriptor Descriptor, Point Anchor)[][] seeds = new (DrawingClipDescriptor, Point)[partitionCount][];
        List<(DrawingClipDescriptor Descriptor, Point Anchor)> openClips = [];
        int cursor = 0;

        for (int partitionIndex = 0; partitionIndex < partitionCount; partitionIndex++)
        {
            // Partition boundaries are non-decreasing, so a single forward cursor visits each
            // command exactly once even though every partition boundary is snapshotted.
            int boundary = (partitionIndex * commandCount) / partitionCount;
            for (; cursor < boundary; cursor++)
            {
                if (commands[cursor] is not PathCompositionSceneCommand pathCommand)
                {
                    continue;
                }

                CompositionCommand composition = pathCommand.Command;
                if (composition.Kind == CompositionCommandKind.BeginClip)
                {
                    openClips.Add((composition.ClipDescriptor, composition.DestinationOffset));
                }
                else if (composition.Kind == CompositionCommandKind.EndClip && openClips.Count > 0)
                {
                    openClips.RemoveAt(openClips.Count - 1);
                }
            }

            seeds[partitionIndex] = openClips.Count == 0 ? [] : [.. openClips];
        }

        return seeds;
    }

    /// <summary>
    /// Prepares one contiguous command range into retained scene items and partition-local row builders.
    /// Partitions may run in parallel; each writes only to its own command indices in the shared item
    /// arrays and to its own row builders, so no synchronization is required.
    /// </summary>
    /// <param name="commands">The ordered retained command stream.</param>
    /// <param name="commandStart">The first command index owned by this partition.</param>
    /// <param name="commandEnd">The exclusive end command index owned by this partition.</param>
    /// <param name="clipSeed">The clip scopes opened by earlier partitions, outermost first, or <see langword="null"/> when the batch has no clip controls.</param>
    /// <param name="targetBounds">The destination bounds of the flush.</param>
    /// <param name="firstTargetRowBandIndex">The first row-band index covered by the target.</param>
    /// <param name="targetRowCount">The number of row bands covered by the target.</param>
    /// <param name="allocator">The allocator used for retained row storage.</param>
    /// <param name="fillItems">The shared retained fill items indexed by original command index.</param>
    /// <param name="strokeItems">The shared retained stroke items indexed by original command index.</param>
    /// <param name="layers">The shared retained layer state indexed by begin-layer command index.</param>
    /// <param name="controlItems">The shared layer and apply control operations indexed by original command index; empty when the batch has no apply barriers.</param>
    /// <returns>The partition-local counts and row builders to merge after all partitions complete.</returns>
    private static PartitionState ProcessPartition(
        IReadOnlyList<CompositionSceneCommand> commands,
        int commandStart,
        int commandEnd,
        (DrawingClipDescriptor Descriptor, Point Anchor)[]? clipSeed,
        in Rectangle targetBounds,
        int firstTargetRowBandIndex,
        int targetRowCount,
        MemoryAllocator allocator,
        FillSceneItem?[] fillItems,
        StrokeSceneItem?[] strokeItems,
        DrawingCanvasLayer?[] layers,
        SceneControlItem?[] controlItems)
    {
        RowBuilder[] rowBuilders = new RowBuilder[targetRowCount];
        int fillItemCount = 0;
        int strokeItemCount = 0;
        long totalEdgeCount = 0;
        int singleBandItemCount = 0;
        int smallEdgeItemCount = 0;
        int currentLayerDepth = 0;
        int maxLayerDepth = 0;
        ClipStreamTracker clipTracker = ClipStreamTracker.CreateFromSeed(clipSeed);

        for (int commandIndex = commandStart; commandIndex < commandEnd; commandIndex++)
        {
            CompositionSceneCommand command = commands[commandIndex];
            if (command is PathCompositionSceneCommand pathCommand)
            {
                CompositionCommand composition = pathCommand.Command;

                // Clip commands only mutate the tracker; they retain no scene items. Every draw
                // command that follows captures the composed clip state and anchor from the tracker,
                // keeping the ordered clip stream the single source of truth for clipping.
                if (composition.Kind == CompositionCommandKind.BeginClip)
                {
                    clipTracker.Push(composition.ClipDescriptor, composition.DestinationOffset);
                    continue;
                }

                if (composition.Kind == CompositionCommandKind.EndClip)
                {
                    clipTracker.Pop();
                    continue;
                }

                ProcessPathCommand(
                    composition,
                    commandIndex,
                    clipTracker.Current,
                    clipTracker.Anchor,
                    targetBounds,
                    firstTargetRowBandIndex,
                    rowBuilders,
                    allocator,
                    fillItems,
                    strokeItems,
                    layers,
                    controlItems,
                    ref fillItemCount,
                    ref strokeItemCount,
                    ref totalEdgeCount,
                    ref singleBandItemCount,
                    ref smallEdgeItemCount,
                    ref currentLayerDepth,
                    ref maxLayerDepth);
            }
            else if (command is StrokePathCompositionSceneCommand strokePathCommand)
            {
                ProcessStrokePathCommand(
                    strokePathCommand.Command,
                    commandIndex,
                    clipTracker.Current,
                    clipTracker.Anchor,
                    targetRowCount,
                    firstTargetRowBandIndex,
                    rowBuilders,
                    allocator,
                    strokeItems,
                    ref strokeItemCount,
                    ref totalEdgeCount,
                    ref singleBandItemCount,
                    ref smallEdgeItemCount);
            }
            else if (command is LineSegmentCompositionSceneCommand lineSegmentCommand)
            {
                ProcessLineSegmentCommand(
                    lineSegmentCommand.Command,
                    commandIndex,
                    clipTracker.Current,
                    clipTracker.Anchor,
                    targetRowCount,
                    firstTargetRowBandIndex,
                    rowBuilders,
                    allocator,
                    strokeItems,
                    ref strokeItemCount,
                    ref totalEdgeCount,
                    ref singleBandItemCount,
                    ref smallEdgeItemCount);
            }
            else if (command is PolylineCompositionSceneCommand polylineCommand)
            {
                ProcessPolylineCommand(
                    polylineCommand.Command,
                    commandIndex,
                    clipTracker.Current,
                    clipTracker.Anchor,
                    targetRowCount,
                    firstTargetRowBandIndex,
                    rowBuilders,
                    allocator,
                    strokeItems,
                    ref strokeItemCount,
                    ref totalEdgeCount,
                    ref singleBandItemCount,
                    ref smallEdgeItemCount);
            }
        }

        return new PartitionState(
            fillItemCount,
            strokeItemCount,
            totalEdgeCount,
            singleBandItemCount,
            smallEdgeItemCount,
            currentLayerDepth,
            maxLayerDepth,
            rowBuilders);
    }

    /// <summary>
    /// Prepares one path-backed composition command, retaining an apply, layer, or fill scene item
    /// and distributing its row operations into the partition-local row builders.
    /// </summary>
    /// <param name="command">The composition command to prepare.</param>
    /// <param name="commandIndex">The original command index; the retained item is stored at this index.</param>
    /// <param name="clipState">The composed clip state resolved from the clip stream at this command.</param>
    /// <param name="clipAnchor">The destination offset the open clip scopes are anchored at.</param>
    /// <param name="targetBounds">The destination bounds of the flush.</param>
    /// <param name="firstTargetRowBandIndex">The first row-band index covered by the target.</param>
    /// <param name="rowBuilders">The partition-owned row builders, one slot per target row band.</param>
    /// <param name="allocator">The allocator used for retained row storage.</param>
    /// <param name="fillItems">The shared retained fill items indexed by original command index.</param>
    /// <param name="strokeItems">The shared retained stroke items indexed by original command index.</param>
    /// <param name="layers">The shared retained layer state indexed by begin-layer command index.</param>
    /// <param name="controlItems">The shared layer and apply control operations; empty when the batch has no apply barriers.</param>
    /// <param name="fillItemCount">The running partition fill item count.</param>
    /// <param name="strokeItemCount">The running partition stroke item count.</param>
    /// <param name="totalEdgeCount">The running total of encoded raster edges.</param>
    /// <param name="singleBandItemCount">The running count of items that occupy a single row band.</param>
    /// <param name="smallEdgeItemCount">The running count of covered bands with a small edge count.</param>
    /// <param name="currentLayerDepth">The running layer depth relative to the partition start.</param>
    /// <param name="maxLayerDepth">The maximum layer depth observed relative to the partition start.</param>
    private static void ProcessPathCommand(
        in CompositionCommand command,
        int commandIndex,
        DrawingClipState clipState,
        Point clipAnchor,
        in Rectangle targetBounds,
        int firstTargetRowBandIndex,
        RowBuilder[] rowBuilders,
        MemoryAllocator allocator,
        FillSceneItem?[] fillItems,
        StrokeSceneItem?[] strokeItems,
        DrawingCanvasLayer?[] layers,
        SceneControlItem?[] controlItems,
        ref int fillItemCount,
        ref int strokeItemCount,
        ref long totalEdgeCount,
        ref int singleBandItemCount,
        ref int smallEdgeItemCount,
        ref int currentLayerDepth,
        ref int maxLayerDepth)
    {
        if (command.Kind == CompositionCommandKind.Apply)
        {
            RectangleF rawOutputBounds = RectangleF.Transform(command.ApplyOutputBounds, command.DrawingOptions.Transform);
            Rectangle outputRect = Rectangle.Intersect(command.ApplyCanvasBounds, ToConservativeBounds(rawOutputBounds));
            if (outputRect.Width <= 0 || outputRect.Height <= 0)
            {
                return;
            }

            if (!TryPrepareFillPath(
                    command.SourcePath,
                    Brushes.Solid(Color.White),
                    command.DrawingOptions,
                    command.RasterizerOptions,
                    command.TargetBounds,
                    command.DestinationOffset,
                    allocator,
                    out PreparedFillItem preparedApply))
            {
                return;
            }

            if (!PreparedPathClipState.TryCreate(
                    clipState,
                    command.TargetBounds,
                    clipAnchor,
                    allocator,
                    out PreparedPathClipState? preparedApplyPathClips))
            {
                preparedApply.Rasterizable.Dispose();
                return;
            }

            Point brushOffset = new(
                outputRect.X - (int)MathF.Floor(rawOutputBounds.Left),
                outputRect.Y - (int)MathF.Floor(rawOutputBounds.Top));

            controlItems[commandIndex] = new SceneControlItem(
                new ApplySceneItem(
                    command.ApplyBarrier.Operation,
                    outputRect,
                    outputRect,
                    command.ApplyBarrier.WriteBackOffset,
                    brushOffset,
                    preparedApply.GraphicsOptions,
                    preparedApply.BrushBounds,
                    preparedApply.Rasterizable,
                    clipState,
                    preparedApplyPathClips,
                    clipAnchor,
                    command.OwnerLayer));
            return;
        }

        if (TryGetLayerOperation(
            command,
            targetBounds,
            firstTargetRowBandIndex,
            out CompositionCommandKind operationKind,
            out Rectangle layerBounds,
            out int firstRowSlot,
            out int lastRowSlot))
        {
            if (operationKind == CompositionCommandKind.BeginLayer)
            {
                currentLayerDepth++;
                maxLayerDepth = Math.Max(maxLayerDepth, currentLayerDepth);
            }
            else
            {
                currentLayerDepth--;
            }

            int layerOptionsIndex = commandIndex;
            if (operationKind == CompositionCommandKind.BeginLayer)
            {
                // BeginLayer carries the shared layer state used later by the matching EndLayer.
                // Store it at the command index so row operations can keep a compact integer reference.
                layers[commandIndex] = command.Layer;
            }

            if (controlItems.Length != 0)
            {
                controlItems[commandIndex] = new SceneControlItem(operationKind, layerBounds, command.Layer);
            }

            AppendLayerOperations(rowBuilders, firstRowSlot, lastRowSlot, layerBounds, operationKind, layerOptionsIndex, targetBounds, allocator);
            return;
        }

        if (!IsSceneDrawable(command))
        {
            return;
        }

        if (!TryPrepareFillPath(command, allocator, out PreparedFillItem preparedFill) ||
            preparedFill.Rasterizable.RowBandCount == 0)
        {
            return;
        }

        if (!PreparedPathClipState.TryCreate(
                clipState,
                command.TargetBounds,
                clipAnchor,
                allocator,
                out PreparedPathClipState? preparedPathClips))
        {
            preparedFill.Rasterizable.Dispose();
            return;
        }

        fillItems[commandIndex] = new FillSceneItem(
            preparedFill.Brush,
            preparedFill.GraphicsOptions,
            preparedFill.BrushBounds,
            preparedFill.Rasterizable,
            clipState,
            preparedPathClips,
            clipAnchor,
            command.OwnerLayer);
        fillItemCount++;
        AccumulateFillItemStats(preparedFill.Rasterizable, ref totalEdgeCount, ref smallEdgeItemCount, ref singleBandItemCount);
        GetEffectiveRowSlotRange(command.TargetBounds, firstTargetRowBandIndex, rowBuilders.Length, command.IsInsideLayer, out int rowStart, out int rowEnd);
        AppendFillRowOperations(rowBuilders, rowStart, rowEnd, firstTargetRowBandIndex, commandIndex, preparedFill.Rasterizable, allocator);
    }

    /// <summary>
    /// Prepares one stroked path command, retaining a stroke scene item and distributing its row
    /// operations into the partition-local row builders.
    /// </summary>
    /// <param name="command">The stroke path command to prepare.</param>
    /// <param name="commandIndex">The original command index; the retained item is stored at this index.</param>
    /// <param name="clipState">The composed clip state resolved from the clip stream at this command.</param>
    /// <param name="clipAnchor">The destination offset the open clip scopes are anchored at.</param>
    /// <param name="targetRowCount">The number of row bands covered by the target.</param>
    /// <param name="firstTargetRowBandIndex">The first row-band index covered by the target.</param>
    /// <param name="rowBuilders">The partition-owned row builders, one slot per target row band.</param>
    /// <param name="allocator">The allocator used for retained row storage.</param>
    /// <param name="strokeItems">The shared retained stroke items indexed by original command index.</param>
    /// <param name="strokeItemCount">The running partition stroke item count.</param>
    /// <param name="totalEdgeCount">The running total of encoded raster edges.</param>
    /// <param name="singleBandItemCount">The running count of items that occupy a single row band.</param>
    /// <param name="smallEdgeItemCount">The running count of covered bands with a small edge count.</param>
    private static void ProcessStrokePathCommand(
        in StrokePathCommand command,
        int commandIndex,
        DrawingClipState clipState,
        Point clipAnchor,
        int targetRowCount,
        int firstTargetRowBandIndex,
        RowBuilder[] rowBuilders,
        MemoryAllocator allocator,
        StrokeSceneItem?[] strokeItems,
        ref int strokeItemCount,
        ref long totalEdgeCount,
        ref int singleBandItemCount,
        ref int smallEdgeItemCount)
    {
        if (!TryPrepareStrokePath(command, allocator, out PreparedStrokeItem preparedStroke) ||
            preparedStroke.Rasterizable.RowBandCount == 0)
        {
            return;
        }

        if (!PreparedPathClipState.TryCreate(
                clipState,
                command.TargetBounds,
                clipAnchor,
                allocator,
                out PreparedPathClipState? preparedPathClips))
        {
            preparedStroke.Rasterizable.Dispose();
            return;
        }

        strokeItems[commandIndex] = new StrokeSceneItem(
            preparedStroke.Brush,
            preparedStroke.GraphicsOptions,
            preparedStroke.BrushBounds,
            preparedStroke.Rasterizable,
            clipState,
            preparedPathClips,
            clipAnchor,
            command.OwnerLayer);
        strokeItemCount++;
        AccumulateStrokeItemStats(preparedStroke.Rasterizable, ref totalEdgeCount, ref smallEdgeItemCount, ref singleBandItemCount);
        GetEffectiveRowSlotRange(command.TargetBounds, firstTargetRowBandIndex, targetRowCount, command.IsInsideLayer, out int rowStart, out int rowEnd);
        AppendStrokeRowOperations(rowBuilders, rowStart, rowEnd, firstTargetRowBandIndex, commandIndex, preparedStroke.Rasterizable, allocator);
    }

    /// <summary>
    /// Prepares one stroked line segment command, retaining a stroke scene item and distributing its
    /// row operations into the partition-local row builders.
    /// </summary>
    /// <param name="command">The line segment stroke command to prepare.</param>
    /// <param name="commandIndex">The original command index; the retained item is stored at this index.</param>
    /// <param name="clipState">The composed clip state resolved from the clip stream at this command.</param>
    /// <param name="clipAnchor">The destination offset the open clip scopes are anchored at.</param>
    /// <param name="targetRowCount">The number of row bands covered by the target.</param>
    /// <param name="firstTargetRowBandIndex">The first row-band index covered by the target.</param>
    /// <param name="rowBuilders">The partition-owned row builders, one slot per target row band.</param>
    /// <param name="allocator">The allocator used for retained row storage.</param>
    /// <param name="strokeItems">The shared retained stroke items indexed by original command index.</param>
    /// <param name="strokeItemCount">The running partition stroke item count.</param>
    /// <param name="totalEdgeCount">The running total of encoded raster edges.</param>
    /// <param name="singleBandItemCount">The running count of items that occupy a single row band.</param>
    /// <param name="smallEdgeItemCount">The running count of covered bands with a small edge count.</param>
    private static void ProcessLineSegmentCommand(
        in StrokeLineSegmentCommand command,
        int commandIndex,
        DrawingClipState clipState,
        Point clipAnchor,
        int targetRowCount,
        int firstTargetRowBandIndex,
        RowBuilder[] rowBuilders,
        MemoryAllocator allocator,
        StrokeSceneItem?[] strokeItems,
        ref int strokeItemCount,
        ref long totalEdgeCount,
        ref int singleBandItemCount,
        ref int smallEdgeItemCount)
    {
        if (!TryPrepareLineSegmentStroke(command, allocator, out PreparedStrokeItem preparedStroke) ||
            preparedStroke.Rasterizable.RowBandCount == 0)
        {
            return;
        }

        if (!PreparedPathClipState.TryCreate(
                clipState,
                command.TargetBounds,
                clipAnchor,
                allocator,
                out PreparedPathClipState? preparedPathClips))
        {
            preparedStroke.Rasterizable.Dispose();
            return;
        }

        strokeItems[commandIndex] = new StrokeSceneItem(
            preparedStroke.Brush,
            preparedStroke.GraphicsOptions,
            preparedStroke.BrushBounds,
            preparedStroke.Rasterizable,
            clipState,
            preparedPathClips,
            clipAnchor,
            command.OwnerLayer);
        strokeItemCount++;
        AccumulateStrokeItemStats(preparedStroke.Rasterizable, ref totalEdgeCount, ref smallEdgeItemCount, ref singleBandItemCount);
        GetEffectiveRowSlotRange(command.TargetBounds, firstTargetRowBandIndex, targetRowCount, command.IsInsideLayer, out int rowStart, out int rowEnd);
        AppendStrokeRowOperations(rowBuilders, rowStart, rowEnd, firstTargetRowBandIndex, commandIndex, preparedStroke.Rasterizable, allocator);
    }

    /// <summary>
    /// Prepares one stroked polyline command, retaining a stroke scene item and distributing its row
    /// operations into the partition-local row builders.
    /// </summary>
    /// <param name="command">The polyline stroke command to prepare.</param>
    /// <param name="commandIndex">The original command index; the retained item is stored at this index.</param>
    /// <param name="clipState">The composed clip state resolved from the clip stream at this command.</param>
    /// <param name="clipAnchor">The destination offset the open clip scopes are anchored at.</param>
    /// <param name="targetRowCount">The number of row bands covered by the target.</param>
    /// <param name="firstTargetRowBandIndex">The first row-band index covered by the target.</param>
    /// <param name="rowBuilders">The partition-owned row builders, one slot per target row band.</param>
    /// <param name="allocator">The allocator used for retained row storage.</param>
    /// <param name="strokeItems">The shared retained stroke items indexed by original command index.</param>
    /// <param name="strokeItemCount">The running partition stroke item count.</param>
    /// <param name="totalEdgeCount">The running total of encoded raster edges.</param>
    /// <param name="singleBandItemCount">The running count of items that occupy a single row band.</param>
    /// <param name="smallEdgeItemCount">The running count of covered bands with a small edge count.</param>
    private static void ProcessPolylineCommand(
        in StrokePolylineCommand command,
        int commandIndex,
        DrawingClipState clipState,
        Point clipAnchor,
        int targetRowCount,
        int firstTargetRowBandIndex,
        RowBuilder[] rowBuilders,
        MemoryAllocator allocator,
        StrokeSceneItem?[] strokeItems,
        ref int strokeItemCount,
        ref long totalEdgeCount,
        ref int singleBandItemCount,
        ref int smallEdgeItemCount)
    {
        if (!TryPreparePolylineStroke(command, allocator, out PreparedStrokeItem preparedStroke) ||
            preparedStroke.Rasterizable.RowBandCount == 0)
        {
            return;
        }

        if (!PreparedPathClipState.TryCreate(
                clipState,
                command.TargetBounds,
                clipAnchor,
                allocator,
                out PreparedPathClipState? preparedPathClips))
        {
            preparedStroke.Rasterizable.Dispose();
            return;
        }

        strokeItems[commandIndex] = new StrokeSceneItem(
            preparedStroke.Brush,
            preparedStroke.GraphicsOptions,
            preparedStroke.BrushBounds,
            preparedStroke.Rasterizable,
            clipState,
            preparedPathClips,
            clipAnchor,
            command.OwnerLayer);
        strokeItemCount++;
        AccumulateStrokeItemStats(preparedStroke.Rasterizable, ref totalEdgeCount, ref smallEdgeItemCount, ref singleBandItemCount);
        GetEffectiveRowSlotRange(command.TargetBounds, firstTargetRowBandIndex, targetRowCount, command.IsInsideLayer, out int rowStart, out int rowEnd);
        AppendStrokeRowOperations(rowBuilders, rowStart, rowEnd, firstTargetRowBandIndex, commandIndex, preparedStroke.Rasterizable, allocator);
    }

    /// <summary>
    /// Appends one begin or end layer operation into every row builder the layer touches so that
    /// per-row rendering can open and close the layer at the correct point in row order.
    /// </summary>
    /// <param name="rowBuilders">The partition-owned row builders, one slot per target row band.</param>
    /// <param name="firstRowSlot">The first row slot touched by the layer.</param>
    /// <param name="lastRowSlot">The last row slot touched by the layer, inclusive.</param>
    /// <param name="layerBandBounds">The layer bounds intersected with the target bounds.</param>
    /// <param name="operationKind">The begin or end layer operation kind.</param>
    /// <param name="layerOptionsIndex">The command index where the shared layer state is stored.</param>
    /// <param name="targetBounds">The destination bounds of the flush.</param>
    /// <param name="allocator">The allocator used for row-block storage.</param>
    private static void AppendLayerOperations(
        RowBuilder[] rowBuilders,
        int firstRowSlot,
        int lastRowSlot,
        Rectangle layerBandBounds,
        CompositionCommandKind operationKind,
        int layerOptionsIndex,
        in Rectangle targetBounds,
        MemoryAllocator allocator)
    {
        for (int rowSlot = firstRowSlot; rowSlot <= lastRowSlot; rowSlot++)
        {
            ref RowBuilder builder = ref rowBuilders[rowSlot];
            if (!builder.IsInitialized)
            {
                builder = new RowBuilder(allocator);
            }

            int rowTop = targetBounds.Top + (rowSlot * DefaultRasterizer.DefaultTileHeight);
            Rectangle rowBounds = new(targetBounds.Left, rowTop, targetBounds.Width, DefaultRasterizer.DefaultTileHeight);
            Rectangle rowLayerBounds = Rectangle.Intersect(layerBandBounds, rowBounds);
            builder.Append(new SceneOperation(operationKind, rowLayerBounds, layerOptionsIndex));
        }
    }

    /// <summary>
    /// Prepares the retained fill payload for one fill-layer composition command.
    /// </summary>
    /// <param name="command">The command supplying the path, brush, and options.</param>
    /// <param name="allocator">The allocator used for retained raster storage.</param>
    /// <param name="prepared">The prepared fill payload when successful.</param>
    /// <returns><see langword="true"/> when the fill intersects the target and rasterizable geometry was created; otherwise <see langword="false"/>.</returns>
    private static bool TryPrepareFillPath(
        in CompositionCommand command,
        MemoryAllocator allocator,
        out PreparedFillItem prepared)
        => TryPrepareFillPath(
            command.SourcePath,
            command.Brush,
            command.DrawingOptions,
            command.RasterizerOptions,
            command.TargetBounds,
            command.DestinationOffset,
            allocator,
            out prepared);

    /// <summary>
    /// Prepares the retained fill payload for a path. The transform is split into an axis-aligned
    /// scale, applied while flattening so curve tolerance tracks the device resolution, and a
    /// residual matrix applied during rasterization.
    /// </summary>
    /// <param name="path">The source path to fill.</param>
    /// <param name="sourceBrush">The brush recorded with the command.</param>
    /// <param name="drawingOptions">The drawing options supplying the transform and graphics options.</param>
    /// <param name="sourceRasterizerOptions">The rasterizer options recorded with the command.</param>
    /// <param name="targetBounds">The destination bounds of the flush.</param>
    /// <param name="destinationOffset">The offset from path-local to destination coordinates.</param>
    /// <param name="allocator">The allocator used for retained raster storage.</param>
    /// <param name="prepared">The prepared fill payload when successful.</param>
    /// <returns><see langword="true"/> when the fill intersects the target and rasterizable geometry was created; otherwise <see langword="false"/>.</returns>
    internal static bool TryPrepareFillPath(
        IPath path,
        Brush sourceBrush,
        DrawingOptions drawingOptions,
        in RasterizerOptions sourceRasterizerOptions,
        Rectangle targetBounds,
        Point destinationOffset,
        MemoryAllocator allocator,
        out PreparedFillItem prepared)
    {
        Matrix4x4 transform = drawingOptions.Transform;
        Vector2 scale = MatrixUtilities.GetScale(transform);
        Matrix4x4 residual = MatrixUtilities.GetResidual(scale, transform);
        LinearGeometry geometry = path.ToLinearGeometry(scale);
        RectangleF geometryBounds = residual.IsIdentity ? geometry.Info.Bounds : RectangleF.Transform(geometry.Info.Bounds, residual);

        if (!TryResolveRasterization(
                sourceBrush,
                geometryBounds,
                sourceRasterizerOptions,
                destinationOffset,
                targetBounds,
                out Brush brush,
                out RasterizerOptions rasterizerOptions,
                out Rectangle brushBounds))
        {
            prepared = default;
            return false;
        }

        DefaultRasterizer.RasterizableGeometry? rasterizable = DefaultRasterizer.CreateRasterizableGeometry(
            geometry,
            residual,
            destinationOffset.X,
            destinationOffset.Y,
            rasterizerOptions,
            allocator);

        if (rasterizable is null)
        {
            prepared = default;
            return false;
        }

        prepared = new PreparedFillItem(brush, drawingOptions.GraphicsOptions, brushBounds, rasterizable);
        return true;
    }

    /// <summary>
    /// Prepares the retained stroke payload for a stroked path. The transform is split into an
    /// axis-aligned scale, applied while flattening, and a residual matrix applied during
    /// rasterization; the stroke width is scaled separately by the transform's determinant scale.
    /// </summary>
    /// <param name="command">The command supplying the path, pen, brush, and options.</param>
    /// <param name="allocator">The allocator used for retained raster storage.</param>
    /// <param name="prepared">The prepared stroke payload when successful.</param>
    /// <returns><see langword="true"/> when the stroke intersects the target and rasterizable geometry was created; otherwise <see langword="false"/>.</returns>
    private static bool TryPrepareStrokePath(
        in StrokePathCommand command,
        MemoryAllocator allocator,
        out PreparedStrokeItem prepared)
    {
        IPath path = command.SourcePath;
        Matrix4x4 transform = command.Transform;
        Vector2 scale = MatrixUtilities.GetScale(transform);
        Matrix4x4 residual = MatrixUtilities.GetResidual(scale, transform);
        LinearGeometry geometry = path.ToLinearGeometry(scale);
        float widthScale = GetTransformWidthScale(transform);
        RectangleF geometryBounds = residual.IsIdentity ? geometry.Info.Bounds : RectangleF.Transform(geometry.Info.Bounds, residual);
        RectangleF strokeBounds = GetStrokeBounds(geometryBounds, command.Pen, widthScale);
        Brush sourceBrush = command.Brush;

        if (!TryResolveRasterization(
                sourceBrush,
                strokeBounds,
                command.RasterizerOptions,
                command.DestinationOffset,
                command.TargetBounds,
                out Brush brush,
                out RasterizerOptions rasterizerOptions,
                out Rectangle brushBounds))
        {
            prepared = default;
            return false;
        }

        DefaultRasterizer.StrokeRasterizableGeometry? rasterizable = DefaultRasterizer.CreatePathStrokeRasterizableGeometry(
            geometry,
            residual,
            command.Pen,
            command.DestinationOffset.X,
            command.DestinationOffset.Y,
            rasterizerOptions,
            widthScale,
            allocator);
        if (rasterizable is null)
        {
            prepared = default;
            return false;
        }

        prepared = new PreparedStrokeItem(brush, command.GraphicsOptions, brushBounds, rasterizable);
        return true;
    }

    /// <summary>
    /// Prepares the retained stroke payload for a single line segment. The endpoints are fully
    /// transformed up front, so no residual matrix is needed at rasterization time.
    /// </summary>
    /// <param name="command">The command supplying the endpoints, pen, brush, and options.</param>
    /// <param name="allocator">The allocator used for retained raster storage.</param>
    /// <param name="prepared">The prepared stroke payload when successful.</param>
    /// <returns><see langword="true"/> when the stroke intersects the target and rasterizable geometry was created; otherwise <see langword="false"/>.</returns>
    private static bool TryPrepareLineSegmentStroke(
        in StrokeLineSegmentCommand command,
        MemoryAllocator allocator,
        out PreparedStrokeItem prepared)
    {
        Matrix4x4 transform = command.Transform;
        bool hasTransform = !transform.IsIdentity;
        PointF start = hasTransform ? PointF.Transform(command.SourceStart, transform) : command.SourceStart;
        PointF end = hasTransform ? PointF.Transform(command.SourceEnd, transform) : command.SourceEnd;
        float widthScale = GetTransformWidthScale(transform);
        RectangleF segmentBounds = RectangleF.FromLTRB(
            MathF.Min(start.X, end.X),
            MathF.Min(start.Y, end.Y),
            MathF.Max(start.X, end.X),
            MathF.Max(start.Y, end.Y));
        RectangleF bounds = GetStrokeBounds(segmentBounds, command.Pen, widthScale);
        Brush sourceBrush = command.Brush;

        if (!TryResolveRasterization(
                sourceBrush,
                bounds,
                command.RasterizerOptions,
                command.DestinationOffset,
                command.TargetBounds,
                out Brush brush,
                out RasterizerOptions rasterizerOptions,
                out Rectangle brushBounds))
        {
            prepared = default;
            return false;
        }

        DefaultRasterizer.StrokeRasterizableGeometry? rasterizable = DefaultRasterizer.CreateLineSegmentStrokeRasterizableGeometry(
            start,
            end,
            command.Pen,
            command.DestinationOffset.X,
            command.DestinationOffset.Y,
            rasterizerOptions,
            widthScale,
            allocator);

        if (rasterizable is null)
        {
            prepared = default;
            return false;
        }

        prepared = new PreparedStrokeItem(brush, command.GraphicsOptions, brushBounds, rasterizable);
        return true;
    }

    /// <summary>
    /// Prepares the retained stroke payload for an open polyline using the same scale and residual
    /// transform split as stroked paths.
    /// </summary>
    /// <param name="command">The command supplying the points, pen, brush, and options.</param>
    /// <param name="allocator">The allocator used for retained raster storage.</param>
    /// <param name="prepared">The prepared stroke payload when successful.</param>
    /// <returns><see langword="true"/> when the stroke intersects the target and rasterizable geometry was created; otherwise <see langword="false"/>.</returns>
    private static bool TryPreparePolylineStroke(
        in StrokePolylineCommand command,
        MemoryAllocator allocator,
        out PreparedStrokeItem prepared)
    {
        Matrix4x4 transform = command.Transform;
        Vector2 scale = MatrixUtilities.GetScale(transform);
        Matrix4x4 residual = MatrixUtilities.GetResidual(scale, transform);
        LinearGeometry geometry = LinearGeometry.CreateOpenPolyline(command.SourcePoints, scale);
        float widthScale = GetTransformWidthScale(transform);
        RectangleF geometryBounds = residual.IsIdentity ? geometry.Info.Bounds : RectangleF.Transform(geometry.Info.Bounds, residual);
        RectangleF strokeBounds = GetStrokeBounds(geometryBounds, command.Pen, widthScale);
        Brush sourceBrush = command.Brush;

        if (!TryResolveRasterization(
                sourceBrush,
                strokeBounds,
                command.RasterizerOptions,
                command.DestinationOffset,
                command.TargetBounds,
                out Brush brush,
                out RasterizerOptions rasterizerOptions,
                out Rectangle brushBounds))
        {
            prepared = default;
            return false;
        }

        DefaultRasterizer.StrokeRasterizableGeometry? rasterizable = DefaultRasterizer.CreatePathStrokeRasterizableGeometry(
            geometry,
            residual,
            command.Pen,
            command.DestinationOffset.X,
            command.DestinationOffset.Y,
            rasterizerOptions,
            widthScale,
            allocator);

        if (rasterizable is null)
        {
            prepared = default;
            return false;
        }

        prepared = new PreparedStrokeItem(brush, command.GraphicsOptions, brushBounds, rasterizable);
        return true;
    }

    /// <summary>
    /// Resolves the destination-clipped rasterizer options and brush bounds for a draw operation,
    /// rejecting operations whose interest area does not intersect the target.
    /// </summary>
    /// <param name="brush">The source brush.</param>
    /// <param name="bounds">The path-local geometry bounds after stroke and residual expansion.</param>
    /// <param name="options">The rasterizer options recorded with the command.</param>
    /// <param name="destinationOffset">The offset from path-local to destination coordinates.</param>
    /// <param name="targetBounds">The destination bounds of the flush.</param>
    /// <param name="resolvedBrush">The brush to use for rendering.</param>
    /// <param name="resolvedOptions">The rasterizer options clipped to the visible destination.</param>
    /// <param name="brushBounds">The unclipped absolute interest area used for applicator creation.</param>
    /// <returns><see langword="true"/> when the operation is visible within the target; otherwise <see langword="false"/>.</returns>
    private static bool TryResolveRasterization(
        Brush brush,
        RectangleF bounds,
        in RasterizerOptions options,
        Point destinationOffset,
        in Rectangle targetBounds,
        out Brush resolvedBrush,
        out RasterizerOptions resolvedOptions,
        out Rectangle brushBounds)
    {
        resolvedBrush = brush;

        Rectangle localInterest = Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right) + 1,
            (int)MathF.Ceiling(bounds.Bottom) + 1);

        Rectangle absoluteInterest = new(
            localInterest.X + destinationOffset.X,
            localInterest.Y + destinationOffset.Y,
            localInterest.Width,
            localInterest.Height);

        Rectangle clippedDestination = Rectangle.Intersect(targetBounds, absoluteInterest);

        if (clippedDestination.Width <= 0 || clippedDestination.Height <= 0)
        {
            resolvedOptions = default;
            brushBounds = default;
            return false;
        }

        resolvedOptions = new RasterizerOptions(
            clippedDestination,
            options.IntersectionRule,
            options.RasterizationMode,
            options.AntialiasThreshold,
            options.CoverageBoost);

        brushBounds = absoluteInterest;
        return true;
    }

    /// <summary>
    /// Rounds fractional bounds outward to the smallest enclosing integer rectangle.
    /// </summary>
    /// <param name="bounds">The fractional bounds.</param>
    /// <returns>The enclosing integer rectangle.</returns>
    private static Rectangle ToConservativeBounds(RectangleF bounds)
        => Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));

    /// <summary>
    /// Expands path bounds by the stroke joins and caps that can extend past the path geometry.
    /// </summary>
    /// <param name="bounds">The path geometry bounds before stroke inflation.</param>
    /// <param name="pen">The pen that defines stroke width, joins, caps, and miter limit.</param>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <returns>The inflated stroke bounds.</returns>
    private static RectangleF GetStrokeBounds(RectangleF bounds, Pen pen, float widthScale)
    {
        float halfWidth = pen.StrokeWidth * widthScale * 0.5F;
        float joinInflate = pen.StrokeOptions.LineJoin switch
        {
            LineJoin.Miter or LineJoin.MiterRevert or LineJoin.MiterRound => (float)(halfWidth * Math.Max(pen.StrokeOptions.MiterLimit, 1D)),
            _ => halfWidth
        };

        float capInflate = pen.StrokeOptions.LineCap == LineCap.Square
            ? halfWidth * MathF.Sqrt(2F)
            : halfWidth;

        float inflate = MathF.Max(joinInflate, capInflate);

        bounds.Inflate(new SizeF(inflate, inflate));
        return bounds;
    }

    /// <summary>
    /// Returns the isotropic scale factor embedded in a drawing transform so stroke widths match device-space pixels.
    /// </summary>
    /// <param name="transform">The transform to inspect.</param>
    /// <returns>The scale factor to apply to stroke widths.</returns>
    /// <remarks>
    /// Uses the square root of the absolute 2D determinant, the SVG-style fallback for non-uniform
    /// scale. Reduces to the uniform scale for pure scale/rotate/translate matrices.
    /// </remarks>
    private static float GetTransformWidthScale(Matrix4x4 transform)
    {
        if (transform.IsIdentity)
        {
            return 1F;
        }

        float det = (transform.M11 * transform.M22) - (transform.M12 * transform.M21);
        return MathF.Sqrt(MathF.Abs(det));
    }

    /// <summary>
    /// Holds the prepared payload for a fill operation before it is committed to a scene item.
    /// </summary>
    internal readonly struct PreparedFillItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PreparedFillItem"/> struct.
        /// </summary>
        /// <param name="brush">The resolved brush.</param>
        /// <param name="graphicsOptions">The graphics options for the fill.</param>
        /// <param name="brushBounds">The absolute brush bounds used for applicator creation.</param>
        /// <param name="rasterizable">The retained rasterizable geometry.</param>
        public PreparedFillItem(
            Brush brush,
            GraphicsOptions graphicsOptions,
            Rectangle brushBounds,
            DefaultRasterizer.RasterizableGeometry rasterizable)
        {
            this.Brush = brush;
            this.GraphicsOptions = graphicsOptions;
            this.BrushBounds = brushBounds;
            this.Rasterizable = rasterizable;
        }

        /// <summary>
        /// Gets the resolved brush.
        /// </summary>
        public Brush Brush { get; }

        /// <summary>
        /// Gets the graphics options for the fill.
        /// </summary>
        public GraphicsOptions GraphicsOptions { get; }

        /// <summary>
        /// Gets the absolute brush bounds used for applicator creation.
        /// </summary>
        public Rectangle BrushBounds { get; }

        /// <summary>
        /// Gets the retained rasterizable geometry.
        /// </summary>
        public DefaultRasterizer.RasterizableGeometry Rasterizable { get; }
    }

    /// <summary>
    /// Holds the prepared payload for a stroke operation before it is committed to a scene item.
    /// </summary>
    private readonly struct PreparedStrokeItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PreparedStrokeItem"/> struct.
        /// </summary>
        /// <param name="brush">The resolved brush.</param>
        /// <param name="graphicsOptions">The graphics options for the stroke.</param>
        /// <param name="brushBounds">The absolute brush bounds used for applicator creation.</param>
        /// <param name="rasterizable">The retained stroke rasterizable geometry.</param>
        public PreparedStrokeItem(
            Brush brush,
            GraphicsOptions graphicsOptions,
            Rectangle brushBounds,
            DefaultRasterizer.StrokeRasterizableGeometry rasterizable)
        {
            this.Brush = brush;
            this.GraphicsOptions = graphicsOptions;
            this.BrushBounds = brushBounds;
            this.Rasterizable = rasterizable;
        }

        /// <summary>
        /// Gets the resolved brush.
        /// </summary>
        public Brush Brush { get; }

        /// <summary>
        /// Gets the graphics options for the stroke.
        /// </summary>
        public GraphicsOptions GraphicsOptions { get; }

        /// <summary>
        /// Gets the absolute brush bounds used for applicator creation.
        /// </summary>
        public Rectangle BrushBounds { get; }

        /// <summary>
        /// Gets the retained stroke rasterizable geometry.
        /// </summary>
        public DefaultRasterizer.StrokeRasterizableGeometry Rasterizable { get; }
    }

    /// <summary>
    /// Holds the counts and row builders produced by one partition, merged sequentially after all
    /// partitions complete. Layer depths are relative to the partition start; the merge composes
    /// them using the running depth carried across partitions.
    /// </summary>
    private readonly struct PartitionState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PartitionState"/> struct.
        /// </summary>
        /// <param name="fillItemCount">The number of fill items retained by the partition.</param>
        /// <param name="strokeItemCount">The number of stroke items retained by the partition.</param>
        /// <param name="totalEdgeCount">The total number of encoded raster edges retained by the partition.</param>
        /// <param name="singleBandItemCount">The number of items that occupy a single row band.</param>
        /// <param name="smallEdgeItemCount">The number of covered bands with a small edge count.</param>
        /// <param name="layerDepthDelta">The net begin minus end layer count across the partition.</param>
        /// <param name="maxLayerDepth">The maximum layer depth reached, relative to the partition start.</param>
        /// <param name="rowBuilders">The partition-owned row builders, one slot per target row band.</param>
        public PartitionState(
            int fillItemCount,
            int strokeItemCount,
            long totalEdgeCount,
            int singleBandItemCount,
            int smallEdgeItemCount,
            int layerDepthDelta,
            int maxLayerDepth,
            RowBuilder[] rowBuilders)
        {
            this.FillItemCount = fillItemCount;
            this.StrokeItemCount = strokeItemCount;
            this.TotalEdgeCount = totalEdgeCount;
            this.SingleBandItemCount = singleBandItemCount;
            this.SmallEdgeItemCount = smallEdgeItemCount;
            this.LayerDepthDelta = layerDepthDelta;
            this.MaxLayerDepth = maxLayerDepth;
            this.RowBuilders = rowBuilders;
        }

        /// <summary>
        /// Gets the number of fill items retained by the partition.
        /// </summary>
        public int FillItemCount { get; }

        /// <summary>
        /// Gets the number of stroke items retained by the partition.
        /// </summary>
        public int StrokeItemCount { get; }

        /// <summary>
        /// Gets the total number of encoded raster edges retained by the partition.
        /// </summary>
        public long TotalEdgeCount { get; }

        /// <summary>
        /// Gets the number of items that occupy a single row band.
        /// </summary>
        public int SingleBandItemCount { get; }

        /// <summary>
        /// Gets the number of covered bands with a small edge count.
        /// </summary>
        public int SmallEdgeItemCount { get; }

        /// <summary>
        /// Gets the net begin minus end layer count across the partition.
        /// </summary>
        public int LayerDepthDelta { get; }

        /// <summary>
        /// Gets the maximum layer depth reached, relative to the partition start.
        /// </summary>
        public int MaxLayerDepth { get; }

        /// <summary>
        /// Gets the partition-owned row builders, one slot per target row band.
        /// </summary>
        public RowBuilder[] RowBuilders { get; }
    }

    /// <summary>
    /// Holds one open scoped layer while command ownership is assigned.
    /// </summary>
    private readonly struct ScopedLayerBuildFrame
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScopedLayerBuildFrame"/> struct.
        /// </summary>
        /// <param name="parent">The parent sequence receiving the finalized layer.</param>
        /// <param name="layer">The retained layer state.</param>
        /// <param name="bounds">The absolute layer target bounds.</param>
        /// <param name="content">The child sequence receiving layer contents.</param>
        public ScopedLayerBuildFrame(
            SceneSequenceBuilder parent,
            DrawingCanvasLayer layer,
            Rectangle bounds,
            SceneSequenceBuilder content)
        {
            this.Parent = parent;
            this.Layer = layer;
            this.Bounds = bounds;
            this.Content = content;
        }

        /// <summary>
        /// Gets the parent sequence receiving the finalized layer.
        /// </summary>
        public SceneSequenceBuilder Parent { get; }

        /// <summary>
        /// Gets the retained layer state.
        /// </summary>
        public DrawingCanvasLayer Layer { get; }

        /// <summary>
        /// Gets the absolute layer target bounds.
        /// </summary>
        public Rectangle Bounds { get; }

        /// <summary>
        /// Gets the child sequence receiving layer contents.
        /// </summary>
        public SceneSequenceBuilder Content { get; }
    }

    /// <summary>
    /// Builds an ordered sequence of retained row segments.
    /// </summary>
    private sealed class SceneSequenceBuilder
    {
        private readonly MemoryAllocator allocator;
        private readonly int firstTargetRowBandIndex;
        private readonly int targetRowCount;
        private readonly List<SceneSegmentBuilder> segments = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneSequenceBuilder"/> class.
        /// </summary>
        /// <param name="allocator">The allocator used for retained row storage.</param>
        /// <param name="firstTargetRowBandIndex">The first target row-band index.</param>
        /// <param name="targetRowCount">The target row-band count.</param>
        public SceneSequenceBuilder(
            MemoryAllocator allocator,
            int firstTargetRowBandIndex,
            int targetRowCount)
        {
            this.allocator = allocator;
            this.firstTargetRowBandIndex = firstTargetRowBandIndex;
            this.targetRowCount = targetRowCount;
            this.CurrentSegment = this.CreateSegment();
        }

        /// <summary>
        /// Gets the segment currently receiving retained row operations.
        /// </summary>
        public SceneSegmentBuilder CurrentSegment { get; private set; }

        /// <summary>
        /// Appends an apply operation after the current row segment.
        /// </summary>
        /// <param name="applyItem">The retained apply operation.</param>
        public void AddApply(ApplySceneItem applyItem)
        {
            this.CurrentSegment.SetApply(applyItem);
            this.CurrentSegment = this.CreateSegment();
        }

        /// <summary>
        /// Appends a scoped layer after the current row segment.
        /// </summary>
        /// <param name="layerBuilder">The retained scoped layer builder.</param>
        public void AddLayer(ScopedLayerSceneBuilder layerBuilder)
        {
            this.CurrentSegment.SetLayer(layerBuilder);
            this.CurrentSegment = this.CreateSegment();
        }

        /// <summary>
        /// Finalizes all retained segments in this sequence.
        /// </summary>
        /// <param name="rowCount">The total retained row count.</param>
        /// <param name="rowItemCount">The total retained row operation count.</param>
        /// <returns>The finalized retained segments.</returns>
        public SceneSegment[] FinalizeSegments(out int rowCount, out int rowItemCount)
        {
            List<SceneSegment> finalized = [];
            rowCount = 0;
            rowItemCount = 0;

            for (int i = 0; i < this.segments.Count; i++)
            {
                if (this.segments[i].FinalizeSegment(
                    this.firstTargetRowBandIndex,
                    out SceneSegment? segment,
                    out int segmentRowCount,
                    out int segmentRowItemCount))
                {
                    finalized.Add(segment);
                    rowCount += segmentRowCount;
                    rowItemCount += segmentRowItemCount;
                }
            }

            return finalized.Count == 0 ? [] : [.. finalized];
        }

        private SceneSegmentBuilder CreateSegment()
        {
            SceneSegmentBuilder segment = new(this.allocator, this.targetRowCount);
            this.segments.Add(segment);
            return segment;
        }
    }

    /// <summary>
    /// Builds one retained row segment and its optional trailing operation.
    /// </summary>
    private sealed class SceneSegmentBuilder
    {
        private readonly MemoryAllocator allocator;
        private readonly int targetRowCount;
        private RowBuilder[]? rowBuilders;
        private ApplySceneItem? applyItem;
        private ScopedLayerSceneBuilder? layerBuilder;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneSegmentBuilder"/> class.
        /// </summary>
        /// <param name="allocator">The allocator used for retained row storage.</param>
        /// <param name="targetRowCount">The target row-band count.</param>
        public SceneSegmentBuilder(MemoryAllocator allocator, int targetRowCount)
        {
            this.allocator = allocator;
            this.targetRowCount = targetRowCount;
        }

        /// <summary>
        /// Appends one retained row operation to this segment.
        /// </summary>
        /// <param name="rowSlot">The target row slot.</param>
        /// <param name="operation">The retained row operation.</param>
        public void Append(int rowSlot, SceneOperation operation)
        {
            this.rowBuilders ??= new RowBuilder[this.targetRowCount];
            ref RowBuilder builder = ref this.rowBuilders[rowSlot];
            if (!builder.IsInitialized)
            {
                builder = new RowBuilder(this.allocator);
            }

            builder.Append(operation);
        }

        /// <summary>
        /// Sets the apply operation executed after this segment's rows.
        /// </summary>
        /// <param name="item">The retained apply operation.</param>
        public void SetApply(ApplySceneItem item) => this.applyItem = item;

        /// <summary>
        /// Sets the scoped layer executed after this segment's rows.
        /// </summary>
        /// <param name="builder">The retained scoped layer builder.</param>
        public void SetLayer(ScopedLayerSceneBuilder builder) => this.layerBuilder = builder;

        /// <summary>
        /// Finalizes this builder into retained segment storage.
        /// </summary>
        /// <param name="firstTargetRowBandIndex">The first target row-band index.</param>
        /// <param name="segment">The finalized retained segment.</param>
        /// <param name="rowCount">The retained row count in the segment and any child layer.</param>
        /// <param name="rowItemCount">The retained row operation count in the segment and any child layer.</param>
        /// <returns>True when the segment has retained work.</returns>
        public bool FinalizeSegment(
            int firstTargetRowBandIndex,
            [NotNullWhen(true)] out SceneSegment? segment,
            out int rowCount,
            out int rowItemCount)
        {
            int segmentRowCount = 0;
            int segmentRowItemCount = 0;
            SceneRow[] rows = [];

            if (this.rowBuilders is not null)
            {
                for (int i = 0; i < this.rowBuilders.Length; i++)
                {
                    if (!this.rowBuilders[i].IsInitialized)
                    {
                        continue;
                    }

                    segmentRowCount++;
                    segmentRowItemCount += this.rowBuilders[i].Count;
                }

                rows = segmentRowCount == 0
                    ? []
                    : FinalizeRows(this.rowBuilders, firstTargetRowBandIndex, segmentRowCount);
            }

            ScopedLayerSceneItem? layerItem = this.layerBuilder?.FinalizeSceneItem();
            rowCount = segmentRowCount + (layerItem?.RowCount ?? 0);
            rowItemCount = segmentRowItemCount + (layerItem?.RowItemCount ?? 0);

            if (segmentRowCount == 0 && this.applyItem is null && layerItem is null)
            {
                segment = null;
                return false;
            }

            segment = new SceneSegment(rows, segmentRowItemCount, this.applyItem, layerItem);
            return true;
        }
    }

    /// <summary>
    /// Builds a retained scoped layer from child segments.
    /// </summary>
    private sealed class ScopedLayerSceneBuilder
    {
        private readonly DrawingCanvasLayer layer;
        private readonly Rectangle bounds;
        private readonly SceneSequenceBuilder content;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScopedLayerSceneBuilder"/> class.
        /// </summary>
        /// <param name="layer">The retained layer state.</param>
        /// <param name="bounds">The absolute layer target bounds.</param>
        /// <param name="content">The retained layer content builder.</param>
        public ScopedLayerSceneBuilder(
            DrawingCanvasLayer layer,
            Rectangle bounds,
            SceneSequenceBuilder content)
        {
            this.layer = layer;
            this.bounds = bounds;
            this.content = content;
        }

        /// <summary>
        /// Finalizes this builder into retained scoped layer storage.
        /// </summary>
        /// <returns>The finalized retained scoped layer.</returns>
        public ScopedLayerSceneItem FinalizeSceneItem()
        {
            SceneSegment[] segments = this.content.FinalizeSegments(out int rowCount, out int rowItemCount);
            return new ScopedLayerSceneItem(this.layer, this.bounds, segments, rowCount, rowItemCount);
        }
    }

    /// <summary>
    /// Tracks the active clip stack resolved from the ordered begin/end-clip command stream.
    /// </summary>
    /// <remarks>
    /// Clip descriptors are recorded in canvas-local coordinates; the canvas re-anchors the
    /// stream at each consuming state's destination offset, so at every draw command all open
    /// descriptors share one anchor offset.
    /// </remarks>
    private sealed class ClipStreamTracker
    {
        private readonly Stack<DrawingClipState> previous = new();

        /// <summary>
        /// Gets the clip state composed from the currently open clip scopes.
        /// </summary>
        public DrawingClipState Current { get; private set; } = DrawingClipState.Empty;

        /// <summary>
        /// Gets the destination offset the open clip scopes are anchored at.
        /// </summary>
        public Point Anchor { get; private set; }

        /// <summary>
        /// Creates a tracker seeded with the clip scopes opened by earlier partitions.
        /// </summary>
        /// <param name="seed">The seed clip scopes, outermost first.</param>
        /// <returns>The seeded tracker.</returns>
        public static ClipStreamTracker CreateFromSeed((DrawingClipDescriptor Descriptor, Point Anchor)[]? seed)
        {
            ClipStreamTracker tracker = new();
            if (seed is not null)
            {
                for (int i = 0; i < seed.Length; i++)
                {
                    tracker.Push(seed[i].Descriptor, seed[i].Anchor);
                }
            }

            return tracker;
        }

        /// <summary>
        /// Opens one clip scope.
        /// </summary>
        /// <param name="descriptor">The clip descriptor to open.</param>
        /// <param name="anchor">The destination offset the descriptor is anchored at.</param>
        public void Push(DrawingClipDescriptor descriptor, Point anchor)
        {
            this.previous.Push(this.Current);
            this.Current = this.Current.Append(descriptor);
            this.Anchor = anchor;
        }

        /// <summary>
        /// Closes the most recently opened clip scope.
        /// </summary>
        public void Pop() => this.Current = this.previous.Count > 0 ? this.previous.Pop() : DrawingClipState.Empty;
    }
}
