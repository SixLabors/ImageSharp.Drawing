// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Deutsche Post Identcode, the identification code of a mail item. An Interleaved 2 of 5 symbol
/// carries it. The code is twelve digits: the two digit origin mail centre, the customer number, the
/// item number and the check digit. Deutsche Post fixes the narrow module at 0.375 mm to 0.5 mm, the
/// wide to narrow ratio at 1:2 to 1:3, the height at 25 mm or more and the quiet zone at 5 mm or more on
/// each side.
/// <para>
/// The input is the eleven data digits, or those digits and the check digit. This class always
/// calculates the check digit. When the input carries one, this class compares the two.
/// </para>
/// <para>
/// The printed line separates the fields with full stops and spaces and sets the check digit off with a
/// space. Deutsche Post assigns customer numbers of one to five digits and gives no rule for where the
/// customer number ends, so this class prints the twelve digits in the groups of two, three, three and
/// three that the reference implementations agree on. The symbol carries neither the full stops nor the
/// spaces.
/// </para>
/// </summary>
public sealed class IdentcodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// The number of data digits, excluding the check digit.
    /// </summary>
    private const int Digits = 11;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentcodeSymbology"/> class.
    /// </summary>
    public IdentcodeSymbology()
    {
    }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Span<char> encoded = stackalloc char[Digits + 1];
        DeutschePostData.Prepare(text, Digits, encoded);

        ReadOnlySpan<char> digits = encoded;
        string readable = options.Font is null
            ? string.Empty
            : $"{digits[..2]}.{digits[2..5]} {digits[5..8]}.{digits[8..11]} {digits[11]}";

        return Interleaved2Of5Encoder.BuildSymbol(
            Interleaved2Of5Encoder.Encode(encoded),
            readable,
            options,
            DeutschePostData.NominalBarHeight);
    }
}
