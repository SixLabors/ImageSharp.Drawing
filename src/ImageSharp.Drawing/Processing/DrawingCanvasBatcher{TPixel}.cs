// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
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
    private readonly List<CompositionCommand> commands = [];

    internal DrawingCanvasBatcher(
        Configuration configuration,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame)
    {
        this.configuration = configuration;
        this.backend = backend;
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
        => this.commands.Add(composition);

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
            // Expand stroke commands to fills in parallel.
            // After this, every command has an immutable pre-flattened fill path.
            this.PrepareCommands();

            CompositionScene scene = new(this.commands);
            this.backend.FlushCompositions(this.configuration, this.TargetFrame, scene);
        }
        finally
        {
            // Always clear the queue, even if backend flush throws.
            this.commands.Clear();
        }
    }

    /// <summary>
    /// Prepares all queued commands in parallel. Each command expands strokes to fills,
    /// applies transforms, clips, and flattens its path via <see cref="CompositionCommand.Prepare(GeometryPreparationCache?)"/>.
    /// After this call every command is a fill with an immutable pre-flattened path.
    /// </summary>
    private void PrepareCommands()
    {
        GeometryPreparationCache geometryCache = new();

        _ = Parallel.ForEach(Partitioner.Create(0, this.commands.Count), range =>
        {
            Span<CompositionCommand> span = CollectionsMarshal.AsSpan(this.commands);
            for (int i = range.Item1; i < range.Item2; i++)
            {
                span[i].Prepare(geometryCache);
            }
        });
    }
}
