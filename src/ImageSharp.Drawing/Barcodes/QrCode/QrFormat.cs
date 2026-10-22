// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The symbol formats <see cref="QrEncoder"/> builds.
/// </summary>
internal enum QrFormat
{
    /// <summary>
    /// QR Code, ISO/IEC 18004: square, versions 1 to 40.
    /// </summary>
    Full,

    /// <summary>
    /// Micro QR Code, ISO/IEC 18004: square, versions M1 to M4.
    /// </summary>
    Micro,

    /// <summary>
    /// Rectangular Micro QR Code, ISO/IEC 23941: rectangular, versions R7x43 to R17x139.
    /// </summary>
    Rectangular,
}
