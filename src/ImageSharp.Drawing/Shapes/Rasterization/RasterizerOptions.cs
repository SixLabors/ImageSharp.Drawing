// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

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
    /// <param name="subpixelCount">Subpixel sampling count.</param>
    /// <param name="intersectionRule">Polygon intersection rule.</param>
    /// <param name="samplingOrigin">Sampling origin alignment.</param>
    public RasterizerOptions(
        Rectangle interest,
        int subpixelCount,
        IntersectionRule intersectionRule,
        RasterizerSamplingOrigin samplingOrigin = RasterizerSamplingOrigin.PixelBoundary)
    {
        Guard.MustBeGreaterThan(subpixelCount, 0, nameof(subpixelCount));

        this.Interest = interest;
        this.SubpixelCount = subpixelCount;
        this.IntersectionRule = intersectionRule;
        this.SamplingOrigin = samplingOrigin;
    }

    /// <summary>
    /// Gets destination bounds to rasterize into.
    /// </summary>
    public Rectangle Interest { get; }

    /// <summary>
    /// Gets the subpixel sampling count.
    /// </summary>
    public int SubpixelCount { get; }

    /// <summary>
    /// Gets the polygon intersection rule.
    /// </summary>
    public IntersectionRule IntersectionRule { get; }

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
        => new(interest, this.SubpixelCount, this.IntersectionRule, this.SamplingOrigin);
}
