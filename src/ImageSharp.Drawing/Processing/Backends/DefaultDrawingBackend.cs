// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// CPU backend that executes path coverage rasterization and brush composition directly against a CPU region.
/// </summary>
public sealed class DefaultDrawingBackend : IDrawingBackend
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
        if (compositionScene.Commands.Count == 0)
        {
            return;
        }

        if (!target.TryGetCpuRegion(out Buffer2DRegion<TPixel> destinationFrame))
        {
            throw new NotSupportedException($"{nameof(DefaultDrawingBackend)} requires CPU-accessible frame targets.");
        }

        using FlushScene scene = FlushScene.Create(
            compositionScene.Commands,
            target.Bounds,
            configuration.MemoryAllocator);

        if (scene.RowCount == 0)
        {
            return;
        }

        ExecuteScene(configuration, destinationFrame, compositionScene.Commands, scene);
    }

    private static void ExecuteScene<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> destinationFrame,
        IReadOnlyList<CompositionCommand> commands,
        FlushScene scene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        for (int i = 0; i < scene.ItemCount; i++)
        {
            ref FlushScene.SceneItem item = ref scene.Items[i];
            _ = item.GetRenderer<TPixel>(configuration, destinationFrame.Width);
        }

        _ = Parallel.For(
            fromInclusive: 0,
            toExclusive: scene.RowCount,
            localInit: () => new WorkerState<TPixel>(
                configuration.MemoryAllocator,
                destinationFrame.Width,
                scene.MaxLayerDepth + 1),
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

    private static void ExecuteSceneRow<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> destinationFrame,
        IReadOnlyList<CompositionCommand> commands,
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
                    switch (operation.Kind)
                    {
                        case CompositionCommandKind.BeginLayer:
                            targetStack[targetCount++] =
                                new BandTarget<TPixel>(
                                    configuration.MemoryAllocator.Allocate2D<TPixel>(operation.LayerBounds.Width, operation.LayerBounds.Height, AllocationOptions.Clean),
                                    operation.LayerBounds,
                                    commands[operation.CommandIndex].GraphicsOptions);
                            break;

                        case CompositionCommandKind.EndLayer:
                            BandTarget<TPixel> source = targetStack[--targetCount];
                            BandTarget<TPixel> destination = targetStack[targetCount - 1];
                            CompositeLayerBand(configuration, source, destination, state.BrushWorkspace);
                            source.Dispose();
                            break;

                        case CompositionCommandKind.FillLayer:
                            BandTarget<TPixel> target = targetStack[targetCount - 1];
                            ref FlushScene.SceneItem sceneItem = ref scene.Items[operation.ItemIndex];
                            ExecuteFillOperation(
                                sceneItem.GetRenderer<TPixel>(configuration, destinationFrame.Width),
                                new DefaultRasterizer.RasterizableItem(sceneItem.Rasterizable, operation.LocalRowIndex),
                                target,
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
                if (operation.Kind != CompositionCommandKind.FillLayer)
                {
                    continue;
                }

                int itemWidth = scene.Items[operation.ItemIndex].Rasterizable.Width;
                if (itemWidth > width)
                {
                    width = itemWidth;
                }
            }
        }

        return width;
    }

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
            bandInfo.Width,
            bandInfo.WordsPerRow,
            bandInfo.CoverStride,
            bandInfo.BandHeight,
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

        for (int y = 0; y < overlap.Height; y++)
        {
            Span<TPixel> sourceRow = source.Region.DangerousGetRowSpan(sourceOffsetY + y).Slice(sourceOffsetX, overlap.Width);
            Span<TPixel> destinationRow = destination.Region.DangerousGetRowSpan(destinationOffsetY + y).Slice(destinationOffsetX, overlap.Width);
            blender.Blend(configuration, destinationRow, destinationRow, sourceRow, amounts[..overlap.Width]);
        }
    }

    private readonly struct FillCoverageRowHandler<TPixel> : IRasterizerCoverageRowHandler
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly BrushRenderer<TPixel> renderer;
        private readonly BandTarget<TPixel> target;
        private readonly BrushWorkspace<TPixel> brushWorkspace;

        public FillCoverageRowHandler(
            BrushRenderer<TPixel> renderer,
            BandTarget<TPixel> target,
            BrushWorkspace<TPixel> brushWorkspace)
        {
            this.renderer = renderer;
            this.target = target;
            this.brushWorkspace = brushWorkspace;
        }

        public void Handle(int y, int startX, Span<float> coverage)
        {
            int localY = y - this.target.AbsoluteTop;
            if ((uint)localY >= (uint)this.target.Region.Height)
            {
                return;
            }

            int clipStartX = Math.Max(startX, this.target.AbsoluteLeft);
            int clipEndX = Math.Min(startX + coverage.Length, this.target.AbsoluteLeft + this.target.Region.Width);
            if (clipEndX <= clipStartX)
            {
                return;
            }

            int coverageOffset = clipStartX - startX;
            int clippedLength = clipEndX - clipStartX;
            Span<TPixel> destinationRow = this.target.Region
                .DangerousGetRowSpan(localY)
                .Slice(clipStartX - this.target.AbsoluteLeft, clippedLength);
            this.renderer.Apply(destinationRow, coverage.Slice(coverageOffset, clippedLength), clipStartX, y, this.brushWorkspace);
        }
    }

    private sealed class BandTarget<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly Buffer2D<TPixel>? owner;

        public BandTarget(Buffer2DRegion<TPixel> region, int absoluteLeft, int absoluteTop, GraphicsOptions? graphicsOptions)
        {
            this.Region = region;
            this.AbsoluteLeft = absoluteLeft;
            this.AbsoluteTop = absoluteTop;
            this.GraphicsOptions = graphicsOptions;
        }

        public BandTarget(Buffer2D<TPixel> owner, Rectangle bounds, GraphicsOptions? graphicsOptions)
        {
            this.owner = owner;
            this.Region = new Buffer2DRegion<TPixel>(owner);
            this.AbsoluteLeft = bounds.X;
            this.AbsoluteTop = bounds.Y;
            this.GraphicsOptions = graphicsOptions;
        }

        public Buffer2DRegion<TPixel> Region { get; }

        public int AbsoluteLeft { get; }

        public int AbsoluteTop { get; }

        public GraphicsOptions? GraphicsOptions { get; }

        public void Dispose() => this.owner?.Dispose();
    }

    private sealed class WorkerState<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly MemoryAllocator allocator;
        private DefaultRasterizer.WorkerScratch? scratch;

        public WorkerState(
            MemoryAllocator allocator,
            int destinationWidth,
            int layerDepth)
        {
            this.allocator = allocator;
            this.BrushWorkspace = new BrushWorkspace<TPixel>(allocator, destinationWidth);
            this.TargetStack = new BandTarget<TPixel>[layerDepth];
        }

        public BrushWorkspace<TPixel> BrushWorkspace { get; }

        public BandTarget<TPixel>[] TargetStack { get; }

        public DefaultRasterizer.WorkerScratch GetOrCreateScratch(int requiredWidth)
        {
            DefaultRasterizer.WorkerScratch? current = this.scratch;
            if (current is not null && current.CanReuse(requiredWidth))
            {
                return current;
            }

            current?.Dispose();
            this.scratch = DefaultRasterizer.CreateWorkerScratch(this.allocator, requiredWidth);
            return this.scratch;
        }

        public void Dispose()
        {
            this.scratch?.Dispose();
            this.BrushWorkspace.Dispose();
        }
    }

#pragma warning disable SA1201 // Keep the public compose entrypoint after the worker-local helper types in this file.
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
#pragma warning restore SA1201

    /// <inheritdoc />
    public bool TryReadRegion<TPixel>(
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
            return false;
        }

        // Clamp the request to the target region to avoid out-of-range row slicing.
        Rectangle clipped = Rectangle.Intersect(
            new Rectangle(0, 0, sourceRegion.Width, sourceRegion.Height),
            sourceRectangle);

        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return false;
        }

        int copyWidth = Math.Min(clipped.Width, destination.Width);
        int copyHeight = Math.Min(clipped.Height, destination.Height);
        for (int y = 0; y < copyHeight; y++)
        {
            sourceRegion.DangerousGetRowSpan(clipped.Y + y)
                .Slice(clipped.X, copyWidth)
                .CopyTo(destination.DangerousGetRowSpan(y));
        }

        return true;
    }
}
