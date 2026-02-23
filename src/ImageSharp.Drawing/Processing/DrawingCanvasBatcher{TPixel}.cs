// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Queues normalized composition commands emitted by <see cref="DrawingCanvas{TPixel}"/>
/// and flushes them to <see cref="IDrawingBackend"/> in deterministic draw order.
/// </summary>
/// <remarks>
/// The batcher owns command buffering and normalization only; it does not rasterize or composite.
/// During flush it groups consecutive commands sharing the same coverage definition into a single
/// <see cref="CompositionBatch"/> so backends rasterize once and apply multiple brushes in order.
/// </remarks>
internal sealed class DrawingCanvasBatcher<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private static int nextFlushId;
    private readonly Configuration configuration;
    private readonly IDrawingBackend backend;
    private readonly ICanvasFrame<TPixel> targetFrame;
    private readonly List<CompositionCommand> commands = [];

    internal DrawingCanvasBatcher(
        Configuration configuration,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame)
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(backend, nameof(backend));
        Guard.NotNull(targetFrame, nameof(targetFrame));

        this.configuration = configuration;
        this.backend = backend;
        this.targetFrame = targetFrame;
    }

    /// <summary>
    /// Appends one normalized composition command to the pending queue.
    /// </summary>
    /// <param name="composition">The command to queue.</param>
    public void AddComposition(in CompositionCommand composition)
        => this.commands.Add(composition);

    /// <summary>
    /// Flushes queued commands to the backend, preserving submission order.
    /// </summary>
    /// <remarks>
    /// This method performs only command normalization and grouping:
    /// <list type="number">
    /// <item><description>Split the queue into contiguous runs of matching <see cref="CompositionCommand.DefinitionKey"/>.</description></item>
    /// <item><description>Clip each run command to the target frame bounds.</description></item>
    /// <item><description>Compute <see cref="PreparedCompositionCommand.SourceOffset"/> so clipped destination pixels map to the correct coverage pixels.</description></item>
    /// <item><description>Send one <see cref="CompositionBatch"/> per contiguous run.</description></item>
    /// </list>
    /// The backend then rasterizes coverage once per batch definition and composites commands in order.
    /// </remarks>
    public void FlushCompositions()
    {
        if (this.commands.Count == 0)
        {
            return;
        }

        try
        {
            Rectangle targetBounds = this.targetFrame.Bounds;
            int index = 0;
            List<CompositionBatch> batches = [];
            while (index < this.commands.Count)
            {
                CompositionCommand definitionCommand = this.commands[index];
                int definitionKey = definitionCommand.DefinitionKey;

                // Build one batch for the contiguous run sharing the same coverage definition.
                List<PreparedCompositionCommand> preparedCommands = [];
                for (; index < this.commands.Count && this.commands[index].DefinitionKey == definitionKey; index++)
                {
                    CompositionCommand command = this.commands[index];
                    Rectangle interest = command.RasterizerOptions.Interest;
                    Rectangle commandDestination = new(
                        command.DestinationOffset.X + interest.X,
                        command.DestinationOffset.Y + interest.Y,
                        interest.Width,
                        interest.Height);

                    Rectangle clippedDestination = Rectangle.Intersect(targetBounds, commandDestination);

                    // Off-target commands in this run are dropped before backend dispatch.
                    if (clippedDestination.Width <= 0 || clippedDestination.Height <= 0)
                    {
                        continue;
                    }

                    Rectangle destinationLocalRegion = new(
                        clippedDestination.X - targetBounds.X,
                        clippedDestination.Y - targetBounds.Y,
                        clippedDestination.Width,
                        clippedDestination.Height);

                    Point sourceOffset = new(
                        clippedDestination.X - commandDestination.X,
                        clippedDestination.Y - commandDestination.Y);

                    // Keep command ordering exactly as submitted.
                    preparedCommands.Add(
                        new PreparedCompositionCommand(
                            destinationLocalRegion,
                            sourceOffset,
                            command.Brush,
                            command.BrushBounds,
                            command.GraphicsOptions));
                }

                if (preparedCommands.Count == 0)
                {
                    continue;
                }

                CompositionCoverageDefinition definition =
                    new(
                        definitionKey,
                        definitionCommand.Path,
                        definitionCommand.RasterizerOptions);

                batches.Add(new CompositionBatch(definition, preparedCommands));
            }

            if (batches.Count == 0)
            {
                return;
            }

            // All batches emitted by this call share one flush id so backends can keep
            // transient per-flush GPU state and finalize once on the last batch.
            int flushId = Interlocked.Increment(ref nextFlushId);
            for (int i = 0; i < batches.Count; i++)
            {
                CompositionBatch batch = batches[i];
                this.backend.FlushCompositions(
                    this.configuration,
                    this.targetFrame,
                    new CompositionBatch(
                        batch.Definition,
                        batch.Commands,
                        flushId,
                        isFinalBatchInFlush: i == batches.Count - 1));
            }
        }
        finally
        {
            // Always clear the queue, even if backend flush throws.
            this.commands.Clear();
        }
    }
}
