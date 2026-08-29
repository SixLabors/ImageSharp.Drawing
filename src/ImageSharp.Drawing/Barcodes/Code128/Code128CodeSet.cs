// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The three Code 128 code sets of section 5.4.3.3 of the GS1 General Specifications. The set in force
/// decides which character each symbol character value stands for.
/// </summary>
internal enum Code128CodeSet
{
    /// <summary>
    /// Code set A: the space through the upper case letters, then the control characters.
    /// </summary>
    A,

    /// <summary>
    /// Code set B: the space through the delete character.
    /// </summary>
    B,

    /// <summary>
    /// Code set C: pairs of digits, two per symbol character.
    /// </summary>
    C,
}
