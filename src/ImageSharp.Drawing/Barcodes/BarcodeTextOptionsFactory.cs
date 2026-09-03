// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Creates the <see cref="RichTextOptions"/> that every barcode symbology measures and draws its human
/// readable interpretation with.
/// </summary>
internal static class BarcodeTextOptionsFactory
{
    /// <summary>
    /// Returns the options for one line of the human readable interpretation. A line anchors on its
    /// alphabetic baseline and centers on its span, so only the origin changes from line to line. One
    /// instance serves both measuring and drawing, so the rectangle measured for a line is the rectangle
    /// the line draws into.
    /// </summary>
    /// <param name="font">The font the line renders in, whose size is in points.</param>
    /// <param name="dpi">The resolution the line renders at, in dots per inch.</param>
    /// <returns>The options.</returns>
    public static RichTextOptions Create(Font font, float dpi)
        => new(font)
        {
            Dpi = dpi,
            HintingMode = HintingMode.Standard
        };
}
