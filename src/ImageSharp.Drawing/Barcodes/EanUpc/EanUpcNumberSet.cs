// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The number sets of Table 5-3 of the GS1 General Specifications that an EAN/UPC symbol character is taken from.
/// </summary>
internal enum EanUpcNumberSet
{
    /// <summary>
    /// Number set A, odd parity.
    /// </summary>
    A,

    /// <summary>
    /// Number set B, even parity.
    /// </summary>
    B,

    /// <summary>
    /// Number set C, the right half of the symbol.
    /// </summary>
    C,
}
