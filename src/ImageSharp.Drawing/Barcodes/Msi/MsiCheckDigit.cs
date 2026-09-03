// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Specifies which check digits an MSI symbol carries after its data. The symbology is not
/// self-checking, and the application chooses the calculation.
/// </summary>
public enum MsiCheckDigit
{
    /// <summary>
    /// The symbol carries no check digit. All of the input is data.
    /// </summary>
    None,

    /// <summary>
    /// One modulo 10 check digit. Every other digit from the right is doubled, the digits of the
    /// products and the remaining digits are added, and the check digit lifts the sum to the next
    /// multiple of ten.
    /// </summary>
    Modulo10,

    /// <summary>
    /// Two modulo 10 check digits. The second is calculated over the data and the first.
    /// </summary>
    Modulo1010,

    /// <summary>
    /// One modulo 11 check digit with the IBM weights 2 to 7. The digits are weighted from the right,
    /// starting at 2 and returning to 2 after 7, and the check digit lifts the weighted sum to the next
    /// multiple of eleven.
    /// </summary>
    Modulo11,

    /// <summary>
    /// A modulo 11 check digit with the IBM weights, then a modulo 10 check digit calculated over the
    /// data and the first.
    /// </summary>
    Modulo1110,

    /// <summary>
    /// One modulo 11 check digit with the NCR weights 2 to 9.
    /// </summary>
    NcrModulo11,

    /// <summary>
    /// A modulo 11 check digit with the NCR weights, then a modulo 10 check digit calculated over the
    /// data and the first.
    /// </summary>
    NcrModulo1110,
}
