// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The pitch of the D3 component of a D3 AQR symbol against the modules of its host QR Code symbol: the
/// width of one D3 module in host modules.
/// </summary>
public enum D3AqrHostRatio
{
    /// <summary>
    /// A D3 module is 2 host modules wide.
    /// </summary>
    Two,

    /// <summary>
    /// A D3 module is 2.5 host modules wide.
    /// </summary>
    TwoAndAHalf,

    /// <summary>
    /// A D3 module is 3 host modules wide.
    /// </summary>
    Three,
}
