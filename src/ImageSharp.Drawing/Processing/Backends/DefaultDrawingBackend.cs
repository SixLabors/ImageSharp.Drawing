// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// CPU backend that executes path coverage rasterization and brush composition directly against a CPU region.
/// </summary>
public sealed partial class DefaultDrawingBackend : IDrawingBackend
{
    /// <summary>
    /// Gets the default backend instance.
    /// </summary>
    public static DefaultDrawingBackend Instance { get; } = new();

    /// <inheritdoc />
    public DrawingBackendScene CreateScene(
        Configuration configuration,
        Rectangle targetBounds,
        DrawingCommandBatch commandBatch,
        IReadOnlyList<IDisposable>? ownedResources = null)
    {
        FlushScene scene = FlushScene.Create(
            commandBatch,
            targetBounds,
            configuration.MemoryAllocator,
            configuration.MaxDegreeOfParallelism);

        return new DefaultDrawingBackendScene(scene, targetBounds, ownedResources);
    }

    /// <inheritdoc />
    public void RenderScene<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        DrawingBackendScene scene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (scene is not DefaultDrawingBackendScene cpuScene)
        {
            throw new InvalidOperationException("The scene is not compatible with the CPU drawing backend.");
        }

        if (!target.TryGetCpuRegion(out Buffer2DRegion<TPixel> destinationFrame))
        {
            throw new NotSupportedException($"{nameof(DefaultDrawingBackend)} requires CPU-accessible frame targets.");
        }

        if (target.Bounds != cpuScene.Bounds)
        {
            throw new InvalidOperationException("The target bounds do not match the CPU drawing backend scene bounds.");
        }

        if (cpuScene.Scene.RowCount != 0 || cpuScene.Scene.HasApply)
        {
            ExecuteScene(configuration, target.Bounds, destinationFrame, cpuScene.Scene);
        }
    }

    /// <summary>
    /// Executes one retained flush scene against a CPU destination frame.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="targetBounds">The logical target bounds represented by <paramref name="destinationFrame"/>.</param>
    /// <param name="destinationFrame">The destination CPU region.</param>
    /// <param name="scene">The retained scene to execute.</param>
    private static void ExecuteScene<TPixel>(
        Configuration configuration,
        Rectangle targetBounds,
        Buffer2DRegion<TPixel> destinationFrame,
        FlushScene scene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Warm the cached renderers before the row loop so the hot execution path only
        // performs retained-scene work and brush application.
        if (scene.FillItemCount > 0)
        {
            for (int i = 0; i < scene.FillItems.Length; i++)
            {
                if (scene.FillItems[i] is FlushScene.FillSceneItem item)
                {
                    _ = item.GetRenderer<TPixel>(configuration, destinationFrame.Width);
                }
            }
        }

        if (scene.StrokeItemCount > 0)
        {
            for (int i = 0; i < scene.StrokeItems.Length; i++)
            {
                if (scene.StrokeItems[i] is FlushScene.StrokeSceneItem item)
                {
                    _ = item.GetRenderer<TPixel>(configuration, destinationFrame.Width);
                }
            }
        }

        BandTarget<TPixel> target = new(destinationFrame, targetBounds.X, targetBounds.Y, null);

        if (scene.Segments.Length != 0)
        {
            ExecuteSceneSegments(configuration, destinationFrame.Width, scene, scene.Segments, target);
            return;
        }

        if (scene.RowCount != 0)
        {
            ExecuteSceneRows(configuration, destinationFrame.Width, scene, target);
        }
    }

    /// <summary>
    /// Executes retained target-wide segments against the supplied target.
    /// </summary>
    private static void ExecuteSceneSegments<TPixel>(
        Configuration configuration,
        int canvasWidth,
        FlushScene scene,
        FlushScene.SceneSegment[] segments,
        BandTarget<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        for (int i = 0; i < segments.Length; i++)
        {
            FlushScene.SceneSegment segment = segments[i];
            if (segment.Rows.Length != 0)
            {
                ExecuteSceneRows(configuration, canvasWidth, scene, segment.Rows, target);
            }

            if (segment.ApplyItem is FlushScene.ApplySceneItem applyItem)
            {
                ExecuteApplyItem(configuration, canvasWidth, applyItem, target);
            }
            else if (segment.LayerItem is FlushScene.ScopedLayerSceneItem layerItem)
            {
                using BandTarget<TPixel> layerTarget = new(
                    configuration.MemoryAllocator.Allocate2D<TPixel>(layerItem.Bounds.Width, layerItem.Bounds.Height, AllocationOptions.Clean),
                    layerItem.Bounds,
                    layerItem.Layer.Options);

                ExecuteSceneSegments(configuration, canvasWidth, scene, layerItem.Segments, layerTarget);
                CompositeLayerTarget(configuration, layerTarget, target);
            }
        }
    }

    /// <summary>
    /// Executes the retained row stream without command filtering.
    /// </summary>
    private static void ExecuteSceneRows<TPixel>(
        Configuration configuration,
        int canvasWidth,
        FlushScene scene,
        BandTarget<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
        => ExecuteSceneRows(configuration, canvasWidth, scene, scene.Rows, target);

    /// <summary>
    /// Executes the supplied retained row stream without command filtering.
    /// </summary>
    private static void ExecuteSceneRows<TPixel>(
        Configuration configuration,
        int canvasWidth,
        FlushScene scene,
        FlushScene.SceneRow[] rows,
        BandTarget<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int requestedParallelism = configuration.MaxDegreeOfParallelism;

        _ = Parallel.For(
            fromInclusive: 0,
            toExclusive: rows.Length,
            parallelOptions: ParallelExecutionHelper.CreateParallelOptions(requestedParallelism, rows.Length),
            localInit: () => new WorkerState<TPixel>(configuration.MemoryAllocator, target.Region.Width),
            body: (rowIndex, _, state) =>
            {
                ExecuteSceneRow(
                    configuration,
                    canvasWidth,
                    scene,
                    rows[rowIndex],
                    target,
                    state);

                return state;
            },
            localFinally: static state => state.Dispose());
    }

    /// <summary>
    /// Executes one retained scene row against the destination band it overlaps.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="canvasWidth">The destination canvas width.</param>
    /// <param name="scene">The retained flush scene.</param>
    /// <param name="row">The retained scene row to execute.</param>
    /// <param name="target">The active target receiving row output.</param>
    /// <param name="state">The worker-local scratch and compositing state.</param>
    private static void ExecuteSceneRow<TPixel>(
        Configuration configuration,
        int canvasWidth,
        FlushScene scene,
        in FlushScene.SceneRow row,
        BandTarget<TPixel> target,
        WorkerState<TPixel> state)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Rectangle targetBounds = target.Bounds;
        int bandTop = row.RowBandIndex * DefaultRasterizer.DefaultTileHeight;
        int bandBottom = bandTop + DefaultRasterizer.DefaultTileHeight;
        int clippedBandTop = Math.Max(bandTop, targetBounds.Y);
        int clippedBandBottom = Math.Min(bandBottom, targetBounds.Bottom);
        int bandHeight = clippedBandBottom - clippedBandTop;
        if (bandHeight <= 0)
        {
            return;
        }

        int localBandTop = clippedBandTop - targetBounds.Y;
        Buffer2DRegion<TPixel> destinationBand = target.Region.GetSubRegion(0, localBandTop, target.Region.Width, bandHeight);
        BandTarget<TPixel> rowTarget = new(destinationBand, targetBounds.X, clippedBandTop, target.GraphicsOptions);
        int scratchWidth = GetRowScratchWidth(scene, row, target.Region.Width);
        DefaultRasterizer.WorkerScratch scratch = state.GetOrCreateScratch(scratchWidth);
        SceneOperationCursor cursor = new(row.FirstBlock);

        ExecuteSceneRowOperations(
            ref cursor,
            configuration,
            canvasWidth,
            scene,
            rowTarget,
            scratch,
            state);
    }

    /// <summary>
    /// Executes retained row operations against the supplied target until the current layer scope ends.
    /// </summary>
    private static void ExecuteSceneRowOperations<TPixel>(
        ref SceneOperationCursor cursor,
        Configuration configuration,
        int canvasWidth,
        FlushScene scene,
        BandTarget<TPixel> target,
        DefaultRasterizer.WorkerScratch scratch,
        WorkerState<TPixel> state)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        while (cursor.TryRead(out FlushScene.SceneOperation operation))
        {
            // Layer execution is recursive rather than stack-backed: the caller's target remains
            // the parent target while the nested call renders into the temporary layer target.
            switch (operation.Kind)
            {
                case FlushScene.SceneOperationKind.BeginLayer:
                    GraphicsOptions? layerOptions = scene.Layers[operation.ItemIndex]?.Options;
                    using (BandTarget<TPixel> layerTarget = new(
                        configuration.MemoryAllocator.Allocate2D<TPixel>(operation.LayerBounds.Width, operation.LayerBounds.Height, AllocationOptions.Clean),
                        operation.LayerBounds,
                        layerOptions))
                    {
                        ExecuteSceneRowOperations(
                            ref cursor,
                            configuration,
                            canvasWidth,
                            scene,
                            layerTarget,
                            scratch,
                            state);

                        CompositeLayerBand(configuration, layerTarget, target, state.BrushWorkspace);
                    }

                    break;

                case FlushScene.SceneOperationKind.EndLayer:
                    return;

                case FlushScene.SceneOperationKind.FillItem:
                    FlushScene.FillSceneItem sceneItem = scene.FillItems[operation.ItemIndex]!;
                    ExecuteFillOperation(
                        sceneItem.GetRenderer<TPixel>(configuration, canvasWidth),
                        new DefaultRasterizer.RasterizableItem(sceneItem.Rasterizable, operation.LocalRowIndex),
                        target,
                        scratch,
                        state);
                    break;

                case FlushScene.SceneOperationKind.StrokeItem:
                    FlushScene.StrokeSceneItem strokeSceneItem = scene.StrokeItems[operation.ItemIndex]!;
                    ExecuteStrokeOperation(
                        strokeSceneItem.GetRenderer<TPixel>(configuration, canvasWidth),
                        new DefaultRasterizer.StrokeRasterizableItem(strokeSceneItem.Rasterizable, operation.LocalRowIndex),
                        target,
                        scratch,
                        state);
                    break;
            }
        }
    }

    /// <summary>
    /// Computes the minimum reusable scratch width needed to execute one retained scene row.
    /// </summary>
    /// <param name="scene">The retained flush scene.</param>
    /// <param name="row">The retained scene row.</param>
    /// <param name="minimumWidth">The baseline width taken from the destination band.</param>
    /// <returns>The scratch width required by the row.</returns>
    private static int GetRowScratchWidth(
        FlushScene scene,
        in FlushScene.SceneRow row,
        int minimumWidth)
    {
        int width = minimumWidth;
        for (FlushScene.SceneOperationBlock? block = row.FirstBlock; block is not null; block = block.Next)
        {
            foreach (FlushScene.SceneOperation operation in block.Items)
            {
                if (operation.Kind is FlushScene.SceneOperationKind.BeginLayer or FlushScene.SceneOperationKind.EndLayer)
                {
                    continue;
                }

                int itemWidth = operation.Kind == FlushScene.SceneOperationKind.FillItem
                    ? scene.FillItems[operation.ItemIndex]!.Rasterizable.Width
                    : scene.StrokeItems[operation.ItemIndex]!.Rasterizable.Width;
                if (itemWidth > width)
                {
                    width = itemWidth;
                }
            }
        }

        return width;
    }

    /// <summary>
    /// Executes every retained row band for one apply item against the supplied target.
    /// </summary>
    private static void ExecuteApplyItemParallel<TPixel>(
        Configuration configuration,
        BrushRenderer<TPixel> renderer,
        FlushScene.ApplySceneItem item,
        BandTarget<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int rowBandCount = item.Rasterizable.RowBandCount;

        _ = Parallel.For(
            fromInclusive: 0,
            toExclusive: rowBandCount,
            parallelOptions: ParallelExecutionHelper.CreateParallelOptions(configuration.MaxDegreeOfParallelism, rowBandCount),
            localInit: () => new WorkerState<TPixel>(configuration.MemoryAllocator, target.Region.Width),
            body: (localRowIndex, _, state) =>
            {
                if (item.Rasterizable.HasCoverage(localRowIndex))
                {
                    DefaultRasterizer.WorkerScratch scratch = state.GetOrCreateScratch(Math.Max(target.Region.Width, item.Rasterizable.Width));
                    ExecuteFillOperation(
                        renderer,
                        new DefaultRasterizer.RasterizableItem(item.Rasterizable, localRowIndex),
                        target,
                        scratch,
                        state);
                }

                return state;
            },
            localFinally: static state => state.Dispose());
    }

    /// <summary>
    /// Applies one retained processor operation to the current target.
    /// </summary>
    private static void ExecuteApplyItem<TPixel>(
        Configuration configuration,
        int canvasWidth,
        FlushScene.ApplySceneItem item,
        BandTarget<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Rectangle readRect = item.SourceRect;
        if (item.OwnerLayer is not null)
        {
            readRect = new Rectangle(
                item.SourceRect.X - target.AbsoluteLeft,
                item.SourceRect.Y - target.AbsoluteTop,
                item.SourceRect.Width,
                item.SourceRect.Height);
        }

        using Image<TPixel> sourceImage = new(configuration, item.SourceRect.Width, item.SourceRect.Height);
        CopyTargetToImage(target, readRect, sourceImage.Frames.RootFrame.PixelBuffer.GetRegion());
        sourceImage.Mutate(item.Operation);

        Brush brush = new ImageBrush<TPixel>(sourceImage, sourceImage.Bounds, item.BrushOffset);
        BrushRenderer<TPixel> renderer = brush.CreateRenderer<TPixel>(
            configuration,
            item.GraphicsOptions,
            canvasWidth,
            item.BrushBounds);

        ExecuteApplyItemParallel(configuration, renderer, item, target);
    }

    /// <summary>
    /// Copies the requested target rectangle into an image-sized destination region.
    /// </summary>
    private static void CopyTargetToImage<TPixel>(
        BandTarget<TPixel> target,
        Rectangle readRect,
        Buffer2DRegion<TPixel> destination)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Rectangle clipped = Rectangle.Intersect(new Rectangle(0, 0, target.Region.Width, target.Region.Height), readRect);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return;
        }

        int destinationX = clipped.X - readRect.X;
        int destinationY = clipped.Y - readRect.Y;
        for (int y = 0; y < clipped.Height; y++)
        {
            target.Region.DangerousGetRowSpan(clipped.Y + y)
                .Slice(clipped.X, clipped.Width)
                .CopyTo(destination.DangerousGetRowSpan(destinationY + y).Slice(destinationX, clipped.Width));
        }
    }

    /// <summary>
    /// Executes one retained fill operation through the rasterizer and brush renderer.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="renderer">The memoized brush renderer for the scene item.</param>
    /// <param name="item">The retained rasterizable row item to execute.</param>
    /// <param name="target">The active composition target for the row.</param>
    /// <param name="scratch">The worker-local raster scratch.</param>
    /// <param name="state">The worker-local execution state.</param>
    private static void ExecuteFillOperation<TPixel>(
        BrushRenderer<TPixel> renderer,
        DefaultRasterizer.RasterizableItem item,
        BandTarget<TPixel> target,
        DefaultRasterizer.WorkerScratch scratch,
        WorkerState<TPixel> state)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DefaultRasterizer.RasterizableBandInfo bandInfo = item.Rasterizable.GetBandInfo(item.LocalRowIndex);
        DefaultRasterizer.Context context = scratch.CreateContext(
            bandInfo.IntersectionRule,
            bandInfo.RasterizationMode,
            bandInfo.AntialiasThreshold);
        FillCoverageRowHandler<TPixel> rowHandler = new(renderer, target, state.BrushWorkspace);
        DefaultRasterizer.ExecuteRasterizableItem(
            ref context,
            in item,
            in bandInfo,
            scratch.Scanline,
            ref rowHandler);
    }

    /// <summary>
    /// Executes one retained stroke operation through the rasterizer and brush renderer.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="renderer">The memoized brush renderer for the scene item.</param>
    /// <param name="item">The retained stroke rasterizable row item to execute.</param>
    /// <param name="target">The active composition target for the row.</param>
    /// <param name="scratch">The worker-local raster scratch.</param>
    /// <param name="state">The worker-local execution state.</param>
    private static void ExecuteStrokeOperation<TPixel>(
        BrushRenderer<TPixel> renderer,
        DefaultRasterizer.StrokeRasterizableItem item,
        BandTarget<TPixel> target,
        DefaultRasterizer.WorkerScratch scratch,
        WorkerState<TPixel> state)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DefaultRasterizer.RasterizableBandInfo bandInfo = item.Rasterizable.GetBandInfo(item.LocalRowIndex);
        DefaultRasterizer.Context context = scratch.CreateContext(
            bandInfo.IntersectionRule,
            bandInfo.RasterizationMode,
            bandInfo.AntialiasThreshold);
        FillCoverageRowHandler<TPixel> rowHandler = new(renderer, target, state.BrushWorkspace);
        Span<float> strokeBandCoverage = item.Rasterizable.RequiresBandCoverage ? scratch.StrokeBandCoverage : [];
        DefaultRasterizer.ExecuteStrokeRasterizableItem(
            ref context,
            in item,
            in bandInfo,
            scratch.Scanline,
            strokeBandCoverage,
            ref rowHandler);
    }

    /// <summary>
    /// Composites one full temporary layer target back into its destination target.
    /// </summary>
    private static void CompositeLayerTarget<TPixel>(
        Configuration configuration,
        BandTarget<TPixel> source,
        BandTarget<TPixel> destination)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Rectangle overlap = Rectangle.Intersect(source.Bounds, destination.Bounds);
        if (overlap.Width <= 0 || overlap.Height <= 0 || source.GraphicsOptions is null)
        {
            return;
        }

        int firstRowBandIndex = overlap.Top / DefaultRasterizer.DefaultTileHeight;
        int lastRowBandIndex = (overlap.Bottom - 1) / DefaultRasterizer.DefaultTileHeight;
        int rowBandCount = (lastRowBandIndex - firstRowBandIndex) + 1;

        _ = Parallel.For(
            fromInclusive: 0,
            toExclusive: rowBandCount,
            parallelOptions: ParallelExecutionHelper.CreateParallelOptions(configuration.MaxDegreeOfParallelism, rowBandCount),
            localInit: () => new WorkerState<TPixel>(configuration.MemoryAllocator, overlap.Width),
            body: (rowSlot, _, state) =>
            {
                int bandTop = (firstRowBandIndex + rowSlot) * DefaultRasterizer.DefaultTileHeight;
                Rectangle rowBand = new(overlap.X, bandTop, overlap.Width, DefaultRasterizer.DefaultTileHeight);
                Rectangle band = Rectangle.Intersect(overlap, rowBand);

                if (band.Width > 0 && band.Height > 0)
                {
                    Buffer2DRegion<TPixel> sourceBand = source.Region.GetSubRegion(
                        band.X - source.AbsoluteLeft,
                        band.Y - source.AbsoluteTop,
                        band.Width,
                        band.Height);
                    Buffer2DRegion<TPixel> destinationBand = destination.Region.GetSubRegion(
                        band.X - destination.AbsoluteLeft,
                        band.Y - destination.AbsoluteTop,
                        band.Width,
                        band.Height);
                    BandTarget<TPixel> sourceTarget = new(sourceBand, band.X, band.Y, source.GraphicsOptions);
                    BandTarget<TPixel> destinationTarget = new(destinationBand, band.X, band.Y, destination.GraphicsOptions);
                    CompositeLayerBand(configuration, sourceTarget, destinationTarget, state.BrushWorkspace);
                }

                return state;
            },
            localFinally: static state => state.Dispose());
    }

    /// <summary>
    /// Composites one temporary layer band back into its destination band.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="source">The source layer band.</param>
    /// <param name="destination">The destination band to blend into.</param>
    /// <param name="brushWorkspace">The worker-local amount buffer workspace.</param>
    private static void CompositeLayerBand<TPixel>(
        Configuration configuration,
        BandTarget<TPixel> source,
        BandTarget<TPixel> destination,
        BrushWorkspace<TPixel> brushWorkspace)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int width = source.Region.Width;
        if (width == 0 || source.Region.Height == 0)
        {
            return;
        }

        Rectangle overlap = Rectangle.Intersect(
            new Rectangle(source.AbsoluteLeft, source.AbsoluteTop, source.Region.Width, source.Region.Height),
            new Rectangle(destination.AbsoluteLeft, destination.AbsoluteTop, destination.Region.Width, destination.Region.Height));

        if (overlap.Width <= 0 || overlap.Height <= 0)
        {
            return;
        }

        if (source.GraphicsOptions is not GraphicsOptions graphicsOptions)
        {
            return;
        }

        PixelBlender<TPixel> blender = PixelOperations<TPixel>.Instance.GetPixelBlender(graphicsOptions);
        Span<float> amounts = brushWorkspace.GetAmounts(overlap.Width);
        amounts[..overlap.Width].Fill(graphicsOptions.BlendPercentage);

        int sourceOffsetX = overlap.X - source.AbsoluteLeft;
        int sourceOffsetY = overlap.Y - source.AbsoluteTop;
        int destinationOffsetX = overlap.X - destination.AbsoluteLeft;
        int destinationOffsetY = overlap.Y - destination.AbsoluteTop;

        // Blend the overlapping rows only; the retained scene has already clipped the layer
        // bounds so there is no need for extra per-pixel bounds logic here.
        for (int y = 0; y < overlap.Height; y++)
        {
            Span<TPixel> sourceRow = source.Region.DangerousGetRowSpan(sourceOffsetY + y).Slice(sourceOffsetX, overlap.Width);
            Span<TPixel> destinationRow = destination.Region.DangerousGetRowSpan(destinationOffsetY + y).Slice(destinationOffsetX, overlap.Width);
            blender.Blend(
                configuration,
                destinationRow,
                destinationRow,
                sourceRow,
                amounts[..overlap.Width],
                brushWorkspace.GetBlendScratch(overlap.Width, 3));
        }
    }

    /// <summary>
    /// Composites one CPU-backed frame onto another using the supplied graphics options.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="source">The source frame.</param>
    /// <param name="destination">The destination frame.</param>
    /// <param name="destinationOffset">The destination offset relative to <paramref name="destination"/>.</param>
    /// <param name="options">The graphics options controlling composition.</param>
    public static void ComposeLayer<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> source,
        ICanvasFrame<TPixel> destination,
        Point destinationOffset,
        GraphicsOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));

        if (!source.TryGetCpuRegion(out Buffer2DRegion<TPixel> sourceRegion))
        {
            throw new NotSupportedException($"{nameof(DefaultDrawingBackend)} requires CPU-accessible source frames.");
        }

        if (!destination.TryGetCpuRegion(out Buffer2DRegion<TPixel> destinationRegion))
        {
            throw new NotSupportedException($"{nameof(DefaultDrawingBackend)} requires CPU-accessible destination frames.");
        }

        PixelBlender<TPixel> blender = PixelOperations<TPixel>.Instance.GetPixelBlender(options);
        float blendPercentage = options.BlendPercentage;

        int srcWidth = sourceRegion.Width;
        int srcHeight = sourceRegion.Height;
        int dstWidth = destinationRegion.Width;
        int dstHeight = destinationRegion.Height;

        // Clamp the compositing region to both source and destination bounds.
        int startX = Math.Max(0, -destinationOffset.X);
        int startY = Math.Max(0, -destinationOffset.Y);
        int endX = Math.Min(srcWidth, dstWidth - destinationOffset.X);
        int endY = Math.Min(srcHeight, dstHeight - destinationOffset.Y);

        if (endX <= startX || endY <= startY)
        {
            return;
        }

        int width = endX - startX;

        // Allocate a reusable per-row amount buffer from the memory pool.
        using IMemoryOwner<float> amountsOwner = configuration.MemoryAllocator.Allocate<float>(width);
        Span<float> amounts = amountsOwner.Memory.Span;
        amounts.Fill(blendPercentage);

        for (int y = startY; y < endY; y++)
        {
            Span<TPixel> srcRow = sourceRegion.DangerousGetRowSpan(y).Slice(startX, width);
            int dstX = destinationOffset.X + startX;
            int dstY = destinationOffset.Y + y;
            Span<TPixel> dstRow = destinationRegion.DangerousGetRowSpan(dstY).Slice(dstX, width);

            blender.Blend(configuration, dstRow, dstRow, srcRow, amounts);
        }
    }

    /// <inheritdoc />
    public void CopyPixels<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> source,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        Point targetPoint)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));

        if (!source.TryGetCpuRegion(out Buffer2DRegion<TPixel> sourceRegion))
        {
            throw new NotSupportedException($"{nameof(DefaultDrawingBackend)} requires CPU-accessible source frames.");
        }

        if (!target.TryGetCpuRegion(out Buffer2DRegion<TPixel> targetRegion))
        {
            throw new NotSupportedException($"{nameof(DefaultDrawingBackend)} requires CPU-accessible target frames.");
        }

        Rectangle sourceBounds = new(0, 0, sourceRegion.Width, sourceRegion.Height);
        Rectangle clippedSource = Rectangle.Intersect(sourceBounds, sourceRectangle);
        if (clippedSource.Width <= 0 || clippedSource.Height <= 0)
        {
            return;
        }

        Rectangle targetBounds = new(0, 0, targetRegion.Width, targetRegion.Height);
        Rectangle targetRectangle = new(
            targetPoint.X + clippedSource.X - sourceRectangle.X,
            targetPoint.Y + clippedSource.Y - sourceRectangle.Y,
            clippedSource.Width,
            clippedSource.Height);

        Rectangle clippedTarget = Rectangle.Intersect(targetBounds, targetRectangle);

        if (clippedTarget.Width <= 0 || clippedTarget.Height <= 0)
        {
            return;
        }

        int sourceX = clippedSource.X + clippedTarget.X - targetRectangle.X;
        int sourceY = clippedSource.Y + clippedTarget.Y - targetRectangle.Y;
        int targetX = clippedTarget.X;
        int targetY = clippedTarget.Y;
        int width = clippedTarget.Width;
        int height = clippedTarget.Height;

        for (int y = 0; y < height; y++)
        {
            sourceRegion.DangerousGetRowSpan(sourceY + y)
                .Slice(sourceX, width)
                .CopyTo(targetRegion.DangerousGetRowSpan(targetY + y).Slice(targetX, width));
        }
    }

    /// <inheritdoc />
    public void ReadRegion<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        Buffer2DRegion<TPixel> destination)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(destination.Buffer, nameof(destination));

        // CPU backend readback is available only when the target exposes CPU pixels.
        if (!target.TryGetCpuRegion(out Buffer2DRegion<TPixel> sourceRegion))
        {
            throw new NotSupportedException($"{nameof(DefaultDrawingBackend)} requires CPU-accessible frame targets for readback.");
        }

        // Clamp the request to the target region to avoid out-of-range row slicing.
        Rectangle clipped = Rectangle.Intersect(
            new Rectangle(0, 0, sourceRegion.Width, sourceRegion.Height),
            sourceRectangle);

        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            throw new ArgumentException("The requested readback rectangle does not intersect the target bounds.", nameof(sourceRectangle));
        }

        int copyWidth = Math.Min(clipped.Width, destination.Width);
        int copyHeight = Math.Min(clipped.Height, destination.Height);

        for (int y = 0; y < copyHeight; y++)
        {
            sourceRegion.DangerousGetRowSpan(clipped.Y + y)
                .Slice(clipped.X, copyWidth)
                .CopyTo(destination.DangerousGetRowSpan(y));
        }
    }
}
