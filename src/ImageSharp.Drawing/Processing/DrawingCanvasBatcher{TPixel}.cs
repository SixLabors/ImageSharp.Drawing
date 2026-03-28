// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.Concurrent;
using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Queues normalized composition commands emitted by <see cref="DrawingCanvas{TPixel}"/>
/// and flushes them to <see cref="IDrawingBackend"/> in deterministic draw order.
/// </summary>
/// <remarks>
/// The batcher owns command buffering and normalization only; it does not rasterize or composite.
/// During flush it emits a <see cref="CompositionScene"/> so each backend can plan execution
/// (for example: CPU batching or GPU tiling) without changing the canvas call surface.
/// </remarks>
internal sealed class DrawingCanvasBatcher<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Configuration configuration;
    private readonly IDrawingBackend backend;
    private CompositionCommand[] commands;
    private int commandCount;
    private bool hasLayers;

    internal DrawingCanvasBatcher(
        Configuration configuration,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame)
    {
        this.configuration = configuration;
        this.backend = backend;
        this.commands = [];
        this.TargetFrame = targetFrame;
    }

    /// <summary>
    /// Gets the target frame that this batcher flushes to.
    /// </summary>
    public ICanvasFrame<TPixel> TargetFrame { get; }

    /// <summary>
    /// Appends one normalized composition command to the pending queue.
    /// </summary>
    /// <param name="composition">The command to queue.</param>
    public void AddComposition(in CompositionCommand composition)
    {
        this.EnsureCommandCapacity(this.commandCount + 1);
        this.commands[this.commandCount++] = composition;
        this.hasLayers |= composition.Kind is not CompositionCommandKind.FillLayer;
    }

    /// <summary>
    /// Flushes queued commands to the backend as one scene packet, preserving submission order.
    /// </summary>
    /// <remarks>
    /// Backends are responsible for planning execution (for example: grouping by coverage, caching,
    /// or GPU binning). The batcher only records scene commands and forwards them on flush.
    /// </remarks>
    public void FlushCompositions()
    {
        if (this.commandCount == 0)
        {
            return;
        }

        try
        {
            // Expand stroke commands to fills and clip to target bounds in parallel.
            // After this, every command has an immutable prepared path and visibility state.
            this.PrepareCommands();

            CompositionScene scene = new(
                new ArraySegment<CompositionCommand>(this.commands, 0, this.commandCount),
                this.hasLayers);
            this.backend.FlushCompositions(this.configuration, this.TargetFrame, scene);
        }
        finally
        {
            // Always clear the queue, even if backend flush throws.
            Array.Clear(this.commands, 0, this.commandCount);
            this.commandCount = 0;
            this.hasLayers = false;
        }
    }

    /// <summary>
    /// Prepares all queued commands in parallel. Each command expands strokes to fills,
    /// applies transforms, clips, flattens its path, and clips to target bounds.
    /// After this call every command is a fill with an immutable prepared path
    /// and pre-computed visibility.
    /// </summary>
    private void PrepareCommands()
        => _ = Parallel.ForEach(Partitioner.Create(0, this.commandCount), range =>
        {
            Span<CompositionCommand> span = this.commands.AsSpan(0, this.commandCount);
            for (int i = range.Item1; i < range.Item2; i++)
            {
                span[i].Prepare();
            }
        });

    /// <summary>
    /// Ensures that the command buffer can store the requested command count without reallocating.
    /// </summary>
    /// <param name="requiredCapacity">The required command capacity.</param>
    private void EnsureCommandCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= this.commands.Length)
        {
            return;
        }

        int nextCapacity = this.commands.Length == 0 ? 16 : this.commands.Length * 2;
        if (nextCapacity < requiredCapacity)
        {
            nextCapacity = requiredCapacity;
        }

        Array.Resize(ref this.commands, nextCapacity);
    }
}
