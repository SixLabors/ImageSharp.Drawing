// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// CPU backend that executes path coverage rasterization and brush composition directly against a CPU region.
/// </summary>
public sealed partial class DefaultDrawingBackend
{
    /// <summary>
    /// Adapts rasterizer coverage callbacks into brush application against the active band target.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private readonly struct FillCoverageRowHandler<TPixel> : IRasterizerCoverageRowHandler
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly BrushRenderer<TPixel> renderer;
        private readonly BandTarget<TPixel> target;
        private readonly BrushWorkspace<TPixel> brushWorkspace;

        /// <summary>
        /// Initializes a new instance of the <see cref="FillCoverageRowHandler{TPixel}"/> struct.
        /// </summary>
        /// <param name="renderer">The brush renderer that will consume emitted coverage spans.</param>
        /// <param name="target">The active band target being rendered.</param>
        /// <param name="brushWorkspace">The worker-local brush workspace.</param>
        public FillCoverageRowHandler(
            BrushRenderer<TPixel> renderer,
            BandTarget<TPixel> target,
            BrushWorkspace<TPixel> brushWorkspace)
        {
            this.renderer = renderer;
            this.target = target;
            this.brushWorkspace = brushWorkspace;
        }

        /// <summary>
        /// Applies one emitted coverage span to the active destination band.
        /// </summary>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of the coverage span.</param>
        /// <param name="coverage">The emitted coverage values.</param>
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

            // The rasterizer emits absolute coordinates; clip them once here so the brush
            // renderer can operate against a tight destination span with no extra bounds work.
            int coverageOffset = clipStartX - startX;
            int clippedLength = clipEndX - clipStartX;
            Span<TPixel> destinationRow = this.target.Region
                .DangerousGetRowSpan(localY)
                .Slice(clipStartX - this.target.AbsoluteLeft, clippedLength);
            this.renderer.Apply(destinationRow, coverage.Slice(coverageOffset, clippedLength), clipStartX, y, this.brushWorkspace);
        }
    }

    /// <summary>
    /// Walks retained row-operation blocks without flattening them into another list.
    /// </summary>
    private struct SceneOperationCursor
    {
        private FlushScene.SceneOperationBlock? block;
        private int index;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneOperationCursor"/> struct.
        /// </summary>
        /// <param name="block">The first retained row-operation block.</param>
        public SceneOperationCursor(FlushScene.SceneOperationBlock? block)
        {
            this.block = block;
            this.index = 0;
        }

        /// <summary>
        /// Reads the next retained row operation.
        /// </summary>
        /// <param name="operation">The next operation.</param>
        /// <returns>True when an operation was read; otherwise false.</returns>
        public bool TryRead(out FlushScene.SceneOperation operation)
        {
            while (this.block is not null)
            {
                Span<FlushScene.SceneOperation> items = this.block.Items;
                if (this.index < items.Length)
                {
                    operation = items[this.index++];
                    return true;
                }

                this.block = this.block.Next;
                this.index = 0;
            }

            operation = default;
            return false;
        }
    }

    /// <summary>
    /// Represents one active composition target for a retained row.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private readonly struct BandTarget<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly Buffer2D<TPixel>? owner;

        /// <summary>
        /// Initializes a new instance of the <see cref="BandTarget{TPixel}"/> struct over an existing region.
        /// </summary>
        /// <param name="region">The destination region.</param>
        /// <param name="absoluteLeft">The absolute X origin of the region.</param>
        /// <param name="absoluteTop">The absolute Y origin of the region.</param>
        /// <param name="graphicsOptions">The graphics options used when this target is later composited.</param>
        public BandTarget(Buffer2DRegion<TPixel> region, int absoluteLeft, int absoluteTop, GraphicsOptions? graphicsOptions)
        {
            this.Region = region;
            this.AbsoluteLeft = absoluteLeft;
            this.AbsoluteTop = absoluteTop;
            this.GraphicsOptions = graphicsOptions;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BandTarget{TPixel}"/> struct over an owned temporary buffer.
        /// </summary>
        /// <param name="owner">The owned buffer backing the target.</param>
        /// <param name="bounds">The absolute bounds represented by the target.</param>
        /// <param name="graphicsOptions">The graphics options used when this target is later composited.</param>
        public BandTarget(Buffer2D<TPixel> owner, Rectangle bounds, GraphicsOptions? graphicsOptions)
        {
            this.owner = owner;
            this.Region = owner.GetRegion();
            this.AbsoluteLeft = bounds.X;
            this.AbsoluteTop = bounds.Y;
            this.GraphicsOptions = graphicsOptions;
        }

        /// <summary>
        /// Gets the writable pixel region for the target.
        /// </summary>
        public Buffer2DRegion<TPixel> Region { get; }

        /// <summary>
        /// Gets the absolute bounds represented by <see cref="Region"/>.
        /// </summary>
        public Rectangle Bounds => new(this.AbsoluteLeft, this.AbsoluteTop, this.Region.Width, this.Region.Height);

        /// <summary>
        /// Gets the absolute X origin of <see cref="Region"/>.
        /// </summary>
        public int AbsoluteLeft { get; }

        /// <summary>
        /// Gets the absolute Y origin of <see cref="Region"/>.
        /// </summary>
        public int AbsoluteTop { get; }

        /// <summary>
        /// Gets the graphics options associated with the target when it is used as a layer.
        /// </summary>
        public GraphicsOptions? GraphicsOptions { get; }

        /// <summary>
        /// Releases the owned temporary buffer when the target represents a layer.
        /// </summary>
        public void Dispose() => this.owner?.Dispose();
    }

    /// <summary>
    /// Holds the reusable worker-local scratch used while executing retained scene rows.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class WorkerState<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly MemoryAllocator allocator;
        private DefaultRasterizer.WorkerScratch? scratch;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkerState{TPixel}"/> class.
        /// </summary>
        /// <param name="allocator">The memory allocator used for scratch growth.</param>
        /// <param name="destinationWidth">The destination width used to size the brush workspace.</param>
        public WorkerState(
            MemoryAllocator allocator,
            int destinationWidth)
        {
            this.allocator = allocator;
            this.BrushWorkspace = new BrushWorkspace<TPixel>(allocator, destinationWidth);
        }

        /// <summary>
        /// Gets the reusable brush workspace for the worker.
        /// </summary>
        public BrushWorkspace<TPixel> BrushWorkspace { get; }

        /// <summary>
        /// Returns a reusable raster scratch instance sized for the requested width.
        /// </summary>
        /// <param name="requiredWidth">The minimum scanline width required by the current row.</param>
        /// <returns>A scratch instance that can execute the row.</returns>
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

        /// <summary>
        /// Releases the worker-local scratch and brush workspace.
        /// </summary>
        public void Dispose()
        {
            this.scratch?.Dispose();
            this.BrushWorkspace.Dispose();
        }
    }
}
