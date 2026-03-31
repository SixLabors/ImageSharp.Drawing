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
    /// A fill or stroked-path command.
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
/// One normalized path- or layer-based composition command queued by <see cref="DrawingCanvasBatcher{TPixel}"/>.
/// </summary>
/// <remarks>
/// This type carries path-backed draw commands plus inline layer boundaries.
/// </remarks>
public readonly struct CompositionCommand
{
    private readonly Pen? pen;
    private readonly IPath? sourcePath;
    private readonly Brush? brush;
    private readonly Matrix4x4 transform;
    private readonly IReadOnlyList<IPath>? clipPaths;
    private readonly ShapeOptions? shapeOptions;

    private CompositionCommand(
        CompositionCommandKind kind,
        IPath? sourcePath,
        Brush? brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Rectangle layerBounds,
        Point destinationOffset,
        Pen? pen,
        Matrix4x4 transform,
        IReadOnlyList<IPath>? clipPaths,
        ShapeOptions? shapeOptions)
    {
        this.Kind = kind;
        this.sourcePath = sourcePath;
        this.brush = brush;
        this.GraphicsOptions = graphicsOptions;
        this.RasterizerOptions = rasterizerOptions;
        this.TargetBounds = targetBounds;
        this.LayerBounds = layerBounds;
        this.DestinationOffset = destinationOffset;
        this.pen = pen;
        this.transform = transform;
        this.clipPaths = clipPaths;
        this.shapeOptions = shapeOptions;
    }

    /// <summary>
    /// Gets the command kind.
    /// </summary>
    public CompositionCommandKind Kind { get; }

    /// <summary>
    /// Gets the stroke metadata for stroke commands.
    /// </summary>
    public readonly Pen? Pen => this.pen;

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
    /// Gets graphics options used for composition or layer compositing.
    /// </summary>
    public GraphicsOptions GraphicsOptions { get; }

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
    public Matrix4x4 Transform => this.transform;

    /// <summary>
    /// Gets the clip paths carried by the command.
    /// </summary>
    public IReadOnlyList<IPath>? ClipPaths => this.clipPaths;

    /// <summary>
    /// Gets the shape options carried by the command.
    /// </summary>
    public ShapeOptions ShapeOptions => this.shapeOptions ?? throw new InvalidOperationException("Layer commands do not carry shape options.");

    /// <summary>
    /// Creates a path-backed composition command.
    /// </summary>
    /// <param name="path">Path in target-local coordinates.</param>
    /// <param name="brush">Brush used during composition.</param>
    /// <param name="graphicsOptions">Graphics options used for composition.</param>
    /// <param name="rasterizerOptions">Rasterizer options used to generate coverage.</param>
    /// <param name="shapeOptions">Shape options for clip operations.</param>
    /// <param name="transform">Transform matrix supplied with the command.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target for this command.</param>
    /// <param name="destinationOffset">Absolute destination offset where coverage is composited.</param>
    /// <param name="pen">Optional pen for stroked-path commands.</param>
    /// <param name="clipPaths">Optional clip paths supplied with the command.</param>
    /// <returns>The composition command.</returns>
    public static CompositionCommand Create(
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        ShapeOptions shapeOptions,
        Matrix4x4 transform,
        Rectangle targetBounds,
        Point destinationOffset = default,
        Pen? pen = null,
        IReadOnlyList<IPath>? clipPaths = null)
        => new(
            CompositionCommandKind.FillLayer,
            path,
            brush,
            graphicsOptions,
            in rasterizerOptions,
            targetBounds,
            default,
            destinationOffset,
            pen,
            transform,
            clipPaths,
            shapeOptions);

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
            graphicsOptions,
            default,
            layerBounds,
            layerBounds,
            default,
            null,
            Matrix4x4.Identity,
            null,
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
            graphicsOptions,
            default,
            layerBounds,
            layerBounds,
            default,
            null,
            Matrix4x4.Identity,
            null,
            null);
}
