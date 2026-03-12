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
    /// <param name="destinationOffset">The absolute destination offset where coverage is composited.</param>
    public CompositionCoverageDefinition(
        int definitionKey,
        IPath path,
        in RasterizerOptions rasterizerOptions,
        Point destinationOffset = default)
        : this(definitionKey, path, in rasterizerOptions, destinationOffset, null, 0f, default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositionCoverageDefinition"/> struct.
    /// </summary>
    /// <param name="definitionKey">The stable key for this coverage definition.</param>
    /// <param name="path">The path used to generate coverage.</param>
    /// <param name="rasterizerOptions">The rasterizer options used to generate coverage.</param>
    /// <param name="destinationOffset">The absolute destination offset where coverage is composited.</param>
    /// <param name="strokeOptions">Optional stroke options. When present the path is the original centerline and the backend is responsible for stroke expansion.</param>
    /// <param name="strokeWidth">The stroke width in pixels. Only meaningful when <paramref name="strokeOptions"/> is not <see langword="null"/>.</param>
    /// <param name="strokePattern">Optional dash pattern. Each element is a multiple of <paramref name="strokeWidth"/>.</param>
    public CompositionCoverageDefinition(
        int definitionKey,
        IPath path,
        in RasterizerOptions rasterizerOptions,
        Point destinationOffset,
        StrokeOptions? strokeOptions,
        float strokeWidth,
        ReadOnlyMemory<float> strokePattern)
    {
        this.DefinitionKey = definitionKey;
        this.Path = path;
        this.RasterizerOptions = rasterizerOptions;
        this.DestinationOffset = destinationOffset;
        this.StrokeOptions = strokeOptions;
        this.StrokeWidth = strokeWidth;
        this.StrokePattern = strokePattern;
    }

    /// <summary>
    /// Gets the stable key for this coverage definition.
    /// </summary>
    public int DefinitionKey { get; }

    /// <summary>
    /// Gets the closed, flattened path used to generate coverage.
    /// All sub-paths are pre-flattened and oriented for correct fill rasterization.
    /// </summary>
    public IPath Path { get; }

    /// <summary>
    /// Gets the rasterizer options used to generate coverage.
    /// </summary>
    public RasterizerOptions RasterizerOptions { get; }

    /// <summary>
    /// Gets the absolute destination offset where coverage is composited.
    /// </summary>
    public Point DestinationOffset { get; }

    /// <summary>
    /// Gets the stroke options when this definition represents a stroke operation.
    /// </summary>
    /// <remarks>
    /// When not <see langword="null"/>, <see cref="Path"/> is the original centerline and the backend
    /// is responsible for stroke expansion or SDF evaluation.
    /// </remarks>
    public StrokeOptions? StrokeOptions { get; }

    /// <summary>
    /// Gets the stroke width in pixels.
    /// </summary>
    public float StrokeWidth { get; }

    /// <summary>
    /// Gets the optional dash pattern. Each element is a multiple of <see cref="StrokeWidth"/>.
    /// </summary>
    public ReadOnlyMemory<float> StrokePattern { get; }

    /// <summary>
    /// Gets a value indicating whether this definition represents a stroke operation.
    /// </summary>
    public bool IsStroke => this.StrokeOptions is not null;
}
