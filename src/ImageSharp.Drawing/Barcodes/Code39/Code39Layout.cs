// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The dimensions an application standard sets for a Code 39 symbol: the X dimension, the bar height, and the widths of the narrow and wide elements.
/// <see cref="Code39Encoder"/> builds the symbol at them.
/// </summary>
internal readonly struct Code39Layout
{
    /// <summary>
    /// Gets the X dimension in millimetres.
    /// </summary>
    public double XDimension { get; init; }

    /// <summary>
    /// Gets the bar height in modules when the options set none.
    /// </summary>
    public float NominalBarHeight { get; init; }

    /// <summary>
    /// Gets the smallest bar height in modules that the application standard permits, or zero where it gives none. The bar height is clamped to it.
    /// </summary>
    public float MinimumBarHeight { get; init; }

    /// <summary>
    /// Gets the largest bar height in modules that the application standard permits, or <see cref="float.PositiveInfinity"/> where it gives none. The bar
    /// height is clamped to it.
    /// </summary>
    public float MaximumBarHeight { get; init; }

    /// <summary>
    /// Gets the width of a narrow element in run units. A run unit is the module divided by this width, so a narrow element is one module wide.
    /// </summary>
    public int NarrowElement { get; init; }

    /// <summary>
    /// Gets the width of a wide element in run units. The wide to narrow ratio is this width divided by <see cref="NarrowElement"/>.
    /// </summary>
    public int WideElement { get; init; }
}
