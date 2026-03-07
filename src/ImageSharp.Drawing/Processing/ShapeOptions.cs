// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides options for controlling how vector shapes are interpreted during rasterization,
/// including the fill-rule intersection mode and boolean clipping operations.
/// </summary>
public class ShapeOptions : IDeepCloneable<ShapeOptions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShapeOptions"/> class.
    /// </summary>
    public ShapeOptions()
    {
    }

    private ShapeOptions(ShapeOptions source)
    {
        this.IntersectionRule = source.IntersectionRule;
        this.BooleanOperation = source.BooleanOperation;
    }

    /// <summary>
    /// Gets or sets the boolean clipping operation used when a clipping path is applied.
    /// Determines how the clip shape interacts with the target region
    /// (e.g. <see cref="BooleanOperation.Difference"/> subtracts the clip shape).
    /// <para/>
    /// Defaults to <see cref="BooleanOperation.Difference"/>.
    /// </summary>
    public BooleanOperation BooleanOperation { get; set; } = BooleanOperation.Difference;

    /// <summary>
    /// Gets or sets the fill rule that determines how overlapping or nested contours affect coverage.
    /// <see cref="IntersectionRule.NonZero"/> fills any region with a non-zero winding number;
    /// <see cref="IntersectionRule.EvenOdd"/> alternates fill/hole for each contour crossing.
    /// <para/>
    /// Defaults to <see cref="IntersectionRule.NonZero"/>.
    /// </summary>
    public IntersectionRule IntersectionRule { get; set; } = IntersectionRule.NonZero;

    /// <inheritdoc/>
    public ShapeOptions DeepClone() => new(this);
}
