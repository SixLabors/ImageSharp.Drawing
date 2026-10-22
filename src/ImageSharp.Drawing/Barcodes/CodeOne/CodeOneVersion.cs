// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The versions of a Code One symbol.
/// </summary>
public enum CodeOneVersion
{
    /// <summary>
    /// Version A: 16 rows by 18 columns, 10 data codewords.
    /// </summary>
    A,

    /// <summary>
    /// Version B: 22 rows by 22 columns, 19 data codewords.
    /// </summary>
    B,

    /// <summary>
    /// Version C: 28 rows by 32 columns, 44 data codewords.
    /// </summary>
    C,

    /// <summary>
    /// Version D: 40 rows by 42 columns, 91 data codewords.
    /// </summary>
    D,

    /// <summary>
    /// Version E: 52 rows by 54 columns, 182 data codewords.
    /// </summary>
    E,

    /// <summary>
    /// Version F: 70 rows by 76 columns, 370 data codewords.
    /// </summary>
    F,

    /// <summary>
    /// Version G: 104 rows by 98 columns, 732 data codewords.
    /// </summary>
    G,

    /// <summary>
    /// Version H: 148 rows by 134 columns, 1480 data codewords.
    /// </summary>
    H,

    /// <summary>
    /// Version S-10: 8 rows by 11 columns, up to 6 digits only.
    /// </summary>
    S10,

    /// <summary>
    /// Version S-20: 8 rows by 21 columns, up to 12 digits only.
    /// </summary>
    S20,

    /// <summary>
    /// Version S-30: 8 rows by 31 columns, up to 18 digits only.
    /// </summary>
    S30,

    /// <summary>
    /// Version T-16: 16 rows by 17 columns, 10 data codewords.
    /// </summary>
    T16,

    /// <summary>
    /// Version T-32: 16 rows by 33 columns, 24 data codewords.
    /// </summary>
    T32,

    /// <summary>
    /// Version T-48: 16 rows by 49 columns, 38 data codewords.
    /// </summary>
    T48,
}
