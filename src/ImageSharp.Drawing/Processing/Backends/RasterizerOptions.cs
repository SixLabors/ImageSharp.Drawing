// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes whether rasterizers should emit continuous coverage or binary aliased coverage.
/// </summary>
internal enum RasterizationMode
{
    /// <summary>
    /// Emit continuous coverage in the range [0, 1].
    /// </summary>
    Antialiased = 0,

    /// <summary>
    /// Emit binary coverage values (0 or 1).
    /// </summary>
    Aliased = 1
}

/// <summary>
/// Describes where sample coverage is aligned relative to destination pixels.
/// </summary>
internal enum RasterizerSamplingOrigin
{
    /// <summary>
    /// Samples are aligned to pixel boundaries.
    /// </summary>
    PixelBoundary = 0,

    /// <summary>
    /// Samples are aligned to pixel centers.
    /// </summary>
    PixelCenter = 1
}

/// <summary>
/// Immutable options used by rasterizers when scan-converting vector geometry.
/// </summary>
internal readonly struct RasterizerOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RasterizerOptions"/> struct.
    /// </summary>
    /// <param name="interest">Destination bounds to rasterize into.</param>
    /// <param name="intersectionRule">Polygon intersection rule.</param>
    /// <param name="rasterizationMode">Rasterization coverage mode.</param>
    /// <param name="samplingOrigin">Sampling origin alignment.</param>
    public RasterizerOptions(
        Rectangle interest,
        IntersectionRule intersectionRule,
        RasterizationMode rasterizationMode = RasterizationMode.Antialiased,
        RasterizerSamplingOrigin samplingOrigin = RasterizerSamplingOrigin.PixelBoundary)
    {
        this.Interest = interest;
        this.IntersectionRule = intersectionRule;
        this.RasterizationMode = rasterizationMode;
        this.SamplingOrigin = samplingOrigin;
    }

    /// <summary>
    /// Gets destination bounds to rasterize into.
    /// </summary>
    public Rectangle Interest { get; }

    /// <summary>
    /// Gets the polygon intersection rule.
    /// </summary>
    public IntersectionRule IntersectionRule { get; }

    /// <summary>
    /// Gets the rasterization coverage mode.
    /// </summary>
    public RasterizationMode RasterizationMode { get; }

    /// <summary>
    /// Gets the sampling origin alignment.
    /// </summary>
    public RasterizerSamplingOrigin SamplingOrigin { get; }

    /// <summary>
    /// Creates a copy of the current options with a different interest rectangle.
    /// </summary>
    /// <param name="interest">The replacement interest rectangle.</param>
    /// <returns>A new <see cref="RasterizerOptions"/> value.</returns>
    public RasterizerOptions WithInterest(Rectangle interest)
        => new(interest, this.IntersectionRule, this.RasterizationMode, this.SamplingOrigin);
}
