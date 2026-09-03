// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Specifies whether a symbol carries a check character that its symbology makes optional. The encoder
/// either calculates the character or validates the one that the input carries.
/// </summary>
public enum CheckCharacterMode
{
    /// <summary>
    /// The symbol carries no check character. All of the input is data.
    /// </summary>
    None,

    /// <summary>
    /// The encoder calculates the check character from the input and puts it after the data.
    /// </summary>
    Compute,

    /// <summary>
    /// The last character of the input is the check character. The encoder validates it against the data
    /// and carries it once.
    /// </summary>
    Validate,
}
