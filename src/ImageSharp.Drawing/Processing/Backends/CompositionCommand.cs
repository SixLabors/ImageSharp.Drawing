// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Identifies the flush-time operation carried by a <see cref="CompositionCommand"/>.
/// </summary>
public enum CompositionCommandKind : byte
{
    /// <summary>
    /// A prepared fill or stroke command.
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
/// One normalized composition command queued by <see cref="DrawingCanvasBatcher{TPixel}"/>.
/// After <see cref="Prepare"/> is called by the batcher, every <see cref="CompositionCommandKind.FillLayer"/>
/// command is an immutable prepared fill ready for backend execution.
/// </summary>
public struct CompositionCommand
{
    private static readonly Brush SentinelBrush = new SolidBrush(Color.Transparent);
    private static readonly GraphicsOptions SentinelGraphicsOptions = new();
    private static readonly ShapeOptions SentinelShapeOptions = new();

    private readonly Pen? pen;
    private readonly IPath sourcePath;
    private readonly Matrix4x4 transform;
    private readonly IReadOnlyList<IPath>? clipPaths;
    private readonly ShapeOptions shapeOptions;

    private CompositionCommand(
        CompositionCommandKind kind,
        int definitionKey,
        IPath sourcePath,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        Rectangle targetBounds,
        Rectangle layerBounds,
        Point destinationOffset,
        Pen? pen,
        Matrix4x4 transform,
        IReadOnlyList<IPath>? clipPaths,
        ShapeOptions shapeOptions)
    {
        this.Kind = kind;
        this.DefinitionKey = definitionKey;
        this.sourcePath = sourcePath;
        this.PreparedPath = null;
        this.IsVisible = false;
        this.Brush = brush;
        this.GraphicsOptions = graphicsOptions;
        this.RasterizerOptions = rasterizerOptions;
        this.TargetBounds = targetBounds;
        this.LayerBounds = layerBounds;
        this.DestinationOffset = destinationOffset;
        this.DestinationRegion = default;
        this.SourceOffset = default;
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
    /// Gets a stable definition key used for composition-level caching.
    /// Recomputed by <see cref="Prepare"/> after path replacement.
    /// </summary>
    public int DefinitionKey { get; private set; }

    /// <summary>
    /// Gets the prepared path to rasterize in target-local coordinates.
    /// This is the post-transform, post-stroke, post-clip path populated by <see cref="Prepare"/>.
    /// Backends walk this path directly to produce their native rasterization format.
    /// </summary>
    public IPath? PreparedPath { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this command is visible after clipping to its logical target.
    /// Populated by <see cref="Prepare"/>.
    /// </summary>
    public bool IsVisible { get; private set; }

    /// <summary>
    /// Gets the absolute bounds of the logical target for this command.
    /// For fills this is the command target frame; for begin-layer commands this is the layer bounds.
    /// </summary>
    public Rectangle TargetBounds { get; }

    /// <summary>
    /// Gets the absolute bounds of the layer opened by this command.
    /// Only meaningful for <see cref="CompositionCommandKind.BeginLayer"/>.
    /// </summary>
    public Rectangle LayerBounds { get; }

    /// <summary>
    /// Gets the destination region in target-local coordinates.
    /// Populated by <see cref="Prepare"/>.
    /// </summary>
    public Rectangle DestinationRegion { get; private set; }

    /// <summary>
    /// Gets the source offset into the coverage map.
    /// Populated by <see cref="Prepare"/>.
    /// </summary>
    public Point SourceOffset { get; private set; }

    /// <summary>
    /// Gets the brush used during composition.
    /// After <see cref="Prepare"/> this is transformed to match the path coordinate space.
    /// </summary>
    public Brush Brush { get; private set; }

    /// <summary>
    /// Gets brush bounds used for applicator creation.
    /// </summary>
    public Rectangle BrushBounds { get; private set; }

    /// <summary>
    /// Gets graphics options used for composition or layer compositing.
    /// </summary>
    public GraphicsOptions GraphicsOptions { get; }

    /// <summary>
    /// Gets rasterizer options used to generate coverage.
    /// After <see cref="Prepare"/> the interest rect reflects the final path bounds.
    /// </summary>
    public RasterizerOptions RasterizerOptions { get; private set; }

    /// <summary>
    /// Gets the absolute destination offset where the local coverage should be composited.
    /// </summary>
    public Point DestinationOffset { get; }

    /// <summary>
    /// Creates a fill composition command.
    /// </summary>
    /// <param name="path">Path in target-local coordinates.</param>
    /// <param name="brush">Brush used during composition.</param>
    /// <param name="graphicsOptions">Graphics options used for composition.</param>
    /// <param name="rasterizerOptions">Rasterizer options used to generate coverage.</param>
    /// <param name="shapeOptions">Shape options for clip operations.</param>
    /// <param name="transform">Transform matrix to apply during preparation.</param>
    /// <param name="targetBounds">The absolute bounds of the logical target for this command.</param>
    /// <param name="destinationOffset">Absolute destination offset where coverage is composited.</param>
    /// <param name="pen">Optional pen for stroke commands. The batcher expands strokes to fills.</param>
    /// <param name="clipPaths">Optional clip paths to apply during preparation.</param>
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
    {
        int definitionKey = ComputeCoverageDefinitionKey(path, in rasterizerOptions);

        return new(
            CompositionCommandKind.FillLayer,
            definitionKey,
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
    }

    /// <summary>
    /// Creates a begin-layer composition command.
    /// </summary>
    /// <param name="layerBounds">The absolute bounds of the layer.</param>
    /// <param name="graphicsOptions">The compositing options used when the layer closes.</param>
    /// <returns>The begin-layer command.</returns>
    public static CompositionCommand CreateBeginLayer(Rectangle layerBounds, GraphicsOptions graphicsOptions)
        => new(
            CompositionCommandKind.BeginLayer,
            0,
            EmptyPath.ClosedPath,
            SentinelBrush,
            graphicsOptions,
            default,
            layerBounds,
            layerBounds,
            default,
            null,
            Matrix4x4.Identity,
            null,
            SentinelShapeOptions);

    /// <summary>
    /// Creates an end-layer composition command.
    /// </summary>
    /// <returns>The end-layer command.</returns>
    public static CompositionCommand CreateEndLayer()
        => new(
            CompositionCommandKind.EndLayer,
            0,
            EmptyPath.ClosedPath,
            SentinelBrush,
            SentinelGraphicsOptions,
            default,
            default,
            default,
            default,
            null,
            Matrix4x4.Identity,
            null,
            SentinelShapeOptions);

    /// <summary>
    /// Prepares this command for backend execution. Expands strokes to fills,
    /// clips, transforms the source path, and clips to the logical target.
    /// After this call the command is a fill with an immutable prepared path
    /// and pre-computed visibility against the target.
    /// </summary>
    internal void Prepare()
    {
        if (this.Kind is not CompositionCommandKind.FillLayer)
        {
            this.IsVisible = true;
            return;
        }

        // Expand the queued draw into the final fill geometry the backend will consume.
        IPath preparedPath = this.BuildPreparedPath();
        this.PreparedPath = preparedPath;

        // Transform the brush to match the path coordinate space.
        if (!this.transform.IsIdentity)
        {
            this.Brush = this.Brush.Transform(this.transform);
        }

        // Recompute interest, brush bounds, and definition key from the final path.
        RasterizerOptions old = this.RasterizerOptions;
        RectangleF bounds = preparedPath.Bounds;

        // Pixel-center stroke sampling nudges the realized bounds by half a pixel.
        if (old.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter)
        {
            bounds = new RectangleF(bounds.X + 0.5F, bounds.Y + 0.5F, bounds.Width, bounds.Height);
        }

        // Coverage is generated in path-local interest coordinates.
        Rectangle interest = Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));

        this.RasterizerOptions = new RasterizerOptions(
            interest,
            old.IntersectionRule,
            old.RasterizationMode,
            old.SamplingOrigin,
            old.AntialiasThreshold);

        this.BrushBounds = new Rectangle(
            interest.X + this.DestinationOffset.X,
            interest.Y + this.DestinationOffset.Y,
            interest.Width,
            interest.Height);

        RasterizerOptions updated = this.RasterizerOptions;
        this.DefinitionKey = ComputeCoverageDefinitionKey(preparedPath, in updated);

        // Move the interest rect into absolute destination space, then clip it back to the command target.
        Rectangle commandDestination = new(
            this.DestinationOffset.X + interest.X,
            this.DestinationOffset.Y + interest.Y,
            interest.Width,
            interest.Height);

        Rectangle clippedDestination = Rectangle.Intersect(this.TargetBounds, commandDestination);
        if (clippedDestination.Width <= 0 || clippedDestination.Height <= 0)
        {
            this.IsVisible = false;
            return;
        }

        // DestinationRegion is target-local. SourceOffset keeps coverage aligned after clipping.
        this.DestinationRegion = new Rectangle(
            clippedDestination.X - this.TargetBounds.X,
            clippedDestination.Y - this.TargetBounds.Y,
            clippedDestination.Width,
            clippedDestination.Height);

        this.SourceOffset = new Point(
            clippedDestination.X - commandDestination.X,
            clippedDestination.Y - commandDestination.Y);

        this.IsVisible = true;
    }

    /// <summary>
    /// Builds the prepared path for this command without consulting any external cache.
    /// Applies transform, stroke expansion, and clipping. The returned path is ready
    /// for backends to walk directly via <see cref="IPath.Flatten"/>.
    /// </summary>
    /// <returns>The prepared path.</returns>
    internal readonly IPath BuildPreparedPath()
    {
        IPath path = this.sourcePath;

        // Transform to world space once so subsequent stroke and clip work operate in final coordinates.
        if (!this.transform.IsIdentity)
        {
            path = path.Transform(this.transform);
        }

        // Stroke commands are lowered to fills before clipping and rasterization.
        if (this.pen is not null)
        {
            path = this.pen.GeneratePath(path);
        }

        // Clip — path and clip paths are both interpreted in the prepared command space.
        if (this.clipPaths is { Count: > 0 })
        {
            path = path.Clip(this.shapeOptions, this.clipPaths);
        }

        return path;
    }

    /// <summary>
    /// Computes a coverage definition key from path identity and rasterization state.
    /// </summary>
    /// <param name="path">Path to rasterize.</param>
    /// <param name="rasterizerOptions">Rasterizer options used for coverage generation.</param>
    /// <returns>A stable key for coverage-equivalent commands.</returns>
    public static int ComputeCoverageDefinitionKey(
        IPath path,
        in RasterizerOptions rasterizerOptions)
    {
        int pathIdentity = RuntimeHelpers.GetHashCode(path);
        int rasterState = HashCode.Combine(
            rasterizerOptions.Interest.Size,
            (int)rasterizerOptions.IntersectionRule,
            (int)rasterizerOptions.RasterizationMode,
            (int)rasterizerOptions.SamplingOrigin);
        return HashCode.Combine(pathIdentity, rasterState);
    }
}
