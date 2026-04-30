// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Queues normalized composition commands emitted by <see cref="DrawingCanvas{TPixel}"/>
/// and prepares them in deterministic draw order.
/// </summary>
/// <remarks>
/// The batcher owns command buffering and replay ordering only; it does not rasterize or composite.
/// Draw commands are stored in the command buffer until a timeline command-range entry references
/// them. Existing retained scenes passed through <see cref="DrawingCanvas.RenderScene"/> are stored
/// separately and referenced by timeline entry index. During disposal replay, command ranges are
/// lowered to short-lived backend scenes at the position where the canvas recorded the range.
/// </remarks>
internal sealed class DrawingCanvasBatcher<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Configuration configuration;

    // Draw commands stay in this buffer until replay lowers referenced command ranges
    // into backend scenes at their recorded timeline position.
    private CompositionSceneCommand[] commands;
    private int commandCount;
    private int sealedCommandCount;

    // Layer metadata is range-sensitive, so sealing advances this alongside command
    // sealing instead of letting layer state leak across later command ranges.
    private int layerCommandCount;
    private int sealedLayerCommandCount;

    // Clip and dash flags gate whole-buffer command preparation; prepared commands
    // remain in the same command buffer until replay consumes it.
    private bool hasClips;
    private bool hasDashes;

    // Timeline entries keep compact indexes into the command, barrier, and retained
    // scene buffers while preserving the order recorded by the canvas.
    private DrawingCanvasTimelineEntry[] entries;

    // Apply barriers carry replay-time target read/process/write operations.
    private ApplyBarrier[] applyBarriers;
    private int applyBarrierCount;

    // These are existing retained scenes recorded through RenderScene, not scenes
    // produced later from this batcher's own command ranges.
    private DrawingBackendScene[] insertedScenes;
    private int insertedSceneCount;

    internal DrawingCanvasBatcher(Configuration configuration)
    {
        this.configuration = configuration;
        this.commands = [];
        this.entries = [];
        this.applyBarriers = [];
        this.insertedScenes = [];
    }

    /// <summary>
    /// Gets a value indicating whether there are queued commands or timeline entries.
    /// </summary>
    public bool HasRecordedWork => this.commandCount > 0 || this.TimelineEntryCount > 0;

    /// <summary>
    /// Gets the number of ordered replay items recorded in the canvas timeline.
    /// </summary>
    /// <remarks>
    /// This is not a draw-command count. A single entry can represent a contiguous command range,
    /// an apply barrier, or an inserted retained scene.
    /// </remarks>
    public int TimelineEntryCount { get; private set; }

    /// <summary>
    /// Appends one normalized composition command to the pending queue.
    /// </summary>
    /// <param name="composition">The command to queue.</param>
    public void AddComposition(in CompositionCommand composition)
    {
        this.EnsureCommandCapacity(this.commandCount + 1);
        this.commands[this.commandCount++] = new PathCompositionSceneCommand(composition);

        if (composition.Kind is not CompositionCommandKind.FillLayer)
        {
            this.layerCommandCount++;
        }

        this.hasClips |= composition.ClipPaths is not null;
    }

    /// <summary>
    /// Appends one stroked path command to the pending queue.
    /// </summary>
    /// <param name="command">The command to queue.</param>
    public void AddStrokePath(in StrokePathCommand command)
    {
        this.EnsureCommandCapacity(this.commandCount + 1);
        this.commands[this.commandCount++] = new StrokePathCompositionSceneCommand(command);
        this.hasClips |= command.ClipPaths is not null;
        this.hasDashes |= command.Pen.StrokePattern.Length >= 2;
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
    /// Seals currently queued commands into the replay timeline.
    /// </summary>
    /// <remarks>
    /// This records a command range only. Backend scenes are created later by the replay path
    /// from the referenced command range, so sealing does not render or allocate backend scene state.
    /// </remarks>
    public void SealCommands()
    {
        int count = this.commandCount - this.sealedCommandCount;
        if (count == 0)
        {
            return;
        }

        this.EnsureEntryCapacity(this.TimelineEntryCount + 1);
        this.entries[this.TimelineEntryCount++] = DrawingCanvasTimelineEntry.CreateCommandRange(
            this.sealedCommandCount,
            count,
            this.layerCommandCount != this.sealedLayerCommandCount);

        this.sealedCommandCount = this.commandCount;
        this.sealedLayerCommandCount = this.layerCommandCount;
    }

    /// <summary>
    /// Appends an apply barrier to the replay timeline after sealing queued commands.
    /// </summary>
    /// <param name="barrier">The apply barrier to append.</param>
    internal void AddApplyBarrier(ApplyBarrier barrier)
    {
        this.SealCommands();
        this.EnsureApplyBarrierCapacity(this.applyBarrierCount + 1);

        int barrierIndex = this.applyBarrierCount;
        this.applyBarriers[this.applyBarrierCount++] = barrier;
        this.EnsureEntryCapacity(this.TimelineEntryCount + 1);
        this.entries[this.TimelineEntryCount++] = DrawingCanvasTimelineEntry.CreateApplyBarrier(barrierIndex);
    }

    /// <summary>
    /// Records an existing retained scene in the replay timeline after sealing queued commands.
    /// </summary>
    /// <remarks>
    /// This stores only scenes passed to <see cref="DrawingCanvas.RenderScene"/>. Scenes produced
    /// from this canvas's own command ranges are created later by the backend from command batches.
    /// </remarks>
    /// <param name="scene">The retained scene to render at this point in the timeline.</param>
    public void AddScene(DrawingBackendScene scene)
    {
        this.SealCommands();
        this.EnsureInsertedSceneCapacity(this.insertedSceneCount + 1);

        int sceneIndex = this.insertedSceneCount;
        this.insertedScenes[this.insertedSceneCount++] = scene;
        this.EnsureEntryCapacity(this.TimelineEntryCount + 1);
        this.entries[this.TimelineEntryCount++] = DrawingCanvasTimelineEntry.CreateScene(sceneIndex);
    }

    /// <summary>
    /// Creates a retained backend scene from the recorded timeline.
    /// </summary>
    /// <param name="backend">The backend used to create the retained scene.</param>
    /// <param name="targetBounds">The target bounds used for target-dependent scene creation.</param>
    /// <param name="ownedResources">The resources that must stay alive for the returned scene.</param>
    /// <returns>The retained backend scene.</returns>
    public DrawingBackendScene CreateScene(
        IDrawingBackend backend,
        Rectangle targetBounds,
        IReadOnlyList<IDisposable>? ownedResources)
    {
        if (!this.HasRecordedWork)
        {
            throw new InvalidOperationException("Cannot create a retained scene from an empty canvas.");
        }

        this.SealAndPrepareCommands();

        return backend.CreateScene(
            this.configuration,
            targetBounds,
            new DrawingCommandBatch(this.commands, this.commandCount, this.layerCommandCount > 0),
            ownedResources);
    }

    /// <summary>
    /// Seals any pending commands and prepares queued command data for backend scene creation.
    /// </summary>
    public void SealAndPrepareCommands()
    {
        this.SealCommands();

        this.PrepareCommands();
    }

    /// <summary>
    /// Creates a command batch over one recorded command-range timeline entry.
    /// </summary>
    /// <param name="entry">The command-range timeline entry.</param>
    /// <returns>The command batch.</returns>
    public DrawingCommandBatch CreateCommandBatch(DrawingCanvasTimelineEntry entry)
        => new(this.commands, entry.Index, entry.Count, entry.HasLayers);

    /// <summary>
    /// Gets one recorded timeline entry.
    /// </summary>
    /// <param name="index">The entry index.</param>
    /// <returns>The recorded timeline entry.</returns>
    public DrawingCanvasTimelineEntry GetEntry(int index)
        => this.entries[index];

    /// <summary>
    /// Gets one recorded apply barrier.
    /// </summary>
    /// <param name="index">The apply-barrier index.</param>
    /// <returns>The recorded apply barrier.</returns>
    internal ApplyBarrier GetApplyBarrier(int index)
        => this.applyBarriers[index];

    /// <summary>
    /// Gets one retained scene reference recorded through <see cref="DrawingCanvas.RenderScene"/>.
    /// </summary>
    /// <param name="index">The retained-scene reference index.</param>
    /// <returns>The retained scene to render at the timeline entry.</returns>
    public DrawingBackendScene GetInsertedScene(int index)
        => this.insertedScenes[index];

    /// <summary>
    /// Clears command references after a prepared batch has been consumed.
    /// </summary>
    public void ClearCommandBatch()
    {
        Array.Clear(this.commands, 0, this.commandCount);
        Array.Clear(this.entries, 0, this.TimelineEntryCount);
        Array.Clear(this.applyBarriers, 0, this.applyBarrierCount);
        Array.Clear(this.insertedScenes, 0, this.insertedSceneCount);
        this.commandCount = 0;
        this.sealedCommandCount = 0;
        this.layerCommandCount = 0;
        this.sealedLayerCommandCount = 0;
        this.TimelineEntryCount = 0;
        this.applyBarrierCount = 0;
        this.insertedSceneCount = 0;
        this.hasClips = false;
        this.hasDashes = false;
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

    /// <summary>
    /// Ensures that the timeline entry buffer can store the requested entry count without reallocating.
    /// </summary>
    /// <param name="requiredCapacity">The required entry capacity.</param>
    private void EnsureEntryCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= this.entries.Length)
        {
            return;
        }

        int nextCapacity = this.entries.Length == 0 ? 4 : this.entries.Length * 2;
        if (nextCapacity < requiredCapacity)
        {
            nextCapacity = requiredCapacity;
        }

        Array.Resize(ref this.entries, nextCapacity);
    }

    /// <summary>
    /// Ensures that the apply-barrier buffer can store the requested barrier count without reallocating.
    /// </summary>
    /// <param name="requiredCapacity">The required barrier capacity.</param>
    private void EnsureApplyBarrierCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= this.applyBarriers.Length)
        {
            return;
        }

        int nextCapacity = this.applyBarriers.Length == 0 ? 2 : this.applyBarriers.Length * 2;
        if (nextCapacity < requiredCapacity)
        {
            nextCapacity = requiredCapacity;
        }

        Array.Resize(ref this.applyBarriers, nextCapacity);
    }

    /// <summary>
    /// Ensures that the inserted-scene buffer can store the requested scene count without reallocating.
    /// </summary>
    /// <param name="requiredCapacity">The required scene capacity.</param>
    private void EnsureInsertedSceneCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= this.insertedScenes.Length)
        {
            return;
        }

        int nextCapacity = this.insertedScenes.Length == 0 ? 2 : this.insertedScenes.Length * 2;
        if (nextCapacity < requiredCapacity)
        {
            nextCapacity = requiredCapacity;
        }

        Array.Resize(ref this.insertedScenes, nextCapacity);
    }

    private void PrepareCommands()
    {
        if (!this.hasClips && !this.hasDashes)
        {
            return;
        }

        // If clipping is present we need to apply that now before handing the command
        // to the backend. This avoids complicating the backend with clipping logic
        // and allows us to reuse the same optimized backend code for clipped and unclipped paths.
        int requestedParallelism = this.configuration.MaxDegreeOfParallelism;
        int partitionCount = ParallelExecutionHelper.GetPartitionCount(requestedParallelism, this.commandCount);

        if (partitionCount <= 1)
        {
            for (int i = 0; i < this.commandCount; i++)
            {
                PrepareCommand(ref this.commands[i]);
            }

            return;
        }

        _ = Parallel.For(
            0,
            partitionCount,
            ParallelExecutionHelper.CreateParallelOptions(requestedParallelism, partitionCount),
            partitionIndex =>
            {
                // Integer division splits the commands into contiguous half-open ranges,
                // keeping the partitions balanced while assigning each command exactly once.
                int commandStart = (partitionIndex * this.commandCount) / partitionCount;
                int commandEnd = ((partitionIndex + 1) * this.commandCount) / partitionCount;

                for (int i = commandStart; i < commandEnd; i++)
                {
                    PrepareCommand(ref this.commands[i]);
                }
            });
    }

    private static void PrepareCommand(ref CompositionSceneCommand command)
    {
        if (command is PathCompositionSceneCommand pathCommand)
        {
            CompositionCommand composition = pathCommand.Command;
            if (composition.ClipPaths is { Count: > 0 })
            {
                IPath path = composition.SourcePath;
                DrawingOptions sourceOptions = composition.DrawingOptions;

                if (sourceOptions.Transform != Matrix4x4.Identity)
                {
                    path = path.Transform(sourceOptions.Transform);
                }

                path = path.Clip(sourceOptions.ShapeOptions, composition.ClipPaths);

                RasterizerOptions rasterizerOptions = composition.RasterizerOptions;
                DrawingOptions preparedOptions = WithIdentityTransform(sourceOptions);

                // Update the command with the clipped path.
                pathCommand.Command = CompositionCommand.Create(
                    path,
                    composition.Brush.Transform(sourceOptions.Transform),
                    preparedOptions,
                    in rasterizerOptions,
                    composition.TargetBounds,
                    composition.DestinationOffset,
                    null,
                    composition.IsInsideLayer);
            }
        }
        else if (command is StrokePathCompositionSceneCommand strokePathCommand)
        {
            StrokePathCommand composition = strokePathCommand.Command;

            if (composition.ClipPaths is { Count: > 0 })
            {
                IPath path = composition.Pen.GeneratePath(composition.SourcePath);
                DrawingOptions sourceOptions = composition.DrawingOptions;

                if (sourceOptions.Transform != Matrix4x4.Identity)
                {
                    path = path.Transform(sourceOptions.Transform);
                }

                path = path.Clip(sourceOptions.ShapeOptions, composition.ClipPaths);

                RasterizerOptions rasterizerOptions = composition.RasterizerOptions;
                DrawingOptions preparedOptions = WithIdentityTransform(sourceOptions);

                command = new PathCompositionSceneCommand(
                    CompositionCommand.Create(
                        path,
                        composition.Brush.Transform(sourceOptions.Transform),
                        preparedOptions,
                        in rasterizerOptions,
                        composition.TargetBounds,
                        composition.DestinationOffset,
                        null,
                        composition.IsInsideLayer));
            }
            else
            {
                // We need to dash the path here before sending it to the backend.
                Pen pen = composition.Pen;
                if (pen.StrokePattern.Length >= 2)
                {
                    strokePathCommand.Command = new StrokePathCommand(
                        composition.SourcePath.GenerateDashes(pen.StrokeWidth, pen.StrokePattern.Span),
                        composition.Brush,
                        composition.DrawingOptions,
                        composition.RasterizerOptions,
                        composition.TargetBounds,
                        composition.DestinationOffset,
                        composition.Pen,
                        null,
                        composition.IsInsideLayer);
                }
            }
        }
    }

    private static DrawingOptions WithIdentityTransform(DrawingOptions source)
        => source.Transform == Matrix4x4.Identity
            ? source
            : new DrawingOptions(source.GraphicsOptions, source.ShapeOptions, Matrix4x4.Identity);
}
