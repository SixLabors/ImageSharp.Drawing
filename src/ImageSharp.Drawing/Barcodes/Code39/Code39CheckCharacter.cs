// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// How a Code 39 symbol treats the modulo 43 check character, which the symbology does not require.
/// </summary>
public enum Code39CheckCharacter
{
    /// <summary>
    /// The symbol carries no check character and the whole input is data.
    /// </summary>
    None,

    /// <summary>
    /// The check character is worked out over the input and carried behind it.
    /// </summary>
    Compute,

    /// <summary>
    /// The last character of the input is a check character the caller has already worked out. It is
    /// validated against the data and carried once.
    /// </summary>
    Validate,
}
