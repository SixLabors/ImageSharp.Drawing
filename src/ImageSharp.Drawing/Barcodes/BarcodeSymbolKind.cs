// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The form of an encoded symbol.
/// </summary>
internal enum BarcodeSymbolKind
{
    /// <summary>
    /// No symbol has been encoded.
    /// </summary>
    None,

    /// <summary>
    /// One row of bars and spaces.
    /// </summary>
    Linear,

    /// <summary>
    /// Rows of bars and spaces stacked from the top down.
    /// </summary>
    Stacked,

    /// <summary>
    /// A grid of square modules.
    /// </summary>
    Matrix,

    /// <summary>
    /// Offset rows of hexagonal modules around a circular finder, the form of MaxiCode. The modules are a grid of rows and columns in which every odd row
    /// stands half a module to the right.
    /// </summary>
    Hexagonal,

    /// <summary>
    /// A grid of round dots, the form of DotCode. The modules are a grid of square cells, and each dark module is a dot centred in its cell.
    /// </summary>
    Dotted,

    /// <summary>
    /// A grid of square modules in several colours, the form of Ultracode. Each module is a <see cref="BarcodeModuleColor"/>.
    /// </summary>
    Colored,
}
