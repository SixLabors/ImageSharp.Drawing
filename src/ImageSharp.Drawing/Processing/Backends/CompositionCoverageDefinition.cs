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
    /// <param name="path">The path used to generate coverage.</param>
    /// <param name="rasterizerOptions">The rasterizer options used to generate coverage.</param>
    public CompositionCoverageDefinition(int definitionKey, IPath path, in RasterizerOptions rasterizerOptions)
    {
        this.DefinitionKey = definitionKey;
        this.Path = path;
        this.RasterizerOptions = rasterizerOptions;
    }

    /// <summary>
    /// Gets the stable key for this coverage definition.
    /// </summary>
    public int DefinitionKey { get; }

    /// <summary>
    /// Gets the path used to generate coverage.
    /// </summary>
    public IPath Path { get; }

    /// <summary>
    /// Gets the rasterizer options used to generate coverage.
    /// </summary>
    public RasterizerOptions RasterizerOptions { get; }
}
