// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Default drawing backend.
/// </summary>
internal sealed class DefaultDrawingBackend : IDrawingBackend
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDrawingBackend"/> class.
    /// </summary>
    /// <param name="primaryRasterizer">Rasterizer used for coverage generation.</param>
    private DefaultDrawingBackend(IRasterizer primaryRasterizer)
    {
        Guard.NotNull(primaryRasterizer, nameof(primaryRasterizer));
        this.PrimaryRasterizer = primaryRasterizer;
    }

    /// <summary>
    /// Gets the default backend instance.
    /// </summary>
    public static DefaultDrawingBackend Instance { get; } = new(DefaultRasterizer.Instance);

    /// <summary>
    /// Gets the primary rasterizer used by this backend.
    /// </summary>
    public IRasterizer PrimaryRasterizer { get; }

    /// <summary>
    /// Creates a backend that uses the given rasterizer as the primary implementation.
    /// </summary>
    /// <param name="rasterizer">Primary rasterizer.</param>
    /// <returns>A backend instance.</returns>
    public static DefaultDrawingBackend Create(IRasterizer rasterizer)
    {
        Guard.NotNull(rasterizer, nameof(rasterizer));
        return ReferenceEquals(rasterizer, DefaultRasterizer.Instance) ? Instance : new DefaultDrawingBackend(rasterizer);
    }

    /// <inheritdoc />
    public void FillPath<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        DrawingCanvasBatcher<TPixel> batcher)
        where TPixel : unmanaged, IPixel<TPixel>
        => batcher.AddComposition(CompositionCommand.Create(path, brush, graphicsOptions, rasterizerOptions));

    /// <inheritdoc />
    public void FlushCompositions<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        IReadOnlyList<CompositionCommand> compositions)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        _ = target.TryGetCpuRegion(out Buffer2DRegion<TPixel> destinationFrame);

        CompositionCommand coverageDefinition = compositions[0];
        using Buffer2D<float> coverageMap = this.CreateCoverageMap(coverageDefinition, configuration.MemoryAllocator);
        Buffer2DRegion<TPixel> destinationRegion = destinationFrame.GetSubRegion(coverageDefinition.RasterizerOptions.Interest);

        for (int row = 0; row < coverageMap.Height; row++)
        {
            Span<float> rowCoverage = coverageMap.DangerousGetRowSpan(row);
            int y = destinationRegion.Rectangle.Y + row;

            for (int i = 0; i < compositions.Count; i++)
            {
                CompositionCommand command = compositions[i];

                // TODO: This should be optimized to avoid creating multiple applicators
                // for the same brush/graphics options.
                // We should create them first outside of the loop then dispose after.
                using BrushApplicator<TPixel> applicator = command.Brush.CreateApplicator(
                    configuration,
                    command.GraphicsOptions,
                    destinationRegion,
                    command.BrushBounds);

                applicator.Apply(rowCoverage, destinationRegion.Rectangle.X, y);
            }
        }
    }

    private Buffer2D<float> CreateCoverageMap(
        CompositionCommand command,
        MemoryAllocator allocator)
    {
        Size size = command.RasterizerOptions.Interest.Size;
        Buffer2D<float> coverage = allocator.Allocate2D<float>(size, AllocationOptions.Clean);

        (Buffer2D<float> Buffer, int DestinationTop) state = (coverage, command.RasterizerOptions.Interest.Top);
        this.PrimaryRasterizer.Rasterize(
            command.Path,
            command.RasterizerOptions,
            allocator,
            ref state,
            static (int y, Span<float> scanline, ref (Buffer2D<float> Buffer, int DestinationTop) callbackState) =>
            {
                int row = y - callbackState.DestinationTop;
                scanline.CopyTo(callbackState.Buffer.DangerousGetRowSpan(row));
            });

        return coverage;
    }
}
