// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// PZN7, the seven digit Pharmazentralnummer. A Code 39 symbol carries it. The symbol contains the minus
/// sign identifier of ISO/IEC 15418, six data digits and the check digit.
/// <para>
/// IFA withdrew this length. Use <see cref="Pzn8Symbology"/> for a new symbol. Give six digits, and this
/// class calculates the check digit. Give seven digits, and this class compares the last digit with the
/// calculated one.
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
