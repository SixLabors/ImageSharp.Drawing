// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the United States Postal Service height modulated symbologies, POSTNET and
/// PLANET. Section 708.4.2.5 of the Domestic Mail Manual gives the bar dimensions: "All bars must be
/// 0.020 ±0.005 inch wide", "horizontal spacing of the bars must be 22 ±2 bars per inch", "A full bar
/// must be 0.125 ±0.010 inch high" and "A half bar must be 0.050 ±0.010 inch high". The module is the
/// 0.020 inch bar width. A pitch of 1/22 inch is 25/11 modules, so a run unit is 1/11 module, a bar is 11
/// units and a space is 14 units.
/// </summary>
internal static class UspsPostalEncoder
{
    /// <summary>
    /// The nominal X dimension in millimetres: the 0.020 inch bar width.
    /// </summary>
    public const float NominalXDimension = 0.508F;

    /// <summary>
    /// The width of a run unit in modules.
    /// </summary>
    public const float RunUnit = 1F / 11F;

    /// <summary>
    /// The width of a bar in run units: one module of 0.020 inch.
    /// </summary>
    public const int BarUnits = 11;

    /// <summary>
    /// The width of the space between bars in run units: the rest of the 1/22 inch pitch.
    /// </summary>
    public const int SpaceUnits = 14;

    /// <summary>
    /// The height of a full bar in modules: 0.125 inch at the 0.020 inch module.
    /// </summary>
    public const float FullBar = 6.25F;

    /// <summary>
    /// The height of a half bar in modules: 0.050 inch at the 0.020 inch module.
    /// </summary>
    public const float HalfBar = 2.5F;

    /// <summary>
    /// The clear zone in modules on each end: the "0.125 inch on each end of the barcode" of section
    /// 2.3.2 of USPS-B-3200, whose bars have the same width and pitch. Section 708.4.2 of the Domestic
    /// Mail Manual gives none.
    /// </summary>
    public const float ClearZone = 6.25F;

    /// <summary>
    /// The number of bars in one digit.
    /// </summary>
    public const int BarsPerDigit = 5;

    /// <summary>
    /// The clear space in modules between the bars and the human readable interpretation: the 1/25 inch
    /// of section 202.5.7 c of the Domestic Mail Manual, "The minimum clearance between the barcode and
    /// any information line above or below it within the address block must be at least 1/25 inch", at
    /// the 0.020 inch module.
    /// </summary>
    public const float TextClearance = 2F;

    /// <summary>
    /// Gets the bars of every digit, one bit per bar, most significant bar first: a set bit is a full bar
    /// in POSTNET and a half bar in PLANET. Section 708.4.2.1 of the Domestic Mail Manual: "A tall bar
    /// represents 1, and a short bar represents 0", and the five bars weigh 7, 4, 2, 1 and 0, with the
    /// digit 0 as the exception 11000.
    /// </summary>
    private static ReadOnlySpan<byte> Digits => [0b11000, 0b00011, 0b00101, 0b00110, 0b01001, 0b01010, 0b01100, 0b10001, 0b10010, 0b10100];

    /// <summary>
    /// Validates that the text is digits alone.
    /// </summary>
    /// <param name="text">The text to validate.</param>
    public static void ValidateDigits(string text)
    {
        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException($"The symbology carries only digits; got {current.ToDisplayString()}.", nameof(text));
            }
        }
    }

    /// <summary>
    /// Calculates the correction digit. Section 708.4.2.1: it is "derived from adding the numbers in the
    /// ZIP Code (or ZIP+4 or delivery point code) and determining which single-digit number must be added
    /// to that sum to make the total a multiple of 10".
    /// </summary>
    /// <param name="digits">The digits the symbol carries.</param>
    /// <returns>The correction digit.</returns>
    public static int CorrectionDigit(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        for (int i = 0; i < digits.Length; i++)
        {
            sum += digits[i] - '0';
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>
    /// Builds the symbol: a frame bar, five bars per digit, five for the correction digit and a frame bar,
    /// with full bars where a set bit stands in POSTNET and half bars in PLANET. Section 708.4.2.1: "The
    /// first and last bars of the barcode are frame bars and must always be full bars."
    /// </summary>
    /// <param name="digits">The digits the symbol carries, the correction digit included.</param>
    /// <param name="text">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="setBitIsFull">Whether a set bit is a full bar, as in POSTNET, rather than a half bar.</param>
    /// <returns>The encoded symbol.</returns>
    public static LinearBarcodeSymbol BuildSymbol(ReadOnlySpan<char> digits, string text, BarcodeOptions options, bool setBitIsFull)
    {
        int barCount = (digits.Length * BarsPerDigit) + 2;
        int[] runs = new int[(barCount * 2) - 1];
        for (int i = 0; i < runs.Length; i++)
        {
            runs[i] = (i & 1) == 0 ? BarUnits : SpaceUnits;
        }

        float fullBar = EanUpcEncoder.ResolveBarHeight(options, NominalXDimension, FullBar);
        float halfBar = fullBar * HalfBar / FullBar;
        float[] heights = new float[barCount];
        float[] tops = new float[barCount];
        heights[0] = fullBar;
        heights[barCount - 1] = fullBar;
        int bar = 1;
        for (int i = 0; i < digits.Length; i++)
        {
            int pattern = Digits[digits[i] - '0'];
            for (int bit = BarsPerDigit - 1; bit >= 0; bit--)
            {
                bool set = ((pattern >> bit) & 1) == 1;
                heights[bar] = set == setBitIsFull ? fullBar : halfBar;
                tops[bar] = fullBar - heights[bar];
                bar++;
            }
        }

        float widthInModules = ((barCount * BarUnits) + ((barCount - 1) * SpaceUnits)) * RunUnit;
        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null && text.Length > 0)
        {
            placements = [new BarcodeTextPlacement(text, 0F, widthInModules, BarcodeTextSide.BelowBars, fullBar + TextClearance)];
        }

        return new LinearBarcodeSymbol(runs, heights, tops, placements, ClearZone, ClearZone, 0F, RunUnit, true);
    }
}
