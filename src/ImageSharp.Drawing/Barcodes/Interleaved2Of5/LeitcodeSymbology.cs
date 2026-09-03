// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Deutsche Post Leitcode, the routing code of a mail item. An Interleaved 2 of 5 symbol carries
/// it. The code is fourteen digits: the five digit postal code, the three digit street code, the three
/// digit house number, the two digit product code and the check digit. Deutsche Post fixes the narrow
/// module at 0.375 mm to 0.5 mm, the wide to narrow ratio at 1:2 to 1:3, the height at 25 mm or more
/// and the quiet zone at 5 mm or more on each side.
/// <para>
/// The input is the thirteen data digits, or those digits and the check digit. This class always
/// calculates the check digit. When the input carries one, this class compares the two.
/// </para>
/// <para>
/// The printed line separates the fields with full stops and sets the check digit off with a space:
/// "Die Klartextzeile enthält zwischen den einzelnen Stellenbereichen jeweils einen Punkt, die
/// Prüfziffer wird durch ein Leerzeichen etwas abgesetzt." The symbol carries neither.
/// </para>
/// </summary>
public sealed class LeitcodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// The number of data digits, excluding the check digit.
    /// </summary>
    private const int Digits = 13;

    /// <summary>
    /// Initializes a new instance of the <see cref="LeitcodeSymbology"/> class.
    /// </summary>
    public LeitcodeSymbology()
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
            : $"{digits[..5]}.{digits[5..8]}.{digits[8..11]}.{digits[11..13]} {digits[13]}";

        return Interleaved2Of5Encoder.BuildSymbol(
            Interleaved2Of5Encoder.Encode(encoded),
            readable,
            options,
            DeutschePostData.NominalBarHeight);
    }
}
