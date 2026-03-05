// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One normalized composition command queued by <see cref="DrawingCanvasBatcher{TPixel}"/>.
/// </summary>
internal readonly struct CompositionCommand
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
    private CompositionCommand(
        int definitionKey,
        IPath path,
        Brush brush,
        Rectangle brushBounds,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        Point destinationOffset)
    {
        this.DefinitionKey = definitionKey;
        this.Path = path;
        this.Brush = brush;
        this.BrushBounds = brushBounds;
        this.GraphicsOptions = graphicsOptions;
        this.RasterizerOptions = rasterizerOptions;
        this.DestinationOffset = destinationOffset;
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
    /// Creates a composition command and computes a stable definition key from path geometry and rasterizer options.
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
        RectangleF bounds = path.Bounds;
        Rectangle localBrushBounds = Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));
        Rectangle brushBounds = new(
            localBrushBounds.X + destinationOffset.X,
            localBrushBounds.Y + destinationOffset.Y,
            localBrushBounds.Width,
            localBrushBounds.Height);

        return new(
            definitionKey,
            path,
            brush,
            brushBounds,
            graphicsOptions,
            in rasterizerOptions,
            destinationOffset);
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
}
