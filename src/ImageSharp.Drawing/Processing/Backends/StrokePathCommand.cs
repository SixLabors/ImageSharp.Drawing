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
    private readonly Matrix4x4 transform;
    private readonly IReadOnlyList<IPath>? clipPaths;
    private readonly ShapeOptions shapeOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="StrokePathCommand"/> struct.
    /// </summary>
    /// <param name="sourcePath">The source stroke path.</param>
    /// <param name="brush">The brush used to shade the stroke.</param>
    /// <param name="graphicsOptions">The graphics options used during composition.</param>
    /// <param name="rasterizerOptions">The rasterizer options used to generate coverage.</param>
    /// <param name="shapeOptions">The shape options used for clipping.</param>
    /// <param name="transform">The drawing transform applied after stroke expansion.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target.</param>
    /// <param name="destinationOffset">The absolute destination offset of the command.</param>
    /// <param name="pen">The stroke metadata.</param>
    /// <param name="clipPaths">Optional clip paths supplied with the command.</param>
    public StrokePathCommand(
        IPath sourcePath,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        ShapeOptions shapeOptions,
        Matrix4x4 transform,
        Rectangle targetBounds,
        Point destinationOffset,
        Pen pen,
        IReadOnlyList<IPath>? clipPaths = null)
    {
        this.sourcePath = sourcePath;
        this.shapeOptions = shapeOptions;
        this.transform = transform;
        this.clipPaths = clipPaths;
        this.Brush = brush;
        this.GraphicsOptions = graphicsOptions;
        this.RasterizerOptions = rasterizerOptions;
        this.TargetBounds = targetBounds;
        this.DestinationOffset = destinationOffset;
        this.Pen = pen;
    }

    /// <summary>
    /// Gets the brush used during composition.
    /// </summary>
    public Brush Brush { get; }

    /// <summary>
    /// Gets the graphics options used during composition.
    /// </summary>
    public GraphicsOptions GraphicsOptions { get; }

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
    public Matrix4x4 Transform => this.transform;

    /// <summary>
    /// Gets the optional clip paths carried by the command.
    /// </summary>
    public IReadOnlyList<IPath>? ClipPaths => this.clipPaths;

    /// <summary>
    /// Gets the shape options carried by the command.
    /// </summary>
    public ShapeOptions ShapeOptions => this.shapeOptions;
}
