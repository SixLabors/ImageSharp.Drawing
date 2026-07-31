// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Identifies the flush-time role carried by a <see cref="CompositionCommand"/>.
/// </summary>
public enum CompositionCommandKind : byte
{
    /// <summary>
    /// A fill-path command.
    /// </summary>
    FillLayer = 0,

    /// <summary>
    /// Starts an isolated compositing layer.
    /// </summary>
    BeginLayer = 1,

    /// <summary>
    /// Ends the most recently opened layer.
    /// </summary>
    EndLayer = 2,

    /// <summary>
    /// Applies an image processor to the current target before later commands are rendered.
    /// </summary>
    Apply = 3,

    /// <summary>
    /// Starts a clip scope.
    /// </summary>
    BeginClip = 4,

    /// <summary>
    /// Ends the most recently opened clip scope.
    /// </summary>
    EndClip = 5
}

/// <summary>
/// One normalized fill-path or layer-based composition command queued for backend execution.
/// </summary>
/// <remarks>
/// This type carries fill-path commands plus inline layer boundaries.
/// </remarks>
public readonly struct CompositionCommand
{
    private readonly IPath? sourcePath;
    private readonly Brush? brush;
    private readonly DrawingOptions? drawingOptions;
    private readonly DrawingCanvasLayer? layer;
    private readonly DrawingClipDescriptor? clipDescriptor;
    private readonly ApplyBarrier? applyBarrier;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositionCommand"/> struct.
    /// </summary>
    /// <param name="kind">The command kind.</param>
    /// <param name="sourcePath">The source path, or <see langword="null"/> for commands without geometry.</param>
    /// <param name="brush">The composition brush, or <see langword="null"/> for commands without a brush.</param>
    /// <param name="drawingOptions">The drawing options, or <see langword="null"/> for commands without them.</param>
    /// <param name="layer">The shared layer state, or <see langword="null"/> for non-layer commands.</param>
    /// <param name="rasterizerOptions">The rasterizer options used to generate coverage.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target.</param>
    /// <param name="layerBounds">The absolute layer bounds for begin/end-layer commands.</param>
    /// <param name="destinationOffset">The absolute destination offset for composited coverage.</param>
    /// <param name="clipDescriptor">The clip descriptor, or <see langword="null"/> for non begin-clip commands.</param>
    /// <param name="isInsideLayer">True if the command was recorded inside a layer.</param>
    /// <param name="applyBarrier">The apply barrier, or <see langword="null"/> for non-apply commands.</param>
    /// <param name="subPixelOffset">The fractional translation composed into <see cref="Transform"/>.</param>
    private CompositionCommand(
        CompositionCommandKind kind,
        IPath? sourcePath,
        Brush? brush,
        DrawingOptions? drawingOptions,
        DrawingCanvasLayer? layer,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Rectangle layerBounds,
        Point destinationOffset,
        DrawingClipDescriptor? clipDescriptor,
        bool isInsideLayer,
        ApplyBarrier? applyBarrier,
        Vector2 subPixelOffset)
    {
        this.Kind = kind;
        this.sourcePath = sourcePath;
        this.brush = brush;
        this.drawingOptions = drawingOptions;
        this.layer = layer;
        this.RasterizerOptions = rasterizerOptions;
        this.TargetBounds = targetBounds;
        this.LayerBounds = layerBounds;
        this.DestinationOffset = destinationOffset;
        this.clipDescriptor = clipDescriptor;
        this.IsInsideLayer = isInsideLayer;
        this.applyBarrier = applyBarrier;
        this.SubPixelOffset = subPixelOffset;
    }

    /// <summary>
    /// Gets the command kind.
    /// </summary>
    public CompositionCommandKind Kind { get; }

    /// <summary>
    /// Gets the absolute bounds of the logical target for this command.
    /// </summary>
    public Rectangle TargetBounds { get; }

    /// <summary>
    /// Gets the absolute bounds of the layer opened by this command.
    /// </summary>
    /// <remarks>
    /// Only meaningful for <see cref="CompositionCommandKind.BeginLayer"/> and
    /// <see cref="CompositionCommandKind.EndLayer"/>.
    /// </remarks>
    public Rectangle LayerBounds { get; }

    /// <summary>
    /// Gets the brush used during composition.
    /// </summary>
    public Brush Brush => this.brush ?? throw new InvalidOperationException("Layer commands do not carry a brush.");

    /// <summary>
    /// Gets the drawing options carried by the command.
    /// </summary>
    public DrawingOptions DrawingOptions => this.drawingOptions ?? throw new InvalidOperationException("Layer commands do not carry drawing options.");

    /// <summary>
    /// Gets graphics options used by layer compositing commands.
    /// </summary>
    /// <remarks>
    /// Only valid for commands that carry layer state; accessing it on other commands throws.
    /// </remarks>
    public GraphicsOptions LayerOptions => this.Layer.Options;

    /// <summary>
    /// Gets rasterizer options used to generate coverage.
    /// </summary>
    public RasterizerOptions RasterizerOptions { get; }

    /// <summary>
    /// Gets the absolute destination offset where the local coverage should be composited.
    /// </summary>
    public Point DestinationOffset { get; }

    /// <summary>
    /// Gets the source path carried by the command.
    /// </summary>
    public IPath SourcePath => this.sourcePath ?? throw new InvalidOperationException("Layer commands do not carry path geometry.");

    /// <summary>
    /// Gets the fractional translation applied after the drawing options transform. Glyph
    /// geometry rides an integer destination offset; the sub-pixel remainder travels here so
    /// the queued command does not need per-operation drawing options to carry it.
    /// </summary>
    public Vector2 SubPixelOffset { get; }

    /// <summary>
    /// Gets the command transform: the drawing options transform followed by the sub-pixel
    /// translation.
    /// </summary>
    public Matrix4x4 Transform
    {
        get
        {
            Matrix4x4 transform = this.drawingOptions?.Transform ?? Matrix4x4.Identity;
            if (this.SubPixelOffset == Vector2.Zero)
            {
                return transform;
            }

            return transform.IsIdentity
                ? Matrix4x4.CreateTranslation(this.SubPixelOffset.X, this.SubPixelOffset.Y, 0F)
                : transform * Matrix4x4.CreateTranslation(this.SubPixelOffset.X, this.SubPixelOffset.Y, 0F);
        }
    }

    /// <summary>
    /// Gets the clip descriptor opened by a <see cref="CompositionCommandKind.BeginClip"/> command.
    /// </summary>
    /// <remarks>
    /// The ordered begin/end-clip command stream is the single source of truth for clipping.
    /// Draw commands do not carry clip state; backends resolve the active clip stack from the
    /// stream commands surrounding each draw.
    /// </remarks>
    public DrawingClipDescriptor ClipDescriptor => this.clipDescriptor ?? throw new InvalidOperationException("Only begin-clip commands carry a clip descriptor.");

    /// <summary>
    /// Gets a value indicating whether the command was recorded inside a layer.
    /// </summary>
    public bool IsInsideLayer { get; }

    /// <summary>
    /// Gets a value indicating whether this layer command must be rendered as an isolated target for Apply.
    /// </summary>
    public bool RequiresScopedApply => this.layer?.RequiresScopedApply ?? false;

    /// <summary>
    /// Gets the canvas bounds available to an Apply command.
    /// </summary>
    public Rectangle ApplyCanvasBounds => this.applyBarrier?.CanvasBounds ?? throw new InvalidOperationException("Only apply commands carry apply canvas bounds.");

    /// <summary>
    /// Gets the local bounds within which an Apply command writes its processed output.
    /// </summary>
    public RectangleF ApplyOutputBounds => this.ApplyBarrier.OutputBounds;

    /// <summary>
    /// Gets the image processor carried by an Apply command.
    /// </summary>
    public Action<IImageProcessingContext> ApplyOperation => this.applyBarrier?.Operation ?? throw new InvalidOperationException("Only apply commands carry an apply operation.");

    /// <summary>
    /// Gets the layer effect carried by an Apply command, or <see langword="null"/> when the command represents a direct Apply operation.
    /// </summary>
    public LayerEffect? ApplyEffect => this.ApplyBarrier.Effect;

    /// <summary>
    /// Gets the offset subtracted from an Apply command's write rectangle when reading the source
    /// pixels, so a write-back recorded at an offset still reads the pre-offset region.
    /// </summary>
    public Point ApplyWriteBackOffset => this.applyBarrier?.WriteBackOffset ?? throw new InvalidOperationException("Only apply commands carry an apply write-back offset.");

    /// <summary>
    /// Gets the apply barrier carried by an <see cref="CompositionCommandKind.Apply"/> command.
    /// </summary>
    internal ApplyBarrier ApplyBarrier => this.applyBarrier ?? throw new InvalidOperationException("Only apply commands carry an apply barrier.");

    /// <summary>
    /// Gets the layer state carried by a layer command.
    /// </summary>
    internal DrawingCanvasLayer Layer => this.layer ?? throw new InvalidOperationException("Only layer commands carry layer state.");

    /// <summary>
    /// Gets the layer state for the layer that owned this command when it was recorded.
    /// </summary>
    internal DrawingCanvasLayer? OwnerLayer => this.layer;

    /// <summary>
    /// Creates a fill-path composition command.
    /// </summary>
    /// <param name="path">Path in target-local coordinates.</param>
    /// <param name="brush">Brush used during composition.</param>
    /// <param name="drawingOptions">Drawing options (graphics, shape, transform) used during composition.</param>
    /// <param name="rasterizerOptions">Rasterizer options used to generate coverage.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target for this command.</param>
    /// <param name="destinationOffset">Absolute destination offset where coverage is composited.</param>
    /// <param name="isInsideLayer">True if the command was recorded inside a layer.</param>
    /// <returns>The composition command.</returns>
    internal static CompositionCommand Create(
        IPath path,
        Brush brush,
        DrawingOptions drawingOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Point destinationOffset,
        bool isInsideLayer)
        => new(
            CompositionCommandKind.FillLayer,
            path,
            brush,
            drawingOptions,
            null,
            in rasterizerOptions,
            targetBounds,
            default,
            destinationOffset,
            null,
            isInsideLayer,
            null,
            Vector2.Zero);

    /// <summary>
    /// Creates a fill-path composition command with the owning layer state recorded by the canvas.
    /// </summary>
    /// <param name="path">Path in target-local coordinates.</param>
    /// <param name="brush">Brush used during composition.</param>
    /// <param name="drawingOptions">Drawing options (graphics, shape, transform) used during composition.</param>
    /// <param name="rasterizerOptions">Rasterizer options used to generate coverage.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target for this command.</param>
    /// <param name="destinationOffset">Absolute destination offset where coverage is composited.</param>
    /// <param name="layer">The layer that owned this command when it was recorded.</param>
    /// <returns>The composition command.</returns>
    internal static CompositionCommand Create(
        IPath path,
        Brush brush,
        DrawingOptions drawingOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Point destinationOffset,
        DrawingCanvasLayer? layer)
        => Create(
            path,
            brush,
            drawingOptions,
            in rasterizerOptions,
            targetBounds,
            destinationOffset,
            layer,
            Vector2.Zero);

    /// <summary>
    /// Creates a fill-path composition command carrying a sub-pixel translation alongside the
    /// owning layer state recorded by the canvas.
    /// </summary>
    /// <param name="path">Path in target-local coordinates.</param>
    /// <param name="brush">Brush used during composition.</param>
    /// <param name="drawingOptions">Drawing options (graphics, shape, transform) used during composition.</param>
    /// <param name="rasterizerOptions">Rasterizer options used to generate coverage.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target for this command.</param>
    /// <param name="destinationOffset">Absolute destination offset where coverage is composited.</param>
    /// <param name="layer">The layer that owned this command when it was recorded.</param>
    /// <param name="subPixelOffset">The fractional translation composed into <see cref="Transform"/>.</param>
    /// <returns>The composition command.</returns>
    internal static CompositionCommand Create(
        IPath path,
        Brush brush,
        DrawingOptions drawingOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Point destinationOffset,
        DrawingCanvasLayer? layer,
        Vector2 subPixelOffset)
        => new(
            CompositionCommandKind.FillLayer,
            path,
            brush,
            drawingOptions,
            layer,
            in rasterizerOptions,
            targetBounds,
            default,
            destinationOffset,
            null,
            layer is not null,
            null,
            subPixelOffset);

    /// <summary>
    /// Creates a begin-layer composition command with shared layer state.
    /// </summary>
    /// <param name="layerBounds">The absolute bounds of the layer.</param>
    /// <param name="layer">The layer state shared by the begin and end commands.</param>
    /// <returns>The begin-layer command.</returns>
    internal static CompositionCommand CreateBeginLayer(
        Rectangle layerBounds,
        DrawingCanvasLayer layer)
        => new(
            CompositionCommandKind.BeginLayer,
            null,
            null,
            null,
            layer,
            default,
            layerBounds,
            layerBounds,
            default,
            null,
            false,
            null,
            Vector2.Zero);

    /// <summary>
    /// Creates an end-layer composition command with shared layer state.
    /// </summary>
    /// <param name="layerBounds">The absolute bounds of the layer being closed.</param>
    /// <param name="layer">The layer state shared by the begin and end commands.</param>
    /// <returns>The end-layer command.</returns>
    internal static CompositionCommand CreateEndLayer(Rectangle layerBounds, DrawingCanvasLayer layer)
        => new(
            CompositionCommandKind.EndLayer,
            null,
            null,
            null,
            layer,
            default,
            layerBounds,
            layerBounds,
            default,
            null,
            false,
            null,
            Vector2.Zero);

    /// <summary>
    /// Creates a begin-clip composition command.
    /// </summary>
    /// <param name="descriptor">The clip descriptor opened by the command.</param>
    /// <param name="destinationOffset">Absolute destination offset used to place clip geometry.</param>
    /// <returns>The begin-clip command.</returns>
    internal static CompositionCommand CreateBeginClip(DrawingClipDescriptor descriptor, Point destinationOffset)
        => new(
            CompositionCommandKind.BeginClip,
            null,
            null,
            null,
            null,
            default,
            default,
            default,
            destinationOffset,
            descriptor,
            false,
            null,
            Vector2.Zero);

    /// <summary>
    /// Creates an end-clip composition command.
    /// </summary>
    /// <returns>The end-clip command.</returns>
    internal static CompositionCommand CreateEndClip()
        => new(
            CompositionCommandKind.EndClip,
            null,
            null,
            null,
            null,
            default,
            default,
            default,
            default,
            null,
            false,
            null,
            Vector2.Zero);

    /// <summary>
    /// Creates an apply composition command.
    /// </summary>
    /// <param name="barrier">The apply barrier to execute.</param>
    /// <returns>The apply command.</returns>
    internal static CompositionCommand CreateApply(ApplyBarrier barrier)
    {
        RasterizerOptions rasterizerOptions = CreateApplyRasterizerOptions(barrier.Path, barrier.Options);

        return CreateApply(barrier, barrier.Path, barrier.Options, in rasterizerOptions);
    }

    /// <summary>
    /// Creates an apply composition command from precomputed rasterizer options.
    /// </summary>
    /// <param name="barrier">The apply barrier to execute.</param>
    /// <param name="path">The closed path defining the processed region.</param>
    /// <param name="drawingOptions">The drawing options captured when the barrier was recorded.</param>
    /// <param name="rasterizerOptions">The rasterizer options used to generate coverage.</param>
    /// <returns>The apply command.</returns>
    private static CompositionCommand CreateApply(
        ApplyBarrier barrier,
        IPath path,
        DrawingOptions drawingOptions,
        in RasterizerOptions rasterizerOptions)
    {
        // By default Apply replaces the processed region rather than blending over it, so the
        // command carries options forced to Src alpha composition at full blend percentage. A
        // barrier recorded with explicit write-back options composites the processed pixels back
        // through those options instead, against the still-untouched region content.
        DrawingOptions applyOptions = barrier.WriteBackOptions is null
            ? drawingOptions.CloneForClearOperation()
            : new DrawingOptions(barrier.WriteBackOptions.DeepClone(), drawingOptions.IntersectionRule, drawingOptions.Transform, drawingOptions.TextContrast);

        // The write-back offset translates in device space, after the recorded transform, so the
        // processed pixels land offset from where they were read; the read side subtracts the same
        // offset when snapshotting the source region.
        if (barrier.WriteBackOffset != default)
        {
            Matrix4x4 translated = applyOptions.Transform * Matrix4x4.CreateTranslation(barrier.WriteBackOffset.X, barrier.WriteBackOffset.Y, 0F);
            applyOptions = new DrawingOptions(applyOptions.GraphicsOptions, applyOptions.IntersectionRule, translated, applyOptions.TextContrast);
        }

        return new CompositionCommand(
            CompositionCommandKind.Apply,
            path,
            null,
            applyOptions,
            barrier.OwnerLayer,
            in rasterizerOptions,
            barrier.TargetBounds,
            default,
            barrier.DestinationOffset,
            null,
            barrier.IsInsideLayer,
            barrier,
            Vector2.Zero);
    }

    /// <summary>
    /// Creates the rasterizer options used to generate coverage for an apply command region.
    /// </summary>
    /// <param name="path">The closed path defining the processed region.</param>
    /// <param name="options">The drawing options captured when the barrier was recorded.</param>
    /// <returns>The rasterizer options.</returns>
    private static RasterizerOptions CreateApplyRasterizerOptions(
        IPath path,
        DrawingOptions options)
    {
        GraphicsOptions graphicsOptions = options.GraphicsOptions;
        RasterizationMode rasterizationMode = graphicsOptions.Antialias
            ? RasterizationMode.Antialiased
            : RasterizationMode.Aliased;

        // Interest rectangles are transform-baked and destination-offset-relative, matching the
        // fill and stroke producers. Snap outward to whole pixels so fractional path bounds
        // never crop coverage.
        RectangleF pathBounds = options.Transform == Matrix4x4.Identity
            ? path.Bounds
            : RectangleF.Transform(path.Bounds, options.Transform);

        Rectangle interest = Rectangle.FromLTRB(
            (int)MathF.Floor(pathBounds.Left),
            (int)MathF.Floor(pathBounds.Top),
            (int)MathF.Ceiling(pathBounds.Right),
            (int)MathF.Ceiling(pathBounds.Bottom));

        return new RasterizerOptions(
            interest,
            options.IntersectionRule,
            rasterizationMode,
            graphicsOptions.AntialiasThreshold);
    }
}
