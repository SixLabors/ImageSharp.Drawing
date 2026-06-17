// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One stroked path command queued by the canvas batcher.
/// </summary>
public readonly struct StrokePathCommand
{
    private readonly IPath sourcePath;
    private readonly DrawingOptions drawingOptions;
    private readonly DrawingCanvasLayer? ownerLayer;
    private readonly IReadOnlyList<IPath>? clipPaths;
    private readonly IntersectionRule clipIntersectionRule;

    /// <summary>
    /// Initializes a new instance of the <see cref="StrokePathCommand"/> struct.
    /// </summary>
    /// <param name="sourcePath">The source stroke path.</param>
    /// <param name="brush">The brush used to shade the stroke.</param>
    /// <param name="drawingOptions">The drawing options (graphics, shape, transform) used during composition.</param>
    /// <param name="rasterizerOptions">The rasterizer options used to generate coverage.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target.</param>
    /// <param name="destinationOffset">The absolute destination offset of the command.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <param name="clipPaths">Optional clip paths supplied with the command.</param>
    /// <param name="clipIntersectionRule">The fill rule used to interpret the clip paths.</param>
    /// <param name="isInsideLayer">True if the command was recorded inside a layer.</param>
    public StrokePathCommand(
        IPath sourcePath,
        Brush brush,
        DrawingOptions drawingOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Point destinationOffset,
        Pen pen,
        IReadOnlyList<IPath>? clipPaths,
        IntersectionRule clipIntersectionRule,
        bool isInsideLayer)
        : this(
            sourcePath,
            brush,
            drawingOptions,
            in rasterizerOptions,
            targetBounds,
            destinationOffset,
            pen,
            clipPaths,
            clipIntersectionRule,
            isInsideLayer,
            null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StrokePathCommand"/> struct with the owning layer state recorded by the canvas.
    /// </summary>
    /// <param name="sourcePath">The source stroke path.</param>
    /// <param name="brush">The brush used to shade the stroke.</param>
    /// <param name="drawingOptions">The drawing options (graphics, shape, transform) used during composition.</param>
    /// <param name="rasterizerOptions">The rasterizer options used to generate coverage.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target.</param>
    /// <param name="destinationOffset">The absolute destination offset of the command.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <param name="clipPaths">Optional clip paths supplied with the command.</param>
    /// <param name="clipIntersectionRule">The fill rule used to interpret the clip paths.</param>
    /// <param name="isInsideLayer">True if the command was recorded inside a layer.</param>
    /// <param name="ownerLayer">The layer that owned this command when it was recorded.</param>
    internal StrokePathCommand(
        IPath sourcePath,
        Brush brush,
        DrawingOptions drawingOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Point destinationOffset,
        Pen pen,
        IReadOnlyList<IPath>? clipPaths,
        IntersectionRule clipIntersectionRule,
        bool isInsideLayer,
        DrawingCanvasLayer? ownerLayer)
    {
        this.sourcePath = sourcePath;
        this.drawingOptions = drawingOptions;
        this.ownerLayer = ownerLayer;
        this.clipPaths = clipPaths;
        this.clipIntersectionRule = clipIntersectionRule;
        this.Brush = brush;
        this.RasterizerOptions = rasterizerOptions;
        this.TargetBounds = targetBounds;
        this.DestinationOffset = destinationOffset;
        this.Pen = pen;
        this.IsInsideLayer = isInsideLayer;
    }

    /// <summary>
    /// Gets the brush used during composition.
    /// </summary>
    public Brush Brush { get; }

    /// <summary>
    /// Gets the drawing options carried by the command.
    /// </summary>
    public DrawingOptions DrawingOptions => this.drawingOptions;

    /// <summary>
    /// Gets the graphics options used during composition.
    /// </summary>
    public GraphicsOptions GraphicsOptions => this.drawingOptions.GraphicsOptions;

    /// <summary>
    /// Gets the rasterizer options used to generate coverage.
    /// </summary>
    public RasterizerOptions RasterizerOptions { get; }

    /// <summary>
    /// Gets the absolute bounds of the logical target for this command.
    /// </summary>
    public Rectangle TargetBounds { get; }

    /// <summary>
    /// Gets the absolute destination offset where the local coverage should be composited.
    /// </summary>
    public Point DestinationOffset { get; }

    /// <summary>
    /// Gets the stroke metadata for this command.
    /// </summary>
    public Pen Pen { get; }

    /// <summary>
    /// Gets the source stroke path.
    /// </summary>
    public IPath SourcePath => this.sourcePath;

    /// <summary>
    /// Gets the drawing transform.
    /// </summary>
    public Matrix4x4 Transform => this.drawingOptions.Transform;

    /// <summary>
    /// Gets the optional clip paths carried by the command.
    /// </summary>
    public IReadOnlyList<IPath>? ClipPaths => this.clipPaths;

    /// <summary>
    /// Gets the fill rule used to interpret the clip paths.
    /// </summary>
    public IntersectionRule ClipIntersectionRule => this.clipIntersectionRule;

    /// <summary>
    /// Gets the shape options carried by the command.
    /// </summary>
    public ShapeOptions ShapeOptions => this.drawingOptions.ShapeOptions;

    /// <summary>
    /// Gets a value indicating whether the command was recorded inside a layer.
    /// </summary>
    public bool IsInsideLayer { get; }

    /// <summary>
    /// Gets the layer state for the layer that owned this command when it was recorded.
    /// </summary>
    internal DrawingCanvasLayer? OwnerLayer => this.ownerLayer;
}
