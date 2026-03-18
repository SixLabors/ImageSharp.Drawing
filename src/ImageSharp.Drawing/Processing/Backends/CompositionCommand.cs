// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One normalized composition command queued by <see cref="DrawingCanvasBatcher{TPixel}"/>.
/// After <see cref="Prepare"/> is called by the batcher, every command is a fill
/// with an immutable prepared path ready for backend execution.
/// </summary>
public struct CompositionCommand
{
    private readonly Pen? pen;
    private readonly IPath sourcePath;
    private readonly Matrix4x4 transform;
    private readonly IReadOnlyList<IPath>? clipPaths;
    private readonly ShapeOptions shapeOptions;
    private readonly bool enforceFillOrientation;

    private CompositionCommand(
        int definitionKey,
        IPath sourcePath,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        Point destinationOffset,
        Pen? pen,
        Matrix4x4 transform,
        IReadOnlyList<IPath>? clipPaths,
        ShapeOptions shapeOptions,
        bool enforceFillOrientation)
    {
        this.DefinitionKey = definitionKey;
        this.sourcePath = sourcePath;
        this.PreparedPath = null;
        this.IsVisible = false;
        this.Brush = brush;
        this.GraphicsOptions = graphicsOptions;
        this.RasterizerOptions = rasterizerOptions;
        this.DestinationOffset = destinationOffset;
        this.DestinationRegion = default;
        this.SourceOffset = default;
        this.pen = pen;
        this.transform = transform;
        this.clipPaths = clipPaths;
        this.shapeOptions = shapeOptions;
        this.enforceFillOrientation = enforceFillOrientation;
    }

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
    /// Gets a value indicating whether this command is visible after clipping to target bounds.
    /// Populated by <see cref="Prepare"/>.
    /// </summary>
    public bool IsVisible { get; private set; }

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
    /// Gets graphics options used for composition.
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
    /// Creates a composition command.
    /// </summary>
    /// <param name="path">Path in target-local coordinates.</param>
    /// <param name="brush">Brush used during composition.</param>
    /// <param name="graphicsOptions">Graphics options used for composition.</param>
    /// <param name="rasterizerOptions">Rasterizer options used to generate coverage.</param>
    /// <param name="shapeOptions">Shape options for clip operations.</param>
    /// <param name="transform">Transform matrix to apply during preparation.</param>
    /// <param name="enforceFillOrientation">
    /// When <see langword="true"/>, preparation normalizes closed contour orientation before rasterization.
    /// Callers should only enable this when they explicitly want contour winding rewritten.
    /// </param>
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
        bool enforceFillOrientation,
        Point destinationOffset = default,
        Pen? pen = null,
        IReadOnlyList<IPath>? clipPaths = null)
    {
        int definitionKey = ComputeCoverageDefinitionKey(path, in rasterizerOptions);

        return new(
            definitionKey,
            path,
            brush,
            graphicsOptions,
            in rasterizerOptions,
            destinationOffset,
            pen,
            transform,
            clipPaths,
            shapeOptions,
            enforceFillOrientation);
    }

    /// <summary>
    /// Prepares this command for backend execution. Expands strokes to fills,
    /// clips, transforms the source path, and clips to target bounds.
    /// After this call the command is a fill with an immutable prepared path
    /// and pre-computed visibility against the target.
    /// </summary>
    /// <param name="targetBounds">The target frame bounds for visibility clipping.</param>
    /// <param name="geometryCache">
    /// Optional flush-scoped cache used to share prepared paths across commands that
    /// have identical geometry-affecting inputs.
    /// </param>
    internal void Prepare(in Rectangle targetBounds, GeometryPreparationCache? geometryCache = null)
    {
        IPath preparedPath = geometryCache?.GetOrCreate(this) ?? this.BuildPreparedPath();
        this.PreparedPath = preparedPath;

        // Transform the brush to match the path coordinate space.
        if (!this.transform.IsIdentity)
        {
            this.Brush = this.Brush.Transform(this.transform);
        }

        // Recompute interest, brush bounds, and definition key from the final path.
        RasterizerOptions old = this.RasterizerOptions;
        RectangleF bounds = preparedPath.Bounds;
        if (old.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter)
        {
            bounds = new RectangleF(bounds.X + 0.5F, bounds.Y + 0.5F, bounds.Width, bounds.Height);
        }

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

        // Clip to target bounds and compute destination region.
        Rectangle commandDestination = new(
            this.DestinationOffset.X + interest.X,
            this.DestinationOffset.Y + interest.Y,
            interest.Width,
            interest.Height);

        Rectangle clippedDestination = Rectangle.Intersect(targetBounds, commandDestination);
        if (clippedDestination.Width <= 0 || clippedDestination.Height <= 0)
        {
            this.IsVisible = false;
            return;
        }

        this.DestinationRegion = new Rectangle(
            clippedDestination.X - targetBounds.X,
            clippedDestination.Y - targetBounds.Y,
            clippedDestination.Width,
            clippedDestination.Height);

        this.SourceOffset = new Point(
            clippedDestination.X - commandDestination.X,
            clippedDestination.Y - commandDestination.Y);

        this.IsVisible = true;
    }

    /// <summary>
    /// Creates the flush-scoped cache key used to share prepared paths.
    /// </summary>
    /// <returns>The geometry preparation cache key.</returns>
    internal readonly GeometryPreparationCache.GeometryPreparationKey CreateGeometryPreparationKey()
        => new(this.sourcePath, this.transform, this.pen, this.clipPaths, this.shapeOptions, this.enforceFillOrientation);

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

        // Stroke expansion runs before clipping so the clip sees the actual outline geometry.
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
