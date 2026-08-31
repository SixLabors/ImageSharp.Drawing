// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// PZN8, the eight digit Pharmazentralnummer. A Code 39 symbol carries it. The symbol contains the minus
/// sign identifier of ISO/IEC 15418, seven data digits and the check digit.
/// <para>
/// Give seven digits, and this class calculates the check digit. Give eight digits, and this class
/// compares the last digit with the calculated one. The printed line shows the term PZN, the identifier
/// and the digits. The symbol does not contain the term or the spaces around the identifier.
/// </para>
/// </summary>
public sealed class Pzn8Symbology : BarcodeSymbology
{
    /// <summary>
    /// The number of data digits, excluding the check digit.
    /// </summary>
    private const int Digits = 7;

    /// <summary>
    /// Initializes a new instance of the <see cref="Pzn8Symbology"/> class.
    /// </summary>
    public Pzn8Symbology()
    {
    }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
        => PznData.BuildSymbol(text, Digits, options);
}
