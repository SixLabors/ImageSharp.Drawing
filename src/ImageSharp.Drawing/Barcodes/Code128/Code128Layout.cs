// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The dimensions an application standard sets for a Code 128 symbol: the X dimension, the bar height, the quiet zones and the space between the bars and the
/// text. <see cref="Code128Encoder"/> builds the symbol at them.
/// </summary>
internal readonly struct Code128Layout
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
    /// Gets the smallest bar height as a fraction of the width of the bars when the options set none, or 0 for none.
    /// </summary>
    public float MinimumHeightFraction { get; init; }

    /// <summary>
    /// Gets the space between the bars and the human readable interpretation in modules.
    /// </summary>
    public float TextClearance { get; init; }

    /// <summary>
    /// Gets the space between the bars and the ink of the line above them in modules.
    /// </summary>
    public float AboveClearance { get; init; }

    /// <summary>
    /// Gets the quiet zone before and after the bars in modules. It is at least <see cref="Code128Encoder.QuietZone"/>.
    /// </summary>
    public float HorizontalQuietZone { get; init; }

    /// <summary>
    /// Gets the clear zone above and below the bars in modules.
    /// </summary>
    public float VerticalQuietZone { get; init; }
}
