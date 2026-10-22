// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The 2D components of a GS1 Composite symbol. Each one carries the same bit stream, which ISO/IEC 24723 encodes into a different MicroPDF417 or PDF417
/// symbol.
/// </summary>
public enum Gs1CompositeComponent
{
    /// <summary>
    /// CC-A, a MicroPDF417 variant of 2, 3 or 4 data columns and 3 to 12 rows that carries the bit stream in base 928. A symbol whose data exceeds CC-A takes
    /// CC-B of the same columns, as section 4.4 of ISO/IEC 24723 requires.
    /// </summary>
    CcA,

    /// <summary>
    /// CC-B, a MicroPDF417 symbol of 2, 3 or 4 data columns that carries the bit stream in Byte Compaction mode after the codeword 920.
    /// </summary>
    CcB,

    /// <summary>
    /// CC-C, a PDF417 symbol of 1 to 30 data columns that carries the bit stream in Byte Compaction mode after the codeword 920. Only a GS1-128 Composite
    /// symbol takes it.
    /// </summary>
    CcC,
}
