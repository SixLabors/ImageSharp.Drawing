// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The side of the bars a line of the human readable interpretation prints on.
/// </summary>
internal enum BarcodeTextSide
{
    /// <summary>
    /// The line prints below the bars, growing downward from the bar bottom it faces. The digits of an
    /// EAN-13 symbol print this way.
    /// </summary>
    BelowBars,

    /// <summary>
    /// The line prints above the bars, growing upward from the bar top it faces. The add-on digits and
    /// the data layer captions print this way.
    /// </summary>
    AboveBars,
}
