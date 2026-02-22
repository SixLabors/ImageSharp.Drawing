// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One coverage definition that can be rasterized once and reused by multiple composition commands.
/// </summary>
internal readonly struct CompositionCoverageDefinition
{
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
