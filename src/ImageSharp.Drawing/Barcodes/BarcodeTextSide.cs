// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The side of the bars a line of the human readable interpretation prints on.
/// </summary>
internal enum BarcodeTextSide
{
    /// <summary>
    /// The line prints below the bars and grows downward from its text edge.
    /// </summary>
    BelowBars,

    /// <summary>
    /// The line prints above the bars and grows upward from its text edge.
    /// </summary>
    AboveBars,
}
