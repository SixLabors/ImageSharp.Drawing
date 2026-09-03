// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Specifies where a line of the human readable interpretation stands within its horizontal span.
/// </summary>
internal enum BarcodeTextAlignment
{
    /// <summary>
    /// The line is centred on the span.
    /// </summary>
    Centered,

    /// <summary>
    /// The left edge of the line's ink stands on the left edge of the span.
    /// </summary>
    Left,
}
