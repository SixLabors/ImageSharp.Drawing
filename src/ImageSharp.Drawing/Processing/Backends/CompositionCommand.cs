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
    private CompositionCommand(
        int definitionKey,
        IPath path,
        Brush brush,
        Rectangle brushBounds,
        GraphicsOptions graphicsOptions,
        RasterizerOptions rasterizerOptions)
    {
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(brush, nameof(brush));
        Guard.NotNull(graphicsOptions, nameof(graphicsOptions));

        this.DefinitionKey = definitionKey;
        this.Path = path;
        this.Brush = brush;
        this.BrushBounds = brushBounds;
        this.GraphicsOptions = graphicsOptions;
        this.RasterizerOptions = rasterizerOptions;
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
    /// Creates a composition command and computes a stable definition key from path/brush/rasterizer options.
    /// </summary>
    /// <param name="path">Path to rasterize in target-local coordinates.</param>
    /// <param name="brush">Brush used during composition.</param>
    /// <param name="graphicsOptions">Graphics options used for composition.</param>
    /// <param name="rasterizerOptions">Rasterizer options used to generate coverage.</param>
    /// <returns>The normalized composition command.</returns>
    public static CompositionCommand Create(
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions)
    {
        HashCode hash = default;
        hash.Add(RuntimeHelpers.GetHashCode(path));
        hash.Add(RuntimeHelpers.GetHashCode(brush));
        hash.Add(rasterizerOptions.Interest);
        hash.Add((int)rasterizerOptions.IntersectionRule);
        hash.Add((int)rasterizerOptions.RasterizationMode);
        hash.Add((int)rasterizerOptions.SamplingOrigin);

        return Create(
            hash.ToHashCode(),
            path,
            brush,
            graphicsOptions,
            rasterizerOptions);
    }

    /// <summary>
    /// Creates a composition command using a caller-provided definition key.
    /// </summary>
    /// <param name="definitionKey">Stable definition key used for composition-level caching.</param>
    /// <param name="path">Path to rasterize in target-local coordinates.</param>
    /// <param name="brush">Brush used during composition.</param>
    /// <param name="graphicsOptions">Graphics options used for composition.</param>
    /// <param name="rasterizerOptions">Rasterizer options used to generate coverage.</param>
    /// <returns>The normalized composition command.</returns>
    public static CompositionCommand Create(
        int definitionKey,
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions)
    {
        RectangleF bounds = path.Bounds;
        Rectangle brushBounds = Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));

        return new(
            definitionKey,
            path,
            brush,
            brushBounds,
            graphicsOptions,
            rasterizerOptions);
    }
}
