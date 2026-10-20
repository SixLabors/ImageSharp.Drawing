// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The symbol sizes a Data Matrix symbol can take.
/// </summary>
internal enum DataMatrixFormat
{
    /// <summary>
    /// The square sizes, 10 x 10 to 144 x 144.
    /// </summary>
    Square,

    /// <summary>
    /// The six rectangular sizes, 8 x 18 to 16 x 48.
    /// </summary>
    Rectangular,

    /// <summary>
    /// The rectangular sizes and the rectangular extension sizes, 8 x 18 to 26 x 64.
    /// </summary>
    RectangularExtension,

    /// <summary>
    /// The square sizes and the six rectangular sizes.
    /// </summary>
    SquareOrRectangular,
}
