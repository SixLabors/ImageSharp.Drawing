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
    EndLayer = 2
}

/// <summary>
/// One normalized fill-path or layer-based composition command queued by <see cref="DrawingCanvasBatcher{TPixel}"/>.
/// </summary>
/// <remarks>
/// This type carries fill-path commands plus inline layer boundaries.
/// </remarks>
public readonly struct CompositionCommand
{
    private readonly IPath? sourcePath;
    private readonly Brush? brush;
    private readonly DrawingOptions? drawingOptions;
    private readonly GraphicsOptions? layerGraphicsOptions;
    private readonly IReadOnlyList<IPath>? clipPaths;

    private CompositionCommand(
        CompositionCommandKind kind,
        IPath? sourcePath,
        Brush? brush,
        DrawingOptions? drawingOptions,
        GraphicsOptions? layerGraphicsOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Rectangle layerBounds,
        Point destinationOffset,
        IReadOnlyList<IPath>? clipPaths)
    {
        this.Kind = kind;
        this.sourcePath = sourcePath;
        this.brush = brush;
        this.drawingOptions = drawingOptions;
        this.layerGraphicsOptions = layerGraphicsOptions;
        this.RasterizerOptions = rasterizerOptions;
        this.TargetBounds = targetBounds;
        this.LayerBounds = layerBounds;
        this.DestinationOffset = destinationOffset;
        this.clipPaths = clipPaths;
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
    /// Gets graphics options used for composition or layer compositing.
    /// </summary>
    public GraphicsOptions GraphicsOptions => this.drawingOptions?.GraphicsOptions ?? this.layerGraphicsOptions!;

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
    /// Gets the clip paths carried by the command.
    /// </summary>
    public IReadOnlyList<IPath>? ClipPaths => this.clipPaths;

    /// <summary>
    /// Gets the shape options carried by the command.
    /// </summary>
    public ShapeOptions ShapeOptions => this.drawingOptions?.ShapeOptions ?? throw new InvalidOperationException("Layer commands do not carry shape options.");

    /// <summary>
    /// Creates a fill-path composition command.
    /// </summary>
    /// <param name="path">Path in target-local coordinates.</param>
    /// <param name="brush">Brush used during composition.</param>
    /// <param name="drawingOptions">Drawing options (graphics, shape, transform) used during composition.</param>
    /// <param name="rasterizerOptions">Rasterizer options used to generate coverage.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target for this command.</param>
    /// <param name="destinationOffset">Absolute destination offset where coverage is composited.</param>
    /// <param name="clipPaths">Optional clip paths supplied with the command.</param>
    /// <returns>The composition command.</returns>
    public static CompositionCommand Create(
        IPath path,
        Brush brush,
        DrawingOptions drawingOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Point destinationOffset = default,
        IReadOnlyList<IPath>? clipPaths = null)
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
            clipPaths);

    /// <summary>
    /// Creates a begin-layer composition command.
    /// </summary>
    /// <param name="layerBounds">The absolute bounds of the layer.</param>
    /// <param name="graphicsOptions">The compositing options used when the layer closes.</param>
    /// <returns>The begin-layer command.</returns>
    public static CompositionCommand CreateBeginLayer(Rectangle layerBounds, GraphicsOptions graphicsOptions)
        => new(
            CompositionCommandKind.BeginLayer,
            null,
            null,
            null,
            graphicsOptions,
            default,
            layerBounds,
            layerBounds,
            default,
            null);

    /// <summary>
    /// Creates an end-layer composition command.
    /// </summary>
    /// <param name="layerBounds">The absolute bounds of the layer being closed.</param>
    /// <param name="graphicsOptions">The compositing options used by the layer.</param>
    /// <returns>The end-layer command.</returns>
    public static CompositionCommand CreateEndLayer(Rectangle layerBounds, GraphicsOptions graphicsOptions)
        => new(
            CompositionCommandKind.EndLayer,
            null,
            null,
            null,
            graphicsOptions,
            default,
            layerBounds,
            layerBounds,
            default,
            null);
}
