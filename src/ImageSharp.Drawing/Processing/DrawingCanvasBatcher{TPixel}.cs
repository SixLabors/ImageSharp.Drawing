// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Queues normalized composition commands emitted by <see cref="DrawingCanvas{TPixel}"/>
/// and prepares them in deterministic draw order.
/// </summary>
/// <remarks>
/// The batcher owns command buffering and replay ordering only; it does not rasterize or composite.
/// Draw commands are stored in the command buffer until a timeline command-range entry references
/// them. Existing retained scenes passed through <see cref="DrawingCanvas.RenderScene"/> are scene
/// commands in the same buffer. During disposal replay, command ranges are lowered to short-lived
/// backend scenes at the position where the canvas recorded the range.
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
    private int sceneCommandCount;
    private int sealedSceneCommandCount;

    // Dash and brush-transform flags gate whole-buffer command preparation; prepared commands
    // remain in the same command buffer until replay consumes it.
    private bool hasDashes;
    private bool hasBrushTransforms;

    // Mirrors the physical begin/end-clip command stream. The stream is the single source of
    // truth for clipping in both backends; the canvas re-anchors it via EnsureClipAnchors when
    // drawing moves to a differently-offset context (for example a child region).
    private readonly List<(DrawingClipDescriptor Descriptor, Point AnchorOffset)> openClips = [];

    // Mirrors the physical begin/end-layer command stream. ClipDepth is the open clip count when
    // the layer began, so the clips opened inside each layer are known. Every canvas that shares
    // this batcher records into the same stream, so this is the one source of the open layers.
    private readonly List<(Rectangle Bounds, DrawingCanvasLayer Layer, int ClipDepth)> openLayers = [];

    // The command index and the kind counters when the outermost open layer began. The commands
    // from that index are the content of the open layers (see DetachOpenLayerCommands).
    private CommandCounts outerLayerStart;

    // Timeline entries keep compact indexes into the command buffer, and keep the order that the
    // canvas recorded.
    private DrawingCanvasTimelineEntry[] entries;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvasBatcher{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration providing parallelism settings for command preparation.</param>
    public DrawingCanvasBatcher(Configuration configuration)
    {
        this.configuration = configuration;
        this.commands = [];
        this.entries = [];
    }

    /// <summary>
    /// Gets a value indicating whether there are queued commands or timeline entries.
    /// </summary>
    public bool HasRecordedWork => this.commandCount > 0 || this.TimelineEntryCount > 0;

    /// <summary>
    /// Gets the number of ordered replay items recorded in the canvas timeline.
    /// </summary>
    /// <remarks>
    /// This is not a draw-command count. A single entry represents a contiguous command range.
    /// </remarks>
    public int TimelineEntryCount { get; private set; }

    /// <summary>
    /// Gets the number of layers that are open in the recorded command stream.
    /// </summary>
    public int OpenLayerCount => this.openLayers.Count;

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

            case CompositionCommandKind.BeginLayer:
                if (this.openLayers.Count == 0)
                {
                    this.outerLayerStart = this.CaptureCounts();
                }

                this.openLayers.Add((composition.LayerBounds, composition.Layer, this.openClips.Count));
                break;

            case CompositionCommandKind.EndLayer:
                this.openLayers.RemoveAt(this.openLayers.Count - 1);
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
        this.entries[this.TimelineEntryCount++] = new DrawingCanvasTimelineEntry(
            this.sealedCommandCount,
            count,
            this.layerCommandCount != this.sealedLayerCommandCount,
            this.applyCommandCount != this.sealedApplyCommandCount,
            this.clipCommandCount != this.sealedClipCommandCount,
            this.sceneCommandCount != this.sealedSceneCommandCount);

        this.sealedCommandCount = this.commandCount;
        this.sealedLayerCommandCount = this.layerCommandCount;
        this.sealedApplyCommandCount = this.applyCommandCount;
        this.sealedClipCommandCount = this.clipCommandCount;
        this.sealedSceneCommandCount = this.sceneCommandCount;
    }

    /// <summary>
    /// Appends an apply barrier to the command stream.
    /// </summary>
    /// <param name="barrier">The apply barrier to append.</param>
    public void AddApplyBarrier(ApplyBarrier barrier)
    {
        this.EnsureCommandCapacity(this.commandCount + 1);
        this.commands[this.commandCount++] = new PathCompositionSceneCommand(CompositionCommand.CreateApply(barrier));
        this.applyCommandCount++;
    }

    /// <summary>
    /// Appends a scene command that renders an existing retained scene at this point in the stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This records only scenes passed to <see cref="DrawingCanvas.RenderScene"/>. Scenes produced
    /// from this canvas's own command ranges are created later by the backend from command batches.
    /// The command does not seal the range, so an open layer stays one layer around the scene.
    /// </para>
    /// <para>
    /// Canvas clips do not apply to a retained scene, so the open clips close before the command and
    /// open again after it. Inside a layer, only the clips opened in the innermost layer close. A clip
    /// opened before that layer stays open, because the stream must stay nested.
    /// </para>
    /// </remarks>
    /// <param name="scene">The retained scene to render at this point in the stream.</param>
    public void AddScene(DrawingBackendScene scene)
    {
        int layerCount = this.openLayers.Count;
        (DrawingClipDescriptor Descriptor, Point AnchorOffset)[] clips = this.CloseInnermostLayerClips();

        // Outside every layer the command holds the target bounds of the scene, which the backend
        // requires to match its target. Inside a layer it holds the innermost layer bounds, which
        // limit the rows that the scene can write, the same as for a fill in that layer.
        Rectangle targetBounds = layerCount == 0 ? scene.Bounds : this.openLayers[layerCount - 1].Bounds;
        this.EnsureCommandCapacity(this.commandCount + 1);
        this.commands[this.commandCount++] = new RetainedSceneCompositionSceneCommand(scene, targetBounds, layerCount != 0);
        this.sceneCommandCount++;

        this.ReopenClips(clips);
    }

    /// <summary>
    /// Appends one composition command outside the open clips of the innermost layer.
    /// </summary>
    /// <remarks>
    /// The open clips close before the command and open again after it, as for
    /// <see cref="AddScene"/>. Outside every layer, no clip applies to the command. Inside a layer,
    /// only the clips opened in the innermost layer close, and clips opened before that layer apply.
    /// </remarks>
    /// <param name="composition">The command to queue.</param>
    public void AddUnclippedComposition(in CompositionCommand composition)
    {
        (DrawingClipDescriptor Descriptor, Point AnchorOffset)[] clips = this.CloseInnermostLayerClips();
        this.AddComposition(composition);
        this.ReopenClips(clips);
    }

    /// <summary>
    /// Closes the clips opened in the innermost open layer, or every open clip when no layer is open.
    /// </summary>
    /// <remarks>
    /// A clip opened before the innermost layer stays open, because the stream must stay nested.
    /// </remarks>
    /// <returns>The closed clips, outermost first, for <see cref="ReopenClips"/>.</returns>
    private (DrawingClipDescriptor Descriptor, Point AnchorOffset)[] CloseInnermostLayerClips()
    {
        int layerCount = this.openLayers.Count;
        int clipStart = layerCount == 0 ? 0 : this.openLayers[layerCount - 1].ClipDepth;
        (DrawingClipDescriptor Descriptor, Point AnchorOffset)[] clips = this.openClips.Count == clipStart
            ? []
            : CollectionsMarshal.AsSpan(this.openClips)[clipStart..].ToArray();

        for (int i = 0; i < clips.Length; i++)
        {
            this.AddComposition(CompositionCommand.CreateEndClip());
        }

        return clips;
    }

    /// <summary>
    /// Opens the clips that <see cref="CloseInnermostLayerClips"/> closed again, with the same anchors.
    /// </summary>
    /// <param name="clips">The closed clips, outermost first.</param>
    private void ReopenClips((DrawingClipDescriptor Descriptor, Point AnchorOffset)[] clips)
    {
        for (int i = 0; i < clips.Length; i++)
        {
            this.AddComposition(CompositionCommand.CreateBeginClip(clips[i].Descriptor, clips[i].AnchorOffset));
        }
    }

    /// <summary>
    /// Closes every open clip and layer in the stream, innermost first.
    /// </summary>
    /// <remarks>
    /// The returned scopes are passed to <see cref="ReopenScopes"/> after <see cref="ClearCommandBatch"/>
    /// to open the same clips and layers again, in the same nesting order.
    /// </remarks>
    /// <returns>The open clips and layers, outermost first.</returns>
    public OpenScopes CloseOpenScopes()
    {
        (DrawingClipDescriptor Descriptor, Point AnchorOffset)[] clips = [.. this.openClips];
        (Rectangle Bounds, DrawingCanvasLayer Layer, int ClipDepth)[] layers = [.. this.openLayers];

        // Each layer closes after the clips opened inside it, so the stream stays nested.
        for (int i = layers.Length - 1; i >= 0; i--)
        {
            while (this.openClips.Count > layers[i].ClipDepth)
            {
                this.AddComposition(CompositionCommand.CreateEndClip());
            }

            this.AddComposition(CompositionCommand.CreateEndLayer(layers[i].Bounds, layers[i].Layer));
        }

        while (this.openClips.Count > 0)
        {
            this.AddComposition(CompositionCommand.CreateEndClip());
        }

        return new OpenScopes(clips, layers);
    }

    /// <summary>
    /// Opens the clips and layers closed by <see cref="CloseOpenScopes"/> again, outermost first.
    /// </summary>
    /// <param name="scopes">The open clips and layers, outermost first.</param>
    public void ReopenScopes(OpenScopes scopes)
    {
        int clipIndex = 0;
        for (int i = 0; i < scopes.Layers.Length; i++)
        {
            for (; clipIndex < scopes.Layers[i].ClipDepth; clipIndex++)
            {
                this.AddComposition(CompositionCommand.CreateBeginClip(scopes.Clips[clipIndex].Descriptor, scopes.Clips[clipIndex].AnchorOffset));
            }

            this.AddComposition(CompositionCommand.CreateBeginLayer(scopes.Layers[i].Bounds, scopes.Layers[i].Layer));
        }

        for (; clipIndex < scopes.Clips.Length; clipIndex++)
        {
            this.AddComposition(CompositionCommand.CreateBeginClip(scopes.Clips[clipIndex].Descriptor, scopes.Clips[clipIndex].AnchorOffset));
        }
    }

    /// <summary>
    /// Takes the content of the open layers out of the stream, so the commands before the outermost
    /// open layer can render alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The content of a layer reaches the target only when the layer closes, so the commands before
    /// the outermost open layer are all the commands that can render now. This method then closes
    /// the clips that were open when that layer began, so the remaining stream is balanced.
    /// <see cref="AttachCommands"/> puts the detached commands back after the remaining stream
    /// renders and <see cref="ClearCommandBatch"/> runs.
    /// </para>
    /// <para>
    /// Nothing seals the stream while a layer is open, so every detached command is unsealed. The
    /// detached commands are not prepared yet, so a later preparation prepares each of them once.
    /// </para>
    /// </remarks>
    /// <returns>The detached commands and the stream state that they need.</returns>
    public DetachedCommands DetachOpenLayerCommands()
    {
        CommandCounts start = this.outerLayerStart;
        int clipDepth = this.openLayers[0].ClipDepth;
        DetachedCommands detached = new(
            this.commands[start.CommandIndex..this.commandCount],
            [.. this.openClips],
            [.. this.openLayers],
            this.layerCommandCount - start.LayerCommandCount,
            this.applyCommandCount - start.ApplyCommandCount,
            this.clipCommandCount - start.ClipCommandCount,
            this.sceneCommandCount - start.SceneCommandCount,
            this.hasDashes,
            this.hasBrushTransforms);

        Array.Clear(this.commands, start.CommandIndex, this.commandCount - start.CommandIndex);
        this.commandCount = start.CommandIndex;
        this.layerCommandCount = start.LayerCommandCount;
        this.applyCommandCount = start.ApplyCommandCount;
        this.clipCommandCount = start.ClipCommandCount;
        this.sceneCommandCount = start.SceneCommandCount;

        // The mirrors return to the state at the start of the outermost layer. Then the clips
        // that were open there close, so the remaining stream is balanced.
        this.openLayers.Clear();
        this.openClips.RemoveRange(clipDepth, this.openClips.Count - clipDepth);
        for (int i = 0; i < clipDepth; i++)
        {
            this.AddComposition(CompositionCommand.CreateEndClip());
        }

        return detached;
    }

    /// <summary>
    /// Puts commands taken out by <see cref="DetachOpenLayerCommands"/> back at the end of the stream.
    /// </summary>
    /// <remarks>
    /// The clips that were open when the outermost layer began open again first, with the same
    /// anchors. Then the stream and its mirrors are the same as before the detach.
    /// </remarks>
    /// <param name="detached">The detached commands.</param>
    public void AttachCommands(DetachedCommands detached)
    {
        int clipDepth = detached.Layers[0].ClipDepth;
        for (int i = 0; i < clipDepth; i++)
        {
            this.AddComposition(CompositionCommand.CreateBeginClip(detached.Clips[i].Descriptor, detached.Clips[i].AnchorOffset));
        }

        this.outerLayerStart = this.CaptureCounts();
        this.EnsureCommandCapacity(this.commandCount + detached.Commands.Length);
        detached.Commands.CopyTo(this.commands, this.commandCount);
        this.commandCount += detached.Commands.Length;
        this.layerCommandCount += detached.LayerCommandCount;
        this.applyCommandCount += detached.ApplyCommandCount;
        this.clipCommandCount += detached.ClipCommandCount;
        this.sceneCommandCount += detached.SceneCommandCount;

        // The flags are those of the whole buffer at the detach, so they include the detached
        // commands. When only rendered commands set a flag, the next preparation scans commands
        // that need no change. The detached commands were never prepared, so none is prepared two
        // times.
        this.hasDashes |= detached.HasDashes;
        this.hasBrushTransforms |= detached.HasBrushTransforms;

        this.openClips.Clear();
        this.openClips.AddRange(detached.Clips);
        this.openLayers.Clear();
        this.openLayers.AddRange(detached.Layers);
    }

    /// <summary>
    /// Captures the command index and the kind counters at the end of the stream.
    /// </summary>
    /// <returns>The captured counts.</returns>
    private CommandCounts CaptureCounts()
        => new(this.commandCount, this.layerCommandCount, this.applyCommandCount, this.clipCommandCount, this.sceneCommandCount);

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

        // Sealed command ranges are contiguous in the command buffer, so one batch covers them all.
        DrawingCommandBatch commandBatch = this.CreatePreparedCommandBatch(
            0,
            this.commandCount,
            this.layerCommandCount > 0,
            this.applyCommandCount > 0,
            this.clipCommandCount > 0,
            this.sceneCommandCount > 0);

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
            entry.HasClipControls,
            entry.HasScenes);

    /// <summary>
    /// Creates a command batch over a contiguous range of the shared command buffer.
    /// </summary>
    /// <param name="startIndex">The first command index.</param>
    /// <param name="commandCount">The command count.</param>
    /// <param name="hasLayers">Indicates whether the range contains layer boundary commands.</param>
    /// <param name="hasApply">Indicates whether the range contains apply barriers.</param>
    /// <param name="hasClipControls">Indicates whether the range contains ordered clip-control commands.</param>
    /// <param name="hasScenes">Indicates whether the range contains retained scene commands.</param>
    /// <returns>The command batch.</returns>
    private DrawingCommandBatch CreatePreparedCommandBatch(
        int startIndex,
        int commandCount,
        bool hasLayers,
        bool hasApply,
        bool hasClipControls,
        bool hasScenes)
        => new(this.commands, startIndex, commandCount, hasLayers, hasApply, hasClipControls, hasScenes);

    /// <summary>
    /// Gets one recorded timeline entry.
    /// </summary>
    /// <param name="index">The entry index.</param>
    /// <returns>The recorded timeline entry.</returns>
    public DrawingCanvasTimelineEntry GetEntry(int index)
        => this.entries[index];

    /// <summary>
    /// Clears command references after a prepared batch has been consumed.
    /// </summary>
    public void ClearCommandBatch()
    {
        Array.Clear(this.commands, 0, this.commandCount);
        Array.Clear(this.entries, 0, this.TimelineEntryCount);
        this.commandCount = 0;
        this.sealedCommandCount = 0;
        this.layerCommandCount = 0;
        this.sealedLayerCommandCount = 0;
        this.applyCommandCount = 0;
        this.sealedApplyCommandCount = 0;
        this.clipCommandCount = 0;
        this.sealedClipCommandCount = 0;
        this.sceneCommandCount = 0;
        this.sealedSceneCommandCount = 0;
        this.TimelineEntryCount = 0;
        this.hasDashes = false;
        this.hasBrushTransforms = false;

        // The canvas re-opens its active clip and layer state after clearing, so the mirrored
        // physical clip and layer streams start empty alongside the new command buffer.
        this.openClips.Clear();
        this.openLayers.Clear();
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
                // Position-independent brushes return themselves untransformed, and every
                // glyph command reaches here because its composed transform carries the
                // sub-pixel fraction, so the rebuild is skipped when nothing changed.
                Brush brush = composition.Brush.Transform(
                    composition.Transform,
                    composition.RasterizerOptions.Interest,
                    composition.RasterizerOptions.Interest);

                if (!ReferenceEquals(brush, composition.Brush))
                {
                    pathCommand.Command = CompositionCommand.Create(
                        composition.SourcePath,
                        brush,
                        composition.DrawingOptions,
                        composition.RasterizerOptions,
                        composition.TargetBounds,
                        composition.DestinationOffset,
                        composition.OwnerLayer,
                        composition.SubPixelOffset);
                }
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

            // Position-independent brushes return themselves untransformed, so only dashing
            // or a genuinely transformed brush warrants rebuilding the command.
            if (hasDashes || !ReferenceEquals(brush, composition.Brush))
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
                    composition.OwnerLayer,
                    composition.SubPixelOffset);
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

                // Position-independent brushes return themselves untransformed; skipping the
                // rebuild also skips allocating a replacement scene command wrapper.
                if (!ReferenceEquals(brush, composition.Brush))
                {
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

                // Position-independent brushes return themselves untransformed; skipping the
                // rebuild also skips allocating a replacement scene command wrapper.
                if (!ReferenceEquals(brush, composition.Brush))
                {
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

    /// <summary>
    /// Holds the clips and layers that were open in the stream, outermost first.
    /// </summary>
    public readonly struct OpenScopes
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OpenScopes"/> struct.
        /// </summary>
        /// <param name="clips">The open clips, outermost first.</param>
        /// <param name="layers">The open layers, outermost first, with the open clip count when each layer began.</param>
        public OpenScopes(
            (DrawingClipDescriptor Descriptor, Point AnchorOffset)[] clips,
            (Rectangle Bounds, DrawingCanvasLayer Layer, int ClipDepth)[] layers)
        {
            this.Clips = clips;
            this.Layers = layers;
        }

        /// <summary>
        /// Gets the open clips, outermost first.
        /// </summary>
        public (DrawingClipDescriptor Descriptor, Point AnchorOffset)[] Clips { get; }

        /// <summary>
        /// Gets the open layers, outermost first, with the open clip count when each layer began.
        /// </summary>
        public (Rectangle Bounds, DrawingCanvasLayer Layer, int ClipDepth)[] Layers { get; }
    }

    /// <summary>
    /// Holds a command index and the kind counters of the stream at that index.
    /// </summary>
    private readonly struct CommandCounts
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CommandCounts"/> struct.
        /// </summary>
        /// <param name="commandIndex">The command index.</param>
        /// <param name="layerCommandCount">The layer command count before the index.</param>
        /// <param name="applyCommandCount">The apply command count before the index.</param>
        /// <param name="clipCommandCount">The clip command count before the index.</param>
        /// <param name="sceneCommandCount">The scene command count before the index.</param>
        public CommandCounts(int commandIndex, int layerCommandCount, int applyCommandCount, int clipCommandCount, int sceneCommandCount)
        {
            this.CommandIndex = commandIndex;
            this.LayerCommandCount = layerCommandCount;
            this.ApplyCommandCount = applyCommandCount;
            this.ClipCommandCount = clipCommandCount;
            this.SceneCommandCount = sceneCommandCount;
        }

        /// <summary>
        /// Gets the command index.
        /// </summary>
        public int CommandIndex { get; }

        /// <summary>
        /// Gets the layer command count before the index.
        /// </summary>
        public int LayerCommandCount { get; }

        /// <summary>
        /// Gets the apply command count before the index.
        /// </summary>
        public int ApplyCommandCount { get; }

        /// <summary>
        /// Gets the clip command count before the index.
        /// </summary>
        public int ClipCommandCount { get; }

        /// <summary>
        /// Gets the scene command count before the index.
        /// </summary>
        public int SceneCommandCount { get; }
    }

    /// <summary>
    /// Holds the content of the open layers that <see cref="DetachOpenLayerCommands"/> took out of
    /// the stream, with the stream state that <see cref="AttachCommands"/> restores.
    /// </summary>
    public sealed class DetachedCommands
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DetachedCommands"/> class.
        /// </summary>
        /// <param name="commands">The detached commands, in stream order.</param>
        /// <param name="clips">The open clips at the detach, outermost first.</param>
        /// <param name="layers">The open layers at the detach, outermost first.</param>
        /// <param name="layerCommandCount">The layer command count of the detached commands.</param>
        /// <param name="applyCommandCount">The apply command count of the detached commands.</param>
        /// <param name="clipCommandCount">The clip command count of the detached commands.</param>
        /// <param name="sceneCommandCount">The scene command count of the detached commands.</param>
        /// <param name="hasDashes">Whether the buffer had dashed strokes to prepare at the detach.</param>
        /// <param name="hasBrushTransforms">Whether the buffer had brush transforms to prepare at the detach.</param>
        public DetachedCommands(
            CompositionSceneCommand[] commands,
            (DrawingClipDescriptor Descriptor, Point AnchorOffset)[] clips,
            (Rectangle Bounds, DrawingCanvasLayer Layer, int ClipDepth)[] layers,
            int layerCommandCount,
            int applyCommandCount,
            int clipCommandCount,
            int sceneCommandCount,
            bool hasDashes,
            bool hasBrushTransforms)
        {
            this.Commands = commands;
            this.Clips = clips;
            this.Layers = layers;
            this.LayerCommandCount = layerCommandCount;
            this.ApplyCommandCount = applyCommandCount;
            this.ClipCommandCount = clipCommandCount;
            this.SceneCommandCount = sceneCommandCount;
            this.HasDashes = hasDashes;
            this.HasBrushTransforms = hasBrushTransforms;
        }

        /// <summary>
        /// Gets the detached commands, in stream order.
        /// </summary>
        public CompositionSceneCommand[] Commands { get; }

        /// <summary>
        /// Gets the open clips at the detach, outermost first.
        /// </summary>
        public (DrawingClipDescriptor Descriptor, Point AnchorOffset)[] Clips { get; }

        /// <summary>
        /// Gets the open layers at the detach, outermost first.
        /// </summary>
        public (Rectangle Bounds, DrawingCanvasLayer Layer, int ClipDepth)[] Layers { get; }

        /// <summary>
        /// Gets the layer command count of the detached commands.
        /// </summary>
        public int LayerCommandCount { get; }

        /// <summary>
        /// Gets the apply command count of the detached commands.
        /// </summary>
        public int ApplyCommandCount { get; }

        /// <summary>
        /// Gets the clip command count of the detached commands.
        /// </summary>
        public int ClipCommandCount { get; }

        /// <summary>
        /// Gets the scene command count of the detached commands.
        /// </summary>
        public int SceneCommandCount { get; }

        /// <summary>
        /// Gets a value indicating whether the buffer had dashed strokes to prepare at the detach.
        /// </summary>
        public bool HasDashes { get; }

        /// <summary>
        /// Gets a value indicating whether the buffer had brush transforms to prepare at the detach.
        /// </summary>
        public bool HasBrushTransforms { get; }
    }
}
