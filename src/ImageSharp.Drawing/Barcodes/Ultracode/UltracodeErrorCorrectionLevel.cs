// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The error correction levels of Ultracode. A higher level adds more check codewords for each 25 message codewords, so it recovers a larger part of a
/// damaged symbol and the symbol grows.
/// </summary>
public enum UltracodeErrorCorrectionLevel
{
    /// <summary>
    /// Level EC1, one check codeword for each 25 message codewords, plus 5.
    /// </summary>
    EC1,

    /// <summary>
    /// Level EC2, two check codewords for each 25 message codewords, plus 5.
    /// </summary>
    EC2,

    /// <summary>
    /// Level EC3, four check codewords for each 25 message codewords, plus 5.
    /// </summary>
    EC3,

    /// <summary>
    /// Level EC4, six check codewords for each 25 message codewords, plus 5.
    /// </summary>
    EC4,

    /// <summary>
    /// Level EC5, eight check codewords for each 25 message codewords, plus 5.
    /// </summary>
    EC5,
}
