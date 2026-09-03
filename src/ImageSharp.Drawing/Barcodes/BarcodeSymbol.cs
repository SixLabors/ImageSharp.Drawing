// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The device independent description of an encoded barcode, measured in modules. A module is the
/// narrowest nominal element of a symbology, which ISO/IEC 15420 and related specifications call the
/// X-dimension. A symbol carries no pixel sizes, brushes or fonts. The emitter adds those when it turns
/// the symbol into canvas commands.
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
    /// Gets the width in modules of the quiet zone that comes before the symbol. Each symbology sets its
    /// own quiet zone width. For example, ISO/IEC 15420 requires 11 modules before an EAN-13 symbol, and
    /// a postal symbology states its clear zone in inches, which is not a whole number of modules.
    /// </summary>
    public abstract float LeadingQuietZone { get; }

    /// <summary>
    /// Gets the width of the quiet zone that must follow the symbol, in modules.
    /// </summary>
    public abstract float TrailingQuietZone { get; }
}
