// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The device-independent description of an encoded barcode, measured in modules. A module is the narrowest
/// nominal element of a symbology (the X-dimension in ISO/IEC 15420 and related symbology specifications).
/// A symbol carries no pixel sizes, brushes or fonts; those are applied when the symbol is converted to canvas commands.
/// </summary>
internal abstract class BarcodeSymbol
{
    /// <summary>
    /// Gets the width of the symbol in modules, excluding quiet zones.
    /// </summary>
    public abstract float WidthInModules { get; }

    /// <summary>
    /// Gets the height of the symbol in modules, excluding the human readable interpretation.
    /// </summary>
    public abstract float HeightInModules { get; }

    /// <summary>
    /// Gets the width of the quiet zone that must precede the symbol, in modules. Quiet zone widths are mandated
    /// per symbology; for example ISO/IEC 15420 requires 11 modules before an EAN-13 symbol.
    /// </summary>
    public abstract int LeadingQuietZone { get; }

    /// <summary>
    /// Gets the width of the quiet zone that must follow the symbol, in modules.
    /// </summary>
    public abstract int TrailingQuietZone { get; }
}
