// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.Concurrent;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Default drawing backend.
/// </summary>
internal sealed class DefaultDrawingBackend : IDrawingBackend
{
    private readonly ConcurrentDictionary<int, Buffer2D<float>> coverageCache = new();

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
        => batcher.AddComposition(
            CompositionCommand.Create(path, brush, graphicsOptions, rasterizerOptions, target.Bounds.Location));

    /// <inheritdoc />
    public void FlushCompositions<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionBatch compositionBatch)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (compositionBatch.Commands.Count == 0)
        {
            return;
        }

        _ = target.TryGetCpuRegion(out Buffer2DRegion<TPixel> destinationFrame);
        CompositionCoverageDefinition definition = compositionBatch.Definition;
        Buffer2D<float> coverageMap = this.GetOrCreateCoverageMap(definition, configuration.MemoryAllocator);

        Rectangle destinationBounds = destinationFrame.Rectangle;
        IReadOnlyList<PreparedCompositionCommand> commands = compositionBatch.Commands;
        int commandCount = commands.Count;
        BrushApplicator<TPixel>[] applicators = new BrushApplicator<TPixel>[commandCount];
        try
        {
            int maxHeight = 0;
            for (int i = 0; i < commandCount; i++)
            {
                PreparedCompositionCommand command = commands[i];
                Buffer2DRegion<TPixel> commandRegion = destinationFrame.GetSubRegion(command.DestinationRegion);
                applicators[i] = command.Brush.CreateApplicator(
                    configuration,
                    command.GraphicsOptions,
                    commandRegion,
                    command.BrushBounds);

                if (command.DestinationRegion.Height > maxHeight)
                {
                    maxHeight = command.DestinationRegion.Height;
                }
            }

            for (int row = 0; row < maxHeight; row++)
            {
                for (int i = 0; i < commandCount; i++)
                {
                    PreparedCompositionCommand command = commands[i];
                    if (row >= command.DestinationRegion.Height)
                    {
                        continue;
                    }

                    int destinationX = destinationBounds.X + command.DestinationRegion.X;
                    int destinationY = destinationBounds.Y + command.DestinationRegion.Y;
                    int sourceStartX = command.SourceOffset.X;
                    int sourceStartY = command.SourceOffset.Y;

                    Span<float> rowCoverage = coverageMap.DangerousGetRowSpan(sourceStartY + row);
                    Span<float> rowSlice = rowCoverage.Slice(sourceStartX, command.DestinationRegion.Width);
                    applicators[i].Apply(rowSlice, destinationX, destinationY + row);
                }
            }
        }
        finally
        {
            for (int i = 0; i < applicators.Length; i++)
            {
                applicators[i]?.Dispose();
            }
        }
    }

    private Buffer2D<float> GetOrCreateCoverageMap(
        in CompositionCoverageDefinition definition,
        MemoryAllocator allocator)
    {
        CompositionCoverageDefinition localDefinition = definition;
        return this.coverageCache.GetOrAdd(
            localDefinition.DefinitionKey,
            _ => this.CreateCoverageMap(localDefinition, allocator));
    }

    private Buffer2D<float> CreateCoverageMap(
        in CompositionCoverageDefinition definition,
        MemoryAllocator allocator)
    {
        Size size = definition.RasterizerOptions.Interest.Size;
        Buffer2D<float> coverage = allocator.Allocate2D<float>(size, AllocationOptions.Clean);

        (Buffer2D<float> Buffer, int DestinationTop) state = (coverage, definition.RasterizerOptions.Interest.Top);
        this.PrimaryRasterizer.Rasterize(
            definition.Path,
            definition.RasterizerOptions,
            allocator,
            ref state,
            static (int y, Span<float> scanline, ref (Buffer2D<float> Buffer, int DestinationTop) callbackState) =>
            {
                int row = y - callbackState.DestinationTop;
                scanline.CopyTo(callbackState.Buffer.DangerousGetRowSpan(row));
            });

        return coverage;
    }

    public void Dispose()
    {
        foreach (Buffer2D<float> entry in this.coverageCache.Values)
        {
            entry.Dispose();
        }

        this.coverageCache.Clear();
    }
}
