// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The linkage flag of a GS1-128 symbol. Section 7.4 of ISO/IEC 24723 links a 2D component to a GS1-128 symbol with an extra code set character in the last
/// position before the check character, and Table 8 chooses the character from the code set active at the end of the data and the kind of 2D component.
/// </summary>
internal enum Code128Linkage : byte
{
    /// <summary>
    /// No 2D component is linked to the symbol.
    /// </summary>
    None,

    /// <summary>
    /// A CC-A or CC-B 2D component is linked to the symbol. Table 8: code set A takes Code B, code set B takes Code C, and code set C takes Code A.
    /// </summary>
    CcAOrCcB,

    /// <summary>
    /// A CC-C 2D component is linked to the symbol. Table 8: code set A takes Code C, code set B takes Code A, and code set C takes Code B.
    /// </summary>
    CcC,
}
