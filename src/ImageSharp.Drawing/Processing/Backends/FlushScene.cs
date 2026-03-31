// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
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
        rows: []);

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
        SceneRow[] rows)
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
        this.Rows = rows;
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
    /// Gets the number of scene rows containing executable work.
    /// </summary>
    public int RowCount { get; }

    /// <summary>
    /// Gets the retained row lists.
    /// </summary>
    internal SceneRow[] Rows { get; }

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
    /// <returns>A flush-ready scene.</returns>
    public static FlushScene Create(
        CompositionScene scene,
        in Rectangle targetBounds,
        MemoryAllocator allocator)
    {
        int commandCount = scene.CommandCount;
        if (commandCount == 0)
        {
            return Empty();
        }

        IReadOnlyList<CompositionSceneCommand> commands = scene.Commands;
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
        int partitionCount = Math.Min(commandCount, Math.Min(Environment.ProcessorCount, targetRowCount));
        PartitionState[] partitions = new PartitionState[partitionCount];

        _ = Parallel.For(
            fromInclusive: 0,
            toExclusive: partitionCount,
            body: partitionIndex =>
            {
                int commandStart = (partitionIndex * commandCount) / partitionCount;
                int commandEnd = ((partitionIndex + 1) * commandCount) / partitionCount;
                partitions[partitionIndex] = ProcessPartition(
                    commands,
                    commandStart,
                    commandEnd,
                    targetRectangle,
                    firstTargetRowBandIndex,
                    targetRowCount,
                    allocator,
                    fillItems,
                    strokeItems);
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

        if ((fillItemCount + strokeItemCount) == 0 || rowItemCount == 0)
        {
            DisposeRows(rowBuilders);
            return Empty();
        }

        SceneRow[] sceneRows = FinalizeRows(rowBuilders, firstTargetRowBandIndex, rowCount);
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
            sceneRows);
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

        for (int i = 0; i < this.FillItems.Length; i++)
        {
            this.FillItems[i]?.Dispose();
        }

        for (int i = 0; i < this.StrokeItems.Length; i++)
        {
            this.StrokeItems[i]?.Dispose();
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

    private static PartitionState ProcessPartition(
        IReadOnlyList<CompositionSceneCommand> commands,
        int commandStart,
        int commandEnd,
        in Rectangle targetBounds,
        int firstTargetRowBandIndex,
        int targetRowCount,
        MemoryAllocator allocator,
        FillSceneItem?[] fillItems,
        StrokeSceneItem?[] strokeItems)
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
                    ref fillItemCount,
                    ref strokeItemCount,
                    ref totalEdgeCount,
                    ref singleBandItemCount,
                    ref smallEdgeItemCount,
                    ref currentLayerDepth,
                    ref maxLayerDepth);
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
            else
            {
                ProcessPolylineCommand(
                    ((PolylineCompositionSceneCommand)command).Command,
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
        ref int fillItemCount,
        ref int strokeItemCount,
        ref long totalEdgeCount,
        ref int singleBandItemCount,
        ref int smallEdgeItemCount,
        ref int currentLayerDepth,
        ref int maxLayerDepth)
    {
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

            AppendLayerOperations(rowBuilders, firstRowSlot, lastRowSlot, layerBounds, operationKind, commandIndex, targetBounds, allocator);
            return;
        }

        if (!IsSceneDrawable(command))
        {
            return;
        }

        if (command.Pen is Pen pen)
        {
            if (!TryPrepareStrokePath(command, pen, allocator, out PreparedStrokeItem preparedStroke) ||
                preparedStroke.Rasterizable.RowBandCount == 0)
            {
                return;
            }

            strokeItems[commandIndex] = new StrokeSceneItem(preparedStroke.Brush, preparedStroke.GraphicsOptions, preparedStroke.BrushBounds, preparedStroke.Rasterizable);
            strokeItemCount++;
            AccumulateStrokeItemStats(preparedStroke.Rasterizable, ref totalEdgeCount, ref smallEdgeItemCount, ref singleBandItemCount);
            AppendStrokeRowOperations(rowBuilders, 0, rowBuilders.Length, firstTargetRowBandIndex, commandIndex, preparedStroke.Rasterizable, allocator);
            return;
        }

        if (!TryPrepareFillPath(command, allocator, out PreparedFillItem preparedFill) ||
            preparedFill.Rasterizable.RowBandCount == 0)
        {
            return;
        }

        fillItems[commandIndex] = new FillSceneItem(preparedFill.Brush, preparedFill.GraphicsOptions, preparedFill.BrushBounds, preparedFill.Rasterizable);
        fillItemCount++;
        AccumulateFillItemStats(preparedFill.Rasterizable, ref totalEdgeCount, ref smallEdgeItemCount, ref singleBandItemCount);
        AppendFillRowOperations(rowBuilders, 0, rowBuilders.Length, firstTargetRowBandIndex, commandIndex, preparedFill.Rasterizable, allocator);
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
        if (!TryPrepareLineSegmentStroke(command, out PreparedStrokeItem preparedStroke) ||
            preparedStroke.Rasterizable.RowBandCount == 0)
        {
            return;
        }

        strokeItems[commandIndex] = new StrokeSceneItem(preparedStroke.Brush, preparedStroke.GraphicsOptions, preparedStroke.BrushBounds, preparedStroke.Rasterizable);
        strokeItemCount++;
        AccumulateStrokeItemStats(preparedStroke.Rasterizable, ref totalEdgeCount, ref smallEdgeItemCount, ref singleBandItemCount);
        AppendStrokeRowOperations(rowBuilders, 0, targetRowCount, firstTargetRowBandIndex, commandIndex, preparedStroke.Rasterizable, allocator);
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

        strokeItems[commandIndex] = new StrokeSceneItem(preparedStroke.Brush, preparedStroke.GraphicsOptions, preparedStroke.BrushBounds, preparedStroke.Rasterizable);
        strokeItemCount++;
        AccumulateStrokeItemStats(preparedStroke.Rasterizable, ref totalEdgeCount, ref smallEdgeItemCount, ref singleBandItemCount);
        AppendStrokeRowOperations(rowBuilders, 0, targetRowCount, firstTargetRowBandIndex, commandIndex, preparedStroke.Rasterizable, allocator);
    }

    private static void AppendLayerOperations(
        RowBuilder[] rowBuilders,
        int firstRowSlot,
        int lastRowSlot,
        Rectangle layerBandBounds,
        CompositionCommandKind operationKind,
        int commandIndex,
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
            builder.Append(new SceneOperation(operationKind, commandIndex, rowLayerBounds));
        }
    }

    private static bool TryPrepareFillPath(
        in CompositionCommand command,
        MemoryAllocator allocator,
        out PreparedFillItem prepared)
    {
        IPath path = command.SourcePath;

        if (!TryResolveRasterization(
                command.Brush,
                path.Bounds,
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

        DefaultRasterizer.RasterizableGeometry? rasterizable = DefaultRasterizer.CreateRasterizableGeometry(
            path.ToLinearGeometry(),
            command.DestinationOffset.X,
            command.DestinationOffset.Y,
            rasterizerOptions,
            allocator);

        if (rasterizable is null)
        {
            prepared = default;
            return false;
        }

        prepared = new PreparedFillItem(brush, command.GraphicsOptions, brushBounds, rasterizable);
        return true;
    }

    private static bool TryPrepareStrokePath(
        in CompositionCommand command,
        Pen pen,
        MemoryAllocator allocator,
        out PreparedStrokeItem prepared)
    {
        IPath path = command.SourcePath;

        if (!TryResolveRasterization(
                command.Brush,
                GetStrokeBounds(path.Bounds, pen),
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

        DefaultRasterizer.StrokeRasterizableGeometry? rasterizable = DefaultRasterizer.CreateStrokeRasterizableGeometry(
            path,
            pen,
            command.DestinationOffset.X,
            command.DestinationOffset.Y,
            rasterizerOptions,
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
        out PreparedStrokeItem prepared)
    {
        PointF start = command.SourceStart;
        PointF end = command.SourceEnd;

        if (!TryResolveRasterization(
                command.Brush,
                StrokeLineSegmentCommand.GetConservativeBounds(start, end, command.Pen),
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

        DefaultRasterizer.StrokeRasterizableGeometry? rasterizable = DefaultRasterizer.CreateStrokeRasterizableGeometry(
            start,
            end,
            command.Pen,
            command.DestinationOffset.X,
            command.DestinationOffset.Y,
            rasterizerOptions);

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
        PointF[] points = command.SourcePoints;

        if (!TryResolveRasterization(
                command.Brush,
                StrokePolylineCommand.GetConservativeBounds(points, command.Pen),
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

        DefaultRasterizer.StrokeRasterizableGeometry? rasterizable = DefaultRasterizer.CreateStrokeRasterizableGeometry(
            points,
            command.Pen,
            command.DestinationOffset.X,
            command.DestinationOffset.Y,
            rasterizerOptions,
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

        if (options.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter)
        {
            bounds = new RectangleF(bounds.X + 0.5F, bounds.Y + 0.5F, bounds.Width, bounds.Height);
        }

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
            options.SamplingOrigin,
            options.AntialiasThreshold);

        brushBounds = absoluteInterest;
        return true;
    }

    private static RectangleF GetStrokeBounds(RectangleF bounds, Pen pen)
    {
        float halfWidth = pen.StrokeWidth * 0.5F;
        float inflate = pen.StrokeOptions.LineJoin switch
        {
            LineJoin.Miter or LineJoin.MiterRevert or LineJoin.MiterRound => (float)(halfWidth * Math.Max(pen.StrokeOptions.MiterLimit, 1D)),
            _ => halfWidth
        };

        bounds.Inflate(new SizeF(inflate, inflate));
        return bounds;
    }

    private readonly struct PreparedFillItem
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
}
