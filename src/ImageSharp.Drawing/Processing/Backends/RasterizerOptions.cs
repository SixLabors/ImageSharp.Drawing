// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes whether rasterizers should emit continuous coverage or binary aliased coverage.
/// </summary>
public enum RasterizationMode
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
/// Immutable options used by rasterizers when scan-converting vector geometry.
/// </summary>
public readonly struct RasterizerOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RasterizerOptions"/> struct.
    /// </summary>
    /// <param name="interest">Destination bounds to rasterize into.</param>
    /// <param name="intersectionRule">Polygon intersection rule.</param>
    /// <param name="rasterizationMode">Rasterization coverage mode.</param>
    /// <param name="coverageBoost">Perceptual coverage boost for antialiased mode (0 disables).</param>
    public RasterizerOptions(
        Rectangle interest,
        IntersectionRule intersectionRule,
        RasterizationMode rasterizationMode,
        float coverageBoost = 0F)
    {
        this.Interest = interest;
        this.IntersectionRule = intersectionRule;
        this.RasterizationMode = rasterizationMode;
        this.CoverageBoost = coverageBoost;
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
    /// Gets the perceptual coverage boost used when <see cref="RasterizationMode"/> is
    /// <see cref="RasterizationMode.Antialiased"/>. Partial coverage values are remapped by
    /// the S-curve <c>a + boost * a * (1 - a) * (2a - 1)</c>, darkening mostly covered pixels
    /// and lightening mostly empty ones while 0, 0.5, and 1 stay fixed; at <c>1</c> the remap
    /// is exactly smoothstep. <c>0</c> disables the boost. Text rendering uses this to counter
    /// the soft, washed-out appearance of small blended glyphs.
    /// </summary>
    public float CoverageBoost { get; }

    /// <summary>
    /// Creates a copy of the current options with a different interest rectangle.
    /// </summary>
    /// <param name="interest">The replacement interest rectangle.</param>
    /// <returns>A new <see cref="RasterizerOptions"/> value.</returns>
    public RasterizerOptions WithInterest(Rectangle interest)
        => new(interest, this.IntersectionRule, this.RasterizationMode, this.CoverageBoost);
}
