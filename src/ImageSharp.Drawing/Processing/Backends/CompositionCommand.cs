// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One normalized composition command queued by <see cref="DrawingCanvasBatcher{TPixel}"/>.
/// </summary>
public readonly struct CompositionCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompositionCommand"/> struct.
    /// </summary>
    /// <param name="definitionKey">Stable definition key used for composition-level caching.</param>
    /// <param name="path">Path to rasterize in target-local coordinates.</param>
    /// <param name="brush">Brush used during composition.</param>
    /// <param name="brushBounds">Brush bounds used for applicator creation.</param>
    /// <param name="graphicsOptions">Graphics options used for composition.</param>
    /// <param name="rasterizerOptions">Rasterizer options used to generate coverage.</param>
    /// <param name="destinationOffset">Absolute destination offset where coverage is composited.</param>
    /// <param name="strokeOptions">Optional stroke options for backend-side stroke expansion.</param>
    /// <param name="strokeWidth">Stroke width in pixels when <paramref name="strokeOptions"/> is present.</param>
    /// <param name="strokePattern">Optional dash pattern when <paramref name="strokeOptions"/> is present.</param>
    private CompositionCommand(
        int definitionKey,
        IPath path,
        Brush brush,
        Rectangle brushBounds,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        Point destinationOffset,
        StrokeOptions? strokeOptions,
        float strokeWidth,
        ReadOnlyMemory<float> strokePattern)
    {
        this.DefinitionKey = definitionKey;
        this.Path = path;
        this.Brush = brush;
        this.BrushBounds = brushBounds;
        this.GraphicsOptions = graphicsOptions;
        this.RasterizerOptions = rasterizerOptions;
        this.DestinationOffset = destinationOffset;
        this.StrokeOptions = strokeOptions;
        this.StrokeWidth = strokeWidth;
        this.StrokePattern = strokePattern;
    }

    /// <summary>
    /// Gets a stable definition key used for composition-level caching.
    /// </summary>
    public int DefinitionKey { get; }

    /// <summary>
    /// Gets the path to rasterize in target-local coordinates.
    /// </summary>
    public IPath Path { get; }

    /// <summary>
    /// Gets the brush used during composition.
    /// </summary>
    public Brush Brush { get; }

    /// <summary>
    /// Gets brush bounds used for applicator creation.
    /// </summary>
    public Rectangle BrushBounds { get; }

    /// <summary>
    /// Gets graphics options used for composition.
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
    /// Gets the stroke options when this command represents a stroke operation.
    /// </summary>
    public StrokeOptions? StrokeOptions { get; }

    /// <summary>
    /// Gets the stroke width in pixels.
    /// </summary>
    public float StrokeWidth { get; }

    /// <summary>
    /// Gets the optional dash pattern.
    /// </summary>
    public ReadOnlyMemory<float> StrokePattern { get; }

    /// <summary>
    /// Creates a fill composition command.
    /// </summary>
    /// <param name="path">Path to rasterize in target-local coordinates.</param>
    /// <param name="brush">Brush used during composition.</param>
    /// <param name="graphicsOptions">Graphics options used for composition.</param>
    /// <param name="rasterizerOptions">Rasterizer options used to generate coverage.</param>
    /// <param name="destinationOffset">Absolute destination offset where coverage is composited.</param>
    /// <param name="definitionKeyCache">Optional scoped cache to avoid repeated path flattening for the same <see cref="IPath"/> reference.</param>
    /// <returns>The normalized composition command.</returns>
    public static CompositionCommand Create(
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        Point destinationOffset = default,
        Dictionary<int, (IPath Path, int RasterState, int DefinitionKey)>? definitionKeyCache = null)
    {
        int definitionKey = ComputeCoverageDefinitionKey(path, in rasterizerOptions, definitionKeyCache);
        Rectangle brushBounds = ComputeBrushBounds(path, destinationOffset);

        return new(
            definitionKey,
            path,
            brush,
            brushBounds,
            graphicsOptions,
            in rasterizerOptions,
            destinationOffset,
            null,
            0f,
            default);
    }

    /// <summary>
    /// Creates a stroke composition command where the backend is responsible for stroke expansion.
    /// </summary>
    /// <param name="path">The original centerline path in target-local coordinates.</param>
    /// <param name="brush">Brush used during composition.</param>
    /// <param name="graphicsOptions">Graphics options used for composition.</param>
    /// <param name="rasterizerOptions">Rasterizer options with interest inflated for stroke bounds.</param>
    /// <param name="strokeOptions">Stroke geometry options.</param>
    /// <param name="strokeWidth">Stroke width in pixels.</param>
    /// <param name="strokePattern">Optional dash pattern. Each element is a multiple of <paramref name="strokeWidth"/>.</param>
    /// <param name="destinationOffset">Absolute destination offset where coverage is composited.</param>
    /// <param name="definitionKeyCache">Optional scoped cache to avoid repeated path flattening for the same <see cref="IPath"/> reference.</param>
    /// <returns>The normalized stroke composition command.</returns>
    public static CompositionCommand CreateStroke(
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        StrokeOptions strokeOptions,
        float strokeWidth,
        ReadOnlyMemory<float> strokePattern = default,
        Point destinationOffset = default,
        Dictionary<int, (IPath Path, int RasterState, int DefinitionKey)>? definitionKeyCache = null)
    {
        int definitionKey = ComputeCoverageDefinitionKey(path, in rasterizerOptions, definitionKeyCache);
        Rectangle brushBounds = ComputeBrushBounds(rasterizerOptions.Interest, destinationOffset);

        return new(
            definitionKey,
            path,
            brush,
            brushBounds,
            graphicsOptions,
            in rasterizerOptions,
            destinationOffset,
            strokeOptions,
            strokeWidth,
            strokePattern);
    }

    /// <summary>
    /// Computes a coverage definition key from path identity and rasterization state.
    /// </summary>
    /// <param name="path">Path to rasterize.</param>
    /// <param name="rasterizerOptions">Rasterizer options used for coverage generation.</param>
    /// <param name="definitionKeyCache">Unused. Retained for API compatibility.</param>
    /// <returns>A stable key for coverage-equivalent commands.</returns>
    public static int ComputeCoverageDefinitionKey(
        IPath path,
        in RasterizerOptions rasterizerOptions,
        Dictionary<int, (IPath Path, int RasterState, int DefinitionKey)>? definitionKeyCache = null)
    {
        int pathIdentity = RuntimeHelpers.GetHashCode(path);
        int rasterState = HashCode.Combine(
            rasterizerOptions.Interest.Size,
            (int)rasterizerOptions.IntersectionRule,
            (int)rasterizerOptions.RasterizationMode,
            (int)rasterizerOptions.SamplingOrigin);
        return HashCode.Combine(pathIdentity, rasterState);
    }

    private static Rectangle ComputeBrushBounds(IPath path, Point destinationOffset)
    {
        RectangleF bounds = path.Bounds;
        Rectangle localBrushBounds = Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));
        return new(
            localBrushBounds.X + destinationOffset.X,
            localBrushBounds.Y + destinationOffset.Y,
            localBrushBounds.Width,
            localBrushBounds.Height);
    }

    private static Rectangle ComputeBrushBounds(Rectangle interest, Point destinationOffset)
        => new(
            interest.X + destinationOffset.X,
            interest.Y + destinationOffset.Y,
            interest.Width,
            interest.Height);
}
