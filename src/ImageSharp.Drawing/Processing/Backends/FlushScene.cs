// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Processing;
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
    internal FillSceneItem?[] FillItems { get; }

    /// <summary>
    /// Gets the retained visible stroke scene items.
    /// </summary>
    internal StrokeSceneItem?[] StrokeItems { get; }

    /// <summary>
    /// Gets the retained layer state indexed by begin-layer command index.
    /// </summary>
    internal DrawingCanvasLayer?[] Layers { get; }

    /// <summary>
    /// Gets layer and apply control operations indexed by original command index.
    /// </summary>
    internal SceneControlItem?[] ControlItems { get; }

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
    internal SceneRow[] Rows { get; }

    /// <summary>
    /// Gets retained target-wide execution segments for scenes containing apply barriers.
    /// </summary>
    internal SceneSegment[] Segments { get; }

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

        for (int i = 0; i < partitionCount; i++)
        {
            PartitionState partition = partitions[i];
            fillItemCount += partition.FillItemCount;
            strokeItemCount += partition.StrokeItemCount;
            totalEdgeCount += partition.TotalEdgeCount;
            singleBandItemCount += partition.SingleBandItemCount;
            smallEdgeItemCount += partition.SmallEdgeItemCount;
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
    /// Creates an empty scene instance.
    /// </summary>
    private static FlushScene Empty() => EmptyScene;

    /// <summary>
    /// Identifies whether a path-backed command contributes executable retained raster work to the scene.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSceneDrawable(in CompositionCommand command)
        => command.Kind == CompositionCommandKind.FillLayer;

    /// <summary>
    /// Accumulates retained fill statistics used for scene heuristics.
    /// </summary>
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
    /// <param name="firstTargetRowBandIndex">The first row-band index covered by the partition.</param>
    /// <param name="totalRowSlots">The total number of row slots owned by the partition.</param>
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
                        ReferenceEquals(scopedLayers[scopedLayers.Count - 1].Layer, controlItem.Layer))
                    {
                        ScopedLayerBuildFrame frame = scopedLayers[scopedLayers.Count - 1];
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
                    commandSegments[operation.ItemIndex]?.Append(rowSlot, operation);
                }
            }
        }

        return root.FinalizeSegments(out rowCount, out rowItemCount);
    }

    private static PartitionState ProcessPartition(
        IReadOnlyList<CompositionSceneCommand> commands,
        int commandStart,
        int commandEnd,
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

        for (int commandIndex = commandStart; commandIndex < commandEnd; commandIndex++)
        {
            CompositionSceneCommand command = commands[commandIndex];
            if (command is PathCompositionSceneCommand pathCommand)
            {
                ProcessPathCommand(
                    pathCommand.Command,
                    commandIndex,
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

    private static void ProcessPathCommand(
        in CompositionCommand command,
        int commandIndex,
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
            RectangleF rawBounds = RectangleF.Transform(command.SourcePath.Bounds, command.DrawingOptions.Transform);
            Rectangle sourceRect = Rectangle.Intersect(command.ApplyBarrier.CanvasBounds, ToConservativeBounds(rawBounds));
            if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
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

            Point brushOffset = new(
                sourceRect.X - (int)MathF.Floor(rawBounds.Left),
                sourceRect.Y - (int)MathF.Floor(rawBounds.Top));

            controlItems[commandIndex] = new SceneControlItem(
                new ApplySceneItem(
                    command.ApplyBarrier.Operation,
                    sourceRect,
                    brushOffset,
                    preparedApply.GraphicsOptions,
                    preparedApply.BrushBounds,
                    preparedApply.Rasterizable,
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

        fillItems[commandIndex] = new FillSceneItem(preparedFill.Brush, preparedFill.GraphicsOptions, preparedFill.BrushBounds, preparedFill.Rasterizable, command.OwnerLayer);
        fillItemCount++;
        AccumulateFillItemStats(preparedFill.Rasterizable, ref totalEdgeCount, ref smallEdgeItemCount, ref singleBandItemCount);
        GetEffectiveRowSlotRange(command.TargetBounds, firstTargetRowBandIndex, rowBuilders.Length, command.IsInsideLayer, out int rowStart, out int rowEnd);
        AppendFillRowOperations(rowBuilders, rowStart, rowEnd, firstTargetRowBandIndex, commandIndex, preparedFill.Rasterizable, allocator);
    }

    private static void ProcessStrokePathCommand(
        in StrokePathCommand command,
        int commandIndex,
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

        strokeItems[commandIndex] = new StrokeSceneItem(preparedStroke.Brush, preparedStroke.GraphicsOptions, preparedStroke.BrushBounds, preparedStroke.Rasterizable, command.OwnerLayer);
        strokeItemCount++;
        AccumulateStrokeItemStats(preparedStroke.Rasterizable, ref totalEdgeCount, ref smallEdgeItemCount, ref singleBandItemCount);
        GetEffectiveRowSlotRange(command.TargetBounds, firstTargetRowBandIndex, targetRowCount, command.IsInsideLayer, out int rowStart, out int rowEnd);
        AppendStrokeRowOperations(rowBuilders, rowStart, rowEnd, firstTargetRowBandIndex, commandIndex, preparedStroke.Rasterizable, allocator);
    }

    private static void ProcessLineSegmentCommand(
        in StrokeLineSegmentCommand command,
        int commandIndex,
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

        strokeItems[commandIndex] = new StrokeSceneItem(preparedStroke.Brush, preparedStroke.GraphicsOptions, preparedStroke.BrushBounds, preparedStroke.Rasterizable, command.OwnerLayer);
        strokeItemCount++;
        AccumulateStrokeItemStats(preparedStroke.Rasterizable, ref totalEdgeCount, ref smallEdgeItemCount, ref singleBandItemCount);
        GetEffectiveRowSlotRange(command.TargetBounds, firstTargetRowBandIndex, targetRowCount, command.IsInsideLayer, out int rowStart, out int rowEnd);
        AppendStrokeRowOperations(rowBuilders, rowStart, rowEnd, firstTargetRowBandIndex, commandIndex, preparedStroke.Rasterizable, allocator);
    }

    private static void ProcessPolylineCommand(
        in StrokePolylineCommand command,
        int commandIndex,
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

        strokeItems[commandIndex] = new StrokeSceneItem(preparedStroke.Brush, preparedStroke.GraphicsOptions, preparedStroke.BrushBounds, preparedStroke.Rasterizable, command.OwnerLayer);
        strokeItemCount++;
        AccumulateStrokeItemStats(preparedStroke.Rasterizable, ref totalEdgeCount, ref smallEdgeItemCount, ref singleBandItemCount);
        GetEffectiveRowSlotRange(command.TargetBounds, firstTargetRowBandIndex, targetRowCount, command.IsInsideLayer, out int rowStart, out int rowEnd);
        AppendStrokeRowOperations(rowBuilders, rowStart, rowEnd, firstTargetRowBandIndex, commandIndex, preparedStroke.Rasterizable, allocator);
    }

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
        bool hasTransform = !transform.IsIdentity;
        Vector2 scale = ExtractScale(transform);
        Matrix4x4 residual = ComputeResidual(scale, transform);
        LinearGeometry geometry = path.ToLinearGeometry(scale);
        sourceBrush = hasTransform ? sourceBrush.Transform(transform) : sourceBrush;
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

    private static bool TryPrepareStrokePath(
        in StrokePathCommand command,
        MemoryAllocator allocator,
        out PreparedStrokeItem prepared)
    {
        IPath path = command.SourcePath;
        Matrix4x4 transform = command.Transform;
        bool hasTransform = !transform.IsIdentity;
        Vector2 scale = ExtractScale(transform);
        Matrix4x4 residual = ComputeResidual(scale, transform);
        LinearGeometry geometry = path.ToLinearGeometry(scale);
        float widthScale = GetTransformWidthScale(transform);
        RectangleF geometryBounds = residual.IsIdentity ? geometry.Info.Bounds : RectangleF.Transform(geometry.Info.Bounds, residual);
        RectangleF strokeBounds = GetStrokeBounds(geometryBounds, command.Pen, widthScale);
        Brush sourceBrush = hasTransform ? command.Brush.Transform(transform) : command.Brush;

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
        Brush sourceBrush = hasTransform ? command.Brush.Transform(transform) : command.Brush;

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

    private static bool TryPreparePolylineStroke(
        in StrokePolylineCommand command,
        MemoryAllocator allocator,
        out PreparedStrokeItem prepared)
    {
        Matrix4x4 transform = command.Transform;
        bool hasTransform = !transform.IsIdentity;
        Vector2 scale = ExtractScale(transform);
        Matrix4x4 residual = ComputeResidual(scale, transform);
        LinearGeometry geometry = LinearGeometry.CreateOpenPolyline(command.SourcePoints, scale);
        float widthScale = GetTransformWidthScale(transform);
        RectangleF geometryBounds = residual.IsIdentity ? geometry.Info.Bounds : RectangleF.Transform(geometry.Info.Bounds, residual);
        RectangleF strokeBounds = GetStrokeBounds(geometryBounds, command.Pen, widthScale);
        Brush sourceBrush = hasTransform ? command.Brush.Transform(transform) : command.Brush;

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
            absoluteInterest,
            options.IntersectionRule,
            options.RasterizationMode,
            options.AntialiasThreshold);

        brushBounds = absoluteInterest;
        return true;
    }

    private static Rectangle ToConservativeBounds(RectangleF bounds)
        => Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));

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

    private static Vector2 ExtractScale(Matrix4x4 matrix)
        => new(
            MathF.Sqrt((matrix.M11 * matrix.M11) + (matrix.M12 * matrix.M12)),
            MathF.Sqrt((matrix.M21 * matrix.M21) + (matrix.M22 * matrix.M22)));

    private static Matrix4x4 ComputeResidual(Vector2 scale, Matrix4x4 matrix)
        => Matrix4x4.CreateScale(1F / scale.X, 1F / scale.Y, 1F) * matrix;

    internal readonly struct PreparedFillItem
    {
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

        public Brush Brush { get; }

        public GraphicsOptions GraphicsOptions { get; }

        public Rectangle BrushBounds { get; }

        public DefaultRasterizer.RasterizableGeometry Rasterizable { get; }
    }

    private readonly struct PreparedStrokeItem
    {
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

        public Brush Brush { get; }

        public GraphicsOptions GraphicsOptions { get; }

        public Rectangle BrushBounds { get; }

        public DefaultRasterizer.StrokeRasterizableGeometry Rasterizable { get; }
    }

    private readonly struct PartitionState
    {
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

        public int FillItemCount { get; }

        public int StrokeItemCount { get; }

        public long TotalEdgeCount { get; }

        public int SingleBandItemCount { get; }

        public int SmallEdgeItemCount { get; }

        public int LayerDepthDelta { get; }

        public int MaxLayerDepth { get; }

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
                    finalized.Add(segment!);
                    rowCount += segmentRowCount;
                    rowItemCount += segmentRowItemCount;
                }
            }

            return finalized.Count == 0 ? [] : finalized.ToArray();
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
            out SceneSegment? segment,
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
}
