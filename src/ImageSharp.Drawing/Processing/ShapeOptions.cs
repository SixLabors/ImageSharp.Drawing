// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Options for influencing the drawing functions.
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
        this.ClippingOperation = source.ClippingOperation;
    }

    /// <summary>
    /// Gets or sets the clipping operation.
    /// <para/>
    /// Defaults to <see cref="BooleanOperation.Difference"/>.
    /// </summary>
    public BooleanOperation ClippingOperation { get; set; } = BooleanOperation.Difference;

    /// <summary>
    /// Gets or sets the rule for calculating intersection points.
    /// <para/>
    /// Defaults to <see cref="IntersectionRule.EvenOdd"/>.
    /// </summary>
    public IntersectionRule IntersectionRule { get; set; } = IntersectionRule.EvenOdd;

    /// <inheritdoc/>
    public ShapeOptions DeepClone() => new(this);
}
