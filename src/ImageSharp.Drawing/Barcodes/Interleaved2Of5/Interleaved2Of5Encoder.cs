// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the Interleaved 2 of 5 symbology family, which AIM USS-I 2/5 defines and ISO/IEC 16390 succeeded. Section 2.1 gives every character
/// "two wide elements and three narrow elements", with the bars carrying "the more significant digit of the pair" and the spaces the less significant. Section
/// 5.3 of the GS1 General Specifications gives the same encodation for ITF-14. A symbol carries a quiet zone, the start pattern, the digit pairs, the stop
/// pattern and a quiet zone.
/// </summary>
internal static class Interleaved2Of5Encoder
{
    /// <summary>
    /// The largest number of digits a symbol carries. Note 1 to Table 1 of AIM USS-I 2/5 "permits encodation of any length numeric field having an even number
    /// of digits", so the symbology sets no maximum of its own.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The smallest quiet zone in modules on each side. Section 3.2 of AIM USS-I 2/5: "The minimum quiet zone width is ten times the X dimension or 0.10 inch
    /// (2.54 mm), whichever is greater." Section 5.3.2.2 of the GS1 General Specifications gives ITF-14 the 10X alone.
    /// </summary>
    public const int QuietZone = 10;

    /// <summary>
    /// The smallest quiet zone in millimetres, the 0.10 inch of section 3.2 of AIM USS-I 2/5.
    /// </summary>
    private const double MinimumQuietZoneMillimetres = 2.54;

    /// <summary>
    /// The number of characters a caller holds inline to build symbol data in. Data this long covers the labels the symbology is used for, and longer data
    /// rents its buffer once.
    /// </summary>
    public const int InlineBufferLength = 64;

    /// <summary>
    /// The element count of one digit, five bars or five spaces.
    /// </summary>
    private const int ElementsPerDigit = 5;

    /// <summary>
    /// The element count of the start pattern. Section 2.3 of AIM USS-I 2/5: "The start pattern consists of four narrow elements beginning with a bar."
    /// </summary>
    private const int StartElements = 4;

    /// <summary>
    /// The element count of the stop pattern. Section 2.3 of AIM USS-I 2/5: "The stop pattern is a wide bar followed by two narrow elements."
    /// </summary>
    private const int StopElements = 3;

    /// <summary>
    /// The width of a wide element in modules. Section 3.2 of AIM USS-I 2/5: "Wide element widths must be in the range 2.0X to 3.0X", narrowing to "2.2X to
    /// 3.0X" below an X dimension of 0.020 inches, and section 5.3.2.2 of the GS1 General Specifications gives ITF-14 the range 2.25:1 to 3.0:1. A wide
    /// element of 3 modules lies inside every range, so the symbologies without a target ratio take it.
    /// </summary>
    public const int WideElement = 3;

    /// <summary>
    /// The width of one digit in modules: the two wide elements and three narrow elements of section 2.1 of AIM USS-I 2/5.
    /// </summary>
    private const int ModulesPerDigit = (2 * WideElement) + 3;

    /// <summary>
    /// The width of the start and stop patterns together in modules: the four narrow elements of the start pattern, then the wide bar and two narrow elements
    /// of the stop pattern.
    /// </summary>
    private const int StartStopModules = StartElements + WideElement + 2;

    /// <summary>
    /// The bar height as a fraction of the symbol length when the caller sets no height. Section 3.2 of AIM USS-I 2/5: "the minimum bar height should be 0.25
    /// inches (6.35 mm) or 15 percent of the bar code symbol length, whichever is greater." Appendix A defines the symbol length as "The distance between the
    /// outside edges of the quiet zones".
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <summary>
    /// The smallest bar height in millimetres when the caller sets none, section 3.2 of AIM USS-I 2/5.
    /// </summary>
    private const float MinimumBarHeightMillimetres = 6.35F;

    /// <summary>
    /// Gets the element widths of every digit, one bit per element, most significant element first: a set bit is a wide element and a clear bit a narrow one.
    /// These are the patterns of Table 2 of AIM USS-I 2/5 and of Table 5-23 of the GS1 General Specifications, indexed by digit. Section 2.2 weights the
    /// positions 1, 2, 4, 7 and parity and gives every digit "exactly two non-zero weights".
    /// </summary>
    private static ReadOnlySpan<byte> Patterns =>
    [
        0b00110, 0b10001, 0b01001, 0b11000, 0b00101,
        0b10100, 0b01100, 0b00011, 0b10010, 0b01010,
    ];

    /// <summary>
    /// Encodes digits and builds the symbol at the proportional bar height of section 3.2 of AIM USS-I 2/5.
    /// </summary>
    /// <param name="digits">The digits to encode, an even number of them, already validated.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="xDimension">The X dimension in millimetres, <see cref="BarcodeSymbol.XDimension"/>.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(ReadOnlySpan<char> digits, ReadOnlySpan<char> readable, BarcodeOptions options, double xDimension, ref BarcodeSymbol symbol)
    {
        // The quiet zone of section 3.2 of AIM USS-I 2/5 is ten modules, or 2.54 mm when that is wider.
        float quietZone = MathF.Max(QuietZone, (float)(MinimumQuietZoneMillimetres / xDimension));

        // Every digit has the same width, so the symbol length that sets the proportional bar height is known before the runs are written.
        float symbolLength = (digits.Length * ModulesPerDigit) + StartStopModules + (2 * quietZone);

        // Section 3.2 gives the minimum bar height as a recommendation, "should be", so it sets the nominal bar height and does not clamp the bar height.
        float nominalBarHeight = MathF.Max(symbolLength * NominalBarHeightFraction, (float)(MinimumBarHeightMillimetres / xDimension));
        Interleaved2Of5Layout layout = new()
        {
            XDimension = xDimension,
            NominalBarHeight = nominalBarHeight,
            MinimumBarHeight = 0F,
            MaximumBarHeight = float.PositiveInfinity,
            QuietZone = quietZone,
            TextClearance = BarcodeTextPlacement.Clearance,
            NarrowElement = 1,
            WideElement = WideElement,
        };

        BuildSymbol(digits, readable, in layout, options, ref symbol);
    }

    /// <summary>
    /// Encodes digits and builds the symbol at the dimensions of an application standard, which can put the symbol inside a bearer bar. The runs alternate bar
    /// and space, start with the first bar of the start pattern, carry the digit pairs and end on the bar of the stop pattern. The 2 of 5 symbologies carry no
    /// guard bars, so every bar runs the full height. Section 5.3.2.4 of the GS1 General Specifications butts the bearer bar "directly against the top and
    /// bottom of the bars", so the bars start one thickness below the symbol top, and the human readable interpretation faces the bottom of the lower bearer
    /// bar.
    /// </summary>
    /// <param name="digits">The digits to encode, an even number of them, already validated.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="layout">The dimensions of the application standard.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(
        ReadOnlySpan<char> digits,
        ReadOnlySpan<char> readable,
        in Interleaved2Of5Layout layout,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        // The runs count run units, and a narrow element is one module, so a run unit is the module divided by the narrow width.
        int narrow = layout.NarrowElement;
        int wide = layout.WideElement;
        float runUnit = 1F / narrow;
        int runCount = (digits.Length * ElementsPerDigit) + StartElements + StopElements;
        Span<int> runs = symbol.BeginLinear(runCount, layout.XDimension, out Span<float> heights, out Span<float> tops);
        runs[..StartElements].Fill(narrow);
        int written = StartElements;

        // Section 2.2.3 of AIM USS-I 2/5: "the more significant digit is encoded in the bars and the less significant digit is encoded in the spaces". Step 5
        // of section 5.3.2.1.1 of the GS1 General Specifications takes those elements alternately, starting with the first bar.
        for (int i = 0; i < digits.Length; i += 2)
        {
            int bars = Patterns[digits[i] - '0'];
            int spaces = Patterns[digits[i + 1] - '0'];
            for (int bit = ElementsPerDigit - 1; bit >= 0; bit--)
            {
                runs[written++] = ((bars >> bit) & 1) != 0 ? wide : narrow;
                runs[written++] = ((spaces >> bit) & 1) != 0 ? wide : narrow;
            }
        }

        runs[written++] = wide;
        runs[written++] = narrow;
        runs[written] = narrow;

        // Every digit has two wide and three narrow elements, the start pattern four narrow elements and the stop pattern a wide and two narrow elements.
        float widthInModules = ((digits.Length * ((2 * wide) + (3 * narrow))) + (StartElements * narrow) + wide + (2 * narrow)) * runUnit;
        float bearerBarThickness = layout.BearerBarThickness;
        float barHeight = options.ResolveBarHeight(layout.XDimension, layout.MinimumBarHeight, layout.NominalBarHeight, layout.MaximumBarHeight);
        heights.Fill(barHeight);
        tops.Fill(bearerBarThickness);

        if (options.Font is not null)
        {
            float textEdge = bearerBarThickness + barHeight + bearerBarThickness + layout.TextClearance;
            symbol.ReserveText(1, readable.Length);
            symbol.AddText(readable, 0F, widthInModules, BarcodeTextSide.BelowBars, textEdge);
        }

        symbol.EndLinear(layout.QuietZone, layout.QuietZone, bearerBarThickness, runUnit, false);
    }

    /// <summary>
    /// Calculates the optional check digit of section 2.5 of AIM USS-I 2/5 by the modulo 10 calculation of Appendix C: the digits are weighted 3 and 1
    /// alternately from the rightmost digit, which takes 3, and the check digit lifts the sum to the next multiple of ten.
    /// </summary>
    /// <param name="digits">The data digits.</param>
    /// <returns>The check digit value.</returns>
    public static int CheckDigit(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        int weight = 3;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            sum += (digits[i] - '0') * weight;
            weight = 4 - weight;
        }

        return (10 - (sum % 10)) % 10;
    }
}
