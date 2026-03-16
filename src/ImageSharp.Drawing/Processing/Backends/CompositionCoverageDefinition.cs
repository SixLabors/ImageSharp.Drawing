// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One coverage definition that can be rasterized once and reused by multiple composition commands.
/// </summary>
public readonly struct CompositionCoverageDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompositionCoverageDefinition"/> struct.
    /// </summary>
    /// <param name="definitionKey">The stable key for this coverage definition.</param>
    /// <param name="geometry">The prepared geometry used to generate coverage.</param>
    /// <param name="rasterizerOptions">The rasterizer options used to generate coverage.</param>
    /// <param name="destinationOffset">The absolute destination offset where coverage is composited.</param>
    public CompositionCoverageDefinition(
        int definitionKey,
        PreparedGeometry geometry,
        in RasterizerOptions rasterizerOptions,
        Point destinationOffset = default)
    {
        this.DefinitionKey = definitionKey;
        this.Geometry = geometry;
        this.RasterizerOptions = rasterizerOptions;
        this.DestinationOffset = destinationOffset;
    }

    /// <summary>
    /// Gets the stable key for this coverage definition.
    /// </summary>
    public int DefinitionKey { get; }

    /// <summary>
    /// Gets the backend-neutral prepared geometry used to generate coverage.
    /// </summary>
    public PreparedGeometry Geometry { get; }

    /// <summary>
    /// Gets the rasterizer options used to generate coverage.
    /// </summary>
    public RasterizerOptions RasterizerOptions { get; }

    /// <summary>
    /// Gets the absolute destination offset where coverage is composited.
    /// </summary>
    public Point DestinationOffset { get; }
}
