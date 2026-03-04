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
/// During flush it emits a <see cref="CompositionScene"/> so each backend can plan execution
/// (for example: CPU batching or GPU tiling) without changing the canvas call surface.
/// </remarks>
internal sealed class DrawingCanvasBatcher<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Configuration configuration;
    private readonly IDrawingBackend backend;
    private readonly ICanvasFrame<TPixel> targetFrame;
    private readonly List<CompositionCommand> commands = [];
    private DrawingCanvasBatcher<TPixel>? mirrorBatcher;

    internal DrawingCanvasBatcher(
        Configuration configuration,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame)
    {
        this.configuration = configuration;
        this.backend = backend;
        this.targetFrame = targetFrame;
    }

    /// <summary>
    /// Appends one normalized composition command to the pending queue.
    /// </summary>
    /// <param name="composition">The command to queue.</param>
    public void AddComposition(in CompositionCommand composition)
    {
        this.commands.Add(composition);
        this.mirrorBatcher?.commands.Add(composition);
    }

    /// <summary>
    /// Sets an optional mirror batcher that receives the same queued commands.
    /// </summary>
    /// <param name="mirrorBatcher">The mirror batcher, or <see langword="null"/> to disable mirroring.</param>
    public void SetMirror(DrawingCanvasBatcher<TPixel>? mirrorBatcher)
        => this.mirrorBatcher = mirrorBatcher;

    /// <summary>
    /// Flushes queued commands to the backend as one scene packet, preserving submission order.
    /// </summary>
    /// <remarks>
    /// Backends are responsible for planning execution (for example: grouping by coverage, caching,
    /// or GPU binning). The batcher only records scene commands and forwards them on flush.
    /// </remarks>
    public void FlushCompositions()
    {
        if (this.commands.Count == 0)
        {
            return;
        }

        try
        {
            CompositionScene scene = new(this.commands.ToArray());
            this.backend.FlushCompositions(this.configuration, this.targetFrame, scene);
        }
        finally
        {
            // Always clear the queue, even if backend flush throws.
            this.commands.Clear();
        }
    }
}
