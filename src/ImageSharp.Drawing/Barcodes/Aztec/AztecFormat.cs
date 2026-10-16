// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The formats an Aztec Code symbol can take, section 5.1 of ISO/IEC 24778.
/// </summary>
internal enum AztecFormat
{
    /// <summary>
    /// The smallest symbol in the sequence of section 11.5: compact symbols of 1 to 4 layers, then full-range symbols of 4 to 32 layers.
    /// </summary>
    Auto,

    /// <summary>
    /// A compact symbol of 1 to 4 layers, with a 2-ring bullseye.
    /// </summary>
    Compact,

    /// <summary>
    /// A full-range symbol of 1 to 32 layers, with a 3-ring bullseye and a reference grid.
    /// </summary>
    Full,
}
