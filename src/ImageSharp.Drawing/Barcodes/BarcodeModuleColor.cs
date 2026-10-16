// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The colour of a module of a colour symbol. Light and dark take the background and the bar brush, as the modules of every other symbol do; the four
/// data colours are fixed, because a reader tells them apart by hue.
/// </summary>
internal enum BarcodeModuleColor : byte
{
    /// <summary>
    /// A light module, drawn as the background.
    /// </summary>
    Light,

    /// <summary>
    /// A dark module, drawn with the bar brush.
    /// </summary>
    Dark,

    /// <summary>
    /// A cyan module, sRGB 00FFFF.
    /// </summary>
    Cyan,

    /// <summary>
    /// A magenta module, sRGB FF00FF.
    /// </summary>
    Magenta,

    /// <summary>
    /// A yellow module, sRGB FFFF00.
    /// </summary>
    Yellow,

    /// <summary>
    /// A green module, sRGB 00FF00.
    /// </summary>
    Green,
}
