// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The character set of a component of GS1 Application Identifier data, which the GS1 Barcode Syntax Dictionary writes as N, X, Y or Z.
/// </summary>
internal enum Gs1CharacterSet : byte
{
    /// <summary>
    /// N: the digits.
    /// </summary>
    Digits,

    /// <summary>
    /// X: GS1 AI encodable character set 82, Table 7-20 of the GS1 General Specifications.
    /// </summary>
    Set82,

    /// <summary>
    /// Y: GS1 AI encodable character set 39, Table 7-21.
    /// </summary>
    Set39,

    /// <summary>
    /// Z: GS1 AI encodable character set 64, Table 7-22.
    /// </summary>
    Set64,
}
