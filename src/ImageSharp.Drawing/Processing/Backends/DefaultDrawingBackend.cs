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
    public void FlushCompositions<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionScene compositionScene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (compositionScene.CommandCount == 0)
        {
            return;
        }

        if (!target.TryGetCpuRegion(out Buffer2DRegion<TPixel> destinationFrame))
        {
            throw new NotSupportedException($"{nameof(DefaultDrawingBackend)} requires CPU-accessible frame targets.");
        }

        using FlushScene scene = FlushScene.Create(
            compositionScene,
            target.Bounds,
            configuration.MemoryAllocator,
            configuration.MaxDegreeOfParallelism);

        if (scene.RowCount == 0)
        {
            return;
        }

        ExecuteScene(configuration, destinationFrame, compositionScene.Commands, scene);
    }

    /// <summary>
    /// Executes one retained flush scene against a CPU destination frame.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="destinationFrame">The destination CPU region.</param>
    /// <param name="commands">The original composition commands referenced by the retained scene.</param>
    /// <param name="scene">The retained scene to execute.</param>
    private static void ExecuteScene<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> destinationFrame,
        IReadOnlyList<CompositionSceneCommand> commands,
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

        int requestedParallelism = configuration.MaxDegreeOfParallelism;
        _ = Parallel.For(
            fromInclusive: 0,
            toExclusive: scene.RowCount,
            parallelOptions: ParallelExecutionHelper.CreateParallelOptions(requestedParallelism, scene.RowCount),
            localInit: () => new WorkerState<TPixel>(configuration.MemoryAllocator, destinationFrame.Width, scene.MaxLayerDepth + 1),
            body: (rowIndex, _, state) =>
            {
                ExecuteSceneRow(
                    configuration,
                    destinationFrame,
                    commands,
                    scene,
                    scene.Rows[rowIndex],
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
    /// <param name="destinationFrame">The destination CPU region.</param>
    /// <param name="commands">The original composition commands.</param>
    /// <param name="scene">The retained flush scene.</param>
    /// <param name="row">The retained scene row to execute.</param>
    /// <param name="state">The worker-local scratch and compositing state.</param>
    private static void ExecuteSceneRow<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> destinationFrame,
        IReadOnlyList<CompositionSceneCommand> commands,
        FlushScene scene,
        in FlushScene.SceneRow row,
        WorkerState<TPixel> state)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int bandTop = row.RowBandIndex * DefaultRasterizer.DefaultTileHeight;
        int localBandTop = bandTop - destinationFrame.Rectangle.Y;
        int bandHeight = Math.Min(DefaultRasterizer.DefaultTileHeight, destinationFrame.Height - localBandTop);
        if (bandHeight <= 0)
        {
            return;
        }

        Buffer2DRegion<TPixel> destinationBand = destinationFrame.GetSubRegion(0, localBandTop, destinationFrame.Width, bandHeight);
        BandTarget<TPixel>[] targetStack = state.TargetStack;
        int targetCount = 1;
        targetStack[0] = new BandTarget<TPixel>(destinationBand, destinationFrame.Rectangle.X, bandTop, null);
        int scratchWidth = GetRowScratchWidth(scene, row, destinationFrame.Width);
        DefaultRasterizer.WorkerScratch scratch = state.GetOrCreateScratch(scratchWidth);

        try
        {
            for (FlushScene.SceneOperationBlock? block = row.FirstBlock; block is not null; block = block.Next)
            {
                foreach (FlushScene.SceneOperation operation in block.Items)
                {
                    // Each retained row contains a compact mix of layer control operations and
                    // draw operations in original command order, so the executor can replay the
                    // row without re-walking the full scene description.
                    switch (operation.Kind)
                    {
                        case FlushScene.SceneOperationKind.BeginLayer:
                            targetStack[targetCount++] =
                                new BandTarget<TPixel>(
                                    configuration.MemoryAllocator.Allocate2D<TPixel>(operation.LayerBounds.Width, operation.LayerBounds.Height, AllocationOptions.Clean),
                                    operation.LayerBounds,
                                    ((PathCompositionSceneCommand)commands[operation.CommandIndex]).Command.GraphicsOptions);
                            break;

                        case FlushScene.SceneOperationKind.EndLayer:
                            BandTarget<TPixel> source = targetStack[--targetCount];
                            BandTarget<TPixel> destination = targetStack[targetCount - 1];
                            CompositeLayerBand(configuration, source, destination, state.BrushWorkspace);
                            source.Dispose();
                            break;

                        case FlushScene.SceneOperationKind.FillItem:
                            BandTarget<TPixel> target = targetStack[targetCount - 1];
                            FlushScene.FillSceneItem sceneItem = scene.FillItems[operation.ItemIndex]!;
                            ExecuteFillOperation(
                                sceneItem.GetRenderer<TPixel>(configuration, destinationFrame.Width),
                                new DefaultRasterizer.RasterizableItem(sceneItem.Rasterizable, operation.LocalRowIndex),
                                target,
                                scratch,
                                state);
                            break;

                        case FlushScene.SceneOperationKind.StrokeItem:
                            BandTarget<TPixel> strokeTarget = targetStack[targetCount - 1];
                            FlushScene.StrokeSceneItem strokeSceneItem = scene.StrokeItems[operation.ItemIndex]!;
                            ExecuteStrokeOperation(
                                strokeSceneItem.GetRenderer<TPixel>(configuration, destinationFrame.Width),
                                new DefaultRasterizer.StrokeRasterizableItem(strokeSceneItem.Rasterizable, operation.LocalRowIndex),
                                strokeTarget,
                                scratch,
                                state);
                            break;
                    }
                }
            }
        }
        finally
        {
            for (int i = 1; i < targetCount; i++)
            {
                targetStack[i].Dispose();
                targetStack[i] = null!;
            }

            targetStack[0] = null!;
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
