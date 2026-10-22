// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The ITF-14 symbology, which section 5.3 of the GS1 General Specifications defines as an Interleaved 2 of 5 symbol inside a bearer bar. Section 5.3.1 fixes
/// the data at 14 digits, and section 5.3.2.1.3 requires the check digit of section 7.9. The input is 13 digits, and the encoder calculates the check digit, or
/// 14 digits, and the encoder validates it. Section 4.14 rule 2.a permits spaces in the human readable interpretation and forbids them in the symbol, so the
/// input can carry spaces, which the printed line keeps and the symbol drops.
/// <para>
/// Section 5.3.2.4 requires the bearer bar, which for plate printing "has a constant thickness of 4.83 millimetres (0.190 inch) and must completely surround
/// the symbol, including its Quiet Zones and butt directly against the top and bottom of the bars". The X dimension is the target of 0.495 millimetre of
/// Table 5-47, symbol specification table 2 for trade items scanned in general distribution. The symbol specification tables of section 5.12.3 give ITF-14
/// X dimensions from 0.170 to 1.016 millimetres. An X dimension outside that range draws at the nearest limit. Section 5.3.2.2 gives the 10X quiet zones
/// and 1.02 millimetres between the bearer bar and the human readable characters. The symbol height table of section 5.12.3.2 gives a minimum bar height of
/// 31.75 millimetres and a wide to narrow ratio target of 2.5:1. A wide element rounds half up to a whole pixel, so the ratio is 2.67 at three pixels a
/// module, inside the range of 2.25:1 to 3.0:1 of section 5.3.2.2. The bar height, the bearer bar and the text space are in millimetres, so they hold at any
/// X dimension. The bearer bar is the 4.83 millimetres of plate printing, more than "a minimum of twice the width of a narrow bar" that section 5.3.2.4
/// requires for printing methods without plates. A calculated check digit prints after the data, and after a space when the input carries spaces, as Figure
/// 5-32 shows.
/// </para>
/// </summary>
public sealed class Itf14Symbology : BarcodeSymbology
{
    /// <summary>
    /// The number of digits the symbol carries, the check digit included. Section 5.3.1: "Data string length encodable: fixed length at 14 digits."
    /// </summary>
    private const int Digits = 14;

    /// <summary>
    /// The nominal X dimension in millimetres: the target X of 0.495 millimetres of Table 5-47, symbol specification table 2, which Table 2-37 gives to
    /// "Fixed measure trade items scanned in general distribution".
    /// </summary>
    private const double XDimension = 0.495;

    /// <summary>
    /// The bar height in millimetres when the caller sets none. Footnote (****) of the symbol height table in section 5.12.3.2: "For GS1-128 and ITF-14
    /// symbols the minimum symbol height for General distribution scanning is always 31.75 millimetres (1.250 inch)."
    /// </summary>
    private const double NominalBarHeight = 31.75;

    /// <summary>
    /// The bearer bar thickness in millimetres: the 4.83 millimetres of section 5.3.2.4 for printing plates, more than the two modules that the same section
    /// requires for printing methods without plates at every X dimension of Table 5-47.
    /// </summary>
    private const double BearerBarThickness = 4.83;

    /// <summary>
    /// The smallest space between the bearer bar and the human readable interpretation in millimetres. Section 5.3.2.2: "A minimum space of 1.02 millimetre
    /// (0.040 inch) between the bottom line of the bearer bar and the top of the human readable characters is required."
    /// </summary>
    private const double TextClearance = 1.02;

    /// <summary>
    /// The narrow term of the wide to narrow ratio, <see cref="WideElement"/> / <see cref="NarrowElement"/>.
    /// </summary>
    private const int NarrowElement = 2;

    /// <summary>
    /// The wide term of the wide to narrow ratio. The symbol height table of section 5.12.3.2: "The ITF-14 symbol's bar width ratio target is 2.5:1". Section
    /// 5.3.2.2 gives the range "2.25:1 to 3.0:1".
    /// </summary>
    private const int WideElement = 5;

    /// <summary>
    /// Initializes a new instance of the <see cref="Itf14Symbology"/> class.
    /// </summary>
    public Itf14Symbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));

        // The interpretation repeats the input, and the input can group the digits with spaces, so the input is bounded at one grouping space after each digit.
        Guard.MustBeLessThanOrEqualTo(text.Length, Digits * 2, nameof(text));

        InlineArray14<char> digitStorage = default;
        Span<char> digits = digitStorage;
        int count = 0;
        bool hasSpaces = false;
        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (current.Value == ' ')
            {
                hasSpaces = true;
                continue;
            }

            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException($"ITF-14 carries only digits; got {current.ToDisplayString()}.", nameof(text));
            }

            if (count == Digits)
            {
                throw new ArgumentException($"ITF-14 carries 13 digits and a check digit; got more than {Digits}.", nameof(text));
            }

            digits[count++] = (char)current.Value;
        }

        if (count != Digits - 1 && count != Digits)
        {
            throw new ArgumentException($"ITF-14 carries 13 digits and a check digit; got {count}.", nameof(text));
        }

        int check = Gs1Data.ComputeCheckDigit(digits[..(Digits - 1)]);
        if (count == Digits && digits[Digits - 1] - '0' != check)
        {
            throw new ArgumentException($"Incorrect check digit: expected {check}, got {digits[Digits - 1]}.", nameof(text));
        }

        digits[Digits - 1] = (char)('0' + check);

        // The interpretation is the input as given, with the check digit after it when the input left it out, set off by a space when the input groups its
        // digits with spaces.
        int readableLength = text.Length + (count == Digits ? 0 : hasSpaces ? 2 : 1);
        InlineArray32<char> readableStorage = default;
        Span<char> readable = ((Span<char>)readableStorage)[..readableLength];
        text.CopyTo(readable);
        if (count != Digits)
        {
            readable[^1] = digits[Digits - 1];
            if (hasSpaces)
            {
                readable[^2] = ' ';
            }
        }

        double xDimension = options.ResolveXDimension(Gs1Data.Itf14MinimumXDimension, XDimension, Gs1Data.Itf14MaximumXDimension);

        // The runs count pixels, so every element draws at a whole number of pixels. A wide element is WideElement / NarrowElement modules, rounded half up
        // to a whole pixel: at an odd number of pixels a module the ratio of 2.5 becomes 3.0 at one pixel and 2.67 at three, and every wide element takes the
        // same width. The notes of Tables 5-47 and 5-51 of the GS1 General Specifications: "There is no maximum for the height". The notes of Tables
        // 5-50 and 5-51: "In no case SHALL the bar height be less than 5.08 millimetres".
        int pixels = options.ModulePixels(xDimension);
        Interleaved2Of5Layout layout = new()
        {
            XDimension = xDimension,
            NominalBarHeight = (float)(NominalBarHeight / xDimension),
            MinimumBarHeight = (float)(Gs1Data.MinimumTruncatedBarHeight / xDimension),
            MaximumBarHeight = float.PositiveInfinity,
            QuietZone = Interleaved2Of5Encoder.QuietZone,
            BearerBarThickness = (float)(BearerBarThickness / xDimension),
            TextClearance = MathF.Max(BarcodeTextPlacement.Clearance, (float)(TextClearance / xDimension)),
            NarrowElement = pixels,
            WideElement = ((WideElement * pixels) + (NarrowElement / 2)) / NarrowElement,
        };

        Interleaved2Of5Encoder.BuildSymbol(digits, readable, in layout, options, ref symbol);

        // Section 4.14 rule 2.a.iii: "The font size SHOULD be at least 2 mm (0.08 in) in height".
        symbol.SetTextSize(Gs1Data.MinimumTextSize, 0F);
    }
}
