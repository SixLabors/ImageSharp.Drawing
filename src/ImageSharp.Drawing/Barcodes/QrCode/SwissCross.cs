// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Swiss cross of a Swiss QR Code, section 6.4.2 of the Swiss Implementation Guidelines for the QR-bill: a 7 x 7 mm black and white logo over the centre of
/// the 46 x 46 mm symbol. The geometry is the graphic that section refers to, 19.8 units on a side: a white square, a black square inset 1.41785 units, and a
/// white cross of two bars 3.3 units wide and 11 units long.
/// </summary>
internal static class SwissCross
{
    /// <summary>
    /// The width of a Swiss QR Code in millimetres, section 6.4.
    /// </summary>
    public const double SymbolWidth = 46;

    /// <summary>
    /// The side of the cross in millimetres, section 6.4.2.
    /// </summary>
    public const double Width = 7;

    /// <summary>
    /// The side of the cross graphic in its own units.
    /// </summary>
    public const float Side = 19.8F;

    /// <summary>
    /// Gets the black parts of the cross as rectangles, each as its left, top, right and bottom edge in the units of <see cref="Side"/>, the y axis down. The
    /// black square runs from 1.41785 to 18.38215: the square of the graphic from 0.7 to 19.1 less half of its 1.4357-unit white outline. The white cross is a
    /// vertical bar from x 8.3 to 11.6 and y 4 to 15 and a horizontal bar from x 4.4 to 15.4 and y 7.9 to 11.2, so the black square less the cross is these
    /// eight rectangles: a band above and below the cross, and on either side of each part of the cross between.
    /// </summary>
    public static ReadOnlySpan<float> DarkEdges =>
    [
        1.41785F, 1.41785F, 18.38215F, 4F,
        1.41785F, 4F, 8.3F, 7.9F,
        11.6F, 4F, 18.38215F, 7.9F,
        1.41785F, 7.9F, 4.4F, 11.2F,
        15.4F, 7.9F, 18.38215F, 11.2F,
        1.41785F, 11.2F, 8.3F, 15F,
        11.6F, 11.2F, 18.38215F, 15F,
        1.41785F, 15F, 18.38215F, 18.38215F,
    ];
}
