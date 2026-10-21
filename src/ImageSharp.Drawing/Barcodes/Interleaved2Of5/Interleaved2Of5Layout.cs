// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The dimensions an application standard sets for an Interleaved 2 of 5 symbol: the X dimension, the bar height, the quiet zones, the bearer bar, the space
/// between the bars and the text, and the widths of the narrow and wide elements. <see cref="Interleaved2Of5Encoder"/> builds the symbol at them.
/// </summary>
internal readonly struct Interleaved2Of5Layout
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
    /// Gets the smallest bar height in modules that the application standard permits, or zero where it gives none. The code clamps the bar height to it.
    /// </summary>
    public float MinimumBarHeight { get; init; }

    /// <summary>
    /// Gets the largest bar height in modules that the application standard permits, or <see cref="float.PositiveInfinity"/> where it gives none. The code
    /// clamps the bar height to it.
    /// </summary>
    public float MaximumBarHeight { get; init; }

    /// <summary>
    /// Gets the quiet zone before and after the bars in modules.
    /// </summary>
    public float QuietZone { get; init; }

    /// <summary>
    /// Gets the thickness of the bearer bar in modules, or zero for none.
    /// </summary>
    public float BearerBarThickness { get; init; }

    /// <summary>
    /// Gets the space between the bars, or the bearer bar below them, and the human readable interpretation in modules.
    /// </summary>
    public float TextClearance { get; init; }

    /// <summary>
    /// Gets the width of a narrow element in run units. A run unit is the module divided by this width, so a narrow element is one module wide.
    /// </summary>
    public int NarrowElement { get; init; }

    /// <summary>
    /// Gets the width of a wide element in run units. The wide to narrow ratio is this width divided by <see cref="NarrowElement"/>.
    /// </summary>
    public int WideElement { get; init; }
}
