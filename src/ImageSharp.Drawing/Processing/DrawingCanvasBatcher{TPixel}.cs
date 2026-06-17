// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.PolygonGeometry;
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
    private static readonly object TraceLock = new();
    private static readonly string? TracePath = CreateTracePath();
    private static int traceOperationIndex;
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

    // Clip and dash flags gate whole-buffer command preparation; prepared commands
    // remain in the same command buffer until replay consumes it.
    private bool hasClips;
    private bool hasDashes;

    // Timeline entries keep compact indexes into the command and retained
    // scene buffers while preserving the order recorded by the canvas.
    private DrawingCanvasTimelineEntry[] entries;

    // These are existing retained scenes recorded through RenderScene, not scenes
    // produced later from this batcher's own command ranges.
    private DrawingBackendScene[] insertedScenes;
    private int insertedSceneCount;

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
            this.layerCommandCount != this.sealedLayerCommandCount,
            this.applyCommandCount != this.sealedApplyCommandCount);

        this.sealedCommandCount = this.commandCount;
        this.sealedLayerCommandCount = this.layerCommandCount;
        this.sealedApplyCommandCount = this.applyCommandCount;
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
        this.hasClips |= barrier.ClipPaths.Count > 0;
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
            this.applyCommandCount > 0);

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
        => this.CreatePreparedCommandBatch(entry.Index, entry.Count, entry.HasLayers, entry.HasApply);

    private DrawingCommandBatch CreatePreparedCommandBatch(int startIndex, int commandCount, bool hasLayers, bool hasApply)
        => new(this.commands, startIndex, commandCount, hasLayers, hasApply);

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
        this.TimelineEntryCount = 0;
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
                bool hasTransform = sourceOptions.Transform != Matrix4x4.Identity;
                string? tracePath = TracePath;
                if (tracePath is not null)
                {
                    TraceCommand(tracePath, $"PrepareFill sourceRule={sourceOptions.ShapeOptions.IntersectionRule} clipRule={composition.ClipIntersectionRule} clipCount={composition.ClipPaths.Count} sourceBounds={Describe(path.Bounds)} sourceInterest={Describe(composition.RasterizerOptions.Interest)} target={Describe(composition.TargetBounds)} transform={(hasTransform ? "non-identity" : "identity")}");
                }

                if (hasTransform)
                {
                    path = path.Transform(sourceOptions.Transform);
                }

                path = ApplyClipStack(
                    sourceOptions.ShapeOptions,
                    path,
                    composition.ClipPaths,
                    composition.ClipIntersectionRule);

                IntersectionRule intersectionRule = sourceOptions.ShapeOptions.IntersectionRule;
                Rectangle interest = GetClippedPathInterest(path, composition.RasterizerOptions, hasTransform);
                RasterizerOptions rasterizerOptions = WithIntersectionRuleAndInterest(
                    composition.RasterizerOptions,
                    intersectionRule,
                    interest);
                tracePath = TracePath;
                if (tracePath is not null)
                {
                    TraceCommand(tracePath, $"PreparedFill rule={intersectionRule} clippedBounds={Describe(path.Bounds)} clippedInterest={Describe(interest)}");
                }

                DrawingOptions preparedOptions = WithIdentityTransformAndIntersectionRule(sourceOptions, intersectionRule);

                pathCommand.Command = composition.Kind == CompositionCommandKind.Apply
                    ? CompositionCommand.CreatePreparedApply(
                        composition.ApplyBarrier,
                        path,
                        preparedOptions,
                        in rasterizerOptions)
                    : CompositionCommand.Create(
                        path,
                        composition.Brush.Transform(sourceOptions.Transform),
                        preparedOptions,
                        in rasterizerOptions,
                        composition.TargetBounds,
                        composition.DestinationOffset,
                        null,
                        composition.ClipIntersectionRule,
                        composition.OwnerLayer);
            }
        }
        else if (command is StrokePathCompositionSceneCommand strokePathCommand)
        {
            StrokePathCommand composition = strokePathCommand.Command;

            if (composition.ClipPaths is { Count: > 0 })
            {
                DrawingOptions sourceOptions = composition.DrawingOptions;
                bool hasTransform = sourceOptions.Transform != Matrix4x4.Identity;

                IPath path = composition.Pen.GeneratePath(composition.SourcePath);
                string? tracePath = TracePath;
                if (tracePath is not null)
                {
                    TraceCommand(tracePath, $"PrepareStroke sourceRule={sourceOptions.ShapeOptions.IntersectionRule} clipRule={composition.ClipIntersectionRule} clipCount={composition.ClipPaths.Count} sourceBounds={Describe(composition.SourcePath.Bounds)} generatedBounds={Describe(path.Bounds)} sourceInterest={Describe(composition.RasterizerOptions.Interest)} target={Describe(composition.TargetBounds)} transform={(hasTransform ? "non-identity" : "identity")} width={composition.Pen.StrokeWidth:0.###}");
                }

                if (hasTransform)
                {
                    path = path.Transform(sourceOptions.Transform);
                }

                path = ApplyClipStack(
                    sourceOptions.ShapeOptions,
                    path,
                    composition.ClipPaths,
                    composition.ClipIntersectionRule);

                IntersectionRule intersectionRule = sourceOptions.ShapeOptions.IntersectionRule;
                Rectangle interest = GetClippedPathInterest(path, composition.RasterizerOptions, hasTransform);
                RasterizerOptions rasterizerOptions = WithIntersectionRuleAndInterest(
                    composition.RasterizerOptions,
                    intersectionRule,
                    interest);
                tracePath = TracePath;
                if (tracePath is not null)
                {
                    TraceCommand(tracePath, $"PreparedStrokeAsFill rule={intersectionRule} clippedBounds={Describe(path.Bounds)} clippedInterest={Describe(interest)}");
                }

                DrawingOptions preparedOptions = WithIdentityTransformAndIntersectionRule(sourceOptions, intersectionRule);

                command = new PathCompositionSceneCommand(
                    CompositionCommand.Create(
                        path,
                        composition.Brush.Transform(sourceOptions.Transform),
                        preparedOptions,
                        in rasterizerOptions,
                        composition.TargetBounds,
                        composition.DestinationOffset,
                        null,
                        composition.ClipIntersectionRule,
                        composition.OwnerLayer));
            }
            else
            {
                // We need to dash the path here before sending it to the backend.
                Pen pen = composition.Pen;
                if (pen.StrokePattern.Length >= 2)
                {
                    strokePathCommand.Command = new StrokePathCommand(
                        composition.SourcePath.GenerateDashes(pen.StrokeWidth, pen.StrokePattern.Span, pen.StrokePatternOffset),
                        composition.Brush,
                        composition.DrawingOptions,
                        composition.RasterizerOptions,
                        composition.TargetBounds,
                        composition.DestinationOffset,
                        composition.Pen,
                        null,
                        composition.ClipIntersectionRule,
                        composition.IsInsideLayer,
                        composition.OwnerLayer);
                }
            }
        }
    }

    /// <summary>
    /// Intersects a path with the active clip stack in push order.
    /// </summary>
    /// <param name="sourceOptions">The shape options used to interpret the original path.</param>
    /// <param name="path">The path to clip.</param>
    /// <param name="clipPaths">The active clip stack.</param>
    /// <param name="clipIntersectionRule">The fill rule used to interpret the clip paths.</param>
    /// <returns>The clipped path.</returns>
    private static ComplexPolygon ApplyClipStack(
        ShapeOptions sourceOptions,
        IPath path,
        IReadOnlyList<IPath> clipPaths,
        IntersectionRule clipIntersectionRule)
    {
        IPath clipPath = clipPaths.Count == 1
            ? clipPaths[0]
            : new ComplexPolygon(clipPaths);

        return ClippedShapeGenerator.GenerateClippedShapes(
            sourceOptions,
            path,
            clipPath,
            clipIntersectionRule);
    }

    /// <summary>
    /// Creates drawing options for a clipped command whose path has been resolved to the supplied fill rule.
    /// </summary>
    /// <param name="source">The source drawing options.</param>
    /// <param name="intersectionRule">The intersection rule for the resolved path.</param>
    /// <returns>Drawing options with an identity transform and the resolved path fill rule.</returns>
    private static DrawingOptions WithIdentityTransformAndIntersectionRule(DrawingOptions source, IntersectionRule intersectionRule)
    {
        bool hasExpectedRule = source.ShapeOptions.IntersectionRule == intersectionRule;
        if (source.Transform == Matrix4x4.Identity && hasExpectedRule)
        {
            return source;
        }

        ShapeOptions shapeOptions = source.ShapeOptions;
        if (!hasExpectedRule)
        {
            shapeOptions = shapeOptions.DeepClone();
            shapeOptions.IntersectionRule = intersectionRule;
        }

        return new DrawingOptions(source.GraphicsOptions, shapeOptions, Matrix4x4.Identity);
    }

    /// <summary>
    /// Gets the raster interest for a path after boolean clipping.
    /// </summary>
    /// <param name="path">The clipped path.</param>
    /// <param name="source">The source rasterizer options.</param>
    /// <param name="hasResolvedTransform">True when the path has been moved into transformed coordinates.</param>
    /// <returns>The narrowed raster interest.</returns>
    private static Rectangle GetClippedPathInterest(IPath path, in RasterizerOptions source, bool hasResolvedTransform)
    {
        Rectangle pathInterest = ToRasterizerInterest(path.Bounds);

        // A resolved transform changes the path coordinate space, so the original interest
        // cannot be intersected safely. Without a transform, preserving the existing interest
        // keeps prior source narrowing while the clipped path bounds add clip narrowing.
        return hasResolvedTransform
            ? pathInterest
            : Rectangle.Intersect(source.Interest, pathInterest);
    }

    /// <summary>
    /// Creates rasterizer options for a clipped path whose boolean result has resolved the fill rule.
    /// </summary>
    /// <param name="source">The source rasterizer options.</param>
    /// <param name="intersectionRule">The intersection rule for the resolved path.</param>
    /// <param name="interest">The resolved area of interest.</param>
    /// <returns>Rasterizer options with the resolved path fill rule and narrowed interest.</returns>
    private static RasterizerOptions WithIntersectionRuleAndInterest(
        in RasterizerOptions source,
        IntersectionRule intersectionRule,
        Rectangle interest)
        => new(
            interest,
            intersectionRule,
            source.RasterizationMode,
            source.AntialiasThreshold);

    /// <summary>
    /// Converts local geometry bounds to the rasterizer area of interest.
    /// </summary>
    /// <param name="bounds">The local geometry bounds.</param>
    /// <returns>The conservative rasterizer interest rectangle.</returns>
    private static Rectangle ToRasterizerInterest(RectangleF bounds)
        => Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right) + 1,
            (int)MathF.Ceiling(bounds.Bottom) + 1);

    /// <summary>
    /// Writes one opt-in batcher diagnostic line.
    /// </summary>
    /// <param name="path">The trace file path.</param>
    /// <param name="message">The diagnostic message.</param>
    private static void TraceCommand(string path, string message)
    {
        lock (TraceLock)
        {
            File.AppendAllText(
                path,
                $"{traceOperationIndex++}: {message}{Environment.NewLine}");
        }
    }

    /// <summary>
    /// Gets the opt-in batcher trace path from the capture environment.
    /// </summary>
    /// <returns>The batcher trace path, or <see langword="null"/> when batcher tracing is disabled.</returns>
    private static string? CreateTracePath()
    {
        if (Environment.GetEnvironmentVariable("IMAGESHARP_DRAWING_BATCHER_TRACE") != "1")
        {
            return null;
        }

        string? directory = Environment.GetEnvironmentVariable("IMAGESHARP_DRAWING_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = System.IO.Path.GetTempPath();
        }

        Directory.CreateDirectory(directory);

        return System.IO.Path.Combine(directory, "imagesharp-batcher-trace.log");
    }

    /// <summary>
    /// Formats a floating-point rectangle for diagnostics.
    /// </summary>
    /// <param name="rectangle">The rectangle to format.</param>
    /// <returns>The formatted rectangle.</returns>
    private static string Describe(RectangleF rectangle)
        => $"({rectangle.X:0.###},{rectangle.Y:0.###},{rectangle.Width:0.###},{rectangle.Height:0.###})";

    /// <summary>
    /// Formats an integer rectangle for diagnostics.
    /// </summary>
    /// <param name="rectangle">The rectangle to format.</param>
    /// <returns>The formatted rectangle.</returns>
    private static string Describe(Rectangle rectangle)
        => $"({rectangle.X},{rectangle.Y},{rectangle.Width},{rectangle.Height})";
}
