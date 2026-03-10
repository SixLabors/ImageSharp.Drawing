// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable CA1000 // Do not declare static members on generic types

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
public sealed partial class DrawingCanvas<TPixel> : IDrawingCanvas
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
    /// Reassigned when a layer is pushed or popped via <see cref="SaveLayer()"/>.
    /// </summary>
    private DrawingCanvasBatcher<TPixel> batcher;

    /// <summary>
    /// Active layer data stack. Each entry corresponds to a <see cref="SaveLayer()"/>
    /// call on the saved-states stack and holds the parent batcher and temporary layer buffer.
    /// </summary>
    private readonly Stack<LayerData<TPixel>> layerDataStack = new();

    /// <summary>
    /// Temporary image resources that must stay alive until queued commands are flushed.
    /// </summary>
    private readonly List<Image<TPixel>> pendingImageResources = [];

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
    internal DrawingCanvas(
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
            new DrawingCanvasState(options, clipPaths),
            isRoot: true)
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
    private DrawingCanvas(
        Configuration configuration,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame,
        DrawingCanvasBatcher<TPixel> batcher,
        DrawingCanvasState defaultState)
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

        // Canvas coordinates are local to the current frame; origin stays at (0,0).
        this.Bounds = new Rectangle(0, 0, targetFrame.Bounds.Width, targetFrame.Bounds.Height);
        this.savedStates.Push(defaultState);
    }

    /// <inheritdoc />
    public Rectangle Bounds { get; }

    /// <inheritdoc />
    public int SaveCount => this.savedStates.Count;

    /// <summary>
    /// Creates a drawing canvas over an existing frame.
    /// </summary>
    /// <param name="frame">The frame backing the canvas.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting <paramref name="frame"/>.</returns>
    public static DrawingCanvas<TPixel> FromFrame(
        ImageFrame<TPixel> frame,
        DrawingOptions options,
        params IPath[] clipPaths)
    {
        Guard.NotNull(frame, nameof(frame));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));

        return new DrawingCanvas<TPixel>(
            frame.Configuration,
            new Buffer2DRegion<TPixel>(frame.PixelBuffer, frame.Bounds),
            options,
            clipPaths);
    }

    /// <summary>
    /// Creates a drawing canvas over a specific frame of an image.
    /// </summary>
    /// <param name="image">The image containing the frame.</param>
    /// <param name="frameIndex">The zero-based frame index to target.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting the selected frame.</returns>
    public static DrawingCanvas<TPixel> FromImage(
        Image<TPixel> image,
        int frameIndex,
        DrawingOptions options,
        params IPath[] clipPaths)
    {
        Guard.NotNull(image, nameof(image));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));
        Guard.MustBeBetweenOrEqualTo(frameIndex, 0, image.Frames.Count - 1, nameof(frameIndex));

        return FromFrame(image.Frames[frameIndex], options, clipPaths);
    }

    /// <summary>
    /// Creates a drawing canvas over the root frame of an image.
    /// </summary>
    /// <param name="image">The image whose root frame should be targeted.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting the root frame.</returns>
    public static DrawingCanvas<TPixel> FromRootFrame(
        Image<TPixel> image,
        DrawingOptions options,
        params IPath[] clipPaths)
    {
        Guard.NotNull(image, nameof(image));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));

        return FromFrame(image.Frames.RootFrame, options, clipPaths);
    }

    /// <inheritdoc />
    public int Save()
    {
        this.EnsureNotDisposed();
        DrawingCanvasState current = this.ResolveState();

        // Push a non-layer copy of the current state.
        // Only states pushed by SaveLayer() should trigger layer compositing on restore.
        this.savedStates.Push(new DrawingCanvasState(current.Options, current.ClipPaths));
        return this.savedStates.Count;
    }

    /// <inheritdoc />
    public int Save(DrawingOptions options, params IPath[] clipPaths)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));

        _ = this.Save();
        DrawingCanvasState state = new(options, clipPaths);
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

        // Flush any pending commands to the current target before switching.
        this.Flush();

        // Clamp bounds to the canvas.
        Rectangle layerBounds = Rectangle.Intersect(this.Bounds, bounds);
        if (layerBounds.Width <= 0 || layerBounds.Height <= 0)
        {
            layerBounds = new Rectangle(0, 0, 1, 1);
        }

        // Allocate a layer frame via the backend (CPU image or GPU texture).
        ICanvasFrame<TPixel> currentTarget = this.batcher.TargetFrame;
        ICanvasFrame<TPixel> layerFrame = this.backend.CreateLayerFrame(
            this.configuration,
            currentTarget,
            layerBounds.Width,
            layerBounds.Height);

        // Save the current batcher so we can restore it later.
        DrawingCanvasBatcher<TPixel> parentBatcher = this.batcher;
        LayerData<TPixel> layerData = new(parentBatcher, layerFrame, layerBounds);
        this.layerDataStack.Push(layerData);

        // Redirect commands to the layer target.
        this.batcher = new DrawingCanvasBatcher<TPixel>(this.configuration, this.backend, layerFrame);

        // Push a layer state onto the saved states stack.
        DrawingCanvasState currentState = this.ResolveState();
        DrawingCanvasState layerState = new(currentState.Options, currentState.ClipPaths)
        {
            IsLayer = true,
            LayerOptions = layerOptions,
            LayerBounds = layerBounds,
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
            this.CompositeAndPopLayer(popped);
        }
    }

    /// <inheritdoc />
    public void RestoreTo(int saveCount)
    {
        this.EnsureNotDisposed();
        Guard.MustBeBetweenOrEqualTo(saveCount, 1, this.savedStates.Count, nameof(saveCount));

        while (this.savedStates.Count > saveCount)
        {
            DrawingCanvasState popped = this.savedStates.Pop();
            if (popped.IsLayer)
            {
                this.CompositeAndPopLayer(popped);
            }
        }
    }

    /// <inheritdoc cref="IDrawingCanvas.CreateRegion(Rectangle)" />
    public DrawingCanvas<TPixel> CreateRegion(Rectangle region)
    {
        this.EnsureNotDisposed();

        Rectangle clipped = Rectangle.Intersect(this.Bounds, region);
        ICanvasFrame<TPixel> childFrame = new CanvasRegionFrame<TPixel>(this.targetFrame, clipped);
        return new DrawingCanvas<TPixel>(
            this.configuration,
            this.backend,
            childFrame,
            this.batcher,
            this.ResolveState(),
            isRoot: false);
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
        => this.Fill(brush, this.Bounds);

    /// <inheritdoc />
    public void Fill(Brush brush, Rectangle region)
        => this.Fill(brush, new RectangularPolygon(region.X, region.Y, region.Width, region.Height));

    /// <inheritdoc />
    public void Fill(Brush brush, IPathCollection paths)
    {
        Guard.NotNull(paths, nameof(paths));
        foreach (IPath path in paths)
        {
            this.Fill(brush, path);
        }
    }

    /// <inheritdoc />
    public void Fill(Brush brush, PathBuilder pathBuilder)
    {
        Guard.NotNull(pathBuilder, nameof(pathBuilder));
        this.Fill(brush, pathBuilder.Build());
    }

    /// <inheritdoc />
    public void Fill(Brush brush, IPath path)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(brush, nameof(brush));

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        IPath closed = path.AsClosedPath();

        Brush effectiveBrush = brush;
        IPath effectivePath = closed;
        if (effectiveOptions.Transform != Matrix4x4.Identity)
        {
            effectivePath = FlattenAndTransform(closed, effectiveOptions.Transform);
            effectiveBrush = brush.Transform(effectiveOptions.Transform);
        }

        effectivePath = ApplyClipPaths(effectivePath, effectiveOptions.ShapeOptions, state.ClipPaths);

        this.PrepareCompositionCore(effectivePath, effectiveBrush, effectiveOptions, RasterizerSamplingOrigin.PixelBoundary);
    }

    /// <inheritdoc />
    public void Process(Rectangle region, Action<IImageProcessingContext> operation)
        => this.Process(new RectangularPolygon(region.X, region.Y, region.Width, region.Height), operation);

    /// <inheritdoc />
    public void Process(PathBuilder pathBuilder, Action<IImageProcessingContext> operation)
    {
        Guard.NotNull(pathBuilder, nameof(pathBuilder));
        this.Process(pathBuilder.Build(), operation);
    }

    /// <inheritdoc />
    public void Process(IPath path, Action<IImageProcessingContext> operation)
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
        IPath transformedPath = effectiveOptions.Transform == Matrix4x4.Identity
            ? closed
            : FlattenAndTransform(closed, effectiveOptions.Transform);
        transformedPath = ApplyClipPaths(transformedPath, effectiveOptions.ShapeOptions, state.ClipPaths);

        Rectangle sourceRect = ToConservativeBounds(transformedPath.Bounds);
        sourceRect = Rectangle.Intersect(this.Bounds, sourceRect);
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
        {
            return;
        }

        // Defensive guard: built-in backends should provide either direct readback (CPU/backed surface)
        // or shadow fallback, but custom/inconsistent backend+target combinations can still fail both paths.
        if (!this.TryCreateProcessSourceImage(sourceRect, out Image<TPixel>? sourceImage))
        {
            throw new NotSupportedException("Canvas process operations require either CPU pixels, backend readback support, or shadow fallback.");
        }

        sourceImage.Mutate(operation);

        Point brushOffset = new(
            sourceRect.X - (int)MathF.Floor(transformedPath.Bounds.Left),
            sourceRect.Y - (int)MathF.Floor(transformedPath.Bounds.Top));
        ImageBrush brush = new(sourceImage, sourceImage.Bounds, brushOffset);

        this.pendingImageResources.Add(sourceImage);
        this.PrepareCompositionCore(transformedPath, brush, effectiveOptions, RasterizerSamplingOrigin.PixelBoundary);
    }

    /// <inheritdoc />
    public void DrawArc(Pen pen, PointF center, SizeF radius, float rotation, float startAngle, float sweepAngle)
        => this.Draw(pen, new Path(new ArcLineSegment(center, radius, rotation, startAngle, sweepAngle)));

    /// <inheritdoc />
    public void DrawBezier(Pen pen, params PointF[] points)
    {
        Guard.NotNull(points, nameof(points));
        this.Draw(pen, new Path(new CubicBezierLineSegment(points)));
    }

    /// <inheritdoc />
    public void DrawEllipse(Pen pen, PointF center, SizeF size)
        => this.Draw(pen, new EllipsePolygon(center, size));

    /// <inheritdoc />
    public void DrawLine(Pen pen, params PointF[] points)
    {
        Guard.NotNull(points, nameof(points));
        this.Draw(pen, new Path(points));
    }

    /// <inheritdoc />
    public void Draw(Pen pen, Rectangle region)
        => this.Draw(pen, new RectangularPolygon(region.X, region.Y, region.Width, region.Height));

    /// <inheritdoc />
    public void Draw(Pen pen, IPathCollection paths)
    {
        Guard.NotNull(paths, nameof(paths));
        foreach (IPath path in paths)
        {
            this.Draw(pen, path);
        }
    }

    /// <inheritdoc />
    public void Draw(Pen pen, PathBuilder pathBuilder)
    {
        Guard.NotNull(pathBuilder, nameof(pathBuilder));
        this.Draw(pen, pathBuilder.Build());
    }

    /// <inheritdoc />
    public void Draw(Pen pen, IPath path)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(pen, nameof(pen));
        Guard.NotNull(path, nameof(path));

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

        IPath transformedPath = effectiveOptions.Transform == Matrix4x4.Identity
            ? path
            : FlattenAndTransform(path, effectiveOptions.Transform);

        // Stroke geometry can self-overlap; non-zero winding preserves stroke semantics.
        if (effectiveOptions.ShapeOptions.IntersectionRule != IntersectionRule.NonZero)
        {
            ShapeOptions shapeOptions = effectiveOptions.ShapeOptions.DeepClone();
            shapeOptions.IntersectionRule = IntersectionRule.NonZero;
            effectiveOptions = new DrawingOptions(effectiveOptions.GraphicsOptions, shapeOptions, effectiveOptions.Transform);
        }

        // When clip paths are active we must expand the stroke here so the clip
        // boolean operation can be applied to the expanded outline geometry.
        if (state.ClipPaths.Count > 0)
        {
            IPath outline = pen.GeneratePath(transformedPath);
            outline = ApplyClipPaths(outline, effectiveOptions.ShapeOptions, state.ClipPaths);
            this.PrepareCompositionCore(outline, pen.StrokeFill, effectiveOptions, RasterizerSamplingOrigin.PixelCenter);
            return;
        }

        this.PrepareStrokeCompositionCore(
            transformedPath,
            pen.StrokeFill,
            pen.StrokeWidth,
            pen.StrokeOptions,
            pen.StrokePattern,
            effectiveOptions);
    }

    /// <inheritdoc />
    public void DrawText(
        RichTextOptions textOptions,
        ReadOnlySpan<char> text,
        Brush? brush,
        Pen? pen)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textOptions, nameof(textOptions));

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
        IReadOnlyList<GlyphPathCollection> glyphs)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(brush, nameof(brush));
        Guard.NotNull(pen, nameof(pen));
        Guard.NotNull(glyphs, nameof(glyphs));

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions baseOptions = state.Options;
        IReadOnlyList<IPath> clipPaths = state.ClipPaths;

        for (int glyphIndex = 0; glyphIndex < glyphs.Count; glyphIndex++)
        {
            GlyphPathCollection glyph = glyphs[glyphIndex];
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
    public RectangleF MeasureTextAdvance(RichTextOptions textOptions, ReadOnlySpan<char> text)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textOptions, nameof(textOptions));

        if (text.IsEmpty)
        {
            return RectangleF.Empty;
        }

        FontRectangle advance = TextMeasurer.MeasureAdvance(text, textOptions);
        return RectangleF.FromLTRB(advance.Left, advance.Top, advance.Right, advance.Bottom);
    }

    /// <inheritdoc />
    public RectangleF MeasureTextBounds(RichTextOptions textOptions, ReadOnlySpan<char> text)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textOptions, nameof(textOptions));

        if (text.IsEmpty)
        {
            return RectangleF.Empty;
        }

        FontRectangle bounds = TextMeasurer.MeasureBounds(text, textOptions);
        return RectangleF.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    /// <inheritdoc />
    public RectangleF MeasureTextRenderableBounds(RichTextOptions textOptions, ReadOnlySpan<char> text)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textOptions, nameof(textOptions));

        if (text.IsEmpty)
        {
            return RectangleF.Empty;
        }

        FontRectangle renderableBounds = TextMeasurer.MeasureRenderableBounds(text, textOptions);
        return RectangleF.FromLTRB(renderableBounds.Left, renderableBounds.Top, renderableBounds.Right, renderableBounds.Bottom);
    }

    /// <inheritdoc />
    public RectangleF MeasureTextSize(RichTextOptions textOptions, ReadOnlySpan<char> text)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textOptions, nameof(textOptions));

        if (text.IsEmpty)
        {
            return RectangleF.Empty;
        }

        FontRectangle size = TextMeasurer.MeasureSize(text, textOptions);
        return RectangleF.FromLTRB(size.Left, size.Top, size.Right, size.Bottom);
    }

    /// <inheritdoc />
    public bool TryMeasureCharacterAdvances(RichTextOptions textOptions, ReadOnlySpan<char> text, out ReadOnlySpan<GlyphBounds> advances)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textOptions, nameof(textOptions));

        if (text.IsEmpty)
        {
            advances = [];
            return false;
        }

        return TextMeasurer.TryMeasureCharacterAdvances(text, textOptions, out advances);
    }

    /// <inheritdoc />
    public bool TryMeasureCharacterBounds(RichTextOptions textOptions, ReadOnlySpan<char> text, out ReadOnlySpan<GlyphBounds> bounds)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textOptions, nameof(textOptions));

        if (text.IsEmpty)
        {
            bounds = [];
            return false;
        }

        return TextMeasurer.TryMeasureCharacterBounds(text, textOptions, out bounds);
    }

    /// <inheritdoc />
    public bool TryMeasureCharacterRenderableBounds(RichTextOptions textOptions, ReadOnlySpan<char> text, out ReadOnlySpan<GlyphBounds> bounds)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textOptions, nameof(textOptions));

        if (text.IsEmpty)
        {
            bounds = [];
            return false;
        }

        return TextMeasurer.TryMeasureCharacterRenderableBounds(text, textOptions, out bounds);
    }

    /// <inheritdoc />
    public bool TryMeasureCharacterSizes(RichTextOptions textOptions, ReadOnlySpan<char> text, out ReadOnlySpan<GlyphBounds> sizes)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textOptions, nameof(textOptions));

        if (text.IsEmpty)
        {
            sizes = [];
            return false;
        }

        return TextMeasurer.TryMeasureCharacterSizes(text, textOptions, out sizes);
    }

    /// <inheritdoc />
    public int CountTextLines(RichTextOptions textOptions, ReadOnlySpan<char> text)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textOptions, nameof(textOptions));

        if (text.IsEmpty)
        {
            return 0;
        }

        return TextMeasurer.CountLines(text, textOptions);
    }

    /// <inheritdoc />
    public LineMetrics[] GetTextLineMetrics(RichTextOptions textOptions, ReadOnlySpan<char> text)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textOptions, nameof(textOptions));

        if (text.IsEmpty)
        {
            return [];
        }

        return TextMeasurer.GetLineMetrics(text, textOptions);
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
        => this.DrawImageCore(image, sourceRect, destinationRect, sampler, ownsSourceImage: false);

    private void DrawImageCore(
        Image<TPixel> image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler? sampler,
        bool ownsSourceImage)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(image, nameof(image));
        bool disposeSourceImage = ownsSourceImage;

        DrawingCanvasState state = this.ResolveState();
        DrawingOptions effectiveOptions = state.Options;

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

            ImageBrush brush = new(brushImage, brushImageRegion);
            IPath destinationPath = new RectangularPolygon(
                renderDestinationRect.X,
                renderDestinationRect.Y,
                renderDestinationRect.Width,
                renderDestinationRect.Height);

            this.Fill(brush, destinationPath);
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
    private void PrepareCompositionCore(
        IPath path,
        Brush brush,
        DrawingOptions options,
        RasterizerSamplingOrigin samplingOrigin)
    {
        GraphicsOptions graphicsOptions = options.GraphicsOptions;
        ShapeOptions shapeOptions = options.ShapeOptions;
        RasterizationMode rasterizationMode = graphicsOptions.Antialias ? RasterizationMode.Antialiased : RasterizationMode.Aliased;

        RectangleF bounds = path.Bounds;
        if (samplingOrigin == RasterizerSamplingOrigin.PixelCenter)
        {
            // Keep rasterizer interest aligned with center-sampled scan conversion.
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

        this.batcher.AddComposition(
            CompositionCommand.Create(
                path,
                brush,
                graphicsOptions,
                rasterizerOptions,
                this.targetFrame.Bounds.Location));
    }

    /// <summary>
    /// Prepares a stroke composition command with the original centerline path and enqueues it.
    /// The backend is responsible for stroke expansion or SDF evaluation.
    /// </summary>
    /// <param name="path">Original centerline path in target-local coordinates.</param>
    /// <param name="brush">Brush used for shading.</param>
    /// <param name="strokeWidth">Stroke width in pixels.</param>
    /// <param name="strokeOptions">Stroke geometry options.</param>
    /// <param name="strokePattern">Optional dash pattern.</param>
    /// <param name="options">Effective drawing options.</param>
    private void PrepareStrokeCompositionCore(
        IPath path,
        Brush brush,
        float strokeWidth,
        StrokeOptions strokeOptions,
        ReadOnlyMemory<float> strokePattern,
        DrawingOptions options)
    {
        GraphicsOptions graphicsOptions = options.GraphicsOptions;
        ShapeOptions shapeOptions = options.ShapeOptions;
        RasterizationMode rasterizationMode = graphicsOptions.Antialias ? RasterizationMode.Antialiased : RasterizationMode.Aliased;

        // Inflate path bounds by the maximum possible stroke extent.
        // The miter limit caps the tip extension; the base half-width is always present.
        float halfWidth = strokeWidth / 2f;
        float maxExtent = halfWidth * (float)Math.Max(strokeOptions.MiterLimit, 1D);
        RectangleF bounds = path.Bounds;
        bounds = new RectangleF(
            bounds.X - maxExtent + 0.5F,
            bounds.Y - maxExtent + 0.5F,
            bounds.Width + (maxExtent * 2f),
            bounds.Height + (maxExtent * 2f));

        Rectangle interest = Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));

        RasterizerOptions rasterizerOptions = new(
            interest,
            shapeOptions.IntersectionRule,
            rasterizationMode,
            RasterizerSamplingOrigin.PixelCenter,
            graphicsOptions.AntialiasThreshold);

        this.batcher.AddComposition(
            CompositionCommand.CreateStroke(
                path,
                brush,
                graphicsOptions,
                rasterizerOptions,
                strokeOptions,
                strokeWidth,
                strokePattern,
                this.targetFrame.Bounds.Location));
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
        this.EnsureNotDisposed();

        // Build composition commands and enforce render-pass ordering while preserving
        // original emission order inside each pass. This preserves overlapping color-font
        // layer compositing semantics (for example emoji mouth/teeth layers).
        Dictionary<int, (IPath Path, int RasterState, int DefinitionKey)> definitionKeyCache = [];
        List<(byte RenderPass, int Sequence, CompositionCommand Command)> entries = new(operations.Count);
        for (int i = 0; i < operations.Count; i++)
        {
            DrawingOperation operation = operations[i];
            DrawingOperation clippedOperation = operation;
            clippedOperation.Path = ApplyClipPaths(operation.Path, drawingOptions.ShapeOptions, clipPaths);
            entries.Add((operation.RenderPass, i, this.CreateCompositionCommand(clippedOperation, drawingOptions, definitionKeyCache)));
        }

        entries.Sort(static (a, b) =>
        {
            int cmp = a.RenderPass.CompareTo(b.RenderPass);
            return cmp != 0 ? cmp : a.Sequence.CompareTo(b.Sequence);
        });

        for (int i = 0; i < entries.Count; i++)
        {
            this.batcher.AddComposition(entries[i].Command);
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
        _ = this.Save(options, [.. clipPaths]);
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
    /// </summary>
    /// <param name="sourceRect">Source rectangle in local canvas coordinates.</param>
    /// <param name="sourceImage">The readback image when available.</param>
    /// <returns><see langword="true"/> when source pixels were resolved.</returns>
    private bool TryCreateProcessSourceImage(Rectangle sourceRect, [NotNullWhen(true)] out Image<TPixel>? sourceImage)
        => this.backend.TryReadRegion(this.configuration, this.targetFrame, sourceRect, out sourceImage);

    /// <summary>
    /// Flattens the path first (reusing any cached curve subdivision), then transforms
    /// the resulting flat points. This avoids discarding cached <see cref="CubicBezierLineSegment"/>
    /// subdivision data that <see cref="IPath.Transform"/> would throw away.
    /// </summary>
    /// <summary>
    /// Flattens a path into linear segments, then transforms the resulting points in place.
    /// This avoids redundant curve subdivision that would occur if we transformed the original
    /// path first (which discards cached flattening) and then flattened again.
    /// </summary>
    /// <param name="path">The path to flatten and transform. The original path is not mutated.</param>
    /// <param name="matrix">The transform matrix to apply to the flattened points.</param>
    /// <returns>
    /// A pre-flattened <see cref="IPath"/> whose points are already transformed.
    /// The returned path owns its point buffers and may mutate them on subsequent transforms.
    /// </returns>
    private static IPath FlattenAndTransform(IPath path, Matrix4x4 matrix)
    {
        List<PreFlattenedPath>? subPaths = null;
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (ISimplePath sp in path.Flatten())
        {
            ReadOnlySpan<PointF> srcPoints = sp.Points.Span;
            if (srcPoints.Length < 2)
            {
                continue;
            }

            PointF[] dstPoints = new PointF[srcPoints.Length];
            float spMinX = float.MaxValue, spMinY = float.MaxValue;
            float spMaxX = float.MinValue, spMaxY = float.MinValue;

            for (int i = 0; i < srcPoints.Length; i++)
            {
                ref PointF dst = ref dstPoints[i];
                dst = PointF.Transform(srcPoints[i], matrix);

                if (dst.X < spMinX)
                {
                    spMinX = dst.X;
                }

                if (dst.Y < spMinY)
                {
                    spMinY = dst.Y;
                }

                if (dst.X > spMaxX)
                {
                    spMaxX = dst.X;
                }

                if (dst.Y > spMaxY)
                {
                    spMaxY = dst.Y;
                }
            }

            RectangleF spBounds = new(spMinX, spMinY, spMaxX - spMinX, spMaxY - spMinY);
            subPaths ??= [];
            subPaths.Add(new PreFlattenedPath(dstPoints, sp.IsClosed, spBounds));

            if (spMinX < minX)
            {
                minX = spMinX;
            }

            if (spMinY < minY)
            {
                minY = spMinY;
            }

            if (spMaxX > maxX)
            {
                maxX = spMaxX;
            }

            if (spMaxY > maxY)
            {
                maxY = spMaxY;
            }
        }

        if (subPaths is null)
        {
            return Path.Empty;
        }

        if (subPaths.Count == 1)
        {
            return subPaths[0];
        }

        RectangleF totalBounds = new(minX, minY, maxX - minX, maxY - minY);
        return new PreFlattenedCompositePath([.. subPaths], totalBounds);
    }

    /// <summary>
    /// Applies all clip paths to a subject path using the provided shape options.
    /// </summary>
    /// <param name="subjectPath">Path to clip.</param>
    /// <param name="shapeOptions">Shape options used for clipping.</param>
    /// <param name="clipPaths">Clip paths to apply.</param>
    /// <returns>The clipped path.</returns>
    private static IPath ApplyClipPaths(IPath subjectPath, ShapeOptions shapeOptions, IReadOnlyList<IPath> clipPaths)
    {
        if (clipPaths.Count == 0)
        {
            return subjectPath;
        }

        return subjectPath.Clip(shapeOptions, clipPaths);
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
            // Composite any active layers back before final flush.
            while (this.layerDataStack.Count > 0)
            {
                this.Flush();
                LayerData<TPixel> layerData = this.layerDataStack.Pop();
                this.batcher = layerData.ParentBatcher;
                ICanvasFrame<TPixel> destination = this.batcher.TargetFrame;
                this.backend.ComposeLayer(
                    this.configuration,
                    layerData.LayerFrame,
                    destination,
                    layerData.LayerBounds.Location,
                    new GraphicsOptions());
                this.backend.ReleaseFrameResources(this.configuration, layerData.LayerFrame);
            }

            this.batcher.FlushCompositions();
        }
        finally
        {
            this.DisposePendingImageResources();

            this.isDisposed = true;
        }
    }

    /// <summary>
    /// Ensures this instance is not disposed.
    /// </summary>
    private void EnsureNotDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

    /// <summary>
    /// Flushes the current layer batcher, composites the layer onto its parent target,
    /// restores the parent batcher, and disposes the layer resources.
    /// </summary>
    /// <param name="layerState">The layer state that was just popped.</param>
    private void CompositeAndPopLayer(DrawingCanvasState layerState)
    {
        // Flush pending commands to the layer surface.
        this.Flush();

        LayerData<TPixel> layerData = this.layerDataStack.Pop();

        // Restore the parent batcher.
        this.batcher = layerData.ParentBatcher;

        // Composite the layer onto the parent batcher's target (which may be another layer
        // in the case of nested SaveLayer calls, or the root target frame).
        ICanvasFrame<TPixel> destination = this.batcher.TargetFrame;
        GraphicsOptions options = layerState.LayerOptions ?? new GraphicsOptions();
        Rectangle bounds = layerState.LayerBounds ?? this.Bounds;
        this.backend.ComposeLayer(
            this.configuration,
            layerData.LayerFrame,
            destination,
            bounds.Location,
            options);

        this.backend.ReleaseFrameResources(this.configuration, layerData.LayerFrame);
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
    /// <param name="definitionKeyCache">Optional cache used to reuse definition key computations.</param>
    /// <returns>A composition command ready for batching.</returns>
    private CompositionCommand CreateCompositionCommand(
        DrawingOperation operation,
        DrawingOptions drawingOptions,
        Dictionary<int, (IPath Path, int RasterState, int DefinitionKey)>? definitionKeyCache = null)
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

        Point destinationOffset = new(
            this.targetFrame.Bounds.X + operation.RenderLocation.X,
            this.targetFrame.Bounds.Y + operation.RenderLocation.Y);

        if (operation.Kind == DrawingOperationKind.Draw)
        {
            Pen pen = operation.Pen!;
            IPath path = operation.Path;

            // Stroke geometry can self-overlap; non-zero winding preserves stroke semantics.
            IntersectionRule intersectionRule = operation.IntersectionRule != IntersectionRule.NonZero
                ? IntersectionRule.NonZero
                : operation.IntersectionRule;

            float halfWidth = pen.StrokeWidth / 2f;
            float maxExtent = halfWidth * (float)Math.Max(pen.StrokeOptions.MiterLimit, 1D);
            RectangleF bounds = path.Bounds;
            bounds = new RectangleF(
                bounds.X - maxExtent + 0.5F,
                bounds.Y - maxExtent + 0.5F,
                bounds.Width + (maxExtent * 2f),
                bounds.Height + (maxExtent * 2f));

            Rectangle interest = Rectangle.FromLTRB(
                (int)MathF.Floor(bounds.Left),
                (int)MathF.Floor(bounds.Top),
                (int)MathF.Ceiling(bounds.Right),
                (int)MathF.Ceiling(bounds.Bottom));

            RasterizerOptions rasterizerOptions = new(
                interest,
                intersectionRule,
                rasterizationMode,
                RasterizerSamplingOrigin.PixelCenter,
                graphicsOptions.AntialiasThreshold);

            return CompositionCommand.CreateStroke(
                path,
                compositeBrush,
                graphicsOptions,
                rasterizerOptions,
                pen.StrokeOptions,
                pen.StrokeWidth,
                pen.StrokePattern,
                destinationOffset,
                definitionKeyCache);
        }
        else
        {
            IPath compositionPath = operation.Path;
            RectangleF bounds = compositionPath.Bounds;

            Rectangle interest = Rectangle.FromLTRB(
                (int)MathF.Floor(bounds.Left),
                (int)MathF.Floor(bounds.Top),
                (int)MathF.Ceiling(bounds.Right),
                (int)MathF.Ceiling(bounds.Bottom));

            RasterizerOptions rasterizerOptions = new(
                interest,
                operation.IntersectionRule,
                rasterizationMode,
                RasterizerSamplingOrigin.PixelBoundary,
                graphicsOptions.AntialiasThreshold);

            return CompositionCommand.Create(
                compositionPath,
                compositeBrush,
                graphicsOptions,
                rasterizerOptions,
                destinationOffset,
                definitionKeyCache);
        }
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

        // Compute the transformed axis-aligned bounds so we know how large the output bitmap must be.
        transformedDestinationRect = TransformRectangle(
            new RectangleF(0, 0, image.Width, image.Height),
            sourceToTransformedCanvas);

        // The transform can produce fractional/max bounds; round up to whole pixels for target allocation.
        Size targetSize = new(
            Math.Max(1, (int)MathF.Ceiling(transformedDestinationRect.Width)),
            Math.Max(1, (int)MathF.Ceiling(transformedDestinationRect.Height)));

        // ImageSharp.Transform expects output coordinates relative to the output bitmap origin (0,0).
        // Shift transformed-canvas coordinates so transformedDestinationRect.Left/Top becomes 0,0.
        Matrix4x4 sourceToTarget = sourceToTransformedCanvas
            * Matrix4x4.CreateTranslation(-transformedDestinationRect.X, -transformedDestinationRect.Y, 0);

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
    /// Computes the axis-aligned bounding rectangle of a transformed rectangle.
    /// </summary>
    /// <param name="rectangle">Input rectangle.</param>
    /// <param name="matrix">Transform matrix.</param>
    /// <returns>Axis-aligned bounds of the transformed rectangle.</returns>
    private static RectangleF TransformRectangle(RectangleF rectangle, Matrix4x4 matrix)
    {
        PointF topLeft = PointF.Transform(new PointF(rectangle.Left, rectangle.Top), matrix);
        PointF topRight = PointF.Transform(new PointF(rectangle.Right, rectangle.Top), matrix);
        PointF bottomLeft = PointF.Transform(new PointF(rectangle.Left, rectangle.Bottom), matrix);
        PointF bottomRight = PointF.Transform(new PointF(rectangle.Right, rectangle.Bottom), matrix);

        float left = MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomLeft.X, bottomRight.X));
        float top = MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomLeft.Y, bottomRight.Y));
        float right = MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomLeft.X, bottomRight.X));
        float bottom = MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomLeft.Y, bottomRight.Y));

        return RectangleF.FromLTRB(left, top, right, bottom);
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
}
