// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
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

        List<CompositionBatch> preparedBatches = CompositionScenePlanner.CreatePreparedBatches(
            compositionScene.Commands,
            target.Bounds);

        // A single reusable scratch is maintained across the batch loop so sequential-path
        // commands (single-tile or multi-band) avoid repeated pool allocation round-trips.
        // The parallel multi-tile path creates its own per-worker scratch and ignores this one.
        DefaultRasterizer.WorkerScratch? reusableScratch = null;
        try
        {
            for (int i = 0; i < preparedBatches.Count; i++)
            {
                this.FlushPreparedBatch(configuration, target, preparedBatches[i], ref reusableScratch);
            }
        }
        finally
        {
            reusableScratch?.Dispose();
        }
    }

    /// <inheritdoc />
    public void ReleaseFrameResources<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // No cached resources to release for CPU-only backend.
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
        DefaultRasterizer.WorkerScratch? noScratch = null;
        this.FlushPreparedBatch(configuration, target, compositionBatch, ref noScratch);
        noScratch?.Dispose();
    }

    private void FlushPreparedBatch<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionBatch compositionBatch,
        ref DefaultRasterizer.WorkerScratch? reusableScratch)
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

        IPath rasterPath = definition.Path;
        RasterizerOptions rasterizerOptions = definition.RasterizerOptions;

        if (definition.IsStroke && definition.StrokePattern.Length > 0)
        {
            // Dashed strokes: split into dash segments on the CPU, then stroke-expand
            // each segment via the per-band parallel path (same as solid strokes).
            rasterPath = rasterPath.GenerateDashes(definition.StrokeWidth, definition.StrokePattern.Span);

            // Recompute interest from the split path bounds with stroke expansion.
            float halfWidth = definition.StrokeWidth * 0.5f;
            float maxExtent = halfWidth * Math.Max((float)(definition.StrokeOptions?.MiterLimit ?? 4.0), 1.0f);
            RectangleF pathBounds = rasterPath.Bounds;
            pathBounds = new RectangleF(
                pathBounds.X + 0.5F - maxExtent,
                pathBounds.Y + 0.5F - maxExtent,
                pathBounds.Width + (maxExtent * 2),
                pathBounds.Height + (maxExtent * 2));

            Rectangle interest = Rectangle.FromLTRB(
                (int)MathF.Floor(pathBounds.Left),
                (int)MathF.Floor(pathBounds.Top),
                (int)MathF.Ceiling(pathBounds.Right),
                (int)MathF.Ceiling(pathBounds.Bottom));

            rasterizerOptions = new RasterizerOptions(
                interest,
                rasterizerOptions.IntersectionRule,
                rasterizerOptions.RasterizationMode,
                rasterizerOptions.SamplingOrigin,
                rasterizerOptions.AntialiasThreshold);

            // Re-prepare commands with the dash-split interest so destination
            // regions and source offsets are aligned with the rasterizer.
            CompositionScenePlanner.ReprepareBatchCommands(compositionBatch.Commands, target.Bounds, interest);
        }

        Rectangle destinationBounds = destinationFrame.Rectangle;
        List<PreparedCompositionCommand> commands = compositionBatch.Commands;
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
                rasterizerOptions.Interest.Top);

            if (definition.IsStroke)
            {
                // All strokes (solid and dashed) use per-band parallel stroke expansion.
                DefaultRasterizer.RasterizeStrokeRows(
                    rasterPath,
                    rasterizerOptions,
                    configuration.MemoryAllocator,
                    operation.InvokeCoverageRow,
                    ref reusableScratch,
                    definition.StrokeWidth,
                    definition.StrokeOptions!.LineJoin,
                    definition.StrokeOptions!.LineCap,
                    (float)definition.StrokeOptions!.MiterLimit);
            }
            else
            {
                DefaultRasterizer.RasterizeRows(
                    rasterPath,
                    rasterizerOptions,
                    configuration.MemoryAllocator,
                    operation.InvokeCoverageRow,
                    ref reusableScratch);
            }
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
        private readonly List<PreparedCompositionCommand> commands;
        private readonly BrushApplicator<TPixel>[] applicators;
        private readonly Rectangle destinationBounds;
        private readonly int coverageTop;

        public RowOperation(
            List<PreparedCompositionCommand> commands,
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
