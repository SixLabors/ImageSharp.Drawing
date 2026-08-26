// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Options that control how a barcode is sized and painted.
/// </summary>
public sealed class BarcodeOptions
{
    /// <summary>
    /// Gets or sets the width of one module in pixels. The module is the narrowest nominal element of a
    /// symbology, called the X-dimension in ISO/IEC 15420. All symbol dimensions scale from this value.
    /// </summary>
    public float ModuleWidth { get; set; } = 2F;

    /// <summary>
    /// Gets or sets the bar height in pixels, or <see langword="null"/> to use the nominal height that the
    /// symbology specification defines for its X-dimension. For EAN-13 at nominal size ISO/IEC 15420 specifies
    /// 22.85mm bars at a 0.33mm X-dimension, which is 69.24 modules.
    /// </summary>
    public float? BarHeight { get; set; }

    /// <summary>
    /// Gets or sets the brush used to fill the bars and dark modules.
    /// </summary>
    public Brush BarBrush { get; set; } = Brushes.Solid(Color.Black);

    /// <summary>
    /// Gets or sets the brush used to fill the symbol background including the quiet zones, or
    /// <see langword="null"/> to leave the background untouched. Symbology specifications require a light
    /// background for reliable scanning; a <see langword="null"/> background is only safe over light content.
    /// </summary>
    public Brush? Background { get; set; }

    /// <summary>
    /// Gets or sets the font used for the human readable interpretation, or <see langword="null"/> to omit the
    /// text. The specifications typeset the interpretation in OCR-B at a size proportional to the X-dimension;
    /// the caller selects the font and size.
    /// </summary>
    public Font? Font { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the mandatory quiet zones are reserved inside the drawn area.
    /// When disabled the symbol starts at the draw origin and the caller is responsible for keeping the
    /// surrounding area clear; scanners reject symbols without their quiet zones.
    /// </summary>
    public bool IncludeQuietZones { get; set; } = true;
}
