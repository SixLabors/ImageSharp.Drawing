// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The modes of a MaxiCode symbol, which the first codeword carries.
/// </summary>
public enum MaxiCodeMode
{
    /// <summary>
    /// Mode 2: a structured carrier message with a numeric postal code of up to 9 digits, a 3-digit country code and a 3-digit class of service in the primary
    /// message, and the rest of the data in the secondary message.
    /// </summary>
    StructuredCarrierNumeric = 2,

    /// <summary>
    /// Mode 3: a structured carrier message with an alphanumeric postal code of up to 6 characters, a 3-digit country code and a 3-digit class of service in
    /// the primary message, and the rest of the data in the secondary message.
    /// </summary>
    StructuredCarrierAlphanumeric = 3,

    /// <summary>
    /// Mode 4: up to 93 codewords of data with standard error correction of the secondary message.
    /// </summary>
    Standard = 4,

    /// <summary>
    /// Mode 5: up to 77 codewords of data with enhanced error correction of the secondary message.
    /// </summary>
    FullErrorCorrection = 5,

    /// <summary>
    /// Mode 6: a symbol that programs the reader, laid out as mode 4.
    /// </summary>
    ReaderProgramming = 6,
}
