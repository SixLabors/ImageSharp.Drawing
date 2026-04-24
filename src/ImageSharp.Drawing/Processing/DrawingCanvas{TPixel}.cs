// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
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
public sealed class DrawingCanvas<TPixel> : IDrawingCanvas
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
    /// Tracks whether this instance has already been disposed.
    /// </summary>
    private bool isDisposed;

    /// <summary>
    /// Stack of saved drawing states for Save/Restore operations.
    /// </summary>
    private readonly Stack<DrawingCanvasState> savedStates = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="targetRegion">The destination target region.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    public DrawingCanvas(
        Configuration configuration,
        Buffer2DRegion<TPixel> targetRegion,
        DrawingOptions options,
        params IPath[] clipPaths)
        : this(configuration, new MemoryCanvasFrame<TPixel>(targetRegion), options, clipPaths)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="targetFrame">The destination frame.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    public DrawingCanvas(
        Configuration configuration,
        ICanvasFrame<TPixel> targetFrame,
        DrawingOptions options,
        params IPath[] clipPaths)
        : this(configuration, configuration.GetDrawingBackend(), targetFrame, options, clipPaths)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class with an explicit backend and initial state.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="backend">The drawing backend implementation.</param>
    /// <param name="targetFrame">The destination frame.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    public DrawingCanvas(
        Configuration configuration,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame,
        DrawingOptions options,
        params IPath[] clipPaths)
        : this(
            configuration,
            backend,
            targetFrame,
            new DrawingCanvasBatcher<TPixel>(configuration, backend, targetFrame),
            new DrawingCanvasState(options, clipPaths, targetFrame.Bounds),
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
    /// <param name="defaultState">The default state used when no scoped state is active.</param>
    /// <param name="ownsBatcher">Whether this canvas owns final disposal of the shared batcher.</param>
    private DrawingCanvas(
        Configuration configuration,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame,
        DrawingCanvasBatcher<TPixel> batcher,
        DrawingCanvasState defaultState,
        bool ownsBatcher)
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(backend, nameof(backend));
        Guard.NotNull(targetFrame, nameof(targetFrame));
        Guard.NotNull(batcher, nameof(batcher));
        Guard.NotNull(defaultState, nameof(defaultState));

        if (!targetFrame.TryGetCpuRegion(out _) && !targetFrame.TryGetNativeSurface(out _))
        {
            throw new NotSupportedException("Canvas frame must expose either a CPU region or a native surface.");
        }

        this.configuration = configuration;
        this.backend = backend;
        this.targetFrame = targetFrame;
        this.batcher = batcher;
        this.ownsBatcher = ownsBatcher;

        // Canvas coordinates are local to the current frame; origin stays at (0,0).
        this.Bounds = new Rectangle(0, 0, targetFrame.Bounds.Width, targetFrame.Bounds.Height);
        this.savedStates.Push(defaultState);
    }

    /// <inheritdoc />
    public Rectangle Bounds { get; }

    /// <inheritdoc />
    public int SaveCount => this.savedStates.Count;

    /// <inheritdoc />
    public int Save()
    {
        this.EnsureNotDisposed();
        DrawingCanvasState current = this.ResolveState();

        // Push a non-layer copy of the current state.
        // Only states pushed by SaveLayer() should trigger layer compositing on restore.
        this.savedStates.Push(new DrawingCanvasState(current.Options, current.ClipPaths, current.TargetBounds));
        return this.savedStates.Count;
    }

    /// <inheritdoc />
    public int Save(DrawingOptions options, params IPath[] clipPaths)
        => this.SaveCore(options, clipPaths);

    private int SaveCore(DrawingOptions options, IReadOnlyList<IPath> clipPaths)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));

        _ = this.Save();
        DrawingCanvasState current = this.ResolveState();
        DrawingCanvasState state = new(options, clipPaths, current.TargetBounds);
        _ = this.savedStates.Pop();
        this.savedStates.Push(state);
        return this.savedStates.Count;
    }

    /// <inheritdoc />
    public int SaveLayer()
        => this.SaveLayer(new GraphicsOptions());

    /// <inheritdoc />
    public int SaveLayer(GraphicsOptions layerOptions)
        => this.SaveLayer(layerOptions, this.Bounds);

    /// <inheritdoc />
    public int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(layerOptions, nameof(layerOptions));

        Rectangle layerBounds = Rectangle.Intersect(this.Bounds, bounds);
        Rectangle absoluteLayerBounds = new(
            this.targetFrame.Bounds.X + layerBounds.X,
            this.targetFrame.Bounds.Y + layerBounds.Y,
            layerBounds.Width,
            layerBounds.Height);

        // Keep layer boundaries in the shared command stream so the backend can lower them inline.
        this.batcher.AddComposition(CompositionCommand.CreateBeginLayer(absoluteLayerBounds, layerOptions));

        DrawingCanvasState currentState = this.ResolveState();
        DrawingCanvasState layerState = new(currentState.Options, currentState.ClipPaths, absoluteLayerBounds)
        {
            IsLayer = true,
            LayerOptions = layerOptions,
        };

        this.savedStates.Push(layerState);
        return this.savedStates.Count;
    }

    /// <inheritdoc />
    public void Restore()
    {
        this.EnsureNotDisposed();
        if (this.savedStates.Count <= 1)
        {
            return;
        }

        DrawingCanvasState popped = this.savedStates.Pop();
        if (popped.IsLayer)
        {
            this.batcher.AddComposition(CompositionCommand.CreateEndLayer(popped.TargetBounds, popped.LayerOptions!));
        }
    }

    /// <inheritdoc />
    public void RestoreTo(int saveCount)
    {
        this.EnsureNotDisposed();
        Guard.MustBeBetweenOrEqualTo(saveCount, 1, this.savedStates.Count, nameof(saveCount));

        this.RestoreToCore(saveCount);
    }

    /// <inheritdoc cref="IDrawingCanvas.CreateRegion(Rectangle)" />
    public DrawingCanvas<TPixel> CreateRegion(Rectangle region)
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
            new DrawingCanvasState(currentState.Options, currentState.ClipPaths, childFrame.Bounds)
            {
                IsLayer = currentState.IsLayer,
                LayerOptions = currentState.LayerOptions,
            },
            false);
    }

    /// <inheritdoc />
    IDrawingCanvas IDrawingCanvas.CreateRegion(Rectangle region)
        => this.CreateRegion(region);

    /// <inheritdoc />
    public void Clear(Brush brush)
    {
        DrawingCanvasState state = this.ResolveState();
        DrawingOptions options = state.Options.CloneForClearOperation();
        this.ExecuteWithTemporaryState(options, state.ClipPaths, () => this.Fill(brush));
    }

    /// <inheritdoc />
    public void Clear(Brush brush, Rectangle region)
    {
        DrawingCanvasState state = this.ResolveState();
        DrawingOptions options = state.Options.CloneForClearOperation();
        this.ExecuteWithTemporaryState(options, state.ClipPaths, () => this.Fill(brush, region));
    }

    /// <inheritdoc />
    public void Clear(Brush brush, IPath path)
    {
        DrawingCanvasState state = this.ResolveState();
        DrawingOptions options = state.Options.CloneForClearOperation();
        this.ExecuteWithTemporaryState(options, state.ClipPaths, () => this.Fill(brush, path));
    }

    /// <inheritdoc />
    public void Fill(Brush brush)
        => this.Fill(brush, new RectangularPolygon(this.Bounds.X, this.Bounds.Y, this.Bounds.Width, this.Bounds.Height));

    /// <inheritdoc />
    public void Fill(Brush brush, IPath path)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(brush, nameof(brush));
        this.EnqueueFillPath(brush, path);
    }

    /// <inheritdoc />
    public void Apply(Rectangle region, Action<IImageProcessingContext> operation)
        => this.Apply(new RectangularPolygon(region.X, region.Y, region.Width, region.Height), operation);

    /// <inheritdoc />
    public void Apply(PathBuilder pathBuilder, Action<IImageProcessingContext> operation)
    {
        Guard.NotNull(pathBuilder, nameof(pathBuilder));
        this.Apply(pathBuilder.Build(), operation);
    }

    /// <inheritdoc />
    public void Apply(IPath path, Action<IImageProcessingContext> operation)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(operation, nameof(operation));

        // This operation samples the current destination state. Flush queued commands first
        // so readback observes strict draw-order semantics.
        this.Flush();

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        IPath closed = path.AsClosedPath();

        RectangleF rawBounds = RectangleF.Transform(closed.Bounds, effectiveOptions.Transform);

        Rectangle sourceRect = ToConservativeBounds(rawBounds);
        sourceRect = Rectangle.Intersect(this.Bounds, sourceRect);
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
        {
            return;
        }

        // Defensive guard: built-in backends should provide either direct readback (CPU/backed surface)
        // or shadow fallback. If this fails, it indicates a backend implementation bug or an unsupported custom backend.
        if (!this.TryCreateProcessSourceImage(sourceRect, out Image<TPixel>? sourceImage))
        {
            throw new NotSupportedException("Canvas process operations require either CPU pixels, backend readback support, or shadow fallback.");
        }

        sourceImage.Mutate(operation);

        Point brushOffset = new(
            sourceRect.X - (int)MathF.Floor(rawBounds.Left),
            sourceRect.Y - (int)MathF.Floor(rawBounds.Top));
        ImageBrush<TPixel> brush = new(sourceImage, sourceImage.Bounds, brushOffset);

        this.pendingImageResources.Add(sourceImage);
        this.PrepareCompositionCore(
            closed,
            brush,
            effectiveOptions,
            RasterizerSamplingOrigin.PixelBoundary,
            state.ClipPaths);
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

        // Stroke geometry can self-overlap; non-zero winding preserves stroke semantics.
        if (effectiveOptions.ShapeOptions.IntersectionRule != IntersectionRule.NonZero)
        {
            ShapeOptions shapeOptions = effectiveOptions.ShapeOptions.DeepClone();
            shapeOptions.IntersectionRule = IntersectionRule.NonZero;
            effectiveOptions = new DrawingOptions(effectiveOptions.GraphicsOptions, shapeOptions, effectiveOptions.Transform);
        }

        if (state.ClipPaths.Count > 0 || !pen.StrokePattern.IsEmpty)
        {
            this.PrepareCompositionCore(
                new Path([start, end]),
                pen.StrokeFill,
                effectiveOptions,
                RasterizerSamplingOrigin.PixelCenter,
                state.ClipPaths,
                pen);
            return;
        }

        this.PrepareStrokeLineSegmentCompositionCore(start, end, pen.StrokeFill, effectiveOptions, pen);
    }

    /// <inheritdoc />
    public void DrawLine(Pen pen, params PointF[] points)
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

        // Stroke geometry can self-overlap; non-zero winding preserves stroke semantics.
        if (effectiveOptions.ShapeOptions.IntersectionRule != IntersectionRule.NonZero)
        {
            ShapeOptions shapeOptions = effectiveOptions.ShapeOptions.DeepClone();
            shapeOptions.IntersectionRule = IntersectionRule.NonZero;
            effectiveOptions = new DrawingOptions(effectiveOptions.GraphicsOptions, shapeOptions, effectiveOptions.Transform);
        }

        if (state.ClipPaths.Count > 0 || !pen.StrokePattern.IsEmpty)
        {
            this.PrepareCompositionCore(
                new Path(points),
                pen.StrokeFill,
                effectiveOptions,
                RasterizerSamplingOrigin.PixelCenter,
                state.ClipPaths,
                pen);
            return;
        }

        this.PrepareStrokePolylineCompositionCore(points, pen.StrokeFill, effectiveOptions, pen);
    }

    /// <inheritdoc />
    public void Draw(Pen pen, IPath path)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(pen, nameof(pen));
        Guard.NotNull(path, nameof(path));

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        // Stroke geometry can self-overlap; non-zero winding preserves stroke semantics.
        if (effectiveOptions.ShapeOptions.IntersectionRule != IntersectionRule.NonZero)
        {
            ShapeOptions shapeOptions = effectiveOptions.ShapeOptions.DeepClone();
            shapeOptions.IntersectionRule = IntersectionRule.NonZero;
            effectiveOptions = new DrawingOptions(effectiveOptions.GraphicsOptions, shapeOptions, effectiveOptions.Transform);
        }

        this.PrepareCompositionCore(
            path,
            pen.StrokeFill,
            effectiveOptions,
            RasterizerSamplingOrigin.PixelCenter,
            state.ClipPaths,
            pen);
    }

    /// <inheritdoc />
    public void DrawText(
        RichTextOptions textOptions,
        ReadOnlySpan<char> text,
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

        if (brush is null && pen is null)
        {
            throw new ArgumentException($"Expected a {nameof(brush)} or {nameof(pen)}. Both were null");
        }

        RichTextOptions configuredOptions = ConfigureTextOptions(textOptions);
        using RichTextGlyphRenderer glyphRenderer = new(configuredOptions, effectiveOptions, pen, brush);
        TextRenderer renderer = new(glyphRenderer);
        renderer.RenderText(text, configuredOptions);

        this.DrawTextOperations(glyphRenderer.DrawingOperations, effectiveOptions, state.ClipPaths);
    }

    /// <inheritdoc />
    public void DrawGlyphs(
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

                this.ExecuteWithTemporaryState(layerOptions, clipPaths, () =>
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
    public TextMetrics MeasureText(RichTextOptions textOptions, ReadOnlySpan<char> text)
    {
        this.EnsureNotDisposed();
        return TextMeasurer.Measure(text, textOptions);
    }

    /// <inheritdoc />
    void IDrawingCanvas.DrawImage(
        Image image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler? sampler)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(image, nameof(image));

        if (image is Image<TPixel> specificImage)
        {
            this.DrawImageCore(specificImage, sourceRect, destinationRect, sampler, ownsSourceImage: false);
            return;
        }

        Image<TPixel> convertedImage = image.CloneAs<TPixel>();
        this.DrawImageCore(convertedImage, sourceRect, destinationRect, sampler, ownsSourceImage: true);
    }

    /// <inheritdoc cref="IDrawingCanvas.DrawImage(Image, Rectangle, RectangleF, IResampler?)" />
    public void DrawImage(
        Image<TPixel> image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler? sampler = null)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(image, nameof(image));
        this.DrawImageCore(image, sourceRect, destinationRect, sampler, ownsSourceImage: false);
    }

    private void DrawImageCore(
        Image<TPixel> image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler? sampler,
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
                commandClipPaths = TransformClipPaths(state.ClipPaths, effectiveOptions.Transform);
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

            ImageBrush<TPixel> brush = new(brushImage, brushImageRegion);
            IPath destinationPath = new RectangularPolygon(
                renderDestinationRect.X,
                renderDestinationRect.Y,
                renderDestinationRect.Width,
                renderDestinationRect.Height);

            this.PrepareCompositionCore(
                destinationPath,
                brush,
                commandOptions,
                RasterizerSamplingOrigin.PixelBoundary,
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
    /// <param name="samplingOrigin">Rasterizer sampling origin.</param>
    /// <param name="clipPaths">Optional clip paths to apply during preparation.</param>
    /// <param name="pen">Optional pen for stroke commands.</param>
    private void PrepareCompositionCore(
        IPath path,
        Brush brush,
        DrawingOptions options,
        RasterizerSamplingOrigin samplingOrigin,
        IReadOnlyList<IPath>? clipPaths = null,
        Pen? pen = null)
    {
        brush = this.NormalizeBrush(brush);

        GraphicsOptions graphicsOptions = options.GraphicsOptions;
        ShapeOptions shapeOptions = options.ShapeOptions;
        RasterizationMode rasterizationMode = graphicsOptions.Antialias ? RasterizationMode.Antialiased : RasterizationMode.Aliased;

        RectangleF bounds = path.Bounds;
        if (samplingOrigin == RasterizerSamplingOrigin.PixelCenter)
        {
            bounds = new RectangleF(bounds.X + 0.5F, bounds.Y + 0.5F, bounds.Width, bounds.Height);
        }

        Rectangle interest = Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));

        RasterizerOptions rasterizerOptions = new(
            interest,
            shapeOptions.IntersectionRule,
            rasterizationMode,
            samplingOrigin,
            graphicsOptions.AntialiasThreshold);

        DrawingCanvasState state = this.ResolveState();

        // Commands carry their absolute target bounds and destination origin explicitly.
        // Region canvases therefore only need to update state.TargetBounds.
        if (pen is null)
        {
            this.batcher.AddComposition(
                CompositionCommand.Create(
                    path,
                    brush,
                    options,
                    in rasterizerOptions,
                    state.TargetBounds,
                    state.TargetBounds.Location,
                    clipPaths));
            return;
        }

        this.batcher.AddStrokePath(
            new StrokePathCommand(
                path,
                brush,
                options,
                in rasterizerOptions,
                state.TargetBounds,
                state.TargetBounds.Location,
                pen,
                clipPaths));
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
            RasterizerSamplingOrigin.PixelCenter,
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
                state.TargetBounds.Location,
                pen));
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
            RasterizerSamplingOrigin.PixelCenter,
            graphicsOptions.AntialiasThreshold);

        DrawingCanvasState state = this.ResolveState();
        this.batcher.AddStrokePolyline(
            new StrokePolylineCommand(
                points,
                brush,
                options,
                in rasterizerOptions,
                state.TargetBounds,
                state.TargetBounds.Location,
                pen));
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

        // This brush references an image with a pixel format that doesn't match the canvas target.
        // Clone the image as TPixel
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
            RasterizerSamplingOrigin.PixelBoundary,
            state.ClipPaths);
    }

    /// <summary>
    /// Converts rendered text operations to composition commands and submits them to the batcher.
    /// </summary>
    /// <param name="operations">Text drawing operations produced by glyph layout/rendering.</param>
    /// <param name="drawingOptions">Drawing options applied to each operation.</param>
    /// <param name="clipPaths">Clip paths resolved from effective canvas state.</param>
    private void DrawTextOperations(
        List<DrawingOperation> operations,
        DrawingOptions drawingOptions,
        IReadOnlyList<IPath> clipPaths)
    {
        // Build composition commands and enforce render-pass ordering while preserving
        // original emission order inside each pass. This preserves overlapping color-font
        // layer compositing semantics (for example emoji mouth/teeth layers).
        List<(byte RenderPass, int Sequence, CompositionSceneCommand Command)> entries = new(operations.Count);
        for (int i = 0; i < operations.Count; i++)
        {
            DrawingOperation operation = operations[i];
            entries.Add((operation.RenderPass, i, this.CreateTextCompositionCommand(operation, drawingOptions, clipPaths)));
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
    /// Executes an action with a temporary scoped state, restoring the previous scoped state afterwards.
    /// </summary>
    /// <param name="options">Temporary drawing options.</param>
    /// <param name="clipPaths">Temporary clip paths.</param>
    /// <param name="action">Action to execute.</param>
    private void ExecuteWithTemporaryState(DrawingOptions options, IReadOnlyList<IPath> clipPaths, Action action)
    {
        int saveCount = this.savedStates.Count;
        _ = this.SaveCore(options, clipPaths);
        try
        {
            action();
        }
        finally
        {
            this.RestoreTo(saveCount);
        }
    }

    /// <summary>
    /// Attempts to create a source image for process-in-path operations.
    /// The backend copies pixels directly into the image's pixel buffer — single copy.
    /// </summary>
    /// <param name="sourceRect">Source rectangle in local canvas coordinates.</param>
    /// <param name="sourceImage">The readback image when available.</param>
    /// <returns><see langword="true"/> when source pixels were resolved.</returns>
    private bool TryCreateProcessSourceImage(Rectangle sourceRect, [NotNullWhen(true)] out Image<TPixel>? sourceImage)
    {
        sourceImage = new Image<TPixel>(this.configuration, sourceRect.Width, sourceRect.Height);
        if (!this.backend.TryReadRegion(
                this.configuration,
                this.targetFrame,
                sourceRect,
                new Buffer2DRegion<TPixel>(sourceImage.Frames.RootFrame.PixelBuffer)))
        {
            sourceImage.Dispose();
            sourceImage = null;
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public void Flush()
    {
        this.EnsureNotDisposed();
        try
        {
            this.batcher.FlushCompositions();
        }
        finally
        {
            this.DisposePendingImageResources();
        }
    }

    /// <inheritdoc />
    public void Dispose()
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
                this.batcher.FlushCompositions();
            }
        }
        finally
        {
            if (this.ownsBatcher)
            {
                this.DisposePendingImageResources();
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
                this.batcher.AddComposition(CompositionCommand.CreateEndLayer(popped.TargetBounds, popped.LayerOptions!));
            }
        }
    }

    /// <summary>
    /// Normalizes text options to avoid applying origin translation twice when path-based text is used.
    /// </summary>
    /// <param name="options">Input text options.</param>
    /// <returns>Normalized text options for rendering.</returns>
    private static RichTextOptions ConfigureTextOptions(RichTextOptions options)
    {
        if (options.Path is not null && options.Origin != Vector2.Zero)
        {
            // Path-based text uses the path itself as positioning source; fold origin into the path
            // to avoid applying both path layout and origin translation.
            return new RichTextOptions(options)
            {
                Origin = Vector2.Zero,
                Path = options.Path.Translate(options.Origin)
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
    /// <returns>A composition scene command ready for batching.</returns>
    private CompositionSceneCommand CreateTextCompositionCommand(
        DrawingOperation operation,
        DrawingOptions drawingOptions,
        IReadOnlyList<IPath>? clipPaths = null)
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

        ShapeOptions shapeOptions = drawingOptions.ShapeOptions;

        DrawingCanvasState state = this.ResolveState();
        Point destinationOffset = new(
            state.TargetBounds.X + operation.RenderLocation.X,
            state.TargetBounds.Y + operation.RenderLocation.Y);

        Pen? pen = operation.Kind == DrawingOperationKind.Draw ? operation.Pen : null;

        IntersectionRule intersectionRule = pen is not null && operation.IntersectionRule != IntersectionRule.NonZero
            ? IntersectionRule.NonZero
            : operation.IntersectionRule;

        RasterizerSamplingOrigin samplingOrigin = pen is not null
            ? RasterizerSamplingOrigin.PixelCenter
            : RasterizerSamplingOrigin.PixelBoundary;

        RasterizerOptions rasterizerOptions = new(
            default,
            intersectionRule,
            rasterizationMode,
            samplingOrigin,
            graphicsOptions.AntialiasThreshold);

        // Glyph paths arrive pre-laid-out, so the queued command must report identity transform
        // and the GraphicsOptions clone produced above. Reuse the caller's instance only when both already match.
        DrawingOptions effectiveOptions = ReferenceEquals(graphicsOptions, drawingOptions.GraphicsOptions)
            && drawingOptions.Transform == Matrix4x4.Identity
            ? drawingOptions
            : new DrawingOptions(graphicsOptions, shapeOptions, Matrix4x4.Identity);

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
                    clipPaths));
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
                clipPaths));
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
        Matrix4x4 sourceToDestination = Matrix4x4.CreateScale(
            destinationRect.Width / image.Width,
            destinationRect.Height / image.Height,
            1)
            * Matrix4x4.CreateTranslation(destinationRect.X, destinationRect.Y, 0);

        // Apply the canvas transform after source->destination placement:
        // source -> destination -> transformed-canvas.
        Matrix4x4 sourceToTransformedCanvas = sourceToDestination * transform;

        // Compute the transformed axis-aligned bounds in canvas space.
        RectangleF transformedBounds = RectangleF.Transform(
            new RectangleF(0, 0, image.Width, image.Height),
            sourceToTransformedCanvas);

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
    /// <returns>The transformed clip path list.</returns>
    private static IReadOnlyList<IPath> TransformClipPaths(IReadOnlyList<IPath> clipPaths, Matrix4x4 transform)
    {
        if (clipPaths.Count == 0 || transform.IsIdentity)
        {
            return clipPaths;
        }

        IPath[] transformed = new IPath[clipPaths.Count];
        for (int i = 0; i < transformed.Length; i++)
        {
            transformed[i] = clipPaths[i].Transform(transform);
        }

        return transformed;
    }

    /// <summary>
    /// Computes the axis-aligned bounding rectangle of a transformed rectangle.
    /// </summary>
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
}
