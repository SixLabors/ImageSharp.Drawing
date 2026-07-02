// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides options for controlling how vector shapes are interpreted during rasterization
/// and explicit boolean geometry operations.
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
    /// Gets or sets the boolean operation used by explicit path-combination APIs.
    /// Determines how the operand shape interacts with the target region
    /// (e.g. <see cref="BooleanOperation.Difference"/> subtracts the operand shape).
    /// <para/>
    /// Defaults to <see cref="BooleanOperation.Intersection"/>, matching the zero value of
    /// <see cref="BooleanOperation"/> and <c>PolygonClipper.BooleanOperation</c> so that a
    /// explicit path combination restricts the region rather than subtracting it.
    /// </summary>
    public BooleanOperation BooleanOperation { get; set; } = BooleanOperation.Intersection;

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
