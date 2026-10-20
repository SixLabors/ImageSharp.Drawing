// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Creates the <see cref="RichTextOptions"/> that every barcode symbology measures and draws its human readable interpretation with.
/// </summary>
internal static class BarcodeTextOptionsFactory
{
    /// <summary>
    /// Returns the options for one line of the human readable interpretation. The options keep the default alignment of the text box, so the origin is the
    /// top left corner of the line. The emitter calculates the origin of each line from its measured rectangles. One instance serves both measuring and
    /// drawing, so the rectangle measured for a line is the rectangle the line draws into.
    /// </summary>
    /// <param name="font">The font the line renders in, whose size is in points.</param>
    /// <param name="dpi">The resolution the line renders at, in dots per inch.</param>
    /// <param name="weight">The font weight the symbology requires, <see cref="BarcodeSymbol.TextWeight"/>, or <see langword="null"/> for the weight of the
    /// font.</param>
    /// <returns>The options.</returns>
    public static RichTextOptions Create(Font font, float dpi, FontWeight? weight)
        => new(font)
        {
            Dpi = dpi,
            HintingMode = HintingMode.Standard,
            FontWeight = weight
        };
}
