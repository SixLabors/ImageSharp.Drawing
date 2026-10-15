// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The error correction levels of QR Code, Table 8 of ISO/IEC 18004. A higher level recovers a larger part
/// of a damaged symbol and holds less data in a symbol of the same size.
/// </summary>
public enum QrErrorCorrectionLevel
{
    /// <summary>
    /// Level L, which recovers about 7% of the symbol codewords.
    /// </summary>
    L,

    /// <summary>
    /// Level M, which recovers about 15% of the symbol codewords.
    /// </summary>
    M,

    /// <summary>
    /// Level Q, which recovers about 25% of the symbol codewords.
    /// </summary>
    Q,

    /// <summary>
    /// Level H, which recovers about 30% of the symbol codewords. Micro QR Code symbols do not have it.
    /// </summary>
    H,
}
