// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// CPU backend that executes path coverage rasterization and brush composition directly against a CPU region.
/// </summary>
/// <remarks>
/// <para>
/// This backend provides the reference CPU implementation for composition behavior.
/// </para>
/// <para>
/// Flush execution is intentionally split:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <see cref="FlushCompositions{TPixel}(Configuration, ICanvasFrame{TPixel}, CompositionScene)"/>
/// converts scene commands into prepared batches with <see cref="CompositionScenePlanner"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="FlushPreparedBatch{TPixel}(Configuration, ICanvasFrame{TPixel}, CompositionBatch)"/>
/// rasterizes shared coverage scanlines per batch and applies brushes in original command order.
/// </description>
/// </item>
/// </list>
/// </remarks>
internal sealed class DefaultDrawingBackend : IDrawingBackend
{
    /// <summary>
    /// Gets the default backend instance.
    /// </summary>
    public static DefaultDrawingBackend Instance { get; } = new();

    /// <inheritdoc />
    public bool IsCompositionBrushSupported<TPixel>(Brush brush)
        where TPixel : unmanaged, IPixel<TPixel>
        => true;

    /// <inheritdoc />
    public void FillPath<TPixel>(
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
        CompositionScene compositionScene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (compositionScene.Commands.Count == 0)
        {
            return;
        }

        List<CompositionBatch> preparedBatches = CompositionScenePlanner.CreatePreparedBatches(
            compositionScene.Commands,
            target.Bounds);

        for (int i = 0; i < preparedBatches.Count; i++)
        {
            this.FlushPreparedBatch(configuration, target, preparedBatches[i]);
        }
    }

    /// <inheritdoc />
    public bool TryReadRegion<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        [NotNullWhen(true)] out Image<TPixel>? image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));

        // CPU backend readback is available only when the target exposes CPU pixels.
        if (!target.TryGetCpuRegion(out Buffer2DRegion<TPixel> sourceRegion))
        {
            image = null;
            return false;
        }

        // Clamp the request to the target region to avoid out-of-range row slicing.
        Rectangle clipped = Rectangle.Intersect(
            new Rectangle(0, 0, sourceRegion.Width, sourceRegion.Height),
            sourceRectangle);

        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            image = null;
            return false;
        }

        // Build a tightly packed temporary image for downstream processing operations.
        image = new(configuration, clipped.Width, clipped.Height);
        Buffer2D<TPixel> destination = image.Frames.RootFrame.PixelBuffer;
        for (int y = 0; y < clipped.Height; y++)
        {
            sourceRegion.DangerousGetRowSpan(clipped.Y + y)
                .Slice(clipped.X, clipped.Width)
                .CopyTo(destination.DangerousGetRowSpan(y));
        }

        return true;
    }

    /// <summary>
    /// Executes one prepared batch on the CPU.
    /// </summary>
    /// <typeparam name="TPixel">The destination pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="target">The destination frame.</param>
    /// <param name="compositionBatch">
    /// One prepared batch where all commands share the same coverage definition and differ only by brush/options.
    /// </param>
    /// <remarks>
    /// This method is intentionally reusable so GPU backends can delegate unsupported batches
    /// without reconstructing a full <see cref="CompositionScene"/>.
    /// </remarks>
    internal void FlushPreparedBatch<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionBatch compositionBatch)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (compositionBatch.Commands.Count == 0)
        {
            return;
        }

        if (!target.TryGetCpuRegion(out Buffer2DRegion<TPixel> destinationFrame))
        {
            throw new NotSupportedException($"{nameof(DefaultDrawingBackend)} requires CPU-accessible frame targets.");
        }

        CompositionCoverageDefinition definition = compositionBatch.Definition;
        Rectangle destinationBounds = destinationFrame.Rectangle;
        IReadOnlyList<PreparedCompositionCommand> commands = compositionBatch.Commands;
        int commandCount = commands.Count;
        BrushApplicator<TPixel>[] applicators = new BrushApplicator<TPixel>[commandCount];
        try
        {
            for (int i = 0; i < commandCount; i++)
            {
                PreparedCompositionCommand command = commands[i];
                Buffer2DRegion<TPixel> commandRegion = destinationFrame.GetSubRegion(command.DestinationRegion);
                applicators[i] = command.Brush.CreateApplicator(
                    configuration,
                    command.GraphicsOptions,
                    commandRegion,
                    command.BrushBounds);
            }

            // Stream composition directly from rasterizer scanlines so we do not allocate
            // and then re-read an intermediate coverage map.
            RowOperation<TPixel> operation = new(
                commands,
                applicators,
                destinationBounds,
                definition.RasterizerOptions.Interest.Top);

            DefaultRasterizer.RasterizeRows(
                definition.Path,
                definition.RasterizerOptions,
                configuration.MemoryAllocator,
                operation.InvokeCoverageRow);
        }
        finally
        {
            foreach (BrushApplicator<TPixel>? applicator in applicators)
            {
                applicator?.Dispose();
            }
        }
    }

    private readonly struct RowOperation<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly IReadOnlyList<PreparedCompositionCommand> commands;
        private readonly BrushApplicator<TPixel>[] applicators;
        private readonly Rectangle destinationBounds;
        private readonly int coverageTop;

        public RowOperation(
            IReadOnlyList<PreparedCompositionCommand> commands,
            BrushApplicator<TPixel>[] applicators,
            Rectangle destinationBounds,
            int coverageTop)
        {
            this.commands = commands;
            this.applicators = applicators;
            this.destinationBounds = destinationBounds;
            this.coverageTop = coverageTop;
        }

        public void InvokeCoverageRow(int y, int startX, Span<float> coverage)
        {
            int sourceY = y - this.coverageTop;
            int rowStart = startX;
            int rowEnd = startX + coverage.Length;

            Rectangle destinationBounds = this.destinationBounds;
            BrushApplicator<TPixel>[] applicators = this.applicators;
            for (int i = 0; i < this.commands.Count; i++)
            {
                PreparedCompositionCommand command = this.commands[i];
                Rectangle commandDestination = command.DestinationRegion;

                int commandY = sourceY - command.SourceOffset.Y;
                if ((uint)commandY >= (uint)commandDestination.Height)
                {
                    continue;
                }

                int sourceStartX = command.SourceOffset.X;
                int sourceEndX = sourceStartX + commandDestination.Width;
                int overlapStart = Math.Max(rowStart, sourceStartX);
                int overlapEnd = Math.Min(rowEnd, sourceEndX);
                if (overlapEnd <= overlapStart)
                {
                    continue;
                }

                int localStart = overlapStart - rowStart;
                int localLength = overlapEnd - overlapStart;
                int destinationX = destinationBounds.X + commandDestination.X + (overlapStart - sourceStartX);
                int destinationY = destinationBounds.Y + commandDestination.Y + commandY;

                applicators[i].Apply(coverage.Slice(localStart, localLength), destinationX, destinationY);
            }
        }
    }
}
