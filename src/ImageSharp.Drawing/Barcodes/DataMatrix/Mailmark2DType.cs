// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The three types of 2D Mailmark barcode, each a fixed Data Matrix size.
/// </summary>
public enum Mailmark2DType
{
    /// <summary>
    /// Type 7, 24 x 24 modules, with room for 6 characters of customer content in C40 encodation.
    /// </summary>
    Type7,

    /// <summary>
    /// Type 9, 32 x 32 modules, with room for 45 characters of customer content in C40 encodation.
    /// </summary>
    Type9,

    /// <summary>
    /// Type 29, 16 x 48 modules, with room for 25 characters of customer content in C40 encodation.
    /// </summary>
    Type29,
}
