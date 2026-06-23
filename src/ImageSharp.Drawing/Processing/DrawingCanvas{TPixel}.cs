// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.PolygonGeometry;
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
            new DrawingCanvasState(options, clipPaths, options.ShapeOptions.IntersectionRule, targetFrame.Bounds, targetFrame.Bounds.Location),
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
        this.ownsBatcher = ownsBatcher;
        this.ownsTextCache = ownsTextCache;

        // Canvas coordinates are local to the current frame; origin stays at (0,0).
        this.Bounds = new Rectangle(0, 0, targetFrame.Bounds.Width, targetFrame.Bounds.Height);
        this.savedStates.Push(defaultState);
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
        this.savedStates.Push(new DrawingCanvasState(current.Options, current.ClipPaths, current.ClipIntersectionRule, current.TargetBounds, current.DestinationOffset)
        {
            Layer = current.Layer
        });

        return this.savedStates.Count;
    }

    /// <inheritdoc />
    public override int Save(DrawingOptions options, params IPath[] clipPaths)
        => this.SaveCore(options, clipPaths);

    private int SaveCore(DrawingOptions options, IPath[] clipPaths)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));

        _ = this.Save();
        DrawingCanvasState current = this.ResolveState();

        // Save snapshots the current state: with no clip paths it inherits the existing clip;
        // the transform is changed separately. Explicit clip paths are transformed into the active
        // space once and set as this state's clip, so later draws never re-transform their own copy.
        IReadOnlyList<IPath> clips = clipPaths.Length == 0
            ? current.ClipPaths
            : TransformClipPaths(clipPaths, options.Transform);

        IntersectionRule clipIntersectionRule = clipPaths.Length == 0
            ? current.ClipIntersectionRule
            : options.ShapeOptions.IntersectionRule;

        DrawingCanvasState state = new(options, clips, clipIntersectionRule, current.TargetBounds, current.DestinationOffset)
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
        return this.SaveLayerCore(layerOptions, bounds, currentState.Options, currentState.ClipPaths, currentState.ClipIntersectionRule);
    }

    /// <inheritdoc />
    public override int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds, DrawingOptions options, params IPath[] clipPaths)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(layerOptions, nameof(layerOptions));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));
        Guard.MustBeGreaterThan(bounds.Width, 0, nameof(bounds));
        Guard.MustBeGreaterThan(bounds.Height, 0, nameof(bounds));

        DrawingCanvasState currentState = this.ResolveState();
        IReadOnlyList<IPath> clips = clipPaths.Length == 0
            ? currentState.ClipPaths
            : TransformClipPaths(clipPaths, options.Transform);

        IntersectionRule clipIntersectionRule = clipPaths.Length == 0
            ? currentState.ClipIntersectionRule
            : options.ShapeOptions.IntersectionRule;

        return this.SaveLayerCore(layerOptions, bounds, options, clips, clipIntersectionRule);
    }

    /// <summary>
    /// Pushes a layer state with already-resolved drawing options and clip paths.
    /// </summary>
    /// <param name="layerOptions">Graphics options used when compositing the closed layer.</param>
    /// <param name="bounds">Layer bounds in local canvas coordinates.</param>
    /// <param name="options">Drawing options for commands recorded into the layer.</param>
    /// <param name="clipPaths">Clip paths for commands recorded into the layer.</param>
    /// <param name="clipIntersectionRule">The fill rule used to interpret the clip paths.</param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    private int SaveLayerCore(
        GraphicsOptions layerOptions,
        Rectangle bounds,
        DrawingOptions options,
        IReadOnlyList<IPath> clipPaths,
        IntersectionRule clipIntersectionRule)
    {
        DrawingCanvasState currentState = this.ResolveState();
        Rectangle absoluteLayerBounds = ResolveLayerBounds(options.Transform, currentState.TargetBounds, currentState.DestinationOffset, bounds);
        DrawingCanvasLayer layer = new(layerOptions);

        // Keep layer boundaries in the shared command stream so the backend can lower them inline.
        this.batcher.AddComposition(CompositionCommand.CreateBeginLayer(absoluteLayerBounds, layer));

        // A bounded layer clips and allocates the isolated target, but it does not shift the canvas coordinate system.
        DrawingCanvasState layerState = new(options, clipPaths, clipIntersectionRule, absoluteLayerBounds, currentState.DestinationOffset)
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

        DrawingCanvasState popped = this.savedStates.Pop();
        if (popped.IsLayer)
        {
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
            new DrawingCanvasState(currentState.Options, currentState.ClipPaths, currentState.ClipIntersectionRule, childFrame.Bounds, childFrame.Bounds.Location)
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
        this.ExecuteWithTemporaryState(options, state.ClipPaths, state.ClipIntersectionRule, () => this.Fill(brush, path));
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

        // Clip paths are supplied in the active coordinate space; transform them into the clip
        // space once, here, so the stored clip never has to be re-transformed by later draws.
        Matrix4x4 transform = state.Options.Transform;
        IPath[] transformed = TransformClipPaths(clipPaths, transform);

        // The current shape rule belongs to the incoming clip geometry, not to future draw
        // subjects. Keep it separate from the stored clip rule because each side of a later
        // boolean operation can come from a different canvas state.
        IntersectionRule incomingRule = state.Options.ShapeOptions.IntersectionRule;

        IReadOnlyList<IPath> combined;
        IntersectionRule combinedRule;
        Rectangle targetBounds = state.TargetBounds;
        if (operation == ClipOperation.Intersection && state.ClipPaths.Count == 0)
        {
            if (transformed.Length == 1 && TryGetRectangleClip(transformed[0], state.DestinationOffset, out Rectangle rectangleClip))
            {
                // A first integer rectangle clip is equivalent to shrinking the command target.
                // Keeping it in TargetBounds preserves hard pixel edges and avoids attaching a
                // path clip to every later draw recorded in this state.
                targetBounds = Rectangle.Intersect(state.TargetBounds, rectangleClip);
                combined = [];
                combinedRule = incomingRule;
            }

            // With no stored clip, an intersection clip becomes the stored clip. If there is
            // only one incoming path, keep it as-is so region paths retain their exact metadata.
            // Multiple paths in one Clip call are one incoming clip region, so they must be
            // unioned before storing; otherwise later code would have to remember that this list
            // is one unioned clip operand rather than several sequential clips.
            else if (transformed.Length == 1)
            {
                combined = transformed;
                combinedRule = incomingRule;
            }
            else
            {
                IPath[] rest = new IPath[transformed.Length - 1];
                for (int i = 1; i < transformed.Length; i++)
                {
                    rest[i - 1] = transformed[i];
                }

                ShapeOptions unionOptions = new()
                {
                    BooleanOperation = BooleanOperation.Union,
                    IntersectionRule = incomingRule
                };

                combined = [ClippedShapeGenerator.GenerateClippedShapes(unionOptions, transformed[0], rest)];
                combinedRule = incomingRule;
            }
        }
        else if (operation == ClipOperation.Intersection
            && transformed.Length == 1
            && TryIntersectRegionClips(state.ClipPaths, transformed[0], out IPath regionClip))
        {
            // Two integer region clips can be intersected exactly as rect sets. Region clips
            // represent device-pixel coverage, so keeping them as rectangles preserves their
            // hard edges and avoids lowering a simple dirty-region clip through polygon clipping.
            combined = [regionClip];
            combinedRule = IntersectionRule.NonZero;
        }
        else
        {
            // Every remaining case needs a stored geometric clip:
            //
            // - Intersection with an existing clip:
            //   existing ∩ union(incoming paths)
            //
            // - Difference with or without an existing clip:
            //   existing - union(incoming paths)
            //
            // When no clip path has been stored, the subject side is the current target
            // bounds. Target bounds are absolute, while clip paths are stored in the
            // current canvas destination space. Translate the implicit subject rectangle
            // back to local coordinates before handing it to the path clipper.
            //
            // ClippedShapeGenerator accepts multiple clip paths and lowers them as one clip
            // operand. Passing the incoming paths directly keeps Difference to one polygon clip
            // operation instead of unioning first and clipping the unioned result again.
            RectangleF localTargetBounds = new(
                state.TargetBounds.X - state.DestinationOffset.X,
                state.TargetBounds.Y - state.DestinationOffset.Y,
                state.TargetBounds.Width,
                state.TargetBounds.Height);

            IPath existing = state.ClipPaths.Count == 0
                ? new RectanglePolygon(localTargetBounds)
                : state.ClipPaths.Count == 1
                    ? state.ClipPaths[0]
                    : new ComplexPolygon(state.ClipPaths);

            // The subject side is either the full target rectangle or the previously stored clip.
            // That rule is independent from incomingRule, which belongs only to the new clip paths.
            IntersectionRule operationRule = state.ClipPaths.Count == 0
                ? IntersectionRule.NonZero
                : state.ClipIntersectionRule;

            ShapeOptions operationOptions = new()
            {
                BooleanOperation = operation == ClipOperation.Intersection
                    ? BooleanOperation.Intersection
                    : BooleanOperation.Difference,
                IntersectionRule = operationRule
            };

            combined =
            [
                ClippedShapeGenerator.GenerateClippedShapes(operationOptions, existing, transformed, incomingRule)
            ];

            combinedRule = operation == ClipOperation.Intersection
                ? incomingRule
                : operationRule;
        }

        this.savedStates.Push(new DrawingCanvasState(state.Options, combined, combinedRule, targetBounds, state.DestinationOffset)
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
            state.ClipPaths,
            state.ClipIntersectionRule,
            this.Bounds,
            state.TargetBounds,
            state.DestinationOffset,
            state.Layer,
            operation);

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

        if (state.ClipPaths.Count > 0 || !pen.StrokePattern.IsEmpty)
        {
            this.PrepareCompositionCore(
                new Path([start, end]),
                pen.StrokeFill,
                effectiveOptions,
                state.ClipPaths,
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

        if (state.ClipPaths.Count > 0 || !pen.StrokePattern.IsEmpty)
        {
            this.PrepareCompositionCore(
                new Path(points),
                pen.StrokeFill,
                effectiveOptions,
                state.ClipPaths,
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
            state.ClipPaths,
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
        using RichTextGlyphRenderer glyphRenderer = new(effectiveOptions, configuredPath, pen, brush, this.textCache);
        TextRenderer renderer = new(glyphRenderer);
        renderer.RenderText(text, configuredOptions);

        this.DrawTextOperations(glyphRenderer.DrawingOperations, effectiveOptions, state.ClipPaths, state.ClipIntersectionRule);
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
            effectiveOptions.ShapeOptions,
            Matrix4x4.CreateTranslation(location.X, location.Y, 0) * effectiveOptions.Transform);

        using RichTextGlyphRenderer glyphRenderer = new(placedOptions, path: null, pen, brush, this.textCache);
        textBlock.RenderTo(glyphRenderer, wrappingLength);

        this.DrawTextOperations(glyphRenderer.DrawingOperations, placedOptions, state.ClipPaths, state.ClipIntersectionRule);
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

        using RichTextGlyphRenderer glyphRenderer = new(effectiveOptions, path, pen, brush, this.textCache);
        textBlock.RenderTo(glyphRenderer, wrappingLength);

        this.DrawTextOperations(glyphRenderer.DrawingOperations, effectiveOptions, state.ClipPaths, state.ClipIntersectionRule);
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
            effectiveOptions.ShapeOptions,
            Matrix4x4.CreateTranslation(location.X, location.Y, 0) * effectiveOptions.Transform);

        using RichTextGlyphRenderer glyphRenderer = new(placedOptions, path: null, pen, brush, this.textCache);
        lineLayout.RenderTo(glyphRenderer);

        this.DrawTextOperations(glyphRenderer.DrawingOperations, placedOptions, state.ClipPaths, state.ClipIntersectionRule);
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

        using RichTextGlyphRenderer glyphRenderer = new(effectiveOptions, path, pen, brush, this.textCache);
        lineLayout.RenderTo(glyphRenderer);

        this.DrawTextOperations(glyphRenderer.DrawingOperations, effectiveOptions, state.ClipPaths, state.ClipIntersectionRule);
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

        using RichTextGlyphRenderer glyphRenderer = new(effectiveOptions, path: null, pen, brush, this.textCache);
        TextRenderer renderer = new(glyphRenderer);
        renderer.RenderGlyph(glyphId, options);

        this.DrawTextOperations(glyphRenderer.DrawingOperations, effectiveOptions, state.ClipPaths, state.ClipIntersectionRule);
    }

    /// <inheritdoc />
    public override void DrawText(
        GlyphRun glyphRun,
        RichGlyphOptions options,
        Brush? brush,
        Pen? pen)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(glyphRun, nameof(glyphRun));
        Guard.NotNull(options, nameof(options));

        if (glyphRun.Count == 0)
        {
            return;
        }

        EnsureTextPaint(brush, pen);

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        using RichTextGlyphRenderer glyphRenderer = new(effectiveOptions, path: null, pen, brush, this.textCache);
        TextRenderer renderer = new(glyphRenderer);
        renderer.RenderGlyphRun(glyphRun, options);

        this.DrawTextOperations(
            BatchGlyphRunOperations(glyphRenderer.DrawingOperations),
            effectiveOptions,
            state.ClipPaths,
            state.ClipIntersectionRule);
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
        IReadOnlyList<IPath> clipPaths = state.ClipPaths;
        IntersectionRule clipIntersectionRule = state.ClipIntersectionRule;

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

            float glyphArea = glyph.Bounds.Width * glyph.Bounds.Height;
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
                    float layerArea = layerPaths.ComputeArea();
                    shouldFill = layerArea > 0F && glyphArea > 0F && (layerArea / glyphArea) < 0.50F;
                }

                this.ExecuteWithTemporaryState(layerOptions, clipPaths, clipIntersectionRule, () =>
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
            this.DrawImageCore(specificImage, sourceRect, destinationRect, sampler, wrapX, wrapY, ownsSourceImage: false);
            return;
        }

        Image<TPixel> convertedImage = image.CloneAs<TPixel>();
        this.DrawImageCore(convertedImage, sourceRect, destinationRect, sampler, wrapX, wrapY, ownsSourceImage: true);
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
        this.DrawImageCore(image, sourceRect, destinationRect, sampler, wrapX, wrapY, ownsSourceImage: false);
    }

    /// <inheritdoc />
    public override DrawingBackendScene CreateScene()
    {
        this.EnsureNotDisposed();

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
        }
    }

    /// <inheritdoc />
    public override void RenderScene(DrawingBackendScene scene)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(scene, nameof(scene));
        this.batcher.AddScene(scene);
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
        typedSource.RenderRecordedTimeline();
        this.RenderRecordedTimeline();

        this.backend.CopyPixels(
            this.configuration,
            typedSource.targetFrame,
            this.targetFrame,
            sourceRectangle,
            targetPoint);
    }

    private void DrawImageCore(
        Image<TPixel> image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler? sampler,
        WrapMode wrapX,
        WrapMode wrapY,
        bool ownsSourceImage)
    {
        bool disposeSourceImage = ownsSourceImage;

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;
        DrawingOptions commandOptions = effectiveOptions;
        IReadOnlyList<IPath> commandClipPaths = state.ClipPaths;

        if (sourceRect.Width <= 0 ||
            sourceRect.Height <= 0 ||
            destinationRect.Width <= 0 ||
            destinationRect.Height <= 0)
        {
            return;
        }

        Rectangle clippedSourceRect = Rectangle.Intersect(sourceRect, image.Bounds);
        if (clippedSourceRect.Width <= 0 || clippedSourceRect.Height <= 0)
        {
            return;
        }

        RectangleF clippedDestinationRect = MapSourceClipToDestination(sourceRect, destinationRect, clippedSourceRect);
        if (clippedDestinationRect.Width <= 0 || clippedDestinationRect.Height <= 0)
        {
            return;
        }

        Size scaledSize = new(
            Math.Max(1, (int)MathF.Ceiling(clippedDestinationRect.Width)),
            Math.Max(1, (int)MathF.Ceiling(clippedDestinationRect.Height)));

        bool requiresScaling =
            clippedSourceRect.Width != scaledSize.Width ||
            clippedSourceRect.Height != scaledSize.Height;

        Image<TPixel> brushImage = image;
        RectangleF brushImageRegion = clippedSourceRect;
        RectangleF renderDestinationRect = clippedDestinationRect;
        Image<TPixel>? ownedImage = null;

        try
        {
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
                    effectiveOptions.ShapeOptions,
                    Matrix4x4.Identity);
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
                commandOptions,
                commandClipPaths);
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
    /// <param name="clipPaths">Optional clip paths to apply during preparation.</param>
    /// <param name="pen">Optional pen for stroke commands.</param>
    private void PrepareCompositionCore(
        IPath path,
        Brush brush,
        DrawingOptions options,
        IReadOnlyList<IPath>? clipPaths = null,
        Pen? pen = null)
    {
        brush = this.NormalizeBrush(brush);

        GraphicsOptions graphicsOptions = options.GraphicsOptions;
        ShapeOptions shapeOptions = options.ShapeOptions;
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
            shapeOptions.IntersectionRule,
            rasterizationMode,
            graphicsOptions.AntialiasThreshold);

        DrawingCanvasState state = this.ResolveState();

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
                    clipPaths,
                    state.ClipIntersectionRule,
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
                clipPaths,
                state.ClipIntersectionRule,
                state.Layer is not null,
                state.Layer));
    }

    /// <summary>
    /// Enqueues one explicit two-point stroke line-segment command using the current canvas state.
    /// </summary>
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
            options.ShapeOptions.IntersectionRule,
            rasterizationMode,
            graphicsOptions.AntialiasThreshold);

        DrawingCanvasState state = this.ResolveState();
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
            options.ShapeOptions.IntersectionRule,
            rasterizationMode,
            graphicsOptions.AntialiasThreshold);

        DrawingCanvasState state = this.ResolveState();
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
        return new ImageBrush<TPixel>(convertedImage, imageBrush.SourceRegion, imageBrush.Offset);
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
            state.Options,
            state.ClipPaths);
    }

    /// <summary>
    /// Combines a uniform glyph run into one fill operation and one draw operation.
    /// </summary>
    /// <param name="operations">Glyph operations produced by the text renderer.</param>
    /// <returns>The original operations when they cannot be combined; otherwise the combined operations.</returns>
    private static List<DrawingOperation> BatchGlyphRunOperations(List<DrawingOperation> operations)
    {
        if (operations.Count < 2)
        {
            return operations;
        }

        List<IPath>? fillPaths = null;
        List<IPath>? drawPaths = null;
        DrawingOperation fillOperation = default;
        DrawingOperation drawOperation = default;

        for (int i = 0; i < operations.Count; i++)
        {
            DrawingOperation operation = operations[i];
            switch (operation.Kind)
            {
                case DrawingOperationKind.Fill:
                    if (fillPaths is null)
                    {
                        fillOperation = operation;
                        fillPaths = new List<IPath>(operations.Count);
                    }
                    else if (!CanBatchGlyphRunOperation(fillOperation, operation))
                    {
                        // Color layers and per-glyph paint can depend on the original operation order.
                        // If any render semantics differ, keep the renderer's exact operation stream.
                        return operations;
                    }

                    fillPaths.Add(GetPositionedGlyphPath(operation));
                    break;

                case DrawingOperationKind.Draw:
                    if (drawPaths is null)
                    {
                        drawOperation = operation;
                        drawPaths = new List<IPath>(operations.Count);
                    }
                    else if (!CanBatchGlyphRunOperation(drawOperation, operation))
                    {
                        // Color layers and per-glyph paint can depend on the original operation order.
                        // If any render semantics differ, keep the renderer's exact operation stream.
                        return operations;
                    }

                    drawPaths.Add(GetPositionedGlyphPath(operation));
                    break;
            }
        }

        int capacity = (fillPaths is null ? 0 : 1) + (drawPaths is null ? 0 : 1);
        List<DrawingOperation> batched = new(capacity);

        if (fillPaths is not null)
        {
            fillOperation.Path = fillPaths.Count == 1 ? fillPaths[0] : new ComplexPolygon(fillPaths);
            fillOperation.RenderLocation = default;
            batched.Add(fillOperation);
        }

        if (drawPaths is not null)
        {
            drawOperation.Path = drawPaths.Count == 1 ? drawPaths[0] : new ComplexPolygon(drawPaths);
            drawOperation.RenderLocation = default;
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
    /// Gets a glyph path in canvas-local coordinates.
    /// </summary>
    /// <param name="operation">The glyph operation.</param>
    /// <returns>The positioned path.</returns>
    private static IPath GetPositionedGlyphPath(DrawingOperation operation)
    {
        Point renderLocation = operation.RenderLocation;
        return renderLocation.X == 0 && renderLocation.Y == 0
            ? operation.Path
            : operation.Path.Translate(renderLocation.X, renderLocation.Y);
    }

    /// <summary>
    /// Converts rendered text operations to composition commands and submits them to the batcher.
    /// </summary>
    /// <param name="operations">Text drawing operations produced by glyph layout/rendering.</param>
    /// <param name="drawingOptions">Drawing options applied to each operation.</param>
    /// <param name="clipPaths">Clip paths resolved from effective canvas state.</param>
    /// <param name="clipIntersectionRule">The fill rule used to interpret the clip paths.</param>
    private void DrawTextOperations(
        List<DrawingOperation> operations,
        DrawingOptions drawingOptions,
        IReadOnlyList<IPath> clipPaths,
        IntersectionRule clipIntersectionRule)
    {
        // Build composition commands and enforce render-pass ordering while preserving
        // original emission order inside each pass. This preserves overlapping color-font
        // layer compositing semantics (for example emoji mouth/teeth layers).
        List<(byte RenderPass, int Sequence, CompositionSceneCommand Command)> entries = new(operations.Count);
        for (int i = 0; i < operations.Count; i++)
        {
            DrawingOperation operation = operations[i];
            entries.Add((operation.RenderPass, i, this.CreateTextCompositionCommand(operation, drawingOptions, clipPaths, clipIntersectionRule)));
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
    /// <param name="clipPaths">Temporary clip paths.</param>
    /// <param name="clipIntersectionRule">The fill rule used to interpret the temporary clip paths.</param>
    /// <param name="action">Action to execute.</param>
    private void ExecuteWithTemporaryState(
        DrawingOptions options,
        IReadOnlyList<IPath> clipPaths,
        IntersectionRule clipIntersectionRule,
        Action action)
    {
        this.EnsureNotDisposed();

        int saveCount = this.savedStates.Count;
        DrawingCanvasState current = this.ResolveState();
        this.savedStates.Push(new DrawingCanvasState(options, clipPaths, clipIntersectionRule, current.TargetBounds, current.DestinationOffset)
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
        this.batcher.SealCommands();
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
            DrawingCanvasState popped = this.savedStates.Pop();
            if (popped.IsLayer)
            {
                // Restore and Dispose unwind layers through the same command stream path.
                this.batcher.AddComposition(CompositionCommand.CreateEndLayer(popped.TargetBounds, popped.Layer!));
            }
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
    /// Builds a normalized composition command for a text drawing operation.
    /// </summary>
    /// <param name="operation">The source drawing operation.</param>
    /// <param name="drawingOptions">Drawing options applied to the operation.</param>
    /// <param name="clipPaths">Optional clip paths to apply during preparation.</param>
    /// <param name="clipIntersectionRule">The fill rule used to interpret the clip paths.</param>
    /// <returns>A composition scene command ready for batching.</returns>
    private CompositionSceneCommand CreateTextCompositionCommand(
        DrawingOperation operation,
        DrawingOptions drawingOptions,
        IReadOnlyList<IPath>? clipPaths = null,
        IntersectionRule clipIntersectionRule = IntersectionRule.NonZero)
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
        // glyph's contours overlap. Force non-zero on both rule carriers - the rasterizer rule and the
        // shape options - so neither the per-operation rule nor the canvas's even-odd default applies.
        const IntersectionRule intersectionRule = IntersectionRule.NonZero;
        ShapeOptions shapeOptions = drawingOptions.ShapeOptions;
        if (shapeOptions.IntersectionRule != intersectionRule)
        {
            shapeOptions = shapeOptions.DeepClone();
            shapeOptions.IntersectionRule = intersectionRule;
        }

        DrawingCanvasState state = this.ResolveState();
        Point destinationOffset = new(
            state.DestinationOffset.X + operation.RenderLocation.X,
            state.DestinationOffset.Y + operation.RenderLocation.Y);

        Pen? pen = operation.Kind == DrawingOperationKind.Draw ? operation.Pen : null;

        RectangleF bounds = operation.Path.Bounds;
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

        RasterizerOptions rasterizerOptions = new(
            interest,
            intersectionRule,
            rasterizationMode,
            graphicsOptions.AntialiasThreshold);

        // Glyph paths arrive pre-laid-out, so the queued command must report identity transform and the
        // GraphicsOptions/ShapeOptions produced above. Reuse the caller's instance only when graphics
        // options, shape options (the forced non-zero clone) and transform all already match.
        DrawingOptions effectiveOptions = ReferenceEquals(graphicsOptions, drawingOptions.GraphicsOptions)
            && ReferenceEquals(shapeOptions, drawingOptions.ShapeOptions)
            && drawingOptions.Transform == Matrix4x4.Identity
            ? drawingOptions
            : new DrawingOptions(graphicsOptions, shapeOptions, Matrix4x4.Identity);

        IReadOnlyList<IPath>? operationClipPaths = clipPaths;
        if (clipPaths != null && clipPaths.Count > 0 && (operation.RenderLocation.X != 0 || operation.RenderLocation.Y != 0))
        {
            IPath[] translatedClipPaths = new IPath[clipPaths.Count];

            // Text glyph paths are queued in glyph-local coordinates and placed with RenderLocation,
            // so canvas-space clip paths must be moved into that same local space before clipping.
            for (int i = 0; i < clipPaths.Count; i++)
            {
                translatedClipPaths[i] = clipPaths[i].Translate(-operation.RenderLocation);
            }

            operationClipPaths = translatedClipPaths;
        }

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
                    operationClipPaths,
                    clipIntersectionRule,
                    state.Layer));
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
                operationClipPaths,
                clipIntersectionRule,
                state.Layer is not null,
                state.Layer));
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
    /// Transforms clip paths into the same coordinate space as an eagerly-transformed draw-image command.
    /// </summary>
    /// <param name="clipPaths">Clip paths from the current canvas state.</param>
    /// <param name="transform">Canvas transform already applied to the image content.</param>
    /// <returns>The transformed clip paths.</returns>
    private static IPath[] TransformClipPaths(IPath[] clipPaths, Matrix4x4 transform)
    {
        if (clipPaths.Length == 0 || transform.IsIdentity)
        {
            return clipPaths;
        }

        IPath[] transformed = new IPath[clipPaths.Length];
        for (int i = 0; i < transformed.Length; i++)
        {
            transformed[i] = clipPaths[i].Transform(transform);
        }

        return transformed;
    }

    /// <summary>
    /// Intersects accumulated region clips without lowering them through polygon clipping.
    /// </summary>
    /// <param name="existingPaths">The existing clip paths.</param>
    /// <param name="incoming">The incoming clip path.</param>
    /// <param name="clip">The intersected clip path.</param>
    /// <returns><see langword="true"/> when both clips are region-compatible; otherwise, <see langword="false"/>.</returns>
    private static bool TryIntersectRegionClips(IReadOnlyList<IPath> existingPaths, IPath incoming, out IPath clip)
    {
        clip = EmptyPath.ClosedPath;
        if (existingPaths.Count != 1)
        {
            return false;
        }

        if (!TryGetRegionClip(existingPaths[0], out Region? existingRegion)
            || !TryGetRegionClip(incoming, out Region? incomingRegion))
        {
            return false;
        }

        _ = existingRegion.Intersect(incomingRegion);
        clip = existingRegion.ToPath();
        return true;
    }

    /// <summary>
    /// Gets a pixel-aligned rectangular clip in absolute target coordinates.
    /// </summary>
    /// <param name="path">The local clip path to inspect.</param>
    /// <param name="destinationOffset">The absolute destination offset for the current canvas state.</param>
    /// <param name="rectangle">The absolute target rectangle represented by <paramref name="path"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the clip is a single axis-aligned integer rectangle that can
    /// be represented by target bounds; otherwise, <see langword="false"/>.
    /// </returns>
    private static bool TryGetRectangleClip(IPath path, Point destinationOffset, out Rectangle rectangle)
    {
        rectangle = Rectangle.Empty;
        if (path is IRegionPath regionPath)
        {
            IReadOnlyList<Rectangle> rectangles = regionPath.Rectangles;
            if (rectangles.Count != 1)
            {
                return false;
            }

            Rectangle regionRectangle = rectangles[0];
            rectangle = new Rectangle(
                regionRectangle.X + destinationOffset.X,
                regionRectangle.Y + destinationOffset.Y,
                regionRectangle.Width,
                regionRectangle.Height);

            return true;
        }

        if (path is not RectanglePolygon rectanglePolygon)
        {
            return false;
        }

        float left = rectanglePolygon.Left;
        float top = rectanglePolygon.Top;
        float right = rectanglePolygon.Right;
        float bottom = rectanglePolygon.Bottom;
        if (!IsInteger(left) || !IsInteger(top) || !IsInteger(right) || !IsInteger(bottom))
        {
            return false;
        }

        rectangle = Rectangle.FromLTRB(
            (int)left + destinationOffset.X,
            (int)top + destinationOffset.Y,
            (int)right + destinationOffset.X,
            (int)bottom + destinationOffset.Y);

        return true;
    }

    private static bool IsInteger(float value) => value == MathF.Truncate(value);

    /// <summary>
    /// Gets the integer region represented by a region-compatible clip path.
    /// </summary>
    /// <param name="path">The clip path.</param>
    /// <param name="region">The region represented by the path.</param>
    /// <returns><see langword="true"/> when the path can be treated as a region clip; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetRegionClip(IPath path, [NotNullWhen(true)] out Region? region)
    {
        if (path is IRegionPath regionPath)
        {
            // Region.ToPath exports a boundary IPath but keeps exact rect-set metadata while the
            // path remains in integer region space. Use that metadata here so clipping two region
            // clips stays a region operation instead of lowering through polygon clipping.
            region = new Region(regionPath.Rectangles);
            return true;
        }

        if (TryGetIntegerRectangle(path, out Rectangle rectangle))
        {
            region = new Region(rectangle);
            return true;
        }

        region = null;
        return false;
    }

    /// <summary>
    /// Gets an integer axis-aligned rectangle from a path.
    /// </summary>
    /// <param name="path">The path to inspect.</param>
    /// <param name="rectangle">The integer rectangle.</param>
    /// <returns><see langword="true"/> when the path is one integer axis-aligned rectangle; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetIntegerRectangle(IPath path, out Rectangle rectangle)
    {
        if (path is RectanglePolygon rectanglePolygon)
        {
            return TryGetIntegerRectangle(rectanglePolygon.Bounds, out rectangle);
        }

        if (path is ISimplePath simplePath)
        {
            return TryGetIntegerRectangle(simplePath, out rectangle);
        }

        using IEnumerator<ISimplePath> simplePaths = path.Flatten().GetEnumerator();
        if (!simplePaths.MoveNext())
        {
            rectangle = default;
            return false;
        }

        ISimplePath first = simplePaths.Current;
        if (simplePaths.MoveNext())
        {
            rectangle = default;
            return false;
        }

        return TryGetIntegerRectangle(first, out rectangle);
    }

    /// <summary>
    /// Gets an integer axis-aligned rectangle from a simple path.
    /// </summary>
    /// <param name="path">The path to inspect.</param>
    /// <param name="rectangle">The integer rectangle.</param>
    /// <returns><see langword="true"/> when the path is one integer axis-aligned rectangle; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetIntegerRectangle(ISimplePath path, out Rectangle rectangle)
    {
        rectangle = default;
        if (!path.IsClosed)
        {
            return false;
        }

        ReadOnlySpan<PointF> points = path.Points.Span;
        if (points.Length != 4)
        {
            return false;
        }

        float left = points[0].X;
        float top = points[0].Y;
        float right = points[0].X;
        float bottom = points[0].Y;

        for (int i = 1; i < points.Length; i++)
        {
            PointF point = points[i];
            left = MathF.Min(left, point.X);
            top = MathF.Min(top, point.Y);
            right = MathF.Max(right, point.X);
            bottom = MathF.Max(bottom, point.Y);
        }

        if (!TryGetIntegerRectangle(RectangleF.FromLTRB(left, top, right, bottom), out rectangle))
        {
            return false;
        }

        for (int i = 0; i < points.Length; i++)
        {
            PointF point = points[i];
            if ((point.X != left && point.X != right) || (point.Y != top && point.Y != bottom))
            {
                rectangle = default;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets an integer rectangle from rectangle bounds.
    /// </summary>
    /// <param name="bounds">The rectangle bounds.</param>
    /// <param name="rectangle">The integer rectangle.</param>
    /// <returns><see langword="true"/> when every coordinate is an integer; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetIntegerRectangle(RectangleF bounds, out Rectangle rectangle)
    {
        rectangle = default;
        if (!TryGetInteger(bounds.Left, out int left)
            || !TryGetInteger(bounds.Top, out int top)
            || !TryGetInteger(bounds.Right, out int right)
            || !TryGetInteger(bounds.Bottom, out int bottom))
        {
            return false;
        }

        rectangle = Rectangle.FromLTRB(left, top, right, bottom);
        return true;
    }

    /// <summary>
    /// Gets an integer from an exact single-precision value.
    /// </summary>
    /// <param name="value">The value to inspect.</param>
    /// <param name="result">The integer value.</param>
    /// <returns><see langword="true"/> when the value is exactly integral; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetInteger(float value, out int result)
    {
        result = (int)value;
        return value == result;
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
