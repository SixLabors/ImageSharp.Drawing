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
    Matrix
}
