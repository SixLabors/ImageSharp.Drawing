// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The plain 2 of 5 symbologies that <see cref="Code2Of5Encoder"/> encodes. They share the digit
/// patterns and differ in their start and stop patterns and in whether the spaces carry data.
/// </summary>
internal enum Code2Of5Variant
{
    /// <summary>
    /// Industrial 2 of 5: the digit in five bars, a start pattern of a wide bar, a narrow space, a wide
    /// bar, a narrow space and a narrow bar, and a stop pattern of a wide bar, a narrow space, a narrow
    /// bar, a narrow space and a wide bar.
    /// </summary>
    Industrial,

    /// <summary>
    /// IATA 2 of 5: the digit in five bars, with the start and stop patterns of Interleaved 2 of 5.
    /// </summary>
    Iata,

    /// <summary>
    /// Matrix 2 of 5: the digit in three bars and two spaces, a start pattern of a wide bar and four
    /// narrow elements, and a stop pattern of a wide bar and four narrow elements.
    /// </summary>
    Matrix,

    /// <summary>
    /// COOP 2 of 5: the digit in three bars and two spaces with its own assignment of patterns to digits,
    /// a start pattern of a wide bar, a narrow space, a wide bar and a narrow space, and a stop pattern of
    /// a narrow bar, a wide space and a wide bar.
    /// </summary>
    Coop,

    /// <summary>
    /// Datalogic 2 of 5: the digit in three bars and two spaces, with the start and stop patterns of
    /// Interleaved 2 of 5.
    /// </summary>
    Datalogic,
}
