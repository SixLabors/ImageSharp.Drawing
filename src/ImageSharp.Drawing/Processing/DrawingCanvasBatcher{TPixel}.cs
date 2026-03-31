// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
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
    private CompositionSceneCommand[] commands;
    private int commandCount;
    private bool hasLayers;
    private bool hasClips;

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
        this.commands[this.commandCount++] = new PathCompositionSceneCommand(composition);
        this.hasLayers |= composition.Kind is not CompositionCommandKind.FillLayer;
        this.hasClips |= composition.ClipPaths is not null;
    }

    /// <summary>
    /// Appends one explicit stroked line-segment command to the pending queue.
    /// </summary>
    /// <param name="command">The command to queue.</param>
    public void AddStrokeLineSegment(in StrokeLineSegmentCommand command)
    {
        this.EnsureCommandCapacity(this.commandCount + 1);
        this.commands[this.commandCount++] = new LineSegmentCompositionSceneCommand(command);
    }

    /// <summary>
    /// Appends one explicit stroked polyline command to the pending queue.
    /// </summary>
    /// <param name="command">The command to queue.</param>
    public void AddStrokePolyline(in StrokePolylineCommand command)
    {
        this.EnsureCommandCapacity(this.commandCount + 1);
        this.commands[this.commandCount++] = new PolylineCompositionSceneCommand(command);
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
            this.ApplyClipping();

            CompositionScene scene = new(this.commands, this.hasLayers);

            this.backend.FlushCompositions(this.configuration, this.TargetFrame, scene);
        }
        finally
        {
            Array.Clear(this.commands, 0, this.commandCount);
            this.commandCount = 0;
            this.hasLayers = false;
        }
    }

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

    private void ApplyClipping()
    {
        if (!this.hasClips)
        {
            return;
        }

        _ = Parallel.For(0, this.commandCount, i =>
        {
            CompositionSceneCommand command = this.commands[i];
            if (command is PathCompositionSceneCommand pathCommand)
            {
                CompositionCommand composition = pathCommand.Command;

                // If clipping is present we need to apply that now before handing the command
                // to the backend. This avoids complicating the backend with clipping logic
                // and allows us to reuse the same optimized backend code for clipped and unclipped paths.
                if (composition.ClipPaths is { Count: > 0 })
                {
                    IPath path = composition.SourcePath;

                    path = path.Transform(composition.Transform);

                    if (composition.Pen is not null)
                    {
                        path = path.GenerateOutline(composition.Pen.StrokeWidth);
                    }

                    path = path.Clip(composition.ShapeOptions, composition.ClipPaths);

                    RasterizerOptions rasterizerOptions = composition.RasterizerOptions;

                    // Update the command with the clipped path.
                    pathCommand.Command = CompositionCommand.Create(
                        path,
                        composition.Brush.Transform(composition.Transform),
                        composition.GraphicsOptions,
                        in rasterizerOptions,
                        composition.ShapeOptions,
                        Matrix4x4.Identity,
                        composition.TargetBounds,
                        composition.DestinationOffset);
                }
            }
        });
    }
}
