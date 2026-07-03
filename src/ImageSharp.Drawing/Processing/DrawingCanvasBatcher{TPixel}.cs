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
/// <typeparam name="TPixel">The pixel format.</typeparam>
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
    private int applyCommandCount;
    private int sealedApplyCommandCount;
    private int clipCommandCount;
    private int sealedClipCommandCount;

    // Dash and brush-transform flags gate whole-buffer command preparation; prepared commands
    // remain in the same command buffer until replay consumes it.
    private bool hasDashes;
    private bool hasBrushTransforms;

    // Mirrors the physical begin/end-clip command stream. The stream is the single source of
    // truth for clipping in both backends; the canvas re-anchors it via EnsureClipAnchors when
    // drawing moves to a differently-offset context (for example a child region).
    private readonly List<(DrawingClipDescriptor Descriptor, Point AnchorOffset)> openClips = [];

    // Timeline entries keep compact indexes into the command and retained
    // scene buffers while preserving the order recorded by the canvas.
    private DrawingCanvasTimelineEntry[] entries;

    // These are existing retained scenes recorded through RenderScene, not scenes
    // produced later from this batcher's own command ranges.
    private DrawingBackendScene[] insertedScenes;
    private int insertedSceneCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvasBatcher{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration providing parallelism settings for command preparation.</param>
    internal DrawingCanvasBatcher(Configuration configuration)
    {
        this.configuration = configuration;
        this.commands = [];
        this.entries = [];
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
        switch (composition.Kind)
        {
            case CompositionCommandKind.BeginClip:
                this.openClips.Add((composition.ClipDescriptor, composition.DestinationOffset));
                break;

            case CompositionCommandKind.EndClip:
                if (this.openClips.Count > 0)
                {
                    this.openClips.RemoveAt(this.openClips.Count - 1);
                }

                break;

            default:
                break;
        }

        this.EnsureCommandCapacity(this.commandCount + 1);
        this.commands[this.commandCount++] = new PathCompositionSceneCommand(composition);

        if (composition.Kind is CompositionCommandKind.BeginLayer or CompositionCommandKind.EndLayer)
        {
            this.layerCommandCount++;
        }
        else if (composition.Kind is CompositionCommandKind.BeginClip or CompositionCommandKind.EndClip)
        {
            this.clipCommandCount++;
        }

        this.hasBrushTransforms |= composition.Kind is CompositionCommandKind.FillLayer
            && composition.Transform != Matrix4x4.Identity;
    }

    /// <summary>
    /// Appends one stroked path command to the pending queue.
    /// </summary>
    /// <param name="command">The command to queue.</param>
    public void AddStrokePath(in StrokePathCommand command)
    {
        this.EnsureCommandCapacity(this.commandCount + 1);
        this.commands[this.commandCount++] = new StrokePathCompositionSceneCommand(command);
        this.hasDashes |= command.Pen.StrokePattern.Length >= 2;
        this.hasBrushTransforms |= command.Transform != Matrix4x4.Identity;
    }

    /// <summary>
    /// Appends one explicit stroked line-segment command to the pending queue.
    /// </summary>
    /// <param name="command">The command to queue.</param>
    public void AddStrokeLineSegment(in StrokeLineSegmentCommand command)
    {
        this.EnsureCommandCapacity(this.commandCount + 1);
        this.commands[this.commandCount++] = new LineSegmentCompositionSceneCommand(command);
        this.hasBrushTransforms |= command.Transform != Matrix4x4.Identity;
    }

    /// <summary>
    /// Appends one explicit stroked polyline command to the pending queue.
    /// </summary>
    /// <param name="command">The command to queue.</param>
    public void AddStrokePolyline(in StrokePolylineCommand command)
    {
        this.EnsureCommandCapacity(this.commandCount + 1);
        this.commands[this.commandCount++] = new PolylineCompositionSceneCommand(command);
        this.hasBrushTransforms |= command.Transform != Matrix4x4.Identity;
    }

    /// <summary>
    /// Re-anchors the open ordered clip stream at the consuming state's destination offset.
    /// </summary>
    /// <remarks>
    /// Clip descriptors are recorded in canvas-local coordinates and the begin/end-clip command
    /// stream is the single source of truth for clipping in both backends. The canvas calls this
    /// before recording draws so that when drawing moves to a differently-offset context (for
    /// example a child region inheriting parent clips) the open clip stack is closed and reopened
    /// anchored at the new offset. Glyph render locations do not participate: the canvas anchors
    /// at its state's destination offset, not at per-glyph draw offsets.
    /// </remarks>
    /// <param name="destinationOffset">The destination offset of the consuming canvas state.</param>
    public void EnsureClipAnchors(Point destinationOffset)
    {
        List<(DrawingClipDescriptor Descriptor, Point AnchorOffset)> clips = this.openClips;
        int count = clips.Count;
        if (count == 0)
        {
            return;
        }

        bool anchored = true;
        for (int i = 0; i < count; i++)
        {
            if (clips[i].AnchorOffset != destinationOffset)
            {
                anchored = false;
                break;
            }
        }

        if (anchored)
        {
            return;
        }

        DrawingClipDescriptor[] descriptors = new DrawingClipDescriptor[count];
        for (int i = 0; i < count; i++)
        {
            descriptors[i] = clips[i].Descriptor;
        }

        for (int i = 0; i < count; i++)
        {
            this.AddComposition(CompositionCommand.CreateEndClip());
        }

        for (int i = 0; i < count; i++)
        {
            this.AddComposition(CompositionCommand.CreateBeginClip(descriptors[i], destinationOffset));
        }
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
            this.layerCommandCount != this.sealedLayerCommandCount,
            this.applyCommandCount != this.sealedApplyCommandCount,
            this.clipCommandCount != this.sealedClipCommandCount);

        this.sealedCommandCount = this.commandCount;
        this.sealedLayerCommandCount = this.layerCommandCount;
        this.sealedApplyCommandCount = this.applyCommandCount;
        this.sealedClipCommandCount = this.clipCommandCount;
    }

    /// <summary>
    /// Appends an apply barrier to the command stream.
    /// </summary>
    /// <param name="barrier">The apply barrier to append.</param>
    internal void AddApplyBarrier(ApplyBarrier barrier)
    {
        this.EnsureCommandCapacity(this.commandCount + 1);
        this.commands[this.commandCount++] = new PathCompositionSceneCommand(CompositionCommand.CreateApply(barrier));
        this.applyCommandCount++;
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

        DrawingCommandBatch commandBatch = this.CreatePreparedCommandBatch(
            0,
            this.commandCount,
            this.layerCommandCount > 0,
            this.applyCommandCount > 0,
            this.clipCommandCount > 0);

        return backend.CreateScene(this.configuration, targetBounds, commandBatch, ownedResources);
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
        => this.CreatePreparedCommandBatch(
            entry.Index,
            entry.Count,
            entry.HasLayers,
            entry.HasApply,
            entry.HasClipControls);

    /// <summary>
    /// Creates a command batch over a contiguous range of the shared command buffer.
    /// </summary>
    /// <param name="startIndex">The first command index.</param>
    /// <param name="commandCount">The command count.</param>
    /// <param name="hasLayers">Indicates whether the range contains layer boundary commands.</param>
    /// <param name="hasApply">Indicates whether the range contains apply barriers.</param>
    /// <param name="hasClipControls">Indicates whether the range contains ordered clip-control commands.</param>
    /// <returns>The command batch.</returns>
    private DrawingCommandBatch CreatePreparedCommandBatch(
        int startIndex,
        int commandCount,
        bool hasLayers,
        bool hasApply,
        bool hasClipControls)
        => new(this.commands, startIndex, commandCount, hasLayers, hasApply, hasClipControls);

    /// <summary>
    /// Gets one recorded timeline entry.
    /// </summary>
    /// <param name="index">The entry index.</param>
    /// <returns>The recorded timeline entry.</returns>
    public DrawingCanvasTimelineEntry GetEntry(int index)
        => this.entries[index];

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
        Array.Clear(this.insertedScenes, 0, this.insertedSceneCount);
        this.commandCount = 0;
        this.sealedCommandCount = 0;
        this.layerCommandCount = 0;
        this.sealedLayerCommandCount = 0;
        this.applyCommandCount = 0;
        this.sealedApplyCommandCount = 0;
        this.clipCommandCount = 0;
        this.sealedClipCommandCount = 0;
        this.TimelineEntryCount = 0;
        this.insertedSceneCount = 0;
        this.hasDashes = false;
        this.hasBrushTransforms = false;

        // The canvas re-opens its active clip state after clearing, so the mirrored
        // physical clip stream starts empty alongside the new command buffer.
        this.openClips.Clear();
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

    /// <summary>
    /// Expands dashed strokes and normalizes brush transforms across the whole command buffer.
    /// </summary>
    private void PrepareCommands()
    {
        if (!this.hasDashes && !this.hasBrushTransforms)
        {
            return;
        }

        // Command preparation is the last canvas-owned normalization boundary. Dashed strokes are
        // expanded and brush coordinates are normalized here so CPU and WebGPU do not each bake
        // the transform in their own way. Clipping is not prepared per command: the ordered
        // begin/end-clip command stream is the single source of truth for both backends.
        int requestedParallelism = this.configuration.MaxDegreeOfParallelism;
        int partitionCount = ParallelExecutionHelper.GetPartitionCount(requestedParallelism, this.commandCount);

        if (partitionCount <= 1)
        {
            for (int i = 0; i < this.commandCount; i++)
            {
                PrepareCommand(ref this.commands[i]);
            }

            this.hasDashes = false;
            this.hasBrushTransforms = false;
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

        this.hasDashes = false;
        this.hasBrushTransforms = false;
    }

    /// <summary>
    /// Prepares one queued command in place: expands dashed stroke geometry and bakes
    /// non-identity transforms into brush coordinates.
    /// </summary>
    /// <param name="command">The command slot to prepare.</param>
    private static void PrepareCommand(ref CompositionSceneCommand command)
    {
        if (command is PathCompositionSceneCommand pathCommand)
        {
            CompositionCommand composition = pathCommand.Command;
            if (composition.Kind == CompositionCommandKind.FillLayer && composition.Transform != Matrix4x4.Identity)
            {
                // The batcher is the brush normalization boundary. Backends still use
                // DrawingOptions.Transform for geometry, but brush coordinates are prepared
                // here so CPU and WebGPU do not each bake the transform in their own way.
                Brush brush = composition.Brush.Transform(
                    composition.Transform,
                    composition.RasterizerOptions.Interest,
                    composition.RasterizerOptions.Interest);

                pathCommand.Command = CompositionCommand.Create(
                    composition.SourcePath,
                    brush,
                    composition.DrawingOptions,
                    composition.RasterizerOptions,
                    composition.TargetBounds,
                    composition.DestinationOffset,
                    composition.OwnerLayer);
            }
        }
        else if (command is StrokePathCompositionSceneCommand strokePathCommand)
        {
            StrokePathCommand composition = strokePathCommand.Command;
            Matrix4x4 transform = composition.Transform;
            bool hasTransform = transform != Matrix4x4.Identity;
            Brush brush = hasTransform
                ? composition.Brush.Transform(
                    transform,
                    composition.RasterizerOptions.Interest,
                    composition.RasterizerOptions.Interest)
                : composition.Brush;

            Pen pen = composition.Pen;
            IPath sourcePath = composition.SourcePath;
            bool hasDashes = pen.StrokePattern.Length >= 2;
            if (hasDashes)
            {
                // Dashing changes the subject geometry, so it must be done before the backend
                // retains raster payload for the stroke command.
                sourcePath = sourcePath.GenerateDashes(pen.StrokeWidth, pen.StrokePattern.Span, pen.StrokePatternOffset);
            }

            if (hasDashes || hasTransform)
            {
                strokePathCommand.Command = new StrokePathCommand(
                    sourcePath,
                    brush,
                    composition.DrawingOptions,
                    composition.RasterizerOptions,
                    composition.TargetBounds,
                    composition.DestinationOffset,
                    composition.Pen,
                    composition.IsInsideLayer,
                    composition.OwnerLayer);
            }
        }
        else if (command is LineSegmentCompositionSceneCommand lineSegmentCommand)
        {
            StrokeLineSegmentCommand composition = lineSegmentCommand.Command;
            Matrix4x4 transform = composition.Transform;
            if (transform != Matrix4x4.Identity)
            {
                Brush brush = composition.Brush.Transform(
                    transform,
                    composition.RasterizerOptions.Interest,
                    composition.RasterizerOptions.Interest);

                command = new LineSegmentCompositionSceneCommand(
                    new StrokeLineSegmentCommand(
                        composition.SourceStart,
                        composition.SourceEnd,
                        brush,
                        composition.DrawingOptions,
                        composition.RasterizerOptions,
                        composition.TargetBounds,
                        composition.DestinationOffset,
                        composition.Pen,
                        composition.IsInsideLayer,
                        composition.OwnerLayer));
            }
        }
        else if (command is PolylineCompositionSceneCommand polylineCommand)
        {
            StrokePolylineCommand composition = polylineCommand.Command;
            Matrix4x4 transform = composition.Transform;
            if (transform != Matrix4x4.Identity)
            {
                Brush brush = composition.Brush.Transform(
                    transform,
                    composition.RasterizerOptions.Interest,
                    composition.RasterizerOptions.Interest);

                command = new PolylineCompositionSceneCommand(
                    new StrokePolylineCommand(
                        composition.SourcePoints,
                        brush,
                        composition.DrawingOptions,
                        composition.RasterizerOptions,
                        composition.TargetBounds,
                        composition.DestinationOffset,
                        composition.Pen,
                        composition.IsInsideLayer,
                        composition.OwnerLayer));
            }
        }
    }
}
