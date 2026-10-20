// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Deutsche Post Identcode, the identification code of a mail item. An Interleaved 2 of 5 symbol carries it. The code is twelve digits: the two digit
/// origin mail centre, the customer number, the item number and the check digit. The symbol draws at a narrow module of 0.5 mm with bars 25 mm high when the
/// caller sets neither. The quiet zones are 5 mm on each side, and at least the ten modules of Interleaved 2 of 5.
/// <para>
/// The input is the eleven data digits, or those digits and the check digit. This class always calculates the check digit. When the input carries one, this
/// class compares the two.
/// </para>
/// <para>
/// The printed line separates the fields with full stops and spaces and sets the check digit off with a space. Deutsche Post assigns customer numbers of one to
/// five digits and gives no rule for where the customer number ends, so this class prints the twelve digits in groups of two, three, three and three. The
/// symbol carries neither the full stops nor the spaces.
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
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray12<char> encodedStorage = default;
        Span<char> encoded = encodedStorage;
        DeutschePostData.Prepare(text, Digits, encoded);

        // The interpretation groups the twelve digits as 12.345 678.901 2.
        InlineArray16<char> readableStorage = default;
        Span<char> readable = readableStorage;
        encoded[..2].CopyTo(readable);
        readable[2] = '.';
        encoded[2..5].CopyTo(readable[3..]);
        readable[6] = ' ';
        encoded[5..8].CopyTo(readable[7..]);
        readable[10] = '.';
        encoded[8..11].CopyTo(readable[11..]);
        readable[14] = ' ';
        readable[15] = encoded[11];

        DeutschePostData.BuildSymbol(encoded, readable, options, ref symbol);
    }
}
