// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The error correction levels of Han Xin Code. A higher level recovers a larger part of a damaged symbol and holds less data in a symbol of the same version.
/// </summary>
public enum HanXinErrorCorrectionLevel
{
    /// <summary>
    /// Level L1, which recovers about 8 percent of the symbol.
    /// </summary>
    L1,

    /// <summary>
    /// Level L2, which recovers about 15 percent of the symbol.
    /// </summary>
    L2,

    /// <summary>
    /// Level L3, which recovers about 23 percent of the symbol.
    /// </summary>
    L3,

    /// <summary>
    /// Level L4, which recovers about 30 percent of the symbol.
    /// </summary>
    L4,
}
