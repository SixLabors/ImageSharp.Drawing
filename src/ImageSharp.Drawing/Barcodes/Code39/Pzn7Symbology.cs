// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// PZN7, the seven digit Pharmazentralnummer. A Code 39 symbol carries it. The symbol contains the minus
/// sign identifier of ISO/IEC 15418, six data digits and the check digit.
/// <para>
/// IFA withdrew this length, so a new symbol uses <see cref="Pzn8Symbology"/>. The input is the six data
/// digits, or those digits and the check digit. This class always calculates the check digit. When the
/// input carries one, this class compares the two.
/// </para>
/// </summary>
public sealed class Pzn7Symbology : BarcodeSymbology
{
    /// <summary>
    /// The number of data digits, excluding the check digit.
    /// </summary>
    private const int Digits = 6;

    /// <summary>
    /// Initializes a new instance of the <see cref="Pzn7Symbology"/> class.
    /// </summary>
    public Pzn7Symbology()
    {
    }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
        => PznData.BuildSymbol(text, Digits, options);
}
