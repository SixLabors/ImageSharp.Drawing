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
        ApplyBarrier? applyBarrier)
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
    /// Gets the command transform.
    /// </summary>
    public Matrix4x4 Transform => this.drawingOptions?.Transform ?? Matrix4x4.Identity;

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
    /// Gets the image processor carried by an Apply command.
    /// </summary>
    public Action<IImageProcessingContext> ApplyOperation => this.applyBarrier?.Operation ?? throw new InvalidOperationException("Only apply commands carry an apply operation.");

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
            null);

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
            null);

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
            null);

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
            null);

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
            null);

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
            null);

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

    private static CompositionCommand CreateApply(
        ApplyBarrier barrier,
        IPath path,
        DrawingOptions drawingOptions,
        in RasterizerOptions rasterizerOptions)
    {
        DrawingOptions applyOptions = drawingOptions.CloneForClearOperation();

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
            barrier);
    }

    private static RasterizerOptions CreateApplyRasterizerOptions(
        IPath path,
        DrawingOptions options)
    {
        GraphicsOptions graphicsOptions = options.GraphicsOptions;
        RasterizationMode rasterizationMode = graphicsOptions.Antialias
            ? RasterizationMode.Antialiased
            : RasterizationMode.Aliased;
        RectangleF pathBounds = path.Bounds;
        Rectangle interest = Rectangle.FromLTRB(
            (int)MathF.Floor(pathBounds.Left),
            (int)MathF.Floor(pathBounds.Top),
            (int)MathF.Ceiling(pathBounds.Right),
            (int)MathF.Ceiling(pathBounds.Bottom));

        return new RasterizerOptions(
            interest,
            options.ShapeOptions.IntersectionRule,
            rasterizationMode,
            graphicsOptions.AntialiasThreshold);
    }
}
