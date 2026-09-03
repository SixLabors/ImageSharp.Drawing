// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Specifies the PosiCode version, which sets the character set, the distances between the bar
/// centres, and the start and stop characters.
/// </summary>
public enum PosiCodeVersion
{
    /// <summary>
    /// PosiCode A, whose bar centres lie 2G to 9G apart. It carries all 256 values of ISO/IEC 8859-1.
    /// </summary>
    A,

    /// <summary>
    /// PosiCode B, whose bar centres lie 3G to 10G apart. It carries all 256 values of ISO/IEC 8859-1.
    /// </summary>
    B,

    /// <summary>
    /// Limited PosiCode A, which carries the digits 0 to 9, the letters A to Z, the hyphen and the full
    /// stop.
    /// </summary>
    LimitedA,

    /// <summary>
    /// Limited PosiCode B, with the wider bar centre distances, which carries the same characters as
    /// <see cref="LimitedA"/>.
    /// </summary>
    LimitedB,
}
