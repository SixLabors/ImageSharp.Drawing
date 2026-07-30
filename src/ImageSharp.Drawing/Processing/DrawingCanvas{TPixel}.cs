// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// A drawing canvas over a frame target.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
public sealed class DrawingCanvas<TPixel> : DrawingCanvas
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <summary>
    /// Processing configuration used by operations executed through this canvas.
    /// </summary>
    private readonly Configuration configuration;

    /// <summary>
    /// Backend responsible for rasterizing and composing draw commands.
    /// </summary>
    private readonly IDrawingBackend backend;

    /// <summary>
    /// Destination frame receiving rendered output.
    /// </summary>
    private readonly ICanvasFrame<TPixel> targetFrame;

    /// <summary>
    /// Command batcher used to defer and submit composition commands.
    /// </summary>
    private readonly DrawingCanvasBatcher<TPixel> batcher;

    /// <summary>
    /// Temporary image resources that must stay alive until queued commands are flushed.
    /// </summary>
    private readonly List<Image<TPixel>> pendingImageResources = [];

    /// <summary>
    /// Indicates whether this canvas owns final disposal of the shared batcher.
    /// </summary>
    private readonly bool ownsBatcher;

    /// <summary>
    /// Indicates whether this canvas owns clearing the text drawing cache.
    /// </summary>
    private readonly bool ownsTextCache;

    /// <summary>
    /// Tracks whether this instance has already been disposed.
    /// </summary>
    private bool isDisposed;

    /// <summary>
    /// Stack of saved drawing states for Save/Restore operations.
    /// </summary>
    private readonly Stack<DrawingCanvasState> savedStates = new();

    /// <summary>
    /// Shared text drawing cache used by glyph rendering operations.
    /// </summary>
    private readonly DrawingTextCache textCache;

    /// <summary>
    /// Reusable operation list handed to each text renderer. Hosted by the text cache because
    /// canvases are per-frame objects; sharing the cache-owned list keeps its capacity across
    /// frames instead of regrowing a fresh list of large operation structs per draw.
    /// </summary>
    private readonly List<DrawingOperation> textOperations;

    /// <summary>
    /// Reusable sort buffer for <see cref="DrawTextOperations"/>, hosted by the text cache for
    /// the same reason as <see cref="textOperations"/>.
    /// </summary>
    private readonly List<(byte RenderPass, int Sequence, CompositionSceneCommand Command)> textCommandSortBuffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="targetRegion">The destination target region.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    public DrawingCanvas(
        Configuration configuration,
        DrawingOptions options,
        Buffer2DRegion<TPixel> targetRegion,
        params IPath[] clipPaths)
        : this(configuration, options, new DrawingTextCache(), ownsTextCache: true, new MemoryCanvasFrame<TPixel>(targetRegion), clipPaths)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="textCache">The text drawing cache used by this canvas instance.</param>
    /// <param name="targetRegion">The destination target region.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    public DrawingCanvas(
        Configuration configuration,
        DrawingOptions options,
        DrawingTextCache textCache,
        Buffer2DRegion<TPixel> targetRegion,
        params IPath[] clipPaths)
        : this(configuration, options, textCache, ownsTextCache: false, new MemoryCanvasFrame<TPixel>(targetRegion), clipPaths)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="targetFrame">The destination frame.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    public DrawingCanvas(
        Configuration configuration,
        DrawingOptions options,
        ICanvasFrame<TPixel> targetFrame,
        params IPath[] clipPaths)
        : this(configuration, options, new DrawingTextCache(), ownsTextCache: true, configuration.GetDrawingBackend(), targetFrame, clipPaths)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="textCache">The text drawing cache used by this canvas instance.</param>
    /// <param name="targetFrame">The destination frame.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    public DrawingCanvas(
        Configuration configuration,
        DrawingOptions options,
        DrawingTextCache textCache,
        ICanvasFrame<TPixel> targetFrame,
        params IPath[] clipPaths)
        : this(configuration, options, textCache, ownsTextCache: false, configuration.GetDrawingBackend(), targetFrame, clipPaths)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class with an explicit backend and initial state.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="backend">The drawing backend implementation.</param>
    /// <param name="targetFrame">The destination frame.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    public DrawingCanvas(
        Configuration configuration,
        DrawingOptions options,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame,
        params IPath[] clipPaths)
        : this(configuration, options, new DrawingTextCache(), true, backend, targetFrame, clipPaths)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class with an explicit backend and initial state.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="textCache">The text drawing cache used by this canvas instance.</param>
    /// <param name="backend">The drawing backend implementation.</param>
    /// <param name="targetFrame">The destination frame.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    public DrawingCanvas(
        Configuration configuration,
        DrawingOptions options,
        DrawingTextCache textCache,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame,
        params IPath[] clipPaths)
        : this(configuration, options, textCache, false, backend, targetFrame, clipPaths)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class,
    /// resolving the drawing backend from the configuration.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="textCache">The text drawing cache used by this canvas instance.</param>
    /// <param name="ownsTextCache">Whether this canvas owns clearing the text drawing cache.</param>
    /// <param name="targetFrame">The destination frame.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    private DrawingCanvas(
        Configuration configuration,
        DrawingOptions options,
        DrawingTextCache textCache,
        bool ownsTextCache,
        ICanvasFrame<TPixel> targetFrame,
        params IPath[] clipPaths)
        : this(configuration, options, textCache, ownsTextCache, configuration.GetDrawingBackend(), targetFrame, clipPaths)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class as a root canvas,
    /// creating a fresh batcher and building the default state from the supplied options and clip paths.
    /// </summary>
    /// <remarks>
    /// The initial clip state combines <paramref name="clipPaths"/> as an intersection, with the clip
    /// edge mode derived from the antialiasing settings in <paramref name="options"/>.
    /// </remarks>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="textCache">The text drawing cache used by this canvas instance.</param>
    /// <param name="ownsTextCache">Whether this canvas owns clearing the text drawing cache.</param>
    /// <param name="backend">The drawing backend implementation.</param>
    /// <param name="targetFrame">The destination frame.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    private DrawingCanvas(
        Configuration configuration,
        DrawingOptions options,
        DrawingTextCache textCache,
        bool ownsTextCache,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame,
        params IPath[] clipPaths)
        : this(
            configuration,
            backend,
            targetFrame,
            new DrawingCanvasBatcher<TPixel>(configuration),
            textCache,
            ownsTextCache,
            new DrawingCanvasState(
                options,
                DrawingClipState.FromPaths(
                    clipPaths,
                    ClipOperation.Intersection,
                    options.GraphicsOptions.Antialias ? DrawingClipEdgeMode.Antialiased : DrawingClipEdgeMode.Hard,
                    options.GraphicsOptions.AntialiasThreshold),
                targetFrame.Bounds,
                targetFrame.Bounds.Location),
            true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class
    /// with explicit backend and batcher instances.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="backend">The drawing backend implementation.</param>
    /// <param name="targetFrame">The destination frame.</param>
    /// <param name="batcher">The command batcher used for deferred composition.</param>
    /// <param name="textCache">The text drawing cache used by this canvas instance.</param>
    /// <param name="ownsTextCache">Whether this canvas owns clearing the text drawing cache.</param>
    /// <param name="defaultState">The default state used when no scoped state is active.</param>
    /// <param name="ownsBatcher">Whether this canvas owns final disposal of the shared batcher.</param>
    private DrawingCanvas(
        Configuration configuration,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame,
        DrawingCanvasBatcher<TPixel> batcher,
        DrawingTextCache textCache,
        bool ownsTextCache,
        DrawingCanvasState defaultState,
        bool ownsBatcher)
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(backend, nameof(backend));
        Guard.NotNull(targetFrame, nameof(targetFrame));
        Guard.NotNull(batcher, nameof(batcher));
        Guard.NotNull(textCache, nameof(textCache));
        Guard.NotNull(defaultState, nameof(defaultState));

        if (!targetFrame.TryGetCpuRegion(out _) && !targetFrame.TryGetNativeSurface(out _))
        {
            throw new NotSupportedException("Canvas frame must expose either a CPU region or a native surface.");
        }

        this.configuration = configuration;
        this.backend = backend;
        this.targetFrame = targetFrame;
        this.batcher = batcher;
        this.textCache = textCache;
        this.textOperations = textCache.OperationScratch;
        this.textCommandSortBuffer = textCache.CommandSortScratch;
        this.ownsBatcher = ownsBatcher;
        this.ownsTextCache = ownsTextCache;

        // Canvas coordinates are local to the current frame; origin stays at (0,0).
        this.Bounds = new Rectangle(0, 0, targetFrame.Bounds.Width, targetFrame.Bounds.Height);
        this.savedStates.Push(defaultState);

        if (ownsBatcher)
        {
            // The root canvas owns the command stream. Child regions inherit the already-open
            // parent stack and only narrow TargetBounds, so they must not emit duplicate opens.
            this.AppendBeginClips(defaultState.ClipState, defaultState.DestinationOffset);
        }
    }

    /// <inheritdoc />
    public override Rectangle Bounds { get; }

    /// <inheritdoc />
    public override int SaveCount => this.savedStates.Count;

    /// <inheritdoc />
    public override int Save()
    {
        this.EnsureNotDisposed();
        DrawingCanvasState current = this.ResolveState();

        // Push a state that does not close a layer on restore. If the current state is already
        // inside a layer, keep recording commands into that same layer.
        // Only states pushed by SaveLayer() should trigger layer compositing on restore.
        this.savedStates.Push(new DrawingCanvasState(current.Options, current.ClipState, current.TargetBounds, current.DestinationOffset)
        {
            Layer = current.Layer
        });

        return this.savedStates.Count;
    }

    /// <inheritdoc />
    public override int Save(DrawingOptions options)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(options, nameof(options));

        // Reuse Save() to push the snapshot frame, then swap the top frame for one carrying
        // the caller's options while keeping clip, bounds and layer linkage identical.
        _ = this.Save();
        DrawingCanvasState current = this.ResolveState();
        DrawingCanvasState state = new(options, current.ClipState, current.TargetBounds, current.DestinationOffset)
        {
            IsLayer = current.IsLayer,
            Layer = current.Layer,
        };

        _ = this.savedStates.Pop();
        this.savedStates.Push(state);
        return this.savedStates.Count;
    }

    /// <inheritdoc />
    public override int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(layerOptions, nameof(layerOptions));
        Guard.MustBeGreaterThan(bounds.Width, 0, nameof(bounds));
        Guard.MustBeGreaterThan(bounds.Height, 0, nameof(bounds));

        DrawingCanvasState currentState = this.ResolveState();
        return this.SaveLayerCore(layerOptions, bounds, currentState.Options, currentState.ClipState);
    }

    /// <inheritdoc />
    public override int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds, DrawingOptions options)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(layerOptions, nameof(layerOptions));
        Guard.NotNull(options, nameof(options));
        Guard.MustBeGreaterThan(bounds.Width, 0, nameof(bounds));
        Guard.MustBeGreaterThan(bounds.Height, 0, nameof(bounds));

        DrawingCanvasState currentState = this.ResolveState();
        return this.SaveLayerCore(layerOptions, bounds, options, currentState.ClipState);
    }

    /// <inheritdoc />
    public override int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds, LayerEffect effect)
        => this.SaveEffectLayerCore(layerOptions, bounds, effect, options: null);

    /// <inheritdoc />
    public override int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds, LayerEffect effect, DrawingOptions options)
    {
        Guard.NotNull(options, nameof(options));
        return this.SaveEffectLayerCore(layerOptions, bounds, effect, options);
    }

    /// <inheritdoc />
    public override int SaveLayer(GraphicsOptions layerOptions, IPath region, LayerEffect effect)
        => this.SaveEffectLayerCore(layerOptions, region, effect, options: null);

    /// <inheritdoc />
    public override int SaveLayer(GraphicsOptions layerOptions, IPath region, LayerEffect effect, DrawingOptions options)
    {
        Guard.NotNull(options, nameof(options));
        return this.SaveEffectLayerCore(layerOptions, region, effect, options);
    }

    /// <summary>
    /// Pushes an effect layer for rectangular content bounds: the effect region is the bounds
    /// expanded by the effect's output reach, so blurred and offset output spills naturally around the
    /// content instead of being cut at its edge.
    /// </summary>
    /// <param name="layerOptions">The compositing options used when the layer closes.</param>
    /// <param name="bounds">The content bounds in local canvas coordinates.</param>
    /// <param name="effect">The effect applied to the layer content on restore.</param>
    /// <param name="options">Drawing options for the layer contents, or <see langword="null"/> to inherit.</param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    private int SaveEffectLayerCore(GraphicsOptions layerOptions, Rectangle bounds, LayerEffect effect, DrawingOptions? options)
    {
        Guard.NotNull(effect, nameof(effect));

        // Rectangular content bounds grow into the effect region here so blurred and offset
        // output spills naturally around the content instead of being cut at its edge.
        bounds.Inflate(effect.Reach, effect.Reach);
        return this.SaveEffectLayerCore(layerOptions, new RectanglePolygon(bounds), effect, options);
    }

    /// <summary>
    /// Pushes a layer carrying a pending effect. The effect is recorded as an apply barrier over
    /// the region when the layer is restored, so it transforms exactly the content drawn into the
    /// layer before the layer composites.
    /// </summary>
    /// <param name="layerOptions">The compositing options used when the layer closes.</param>
    /// <param name="region">The path region the effect processes, in local coordinates.</param>
    /// <param name="effect">The effect applied to the layer content on restore.</param>
    /// <param name="options">Drawing options for the layer contents, or <see langword="null"/> to inherit.</param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    private int SaveEffectLayerCore(GraphicsOptions layerOptions, IPath region, LayerEffect effect, DrawingOptions? options)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(layerOptions, nameof(layerOptions));
        Guard.NotNull(region, nameof(region));
        Guard.NotNull(effect, nameof(effect));

        // The layer must contain the effect output, including pixels the write-back lands beyond
        // the region when the effect carries an offset.
        RectangleF regionBounds = region.Bounds;
        int reach = Math.Max(effect.Reach, 1);
        Rectangle layerBounds = Rectangle.FromLTRB(
            (int)MathF.Floor(regionBounds.Left) - reach,
            (int)MathF.Floor(regionBounds.Top) - reach,
            (int)MathF.Ceiling(regionBounds.Right) + reach,
            (int)MathF.Ceiling(regionBounds.Bottom) + reach);

        DrawingCanvasState currentState = this.ResolveState();

        // Backdrop effects filter the pixels already beneath the layer, clipped to the region,
        // before the layer opens; the layer content then renders above the filtered backdrop and
        // nothing remains pending for the restore. The apply is recorded under the supplied
        // drawing options so the region honours the caller's transform.
        if (effect is BackdropLayerEffect)
        {
            if (!effect.IsPassThrough)
            {
                bool pushed = options is not null;
                if (pushed)
                {
                    _ = this.Save(options!);
                }

                this.ApplyCore(region, effect.CreateOperation(), effect, effect.WriteBackOptions, effect.WriteBackOffset);
                if (pushed)
                {
                    this.Restore();
                }
            }

            return this.SaveLayerCore(layerOptions, layerBounds, options ?? currentState.Options, currentState.ClipState);
        }

        int saveCount = this.SaveLayerCore(layerOptions, layerBounds, options ?? currentState.Options, currentState.ClipState);
        this.ResolveState().LayerEffect = new DrawingCanvasLayerEffect(region, effect);
        return saveCount;
    }

    /// <summary>
    /// Pushes a layer state with already-resolved drawing options and clip paths.
    /// </summary>
    /// <param name="layerOptions">Graphics options used when compositing the closed layer.</param>
    /// <param name="bounds">Layer bounds in local canvas coordinates.</param>
    /// <param name="options">Drawing options for commands recorded into the layer.</param>
    /// <param name="clipState">The normalized clip state to apply during backend rendering.</param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    private int SaveLayerCore(
        GraphicsOptions layerOptions,
        Rectangle bounds,
        DrawingOptions options,
        DrawingClipState clipState)
    {
        DrawingCanvasState currentState = this.ResolveState();
        Rectangle absoluteLayerBounds = ResolveLayerBounds(options.Transform, currentState.TargetBounds, currentState.DestinationOffset, bounds);
        DrawingCanvasLayer layer = new(layerOptions);

        // Clips and layers are separate ordered controls. The active clip stack is already open
        // in the command stream; the layer marker only pushes the isolated layer rectangle.
        this.batcher.AddComposition(CompositionCommand.CreateBeginLayer(absoluteLayerBounds, layer));

        // A bounded layer clips and allocates the isolated target, but it does not shift the canvas coordinate system.
        DrawingCanvasState layerState = new(options, clipState, absoluteLayerBounds, currentState.DestinationOffset)
        {
            IsLayer = true,
            Layer = layer,
        };

        this.savedStates.Push(layerState);
        return this.savedStates.Count;
    }

    /// <inheritdoc />
    public override void Restore()
    {
        this.EnsureNotDisposed();
        if (this.savedStates.Count <= 1)
        {
            return;
        }

        this.FlushLayerEffect();
        DrawingCanvasState popped = this.savedStates.Pop();
        DrawingCanvasState current = this.ResolveState();
        this.AppendEndClips(popped.ClipState.Count - current.ClipState.Count);

        if (popped.IsLayer)
        {
            // Layer closure happens after nested clips are closed so the encoded stream remains
            // parent clips -> layer -> layer content -> nested clip ends -> layer end.
            this.batcher.AddComposition(CompositionCommand.CreateEndLayer(popped.TargetBounds, popped.Layer!));
        }
    }

    /// <inheritdoc />
    public override void RestoreTo(int saveCount)
    {
        this.EnsureNotDisposed();
        Guard.MustBeBetweenOrEqualTo(saveCount, 1, this.savedStates.Count, nameof(saveCount));

        this.RestoreToCore(saveCount);
    }

    /// <inheritdoc cref="DrawingCanvas.CreateRegion(Rectangle)" />
    public override DrawingCanvas<TPixel> CreateRegion(Rectangle region)
    {
        this.EnsureNotDisposed();

        Rectangle clipped = Rectangle.Intersect(this.Bounds, region);
        CanvasRegionFrame<TPixel> childFrame = new(this.targetFrame, clipped);
        DrawingCanvasState currentState = this.ResolveState();

        // Regions share the same batcher and deferred image resources. Only the root canvas owns flushing.
        return new DrawingCanvas<TPixel>(
            this.configuration,
            this.backend,
            childFrame,
            this.batcher,
            this.textCache,
            false,
            new DrawingCanvasState(currentState.Options, currentState.ClipState, childFrame.Bounds, childFrame.Bounds.Location)
            {
                IsLayer = currentState.IsLayer,
                Layer = currentState.Layer,
            },
            false);
    }

    /// <inheritdoc />
    public override void Clear(Brush brush, IPath path)
    {
        DrawingCanvasState state = this.ResolveState();
        DrawingOptions options = state.Options.CloneForClearOperation();
        this.ExecuteWithTemporaryState(options, state.ClipState, () => this.Fill(brush, path));
    }

    /// <inheritdoc />
    public override void Fill(Brush brush, IPath path)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(brush, nameof(brush));
        this.EnqueueFillPath(brush, path);
    }

    /// <inheritdoc />
    public override void Clip(params IPath[] clipPaths)
        => this.Clip(ClipOperation.Intersection, clipPaths);

    /// <inheritdoc />
    public override void Clip(ClipOperation operation, params IPath[] clipPaths)
    {
        this.EnsureNotDisposed();
        if (clipPaths is null || clipPaths.Length == 0)
        {
            return;
        }

        DrawingCanvasState state = this.savedStates.Pop();

        // Clip paths are supplied in the active coordinate space. Build descriptors before
        // applying the transform so rectangle and region metadata can survive translations
        // and axis-aligned scales instead of being erased by IPath.Transform.
        Matrix4x4 transform = state.Options.Transform;

        Rectangle targetBounds = state.TargetBounds;

        DrawingClipState incomingState = CreateClipState(
            clipPaths,
            transform,
            operation,
            state.Options.GraphicsOptions.Antialias ? DrawingClipEdgeMode.Antialiased : DrawingClipEdgeMode.Hard,
            state.Options.GraphicsOptions.AntialiasThreshold);

        this.AppendBeginClips(incomingState, state.DestinationOffset);

        if (operation == ClipOperation.Intersection &&
            incomingState.TryGetConservativeBounds(state.DestinationOffset, out Rectangle incomingBounds))
        {
            // TargetBounds is the work scheduler's coarse limit. The exact clip state is still
            // retained below so fractional edge coverage and region membership are not lost.
            targetBounds = Rectangle.Intersect(targetBounds, incomingBounds);
        }

        if (!state.ClipState.HasClips)
        {
            this.savedStates.Push(new DrawingCanvasState(state.Options, incomingState, targetBounds, state.DestinationOffset)
            {
                IsLayer = state.IsLayer,
                Layer = state.Layer,
            });

            return;
        }

        // Clip operations are recorded as ordered state. Backends consume that state as
        // clip primitives, so recording never rewrites future subject geometry.
        DrawingClipState combinedClipState = state.ClipState.Append(incomingState);

        this.savedStates.Push(new DrawingCanvasState(state.Options, combinedClipState, targetBounds, state.DestinationOffset)
        {
            IsLayer = state.IsLayer,
            Layer = state.Layer,
        });
    }

    /// <inheritdoc />
    public override void Apply(Rectangle region, Action<IImageProcessingContext> operation)
        => this.Apply(new RectanglePolygon(region), operation);

    /// <inheritdoc />
    public override void Apply(PathBuilder pathBuilder, Action<IImageProcessingContext> operation)
    {
        Guard.NotNull(pathBuilder, nameof(pathBuilder));
        this.Apply(pathBuilder.Build(), operation);
    }

    /// <inheritdoc />
    public override void Apply(IPath path, Action<IImageProcessingContext> operation)
        => this.ApplyCore(path, operation, effect: null, writeBackOptions: null, writeBackOffset: default);

    /// <inheritdoc />
    public override void Apply(Rectangle region, Action<IImageProcessingContext> operation, GraphicsOptions writeBackOptions, Point writeBackOffset)
        => this.Apply(new RectanglePolygon(region), operation, writeBackOptions, writeBackOffset);

    /// <inheritdoc />
    public override void Apply(IPath path, Action<IImageProcessingContext> operation, GraphicsOptions writeBackOptions, Point writeBackOffset)
    {
        Guard.NotNull(writeBackOptions, nameof(writeBackOptions));
        this.ApplyCore(path, operation, effect: null, writeBackOptions, writeBackOffset);
    }

    /// <summary>
    /// Records an apply barrier for a path region with optional write-back compositing options.
    /// </summary>
    /// <param name="path">The path region to process.</param>
    /// <param name="operation">The image-processing operation to apply to the region.</param>
    /// <param name="effect">The layer effect represented by the operation, or <see langword="null"/> for a direct Apply operation.</param>
    /// <param name="writeBackOptions">
    /// The graphics options used to composite the processed pixels back, or <see langword="null"/>
    /// to replace the region outright.
    /// </param>
    /// <param name="writeBackOffset">The offset at which the processed pixels are written back.</param>
    private void ApplyCore(IPath path, Action<IImageProcessingContext> operation, LayerEffect? effect, GraphicsOptions? writeBackOptions, Point writeBackOffset)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(operation, nameof(operation));

        DrawingCanvasState state = this.ResolveState();
        if (state.Layer is DrawingCanvasLayer layer)
        {
            layer.RequiresScopedApply = true;
        }

        foreach (DrawingCanvasState savedState in this.savedStates)
        {
            if (savedState.Layer is DrawingCanvasLayer savedLayer)
            {
                savedLayer.RequiresScopedApply = true;
            }
        }

        ApplyBarrier barrier = new(
            path.AsClosedPath(),
            state.Options,
            this.Bounds,
            state.TargetBounds,
            state.DestinationOffset,
            state.Layer,
            operation,
            effect,
            writeBackOptions,
            writeBackOffset);

        this.batcher.EnsureClipAnchors(state.DestinationOffset);
        this.batcher.AddApplyBarrier(barrier);
    }

    /// <summary>
    /// Draws a two-point line segment using the provided pen and drawing options.
    /// </summary>
    /// <param name="pen">Pen used to generate the line outline.</param>
    /// <param name="start">Line start point.</param>
    /// <param name="end">Line end point.</param>
    public void DrawLine(Pen pen, PointF start, PointF end)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(pen, nameof(pen));

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        // The dedicated stroke-segment command is a fast path for solid, unclipped strokes only.
        // Dash patterns and active clips route through the general stroked-path pipeline.
        if (state.ClipState.HasClips || !pen.StrokePattern.IsEmpty)
        {
            this.PrepareCompositionCore(
                new Path([start, end]),
                pen.StrokeFill,
                effectiveOptions,
                pen);
            return;
        }

        this.PrepareStrokeLineSegmentCompositionCore(start, end, pen.StrokeFill, effectiveOptions, pen);
    }

    /// <inheritdoc />
    public override void DrawLine(Pen pen, params PointF[] points)
    {
        Guard.NotNull(points, nameof(points));

        if (points.Length == 2)
        {
            this.DrawLine(pen, points[0], points[1]);
            return;
        }

        this.EnsureNotDisposed();
        Guard.NotNull(pen, nameof(pen));

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        // The dedicated polyline command is a fast path for solid, unclipped strokes only.
        // Dash patterns and active clips route through the general stroked-path pipeline.
        if (state.ClipState.HasClips || !pen.StrokePattern.IsEmpty)
        {
            this.PrepareCompositionCore(
                new Path(points),
                pen.StrokeFill,
                effectiveOptions,
                pen);
            return;
        }

        this.PrepareStrokePolylineCompositionCore(points, pen.StrokeFill, effectiveOptions, pen);
    }

    /// <inheritdoc />
    public override void Draw(Pen pen, IPath path)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(pen, nameof(pen));
        Guard.NotNull(path, nameof(path));

        DrawingCanvasState state = this.ResolveState();

        this.PrepareCompositionCore(
            path,
            pen.StrokeFill,
            state.Options,
            pen);
    }

    /// <inheritdoc />
    public override void DrawText(
        RichTextOptions textOptions,
        ReadOnlySpan<char> text,
        Brush? brush,
        Pen? pen)
        => this.DrawTextCore(textOptions, text, path: null, brush, pen);

    /// <inheritdoc />
    public override void DrawText(
        RichTextOptions textOptions,
        ReadOnlySpan<char> text,
        IPath path,
        Brush? brush,
        Pen? pen)
    {
        Guard.NotNull(path, nameof(path));
        this.DrawTextCore(textOptions, text, path, brush, pen);
    }

    /// <summary>
    /// Lays out and renders text, converting the produced glyph operations to queued commands.
    /// </summary>
    /// <param name="textOptions">The text rendering options.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="path">Optional path used as the text baseline; <see langword="null"/> for straight-line layout.</param>
    /// <param name="brush">Optional brush used to fill glyphs.</param>
    /// <param name="pen">Optional pen used to outline glyphs.</param>
    private void DrawTextCore(
        RichTextOptions textOptions,
        ReadOnlySpan<char> text,
        IPath? path,
        Brush? brush,
        Pen? pen)
    {
        this.EnsureNotDisposed();

        if (text.IsEmpty)
        {
            return;
        }

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        EnsureTextPaint(brush, pen);

        RichTextOptions configuredOptions = ConfigureTextOptions(textOptions, path, out IPath? configuredPath);

        // Path text bends the layout along the path so the straight visible band does not
        // apply; caller-supplied bounds always win when present.
        if (configuredPath is null &&
            configuredOptions.VisibleBounds is null &&
            TryGetVisibleTextBounds(state, effectiveOptions.Transform, out FontRectangle visibleBounds))
        {
            configuredOptions = new RichTextOptions(configuredOptions)
            {
                VisibleBounds = visibleBounds
            };
        }

        using RichTextGlyphRenderer glyphRenderer = new(effectiveOptions, configuredPath, pen, brush, this.textCache, this.textOperations);
        TextRenderer renderer = new(glyphRenderer);
        renderer.Render(text, configuredOptions);

        this.DrawTextOperations(glyphRenderer.DrawingOperations, effectiveOptions);
    }

    /// <inheritdoc />
    public override void DrawText(
        TextBlock textBlock,
        PointF location,
        float wrappingLength,
        Brush? brush,
        Pen? pen)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textBlock, nameof(textBlock));
        EnsureTextPaint(brush, pen);

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        // Prepared text already owns shaping and layout options. The caller-supplied
        // location is therefore applied as canvas placement before the active canvas
        // transform, instead of mutating text options or rebuilding the block.
        DrawingOptions placedOptions = new(
            effectiveOptions.GraphicsOptions,
            effectiveOptions.IntersectionRule,
            Matrix4x4.CreateTranslation(location.X, location.Y, 0) * effectiveOptions.Transform,
            effectiveOptions.TextContrast);

        using RichTextGlyphRenderer glyphRenderer = new(placedOptions, path: null, pen, brush, this.textCache, this.textOperations);
        if (TryGetVisibleTextBounds(state, placedOptions.Transform, out FontRectangle visibleBounds))
        {
            textBlock.RenderTo(glyphRenderer, wrappingLength, visibleBounds);
        }
        else
        {
            textBlock.RenderTo(glyphRenderer, wrappingLength);
        }

        this.DrawTextOperations(glyphRenderer.DrawingOperations, placedOptions);
    }

    /// <inheritdoc />
    public override void DrawText(
        TextBlock textBlock,
        IPath path,
        float wrappingLength,
        Brush? brush,
        Pen? pen)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textBlock, nameof(textBlock));
        Guard.NotNull(path, nameof(path));
        EnsureTextPaint(brush, pen);

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        using RichTextGlyphRenderer glyphRenderer = new(effectiveOptions, path, pen, brush, this.textCache, this.textOperations);
        textBlock.RenderTo(glyphRenderer, wrappingLength);

        this.DrawTextOperations(glyphRenderer.DrawingOperations, effectiveOptions);
    }

    /// <inheritdoc />
    public override void DrawText(
        LineLayout lineLayout,
        PointF location,
        Brush? brush,
        Pen? pen)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(lineLayout, nameof(lineLayout));
        EnsureTextPaint(brush, pen);

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        // LineLayout represents a single already-broken line. Placement belongs
        // to the drawing host, so the line can be reused in arbitrary slots
        // without changing the prepared text object.
        DrawingOptions placedOptions = new(
            effectiveOptions.GraphicsOptions,
            effectiveOptions.IntersectionRule,
            Matrix4x4.CreateTranslation(location.X, location.Y, 0) * effectiveOptions.Transform,
            effectiveOptions.TextContrast);

        using RichTextGlyphRenderer glyphRenderer = new(placedOptions, path: null, pen, brush, this.textCache, this.textOperations);
        lineLayout.RenderTo(glyphRenderer);

        this.DrawTextOperations(glyphRenderer.DrawingOperations, placedOptions);
    }

    /// <inheritdoc />
    public override void DrawText(
        LineLayout lineLayout,
        IPath path,
        Brush? brush,
        Pen? pen)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(lineLayout, nameof(lineLayout));
        Guard.NotNull(path, nameof(path));
        EnsureTextPaint(brush, pen);

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        using RichTextGlyphRenderer glyphRenderer = new(effectiveOptions, path, pen, brush, this.textCache, this.textOperations);
        lineLayout.RenderTo(glyphRenderer);

        this.DrawTextOperations(glyphRenderer.DrawingOperations, effectiveOptions);
    }

    /// <inheritdoc />
    public override void DrawText(
        ushort glyphId,
        RichGlyphOptions options,
        Brush? brush,
        Pen? pen)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(options, nameof(options));
        EnsureTextPaint(brush, pen);

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        using RichTextGlyphRenderer glyphRenderer = new(effectiveOptions, path: null, pen, brush, this.textCache, this.textOperations);
        TextRenderer renderer = new(glyphRenderer);
        renderer.Render(glyphId, options);

        this.DrawTextOperations(glyphRenderer.DrawingOperations, effectiveOptions);
    }

    /// <inheritdoc />
    public override void DrawText(ReadOnlySpan<ushort> glyphIds, ReadOnlySpan<Vector2> points, RichGlyphOptions options, Brush? brush, Pen? pen)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(options, nameof(options));
        Guard.IsTrue(glyphIds.Length == points.Length, nameof(points), "Glyph id and point counts must match.");

        if (glyphIds.IsEmpty)
        {
            return;
        }

        EnsureTextPaint(brush, pen);

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        using RichTextGlyphRenderer glyphRenderer = new(effectiveOptions, path: null, pen, brush, this.textCache, this.textOperations);
        TextRenderer renderer = new(glyphRenderer);
        renderer.Render(glyphIds, points, options);

        this.DrawTextOperations(this.BatchGlyphRunOperations(glyphRenderer.DrawingOperations), effectiveOptions);
    }

    /// <inheritdoc />
    public override void DrawGlyphs(
        Brush brush,
        Pen pen,
        IEnumerable<GlyphPathCollection> glyphs)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(brush, nameof(brush));
        Guard.NotNull(pen, nameof(pen));
        Guard.NotNull(glyphs, nameof(glyphs));

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions baseOptions = state.Options;
        DrawingClipState clipState = state.ClipState;

        foreach (GlyphPathCollection glyph in glyphs)
        {
            if (glyph.LayerCount == 0)
            {
                continue;
            }

            if (glyph.LayerCount == 1)
            {
                this.Fill(brush, glyph.Paths);
                continue;
            }

            // The fill-versus-outline heuristic measures color layers against the glyph cell.
            // Decoration lanes ride the same collection but extend beyond the glyph, so the
            // cell is the union of the non-decoration layers only.
            RectangleF glyphCell = default;
            bool hasGlyphCell = false;
            for (int layerIndex = 0; layerIndex < glyph.LayerCount; layerIndex++)
            {
                GlyphLayerInfo layer = glyph.Layers[layerIndex];
                if (layer.Kind != GlyphLayerKind.Decoration)
                {
                    glyphCell = hasGlyphCell ? RectangleF.Union(glyphCell, layer.Bounds) : layer.Bounds;
                    hasGlyphCell = true;
                }
            }

            float glyphArea = glyphCell.Width * glyphCell.Height;
            for (int layerIndex = 0; layerIndex < glyph.LayerCount; layerIndex++)
            {
                GlyphLayerInfo layer = glyph.Layers[layerIndex];
                if (layer.Count == 0)
                {
                    continue;
                }

                PathCollection layerPaths = glyph.GetLayerPaths(layerIndex);
                DrawingOptions layerOptions = baseOptions.CloneOrReturnForRules(
                    layer.IntersectionRule,
                    layer.PixelAlphaCompositionMode,
                    layer.PixelColorBlendingMode);

                bool shouldFill;
                if (layer.Kind is GlyphLayerKind.Decoration or GlyphLayerKind.Glyph)
                {
                    shouldFill = true;
                }
                else
                {
                    // Color layers covering half or more of the glyph cell are treated as the
                    // dominant painted layer and outlined with the pen; only smaller detail
                    // layers are filled with the brush.
                    float layerArea = layerPaths.ComputeArea();
                    shouldFill = layerArea > 0F && glyphArea > 0F && (layerArea / glyphArea) < 0.50F;
                }

                this.ExecuteWithTemporaryState(layerOptions, clipState, () =>
                {
                    if (shouldFill)
                    {
                        this.Fill(brush, layerPaths);
                    }
                    else
                    {
                        this.Draw(pen, layerPaths);
                    }
                });
            }
        }
    }

    /// <inheritdoc />
    public override TextMetrics MeasureText(RichTextOptions textOptions, ReadOnlySpan<char> text)
    {
        this.EnsureNotDisposed();
        return TextMeasurer.Measure(text, textOptions);
    }

    /// <inheritdoc />
    public override void DrawImage(
        Image image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler? sampler)
        => this.DrawImage(image, sourceRect, destinationRect, WrapMode.Repeat, WrapMode.Repeat, sampler);

    /// <inheritdoc />
    public override void DrawImage(
        Image image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        WrapMode wrapX,
        WrapMode wrapY,
        IResampler? sampler)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(image, nameof(image));

        if (image is Image<TPixel> specificImage)
        {
            this.DrawImage(specificImage, sourceRect, destinationRect, wrapX, wrapY, sampler);
            return;
        }

        // Only the pixels inside the clipped source region are ever sampled by the draw operation.
        // When that region covers just part of the image, crop it in the source pixel format first so
        // the per-pixel format conversion runs over the required region instead of the whole image.
        if (!TryGetDrawImageClip(sourceRect, destinationRect, image.Bounds, out Rectangle clippedSourceRect, out RectangleF clippedDestinationRect))
        {
            return;
        }

        if (clippedSourceRect == image.Bounds)
        {
            Image<TPixel> convertedImage = image.CloneAs<TPixel>();
            this.DrawImageCore(convertedImage, clippedSourceRect, clippedDestinationRect, sampler, wrapX, wrapY, true);
            return;
        }

        using Image croppedSource = image.Clone(ctx => ctx.Crop(clippedSourceRect));
        Image<TPixel> convertedRegion = croppedSource.CloneAs<TPixel>();
        this.DrawImageCore(convertedRegion, convertedRegion.Bounds, clippedDestinationRect, sampler, wrapX, wrapY, true);
    }

    /// <inheritdoc cref="DrawingCanvas.DrawImage(Image, Rectangle, RectangleF, IResampler?)" />
    public void DrawImage(
        Image<TPixel> image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler? sampler = null)
        => this.DrawImage(image, sourceRect, destinationRect, WrapMode.Repeat, WrapMode.Repeat, sampler);

    /// <inheritdoc cref="DrawingCanvas.DrawImage(Image, Rectangle, RectangleF, WrapMode, WrapMode, IResampler?)" />
    public void DrawImage(
        Image<TPixel> image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        WrapMode wrapX,
        WrapMode wrapY,
        IResampler? sampler = null)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(image, nameof(image));

        if (!TryGetDrawImageClip(sourceRect, destinationRect, image.Bounds, out Rectangle clippedSourceRect, out RectangleF clippedDestinationRect))
        {
            return;
        }

        this.DrawImageCore(image, clippedSourceRect, clippedDestinationRect, sampler, wrapX, wrapY, false);
    }

    /// <inheritdoc />
    public override DrawingBackendScene CreateScene()
    {
        this.EnsureNotDisposed();
        this.CloseClipsAndSealActiveCommandRange();

        IDisposable[]? ownedResources = this.DetachPendingImageResources();

        try
        {
            return this.batcher.CreateScene(this.backend, this.targetFrame.Bounds, ownedResources);
        }
        catch
        {
            DisposeOwnedResources(ownedResources);
            throw;
        }
        finally
        {
            this.batcher.ClearCommandBatch();

            // The sealed range consumed the balanced clip stream; reopen the active clip
            // stack so later commands keep recording under the same clip state.
            DrawingCanvasState state = this.ResolveState();
            this.AppendBeginClips(state.ClipState, state.DestinationOffset);
        }
    }

    /// <inheritdoc />
    public override void RenderScene(DrawingBackendScene scene)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(scene, nameof(scene));

        this.CloseClipsAndSealActiveCommandRange();
        this.batcher.AddScene(scene);

        DrawingCanvasState state = this.ResolveState();
        this.AppendBeginClips(state.ClipState, state.DestinationOffset);
    }

    /// <inheritdoc />
    public override void CopyPixelsFrom(DrawingCanvas source, Rectangle sourceRectangle, Point targetPoint)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(source, nameof(source));

        if (source is not DrawingCanvas<TPixel> typedSource)
        {
            throw new ArgumentException("The source canvas pixel type must match the target canvas pixel type.", nameof(source));
        }

        typedSource.EnsureNotDisposed();

        // Pixel copy transfers already-rasterized frame contents. Materialize both timelines at
        // this ordering boundary so the backend copies pixels instead of replaying commands.
        typedSource.CloseClipsAndSealActiveCommandRange();
        typedSource.RenderRecordedTimeline();

        DrawingCanvasState sourceState = typedSource.ResolveState();
        typedSource.AppendBeginClips(sourceState.ClipState, sourceState.DestinationOffset);

        this.CloseClipsAndSealActiveCommandRange();
        this.RenderRecordedTimeline();

        DrawingCanvasState targetState = this.ResolveState();
        this.AppendBeginClips(targetState.ClipState, targetState.DestinationOffset);

        this.backend.CopyPixels(
            this.configuration,
            typedSource.targetFrame,
            this.targetFrame,
            sourceRectangle,
            targetPoint);
    }

    /// <summary>
    /// Normalizes a pre-clipped image draw into an image-brush fill: source pixels are cropped/scaled,
    /// then baked through the canvas transform, and the result is queued as a rectangle fill.
    /// </summary>
    /// <param name="image">The source image in the target pixel format.</param>
    /// <param name="clippedSourceRect">The source rectangle, already clipped to the bounds of <paramref name="image"/>.</param>
    /// <param name="clippedDestinationRect">The destination rectangle matching <paramref name="clippedSourceRect"/>, in local canvas coordinates.</param>
    /// <param name="sampler">Optional resampler used when scaling or transforming the image.</param>
    /// <param name="wrapX">The horizontal wrap mode applied when sampling beyond the destination rectangle.</param>
    /// <param name="wrapY">The vertical wrap mode applied when sampling beyond the destination rectangle.</param>
    /// <param name="ownsSourceImage">
    /// Whether this method owns <paramref name="image"/> and must dispose it or transfer its
    /// lifetime to the deferred command batch.
    /// </param>
    private void DrawImageCore(
        Image<TPixel> image,
        Rectangle clippedSourceRect,
        RectangleF clippedDestinationRect,
        IResampler? sampler,
        WrapMode wrapX,
        WrapMode wrapY,
        bool ownsSourceImage)
    {
        bool disposeSourceImage = ownsSourceImage;

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;
        DrawingOptions commandOptions = effectiveOptions;

        Image<TPixel>? ownedImage = null;
        try
        {
            Size scaledSize = new(
                Math.Max(1, (int)MathF.Ceiling(clippedDestinationRect.Width)),
                Math.Max(1, (int)MathF.Ceiling(clippedDestinationRect.Height)));

            bool requiresScaling =
                clippedSourceRect.Width != scaledSize.Width ||
                clippedSourceRect.Height != scaledSize.Height;

            Image<TPixel> brushImage = image;
            RectangleF brushImageRegion = clippedSourceRect;
            RectangleF renderDestinationRect = clippedDestinationRect;

            // Phase 1: Prepare source pixels (crop/scale) in image-local space.
            if (requiresScaling)
            {
                ownedImage = CreateScaledDrawImage(image, clippedSourceRect, scaledSize, sampler);
                brushImage = ownedImage;
                brushImageRegion = ownedImage.Bounds;
            }
            else if (clippedSourceRect != image.Bounds)
            {
                ownedImage = image.Clone(ctx => ctx.Crop(clippedSourceRect));
                brushImage = ownedImage;
                brushImageRegion = ownedImage.Bounds;
            }

            // Phase 2: Apply canvas transform to image content when requested.
            if (effectiveOptions.Transform != Matrix4x4.Identity)
            {
                Image<TPixel> transformed = CreateTransformedDrawImage(
                    brushImage,
                    clippedDestinationRect,
                    effectiveOptions.Transform,
                    sampler,
                    out renderDestinationRect);

                ownedImage?.Dispose();
                ownedImage = transformed;
                brushImage = transformed;
                brushImageRegion = transformed.Bounds;

                // The image pixels and destination rect are already in transformed canvas space,
                // so the queued fill must not apply the canvas transform a second time.
                commandOptions = new DrawingOptions(
                    effectiveOptions.GraphicsOptions,
                    effectiveOptions.IntersectionRule,
                    Matrix4x4.Identity,
                    effectiveOptions.TextContrast);
            }

            if (renderDestinationRect.Width <= 0 || renderDestinationRect.Height <= 0)
            {
                return;
            }

            // Phase 3: Transfer temp-image ownership to deferred batch execution.
            if (!ReferenceEquals(brushImage, image))
            {
                if (disposeSourceImage)
                {
                    image.Dispose();
                    disposeSourceImage = false;
                }

                this.pendingImageResources.Add(brushImage);
                ownedImage = null;
            }
            else if (disposeSourceImage)
            {
                this.pendingImageResources.Add(image);
                disposeSourceImage = false;
            }

            ImageBrush<TPixel> brush = new(brushImage, brushImageRegion, wrapX, wrapY);
            IPath destinationPath = new RectanglePolygon(
                renderDestinationRect.X,
                renderDestinationRect.Y,
                renderDestinationRect.Width,
                renderDestinationRect.Height);

            this.PrepareCompositionCore(
                destinationPath,
                brush,
                commandOptions);
        }
        finally
        {
            ownedImage?.Dispose();
            if (disposeSourceImage)
            {
                image.Dispose();
            }
        }
    }

    /// <summary>
    /// Prepares a path fill composition command and enqueues it in the batcher.
    /// </summary>
    /// <param name="path">Path to fill.</param>
    /// <param name="brush">Brush used for shading.</param>
    /// <param name="options">Effective drawing options.</param>
    /// <param name="pen">Optional pen for stroke commands.</param>
    private void PrepareCompositionCore(
        IPath path,
        Brush brush,
        DrawingOptions options,
        Pen? pen = null)
    {
        brush = this.NormalizeBrush(brush);

        GraphicsOptions graphicsOptions = options.GraphicsOptions;
        RasterizationMode rasterizationMode = graphicsOptions.Antialias ? RasterizationMode.Antialiased : RasterizationMode.Aliased;

        Matrix4x4 transform = options.Transform;
        RectangleF bounds = transform == Matrix4x4.Identity ? path.Bounds : RectangleF.Transform(path.Bounds, transform);
        if (pen is not null)
        {
            float halfWidth = pen.StrokeWidth * GetTransformWidthScale(transform) * 0.5F;
            float joinInflate = pen.StrokeOptions.LineJoin switch
            {
                LineJoin.Miter or LineJoin.MiterRevert or LineJoin.MiterRound => (float)(halfWidth * Math.Max(pen.StrokeOptions.MiterLimit, 1D)),
                _ => halfWidth
            };

            float capInflate = pen.StrokeOptions.LineCap == LineCap.Square
                ? halfWidth * MathF.Sqrt(2F)
                : halfWidth;

            float inflate = MathF.Max(joinInflate, capInflate);

            bounds.Inflate(new SizeF(inflate, inflate));
        }

        Rectangle interest = ToRasterizerInterest(bounds);
        RasterizerOptions rasterizerOptions = new(
            interest,
            options.IntersectionRule,
            rasterizationMode,
            graphicsOptions.AntialiasThreshold);

        DrawingCanvasState state = this.ResolveState();

        // The ordered begin/end-clip stream is the clip source of truth; anchor it at this
        // state's destination offset before recording the draw.
        this.batcher.EnsureClipAnchors(state.DestinationOffset);

        // Commands carry their absolute target bounds and destination origin explicitly.
        // Bounded layers can clip the target while preserving the active canvas coordinate origin.
        if (pen is null)
        {
            this.batcher.AddComposition(
                CompositionCommand.Create(
                    path,
                    brush,
                    options,
                    in rasterizerOptions,
                    state.TargetBounds,
                    state.DestinationOffset,
                    state.Layer));
            return;
        }

        this.batcher.AddStrokePath(
            new StrokePathCommand(
                path,
                brush,
                options,
                in rasterizerOptions,
                state.TargetBounds,
                state.DestinationOffset,
                pen,
                state.Layer is not null,
                state.Layer));
    }

    /// <summary>
    /// Enqueues one explicit two-point stroke line-segment command using the current canvas state.
    /// </summary>
    /// <param name="start">Line start point in local coordinates.</param>
    /// <param name="end">Line end point in local coordinates.</param>
    /// <param name="brush">Brush used for shading the stroke.</param>
    /// <param name="options">Effective drawing options.</param>
    /// <param name="pen">Pen defining the stroke geometry.</param>
    private void PrepareStrokeLineSegmentCompositionCore(
        PointF start,
        PointF end,
        Brush brush,
        DrawingOptions options,
        Pen pen)
    {
        brush = this.NormalizeBrush(brush);

        GraphicsOptions graphicsOptions = options.GraphicsOptions;
        RasterizationMode rasterizationMode = graphicsOptions.Antialias ? RasterizationMode.Antialiased : RasterizationMode.Aliased;
        RectangleF bounds = StrokeLineSegmentCommand.GetConservativeBounds(start, end, pen);
        Rectangle interest = Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right) + 1,
            (int)MathF.Ceiling(bounds.Bottom) + 1);

        RasterizerOptions rasterizerOptions = new(
            interest,
            options.IntersectionRule,
            rasterizationMode,
            graphicsOptions.AntialiasThreshold);

        DrawingCanvasState state = this.ResolveState();
        this.batcher.EnsureClipAnchors(state.DestinationOffset);
        this.batcher.AddStrokeLineSegment(
            new StrokeLineSegmentCommand(
                start,
                end,
                brush,
                options,
                in rasterizerOptions,
                state.TargetBounds,
                state.DestinationOffset,
                pen,
                state.Layer is not null,
                state.Layer));
    }

    /// <summary>
    /// Enqueues one explicit stroked open polyline command using the current canvas state.
    /// </summary>
    /// <param name="points">Polyline points in local coordinates.</param>
    /// <param name="brush">Brush used for shading the stroke.</param>
    /// <param name="options">Effective drawing options.</param>
    /// <param name="pen">Pen defining the stroke geometry.</param>
    private void PrepareStrokePolylineCompositionCore(
        PointF[] points,
        Brush brush,
        DrawingOptions options,
        Pen pen)
    {
        brush = this.NormalizeBrush(brush);

        GraphicsOptions graphicsOptions = options.GraphicsOptions;
        RasterizationMode rasterizationMode = graphicsOptions.Antialias ? RasterizationMode.Antialiased : RasterizationMode.Aliased;
        RectangleF bounds = StrokePolylineCommand.GetConservativeBounds(points, pen);
        Rectangle interest = Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right) + 1,
            (int)MathF.Ceiling(bounds.Bottom) + 1);

        RasterizerOptions rasterizerOptions = new(
            interest,
            options.IntersectionRule,
            rasterizationMode,
            graphicsOptions.AntialiasThreshold);

        DrawingCanvasState state = this.ResolveState();
        this.batcher.EnsureClipAnchors(state.DestinationOffset);
        this.batcher.AddStrokePolyline(
            new StrokePolylineCommand(
                points,
                brush,
                options,
                in rasterizerOptions,
                state.TargetBounds,
                state.DestinationOffset,
                pen,
                state.Layer is not null,
                state.Layer));
    }

    /// <summary>
    /// Normalizes brushes that carry image sources containing the wrong pixel format exactly once.
    /// </summary>
    /// <param name="brush">The logical brush supplied by the caller.</param>
    /// <returns>The brush to queue for this canvas flush.</returns>
    private Brush NormalizeBrush(Brush brush)
    {
        if (brush is not ImageBrush imageBrush)
        {
            return brush;
        }

        if (brush is ImageBrush<TPixel> typedBrush)
        {
            return typedBrush;
        }

        // Normalize the source image once so deferred composition does not repeat per-pixel conversions.
        Image<TPixel> convertedImage = imageBrush.UntypedImage.CloneAs<TPixel>();
        this.pendingImageResources.Add(convertedImage);
        return new ImageBrush<TPixel>(convertedImage, imageBrush.SourceRegion, imageBrush.Offset, imageBrush.WrapX, imageBrush.WrapY);
    }

    /// <summary>
    /// Enqueues a fill command for one path using the current canvas state.
    /// </summary>
    /// <param name="brush">Brush used for shading.</param>
    /// <param name="path">Path to fill.</param>
    private void EnqueueFillPath(Brush brush, IPath path)
    {
        DrawingCanvasState state = this.ResolveState();
        IPath closed = path.AsClosedPath();

        this.PrepareCompositionCore(
            closed,
            brush,
            state.Options);
    }

    /// <summary>
    /// Combines a uniform glyph run into one fill operation and one draw operation.
    /// </summary>
    /// <remarks>
    /// The text renderer emits one operation per glyph. Merging the glyphs of a uniform run (same
    /// brush, pen, blend and render pass) into a single fill path and a single stroke path replaces
    /// N composition commands with one or two, which dominates when the same run is redrawn every
    /// frame. The merged path is built and cached in run-local space (relative to the first glyph,
    /// whose snapped location and sub-pixel fraction ride the combined operation), so a repeat at any
    /// position reuses it without a re-merge or a per-position geometry copy. The run is left as its
    /// per-glyph operations when it is not uniform (for example colour layers or per-glyph paint),
    /// because those depend on the exact per-operation order and paint. The alternative of always
    /// emitting the per-glyph operations would cut the merged-path memory and share glyphs across
    /// runs, at the cost of N commands per run; that trade favours this batching for the redraw case.
    /// </remarks>
    /// <param name="operations">Glyph operations produced by the text renderer.</param>
    /// <returns>The original operations when they cannot be combined; otherwise the combined operations.</returns>
    private List<DrawingOperation> BatchGlyphRunOperations(List<DrawingOperation> operations)
    {
        if (operations.Count < 2)
        {
            return operations;
        }

        DrawingTextCache.RunPathCacheEntry[]? fillEntries = null;
        DrawingTextCache.RunPathCacheEntry[]? drawEntries = null;
        DrawingOperation fillOperation = default;
        DrawingOperation drawOperation = default;
        int fillCount = 0;
        int drawCount = 0;

        for (int i = 0; i < operations.Count; i++)
        {
            DrawingOperation operation = operations[i];
            switch (operation.Kind)
            {
                case DrawingOperationKind.Fill:
                    if (fillEntries is null)
                    {
                        fillOperation = operation;
                        fillEntries = new DrawingTextCache.RunPathCacheEntry[operations.Count];
                    }
                    else if (!CanBatchGlyphRunOperation(fillOperation, operation))
                    {
                        // Color layers and per-glyph paint can depend on the original operation order.
                        // If any render semantics differ, keep the renderer's exact operation stream.
                        return operations;
                    }

                    // Entries are keyed relative to the run origin (the first operation's exact
                    // location including its fraction) so the cached combined path is position
                    // independent: the same run content drawn at a different location, even a
                    // fractionally scrolled one, reuses the cached geometry. The origin's snapped
                    // location and fraction stay on the combined operation.
                    Vector2 fillRelativeLocation = new Vector2(
                        operation.RenderLocation.X - fillOperation.RenderLocation.X,
                        operation.RenderLocation.Y - fillOperation.RenderLocation.Y)
                        + operation.SubPixelOffset - fillOperation.SubPixelOffset;
                    fillEntries[fillCount++] = new DrawingTextCache.RunPathCacheEntry(
                        operation.Path,
                        fillRelativeLocation,
                        operation.GlyphKey,
                        operation.HasGlyphKey);
                    break;

                case DrawingOperationKind.Draw:
                    if (drawEntries is null)
                    {
                        drawOperation = operation;
                        drawEntries = new DrawingTextCache.RunPathCacheEntry[operations.Count];
                    }
                    else if (!CanBatchGlyphRunOperation(drawOperation, operation))
                    {
                        // Color layers and per-glyph paint can depend on the original operation order.
                        // If any render semantics differ, keep the renderer's exact operation stream.
                        return operations;
                    }

                    Vector2 drawRelativeLocation = new Vector2(
                        operation.RenderLocation.X - drawOperation.RenderLocation.X,
                        operation.RenderLocation.Y - drawOperation.RenderLocation.Y)
                        + operation.SubPixelOffset - drawOperation.SubPixelOffset;
                    drawEntries[drawCount++] = new DrawingTextCache.RunPathCacheEntry(
                        operation.Path,
                        drawRelativeLocation,
                        operation.GlyphKey,
                        operation.HasGlyphKey);
                    break;
            }
        }

        int capacity = (fillEntries is null ? 0 : 1) + (drawEntries is null ? 0 : 1);
        List<DrawingOperation> batched = new(capacity);

        // The combined path is built in run-local space; the operations keep their original
        // (first-glyph) RenderLocation and SubPixelOffset so command creation positions the
        // shared geometry via the destination offset plus the sub-pixel transform, without a
        // per-position copy of the run geometry.
        if (fillEntries is not null)
        {
            fillOperation.Path = this.GetPositionedGlyphRunPath(fillEntries, fillCount);
            batched.Add(fillOperation);
        }

        if (drawEntries is not null)
        {
            drawOperation.Path = this.GetPositionedGlyphRunPath(drawEntries, drawCount);
            batched.Add(drawOperation);
        }

        return batched;
    }

    /// <summary>
    /// Returns whether two glyph operations can share one composition command.
    /// </summary>
    /// <param name="left">The first operation.</param>
    /// <param name="right">The second operation.</param>
    /// <returns><see langword="true"/> when the operations have identical drawing semantics.</returns>
    private static bool CanBatchGlyphRunOperation(DrawingOperation left, DrawingOperation right)
        => left.Kind == right.Kind
            && left.IntersectionRule == right.IntersectionRule
            && left.RenderPass == right.RenderPass
            && ReferenceEquals(left.Brush, right.Brush)
            && ReferenceEquals(left.Pen, right.Pen)
            && left.PixelAlphaCompositionMode == right.PixelAlphaCompositionMode
            && left.PixelColorBlendingMode == right.PixelColorBlendingMode;

    /// <summary>
    /// Gets a stable combined path for a uniform glyph-run operation group. Entries carry
    /// run-relative locations, so the returned path is in run-local space and one cache
    /// entry serves the same run content at every draw position.
    /// </summary>
    /// <param name="entries">The run-relative glyph path entries.</param>
    /// <param name="count">The number of entries to include in the path.</param>
    /// <returns>The combined glyph-run path in run-local space.</returns>
    private IPath GetPositionedGlyphRunPath(DrawingTextCache.RunPathCacheEntry[] entries, int count)
    {
        DrawingTextCache.RunPathCacheKey key = new(entries, count);
        if (this.textCache.TryGetRunPath(key, out IPath? path))
        {
            return path;
        }

        IPath positionedPath;
        if (count == 1)
        {
            positionedPath = GetPositionedGlyphPath(entries[0]);
        }
        else
        {
            List<IPath> paths = new(count);
            for (int i = 0; i < count; i++)
            {
                paths.Add(GetPositionedGlyphPath(entries[i]));
            }

            positionedPath = new ComplexPolygon(paths);
        }

        this.textCache.AddRunPath(key, positionedPath);
        return positionedPath;
    }

    /// <summary>
    /// Gets a glyph path translated to the entry's exact run-relative location, including the
    /// fractional component so relative sub-pixel spacing inside the run is preserved.
    /// </summary>
    /// <param name="entry">The run-relative glyph path entry.</param>
    /// <returns>The translated path.</returns>
    private static IPath GetPositionedGlyphPath(DrawingTextCache.RunPathCacheEntry entry)
    {
        Vector2 relativeLocation = entry.RelativeLocation;
        return relativeLocation == Vector2.Zero
            ? entry.Path
            : entry.Path.Translate(relativeLocation.X, relativeLocation.Y);
    }

    /// <summary>
    /// Converts rendered text operations to composition commands and submits them to the batcher.
    /// </summary>
    /// <param name="operations">Text drawing operations produced by glyph layout/rendering.</param>
    /// <param name="drawingOptions">Drawing options applied to each operation.</param>
    private void DrawTextOperations(List<DrawingOperation> operations, DrawingOptions drawingOptions)
    {
        // Build composition commands and enforce render-pass ordering while preserving
        // original emission order inside each pass. This preserves overlapping color-font
        // layer compositing semantics (for example emoji mouth/teeth layers).
        // The cache-owned buffer keeps its capacity across draws; draw calls never overlap on
        // one canvas, and the batcher retains the commands, not this buffer.
        List<(byte RenderPass, int Sequence, CompositionSceneCommand Command)> entries = this.textCommandSortBuffer;
        entries.Clear();

        // Queued glyph commands never carry the canvas transform: glyph geometry arrives with
        // it already applied, and the sub-pixel remainder rides the command itself. One shared
        // identity-transform options instance therefore serves every operation of this draw;
        // per-operation options exist only for glyphs whose blend or composition modes differ.
        DrawingOptions sharedTextOptions = drawingOptions.Transform.IsIdentity && drawingOptions.IntersectionRule == IntersectionRule.NonZero
            ? drawingOptions
            : new DrawingOptions(drawingOptions.GraphicsOptions, IntersectionRule.NonZero, Matrix4x4.Identity, drawingOptions.TextContrast);

        for (int i = 0; i < operations.Count; i++)
        {
            DrawingOperation operation = operations[i];
            entries.Add((operation.RenderPass, i, this.CreateTextCompositionCommand(operation, drawingOptions, sharedTextOptions)));
        }

        entries.Sort(static (a, b) =>
        {
            int cmp = a.RenderPass.CompareTo(b.RenderPass);
            return cmp != 0 ? cmp : a.Sequence.CompareTo(b.Sequence);
        });

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Command is PathCompositionSceneCommand pathCommand)
            {
                this.batcher.AddComposition(pathCommand.Command);
            }
            else
            {
                this.batcher.AddStrokePath(((StrokePathCompositionSceneCommand)entries[i].Command).Command);
            }
        }

        // The buffer outlives the canvas (the text cache hosts it), so release the command
        // references now rather than rooting the final draw's geometry until the next draw.
        entries.Clear();
    }

    /// <summary>
    /// Resolves the currently active drawing state.
    /// </summary>
    /// <returns>The current state.</returns>
    private DrawingCanvasState ResolveState() => this.savedStates.Peek();

    /// <summary>
    /// Ensures text drawing has at least one paint source.
    /// </summary>
    /// <param name="brush">Optional fill brush.</param>
    /// <param name="pen">Optional outline pen.</param>
    private static void EnsureTextPaint(Brush? brush, Pen? pen)
    {
        if (brush is null && pen is null)
        {
            throw new ArgumentException($"Expected a {nameof(brush)} or {nameof(pen)}. Both were null");
        }
    }

    /// <summary>
    /// Executes an action with a temporary scoped state, restoring the previous scoped state afterwards.
    /// </summary>
    /// <param name="options">Temporary drawing options.</param>
    /// <param name="clipState">The normalized clip state used by the temporary state.</param>
    /// <param name="action">Action to execute.</param>
    private void ExecuteWithTemporaryState(
        DrawingOptions options,
        DrawingClipState clipState,
        Action action)
    {
        this.EnsureNotDisposed();

        int saveCount = this.savedStates.Count;
        DrawingCanvasState current = this.ResolveState();
        this.savedStates.Push(new DrawingCanvasState(options, clipState, current.TargetBounds, current.DestinationOffset)
        {
            IsLayer = current.IsLayer,
            Layer = current.Layer,
        });

        try
        {
            action();
        }
        finally
        {
            this.RestoreTo(saveCount);
        }
    }

    /// <inheritdoc />
    public override void Flush()
    {
        this.EnsureNotDisposed();
        this.CloseClipsAndSealActiveCommandRange();
        DrawingCanvasState state = this.ResolveState();
        this.AppendBeginClips(state.ClipState, state.DestinationOffset);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        try
        {
            // Dispose should finalize the same drawing state transitions as RestoreTo(1),
            // otherwise active layers can composite with different options than an explicit restore.
            this.RestoreToCore(1);

            if (this.ownsBatcher)
            {
                this.AppendEndClips(this.ResolveState().ClipState.Count);
                this.RenderRecordedTimeline();
            }
        }
        finally
        {
            if (this.ownsBatcher)
            {
                this.DisposePendingImageResources();
            }

            if (this.ownsTextCache)
            {
                this.textCache.Clear();
            }

            this.isDisposed = true;
        }
    }

    /// <summary>
    /// Ensures this instance is not disposed.
    /// </summary>
    private void EnsureNotDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

    /// <summary>
    /// Renders the recorded timeline owned by the root canvas.
    /// </summary>
    /// <remarks>
    /// Command-range entries are lowered to short-lived backend scenes here. Scene entries
    /// reference retained scenes that were recorded earlier through <see cref="DrawingCanvas.RenderScene"/>.
    /// </remarks>
    private void RenderRecordedTimeline()
    {
        if (!this.batcher.HasRecordedWork)
        {
            return;
        }

        this.batcher.SealAndPrepareCommands();
        try
        {
            for (int i = 0; i < this.batcher.TimelineEntryCount; i++)
            {
                DrawingCanvasTimelineEntry entry = this.batcher.GetEntry(i);
                switch (entry.Kind)
                {
                    case DrawingCanvasTimelineEntryKind.CommandRange:
                        this.RenderCommandBatch(this.batcher.CreateCommandBatch(entry));
                        break;

                    case DrawingCanvasTimelineEntryKind.Scene:
                        this.backend.RenderScene(
                            this.configuration,
                            this.targetFrame,
                            this.batcher.GetInsertedScene(entry.Index));

                        break;
                }
            }
        }
        finally
        {
            this.batcher.ClearCommandBatch();
        }
    }

    /// <summary>
    /// Creates and renders one backend scene for a prepared command batch.
    /// </summary>
    /// <param name="commandBatch">The command batch to render.</param>
    private void RenderCommandBatch(DrawingCommandBatch commandBatch)
    {
        using DrawingBackendScene scene = this.backend.CreateScene(
            this.configuration,
            this.targetFrame.Bounds,
            commandBatch);

        this.backend.RenderScene(this.configuration, this.targetFrame, scene);
    }

    /// <summary>
    /// Restores the saved-state stack to <paramref name="saveCount"/> without public guard checks.
    /// Layer states are unwound through the normal compositing path so restore and disposal
    /// preserve identical layer semantics.
    /// </summary>
    /// <param name="saveCount">The target stack depth to restore to.</param>
    private void RestoreToCore(int saveCount)
    {
        while (this.savedStates.Count > saveCount)
        {
            this.FlushLayerEffect();
            DrawingCanvasState popped = this.savedStates.Pop();
            DrawingCanvasState current = this.ResolveState();
            this.AppendEndClips(popped.ClipState.Count - current.ClipState.Count);

            if (popped.IsLayer)
            {
                // Restore and Dispose unwind layers through the same command stream path.
                this.batcher.AddComposition(CompositionCommand.CreateEndLayer(popped.TargetBounds, popped.Layer!));
            }
        }
    }

    /// <summary>
    /// Records the current state's pending layer effect, if any, as an apply barrier while the
    /// layer is still receiving commands, so the effect transforms the layer's content just before
    /// the layer is composited.
    /// </summary>
    private void FlushLayerEffect()
    {
        DrawingCanvasState current = this.ResolveState();
        if (current.LayerEffect is DrawingCanvasLayerEffect pending)
        {
            current.LayerEffect = null;
            if (pending.Effect.IsPassThrough)
            {
                return;
            }

            this.ApplyCore(
                pending.Region,
                pending.Effect.CreateOperation(),
                pending.Effect,
                pending.Effect.WriteBackOptions,
                pending.Effect.WriteBackOffset);
        }
    }

    /// <summary>
    /// Seals the current command range with the active clip stack balanced inside that range.
    /// </summary>
    private void CloseClipsAndSealActiveCommandRange()
    {
        DrawingCanvasState state = this.ResolveState();

        // Backend scenes are created per sealed command range. A Vello-style clip stream cannot
        // span those scene boundaries, so the canvas closes the active suffix before sealing and
        // the caller reopens it for later commands.
        this.AppendEndClips(state.ClipState.Count);
        this.batcher.SealCommands();
    }

    /// <summary>
    /// Appends begin-clip commands for the supplied clip state.
    /// </summary>
    /// <param name="clipState">The clip state to open.</param>
    /// <param name="destinationOffset">The destination offset associated with the clip state.</param>
    private void AppendBeginClips(DrawingClipState clipState, Point destinationOffset)
    {
        for (int i = 0; i < clipState.Count; i++)
        {
            DrawingClipDescriptor descriptor = clipState.GetDescriptor(i);
            this.batcher.AddComposition(CompositionCommand.CreateBeginClip(descriptor, destinationOffset));
        }
    }

    /// <summary>
    /// Appends end-clip commands for a previously opened clip-state suffix.
    /// </summary>
    /// <param name="count">The number of clip scopes to close.</param>
    private void AppendEndClips(int count)
    {
        for (int i = 0; i < count; i++)
        {
            this.batcher.AddComposition(CompositionCommand.CreateEndClip());
        }
    }

    /// <summary>
    /// Normalizes text options to avoid applying origin translation twice when path-based text is used.
    /// </summary>
    /// <param name="options">Input text options.</param>
    /// <param name="path">Optional path to draw the text along.</param>
    /// <param name="configuredPath">The path translated into text layout space when needed.</param>
    /// <returns>Normalized text options for rendering.</returns>
    private static RichTextOptions ConfigureTextOptions(RichTextOptions options, IPath? path, out IPath? configuredPath)
    {
        configuredPath = path;

        if (path is not null && options.Origin != Vector2.Zero)
        {
            // Path-based text uses the path itself as positioning source; fold origin into the path
            // to avoid applying both path layout and origin translation.
            configuredPath = path.Translate(options.Origin);
            return new RichTextOptions(options)
            {
                Origin = Vector2.Zero
            };
        }

        return options;
    }

    /// <summary>
    /// Computes the visible text-space bounds for the current canvas state when the effective
    /// transform is a pure translation. The state's target bounds already fold the target size
    /// and every conservative clip bound together, so translating them into text space yields
    /// the band the layout engine culls against.
    /// </summary>
    /// <param name="state">The resolved canvas state.</param>
    /// <param name="transform">The effective drawing transform for the text.</param>
    /// <param name="visibleBounds">The visible bounds in text space.</param>
    /// <returns>
    /// <see langword="true"/> when the transform is a pure translation and the bounds are
    /// usable; otherwise <see langword="false"/>. Culling is a fast path only, so rotation,
    /// scale, or skew renders unculled rather than risking incorrect rejection.
    /// </returns>
    private static bool TryGetVisibleTextBounds(DrawingCanvasState state, in Matrix4x4 transform, out FontRectangle visibleBounds)
    {
        if (!MatrixUtilities.IsTranslationOnly(transform))
        {
            visibleBounds = default;
            return false;
        }

        // Target bounds are absolute while glyph geometry is recorded in local canvas
        // coordinates and shifted by the destination offset at command creation, so the
        // text-space band is the target rectangle pulled back through both translations.
        Rectangle target = state.TargetBounds;
        visibleBounds = new FontRectangle(
            target.X - state.DestinationOffset.X - transform.M41,
            target.Y - state.DestinationOffset.Y - transform.M42,
            target.Width,
            target.Height);

        return true;
    }

    /// <summary>
    /// Builds a normalized composition command for a text drawing operation.
    /// </summary>
    /// <param name="operation">The source drawing operation.</param>
    /// <param name="drawingOptions">Drawing options applied to the operation.</param>
    /// <param name="sharedTextOptions">
    /// The identity-transform options shared by every operation of the draw whose graphics
    /// options match the canvas options.
    /// </param>
    /// <returns>A composition scene command ready for batching.</returns>
    private CompositionSceneCommand CreateTextCompositionCommand(DrawingOperation operation, DrawingOptions drawingOptions, DrawingOptions sharedTextOptions)
    {
        Brush compositeBrush = operation.Kind == DrawingOperationKind.Fill
            ? operation.Brush!
            : operation.Pen!.StrokeFill;

        GraphicsOptions graphicsOptions =
            drawingOptions.GraphicsOptions.CloneOrReturnForRules(
                operation.PixelAlphaCompositionMode,
                operation.PixelColorBlendingMode);

        RasterizationMode rasterizationMode = graphicsOptions.Antialias
            ? RasterizationMode.Antialiased
            : RasterizationMode.Aliased;

        // Glyph outlines (fills and strokes) are always non-zero winding; even-odd punches holes where a
        // glyph's contours overlap. Force non-zero on both rule carriers - the rasterizer options and the
        // drawing options - so neither the per-operation rule nor the canvas's even-odd default applies.
        const IntersectionRule intersectionRule = IntersectionRule.NonZero;

        DrawingCanvasState state = this.ResolveState();
        Point destinationOffset = new(
            state.DestinationOffset.X + operation.RenderLocation.X,
            state.DestinationOffset.Y + operation.RenderLocation.Y);

        // The whole-pixel part of the glyph position rides the integer destination offset;
        // the fractional remainder rides the command's dedicated sub-pixel field. The
        // composed transform keeps unit scale, so the backends reuse the scale-keyed
        // flattened geometry and apply the fraction as a residual, rendering the glyph at
        // its exact sub-pixel position with one cached path per glyph.
        Vector2 subPixelOffset = operation.SubPixelOffset;

        Pen? pen = operation.Kind == DrawingOperationKind.Draw ? operation.Pen : null;

        RectangleF bounds = operation.Path.Bounds;
        bounds.Offset(subPixelOffset.X, subPixelOffset.Y);
        if (pen is not null)
        {
            float halfWidth = pen.StrokeWidth * 0.5F;
            float joinInflate = pen.StrokeOptions.LineJoin switch
            {
                LineJoin.Miter or LineJoin.MiterRevert or LineJoin.MiterRound => (float)(halfWidth * Math.Max(pen.StrokeOptions.MiterLimit, 1D)),
                _ => halfWidth
            };

            float capInflate = pen.StrokeOptions.LineCap == LineCap.Square
                ? halfWidth * MathF.Sqrt(2F)
                : halfWidth;

            float inflate = MathF.Max(joinInflate, capInflate);

            bounds.Inflate(new SizeF(inflate, inflate));
        }

        Rectangle interest = ToRasterizerInterest(bounds);

        // Text opts into the perceptual coverage boost here; generic vector fills and strokes
        // never carry one. The boost only applies to antialiased coverage.
        float coverageBoost = rasterizationMode == RasterizationMode.Antialiased
            ? Math.Clamp(drawingOptions.TextContrast, 0F, 1F)
            : 0F;

        RasterizerOptions rasterizerOptions = new(
            interest,
            intersectionRule,
            rasterizationMode,
            graphicsOptions.AntialiasThreshold,
            coverageBoost);

        // Glyph paths arrive pre-laid-out, so the queued command carries identity-transform
        // options and reports the fraction through its sub-pixel field. The shared instance
        // serves every operation whose graphics options match the canvas; a fresh instance
        // exists only for the rare operation whose blend or composition modes forced a clone.
        DrawingOptions effectiveOptions = ReferenceEquals(graphicsOptions, drawingOptions.GraphicsOptions)
            ? sharedTextOptions
            : new DrawingOptions(graphicsOptions, intersectionRule, Matrix4x4.Identity, drawingOptions.TextContrast);

        // Clipping is resolved from the ordered begin/end-clip stream, which the canvas anchors
        // at the state's destination offset. Glyph render locations only move the glyph geometry;
        // they never re-anchor the clip stack, so no per-operation clip translation is required.
        if (pen is null)
        {
            return new PathCompositionSceneCommand(
                CompositionCommand.Create(
                    operation.Path,
                    compositeBrush,
                    effectiveOptions,
                    in rasterizerOptions,
                    state.TargetBounds,
                    destinationOffset,
                    state.Layer,
                    subPixelOffset));
        }

        return new StrokePathCompositionSceneCommand(
            new StrokePathCommand(
                operation.Path,
                compositeBrush,
                effectiveOptions,
                in rasterizerOptions,
                state.TargetBounds,
                destinationOffset,
                pen,
                state.Layer is not null,
                state.Layer,
                subPixelOffset));
    }

    /// <summary>
    /// Converts floating bounds to a conservative integer rectangle using floor/ceiling.
    /// </summary>
    /// <param name="bounds">The floating bounds to convert.</param>
    /// <returns>A rectangle covering the full floating bounds extent.</returns>
    private static Rectangle ToConservativeBounds(RectangleF bounds)
        => Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));

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
    /// Returns the transform scale used when converting stroke width to raster bounds.
    /// </summary>
    /// <param name="transform">The drawing transform.</param>
    /// <returns>The stroke width scale.</returns>
    private static float GetTransformWidthScale(Matrix4x4 transform)
    {
        if (transform.IsIdentity)
        {
            return 1F;
        }

        float det = (transform.M11 * transform.M22) - (transform.M12 * transform.M21);
        return MathF.Sqrt(MathF.Abs(det));
    }

    /// <summary>
    /// Resolves local layer bounds to absolute target bounds using the supplied transform.
    /// </summary>
    /// <param name="transform">The transform active for the layer state.</param>
    /// <param name="targetBounds">The absolute target bounds to clip against.</param>
    /// <param name="destinationOffset">Absolute destination offset for local canvas coordinates.</param>
    /// <param name="bounds">The layer bounds in local canvas coordinates.</param>
    /// <returns>The absolute layer bounds clipped to the active target.</returns>
    private static Rectangle ResolveLayerBounds(Matrix4x4 transform, Rectangle targetBounds, Point destinationOffset, Rectangle bounds)
    {
        RectangleF transformedBounds = bounds;
        if (!transform.IsIdentity)
        {
            transformedBounds = RectangleF.Transform(transformedBounds, transform);
        }

        Rectangle localLayerBounds = ToConservativeBounds(transformedBounds);
        Rectangle absoluteLayerBounds = new(
            destinationOffset.X + localLayerBounds.X,
            destinationOffset.Y + localLayerBounds.Y,
            localLayerBounds.Width,
            localLayerBounds.Height);

        return Rectangle.Intersect(targetBounds, absoluteLayerBounds);
    }

    /// <summary>
    /// Creates resize options used for image drawing operations.
    /// </summary>
    /// <param name="size">Requested output size.</param>
    /// <param name="sampler">Optional resampler. Defaults to bicubic.</param>
    /// <returns>A resize options instance configured for stretch behavior.</returns>
    private static ResizeOptions CreateDrawImageResizeOptions(Size size, IResampler? sampler)
        => new()
        {
            Size = size,
            Mode = ResizeMode.Stretch,
            Sampler = sampler ?? KnownResamplers.Bicubic
        };

    /// <summary>
    /// Creates a scaled image for drawing, optionally cropping to a source region first.
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <param name="clippedSourceRect">The clipped source rectangle.</param>
    /// <param name="scaledSize">The target scaled size.</param>
    /// <param name="sampler">Optional resampler used for scaling.</param>
    /// <returns>A new image containing the scaled pixels.</returns>
    private static Image<TPixel> CreateScaledDrawImage(
        Image<TPixel> image,
        Rectangle clippedSourceRect,
        Size scaledSize,
        IResampler? sampler)
    {
        ResizeOptions effectiveResizeOptions = CreateDrawImageResizeOptions(scaledSize, sampler);
        if (clippedSourceRect == image.Bounds)
        {
            return image.Clone(ctx => ctx.Resize(effectiveResizeOptions));
        }

        Image<TPixel> result = image.Clone(ctx => ctx.Crop(clippedSourceRect));
        result.Mutate(ctx => ctx.Resize(effectiveResizeOptions));
        return result;
    }

    /// <summary>
    /// Applies a transform to image content and returns the transformed image.
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <param name="destinationRect">Destination rectangle in canvas coordinates.</param>
    /// <param name="transform">Canvas transform to apply.</param>
    /// <param name="sampler">Optional resampler used during transform.</param>
    /// <param name="transformedDestinationRect">Receives the transformed destination bounds.</param>
    /// <returns>A new image containing transformed pixels.</returns>
    private static Image<TPixel> CreateTransformedDrawImage(
        Image<TPixel> image,
        RectangleF destinationRect,
        Matrix4x4 transform,
        IResampler? sampler,
        out RectangleF transformedDestinationRect)
    {
        // Source space: pixel coordinates in the untransformed source image (0..Width, 0..Height).
        // Destination space: where that image would land on the canvas without any extra transform.
        // This matrix maps source -> destination by scaling to destination size then translating to destination origin.
        Matrix4x4 sourceToDestination = Matrix4x4.CreateScale(destinationRect.Width / image.Width, destinationRect.Height / image.Height, 1)
            * Matrix4x4.CreateTranslation(destinationRect.X, destinationRect.Y, 0);

        // Apply the canvas transform after source->destination placement:
        // source -> destination -> transformed-canvas.
        Matrix4x4 sourceToTransformedCanvas = sourceToDestination * transform;

        // Compute the transformed axis-aligned bounds in canvas space.
        RectangleF transformedBounds = RectangleF.Transform(new RectangleF(0, 0, image.Width, image.Height), sourceToTransformedCanvas);

        // ImageBrush samples against integer pixel locations. Align the baked bitmap to integer
        // canvas bounds so the bitmap origin and brush sampling origin agree exactly.
        int alignedLeft = (int)MathF.Floor(transformedBounds.Left);
        int alignedTop = (int)MathF.Floor(transformedBounds.Top);
        int alignedRight = (int)MathF.Ceiling(transformedBounds.Right);
        int alignedBottom = (int)MathF.Ceiling(transformedBounds.Bottom);

        transformedDestinationRect = RectangleF.FromLTRB(
            alignedLeft,
            alignedTop,
            alignedRight,
            alignedBottom);

        Size targetSize = new(
            Math.Max(1, alignedRight - alignedLeft),
            Math.Max(1, alignedBottom - alignedTop));

        // ImageSharp.Transform expects output coordinates relative to the output bitmap origin (0,0).
        // Shift transformed-canvas coordinates so the aligned integer canvas bounds become 0,0.
        Matrix4x4 sourceToTarget = sourceToTransformedCanvas
            * Matrix4x4.CreateTranslation(-alignedLeft, -alignedTop, 0);

        // Resample source pixels into the target bitmap using the computed source->target mapping.
        return image.Clone(ctx => ctx.Transform(
            image.Bounds,
            sourceToTarget,
            targetSize,
            sampler ?? KnownResamplers.Bicubic));
    }

    /// <summary>
    /// Computes the source and destination rectangles that a draw-image operation will actually
    /// touch, clipping the requested source rectangle to the image bounds.
    /// </summary>
    /// <param name="sourceRect">Requested source rectangle.</param>
    /// <param name="destinationRect">Requested destination rectangle.</param>
    /// <param name="imageBounds">Bounds of the source image.</param>
    /// <param name="clippedSourceRect">Receives the source rectangle clipped to <paramref name="imageBounds"/>.</param>
    /// <param name="clippedDestinationRect">Receives the destination rectangle matching <paramref name="clippedSourceRect"/>.</param>
    /// <returns><see langword="true"/> when the operation covers a non-empty region; otherwise <see langword="false"/>.</returns>
    private static bool TryGetDrawImageClip(
        Rectangle sourceRect,
        RectangleF destinationRect,
        Rectangle imageBounds,
        out Rectangle clippedSourceRect,
        out RectangleF clippedDestinationRect)
    {
        clippedSourceRect = default;
        clippedDestinationRect = default;

        // A zero-area source cannot be sampled and would divide by zero when mapping to the destination.
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
        {
            return false;
        }

        clippedSourceRect = Rectangle.Intersect(sourceRect, imageBounds);
        if (clippedSourceRect.Width <= 0 || clippedSourceRect.Height <= 0)
        {
            return false;
        }

        clippedDestinationRect = MapSourceClipToDestination(sourceRect, destinationRect, clippedSourceRect);

        // A degenerate (empty or inverted) destination maps to nothing to draw.
        return clippedDestinationRect.Width > 0 && clippedDestinationRect.Height > 0;
    }

    /// <summary>
    /// Maps a clipped source rectangle back to the corresponding destination rectangle.
    /// </summary>
    /// <param name="sourceRect">Original source rectangle.</param>
    /// <param name="destinationRect">Original destination rectangle.</param>
    /// <param name="clippedSourceRect">Source rectangle clipped to image bounds.</param>
    /// <returns>The destination rectangle corresponding to the clipped source region.</returns>
    private static RectangleF MapSourceClipToDestination(
        Rectangle sourceRect,
        RectangleF destinationRect,
        Rectangle clippedSourceRect)
    {
        float scaleX = destinationRect.Width / sourceRect.Width;
        float scaleY = destinationRect.Height / sourceRect.Height;

        float left = destinationRect.Left + ((clippedSourceRect.Left - sourceRect.Left) * scaleX);
        float top = destinationRect.Top + ((clippedSourceRect.Top - sourceRect.Top) * scaleY);
        float width = clippedSourceRect.Width * scaleX;
        float height = clippedSourceRect.Height * scaleY;

        return new RectangleF(left, top, width, height);
    }

    /// <summary>
    /// Creates a normalized clip state in the same coordinate space as recorded commands.
    /// </summary>
    /// <param name="clipPaths">Clip paths from the active canvas state.</param>
    /// <param name="transform">Canvas transform to apply to the clip state.</param>
    /// <param name="operation">The operation used to combine the paths with the existing clip.</param>
    /// <param name="edgeMode">The clip edge mode.</param>
    /// <param name="antialiasThreshold">The coverage threshold used for hard clip edges.</param>
    /// <returns>The transformed clip state.</returns>
    private static DrawingClipState CreateClipState(
        IPath[] clipPaths,
        Matrix4x4 transform,
        ClipOperation operation,
        DrawingClipEdgeMode edgeMode,
        float antialiasThreshold)
    {
        DrawingClipState clipState = DrawingClipState.FromPaths(
            clipPaths,
            operation,
            edgeMode,
            antialiasThreshold);

        // Transform after descriptor creation. DrawingClipDescriptor.Transform has the
        // rectangle/region-specific logic needed to preserve cheap clip primitives.
        return clipState.Transform(transform);
    }

    /// <summary>
    /// Disposes image resources retained for deferred draw execution.
    /// </summary>
    private void DisposePendingImageResources()
    {
        if (this.pendingImageResources.Count == 0)
        {
            return;
        }

        // Release deferred image resources once queued operations have executed.
        for (int i = 0; i < this.pendingImageResources.Count; i++)
        {
            this.pendingImageResources[i].Dispose();
        }

        this.pendingImageResources.Clear();
    }

    /// <summary>
    /// Transfers pending image resources to a retained scene.
    /// </summary>
    /// <returns>The resources that must remain alive for the retained scene, or <see langword="null"/> when none exist.</returns>
    private IDisposable[]? DetachPendingImageResources()
    {
        if (this.pendingImageResources.Count == 0)
        {
            return null;
        }

        IDisposable[] resources = new IDisposable[this.pendingImageResources.Count];

        for (int i = 0; i < this.pendingImageResources.Count; i++)
        {
            resources[i] = this.pendingImageResources[i];
        }

        this.pendingImageResources.Clear();
        return resources;
    }

    /// <summary>
    /// Disposes resources that failed to transfer to a retained scene.
    /// </summary>
    /// <param name="resources">The resources to dispose.</param>
    private static void DisposeOwnedResources(IDisposable[]? resources)
    {
        if (resources is null)
        {
            return;
        }

        for (int i = 0; i < resources.Length; i++)
        {
            resources[i].Dispose();
        }
    }
}
