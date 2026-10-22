// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the United States Postal Service height modulated symbologies, POSTNET and PLANET. Section 708.4.2.5 of the Domestic Mail Manual gives
/// the bar dimensions: "All bars must be 0.020 ±0.005 inch wide", "horizontal spacing of the bars must be 22 ±2 bars per inch", "A full bar must be 0.125
/// ±0.010 inch high" and "A half bar must be 0.050 ±0.010 inch high". The module is the 0.020 inch bar width. The pitch stays 1/22 inch at every bar width,
/// and the space is the rest of it. Section 708.4.2.5 d: "pitch (a bar and a space) must average at least 0.0416 inch but no more than 0.05 inch". The code
/// clamps the pitch to that range in whole pixels.
/// </summary>
internal static class UspsPostalEncoder
{
    /// <summary>
    /// The nominal X dimension in millimetres: the 0.020 inch bar width.
    /// </summary>
    public const double NominalXDimension = 0.508;

    /// <summary>
    /// The smallest X dimension in millimetres, 0.015 inch. DMM 708.4.2.5 c: "All bars must be 0.020 ±0.005 inch wide." USPS-B-3200 section 2.3.1: "Individual
    /// bars shall be printed with a width of 0.020±0.005 inch."
    /// </summary>
    public const double MinimumXDimension = 0.015 * 25.4;

    /// <summary>
    /// The largest X dimension in millimetres, 0.025 inch, from the same tolerance.
    /// </summary>
    public const double MaximumXDimension = 0.025 * 25.4;

    /// <summary>
    /// The pitch, a bar and a space, in millimetres: 1/22 inch, from "22 ±2 bars per inch".
    /// </summary>
    public const double Pitch = 25.4 / 22;

    /// <summary>
    /// The clear zone in modules on each end, 1/8 inch at the 0.020 inch module. Section 202.5.7 c of the Domestic Mail Manual: "The clearance between the
    /// leftmost and rightmost bars and any adjacent printing must be at least 1/8 inch."
    /// </summary>
    public const float ClearZone = 6.25F;

    /// <summary>
    /// The height of a full bar in modules: 0.125 inch at the 0.020 inch module.
    /// </summary>
    private const float FullBar = 6.25F;

    /// <summary>
    /// The smallest full bar in modules: 0.115 inch at the 0.020 inch module. DMM 708.4.2.5 a: "A full bar must be 0.125 ±0.010 inch high." The half bar
    /// keeps its ratio to the full bar, and at each limit of the full bar it stays inside its own "0.050 ±0.010 inch" of 708.4.2.5 b.
    /// </summary>
    private const float MinimumFullBar = 5.75F;

    /// <summary>
    /// The largest full bar in modules: 0.135 inch at the 0.020 inch module, from the same tolerance.
    /// </summary>
    private const float MaximumFullBar = 6.75F;

    /// <summary>
    /// The height of a half bar in modules: 0.050 inch at the 0.020 inch module.
    /// </summary>
    private const float HalfBar = 2.5F;

    /// <summary>
    /// The clear zone in modules above and below the bars, 1/25 inch at the 0.020 inch module. Section 202.5.7 c of the Domestic Mail Manual: "The minimum
    /// clearance between the barcode and any information line above or below it within the address block must be at least 1/25 inch". Section 708.4.4.1 b
    /// gives the same "1/25 inch above and below the barcode".
    /// </summary>
    private const float VerticalClearZone = 2F;

    /// <summary>
    /// The number of bars in one digit.
    /// </summary>
    private const int BarsPerDigit = 5;

    /// <summary>
    /// The fewest bars per inch. Section 708.4.2.5 d: the "pitch (a bar and a space) must average at least 0.0416 inch but no more than 0.05 inch". A pitch
    /// of 0.05 inch is 20 bars per inch.
    /// </summary>
    private const int MinimumBarsPerInch = 20;

    /// <summary>
    /// The most bars per inch, from the same range. A pitch of 0.0416 inch is 24 bars per inch.
    /// </summary>
    private const int MaximumBarsPerInch = 24;

    /// <summary>
    /// Gets the bars of every digit, one bit per bar, most significant bar first: a set bit is a full bar in POSTNET and a half bar in PLANET. Section
    /// 708.4.2.1 of the Domestic Mail Manual: "A tall bar represents "1," and a short bar represents "0."" Each digit sets two of its five bars.
    /// </summary>
    private static ReadOnlySpan<byte> Digits => [0b11000, 0b00011, 0b00101, 0b00110, 0b01001, 0b01010, 0b01100, 0b10001, 0b10010, 0b10100];

    /// <summary>
    /// Builds the symbol: a frame bar, five bars per digit, five for the correction digit and a frame bar, with full bars where a set bit stands in POSTNET and
    /// half bars in PLANET. Section 708.4.2.1: "The first and last bars of the barcode are frame bars and must always be full bars." A half bar stands on the
    /// baseline of the full bars.
    /// </summary>
    /// <param name="digits">The data digits, already validated. The symbol adds the correction digit after them.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="setBitIsFull">Whether a set bit is a full bar, as in POSTNET, rather than a half bar.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(ReadOnlySpan<char> digits, BarcodeOptions options, bool setBitIsFull, ref BarcodeSymbol symbol)
    {
        double xDimension = options.ResolveXDimension(MinimumXDimension, NominalXDimension, MaximumXDimension);

        // The emitter draws every run of these uniform bars at a whole number of pixels, so the runs count pixels. A bar is one module of whole pixels. The
        // pitch stays 1/22 inch at every bar width, so the space is the rest of the pitch at the nearest whole pixel, at least one.
        int barPixels = options.ModulePixels(xDimension);
        double spacePixels = Math.Max(1, Math.Round(((Pitch / xDimension) - 1) * barPixels, MidpointRounding.AwayFromZero));

        // Whole pixels can move the pitch out of 20 to 24 bars per inch at the end of the bar width range or at a low resolution. A pitch of Dpi / n pixels
        // is n bars per inch. So the code clamps the pitch in whole pixels, as ResolveXDimension clamps the module. The upper limit is the largest whole
        // number of pixels at or under the largest pitch. The lower limit is the smallest whole number of pixels at or over the smallest pitch, with a space
        // of at least one pixel. The lower limit applies last.
        double highest = Math.Floor((options.Dpi / MinimumBarsPerInch) + BarcodeOptions.LimitTolerance);
        double lowest = Math.Max(barPixels + 1, Math.Ceiling((options.Dpi / MaximumBarsPerInch) - BarcodeOptions.LimitTolerance));
        int space = (int)Math.Max(Math.Min(barPixels + spacePixels, highest), lowest) - barPixels;

        // Every digit and the correction digit is five bars, and a full frame bar stands at each end.
        int barCount = ((digits.Length + 1) * BarsPerDigit) + 2;
        Span<int> runs = symbol.BeginLinear((barCount * 2) - 1, xDimension, out Span<float> heights, out Span<float> tops);
        for (int i = 0; i < runs.Length; i++)
        {
            runs[i] = (i & 1) == 0 ? barPixels : space;
        }

        // The bar heights and the clear zones are inches in the Domestic Mail Manual, given here in modules of the nominal 0.020 inch bar. The module that the
        // symbol draws at can round away from 0.020 inch, so the same inches are this many of its modules. The half bar keeps its ratio to the full bar.
        float nominal = (float)(NominalXDimension / xDimension);
        float scale = options.ResolveBarHeight(xDimension, MinimumFullBar * nominal, FullBar * nominal, MaximumFullBar * nominal) / FullBar;
        float halfBar = HalfBar * scale;
        float aboveHalfBar = (FullBar - HalfBar) * scale;
        float fullBar = aboveHalfBar + halfBar;

        int correction = CorrectionDigit(digits);
        heights[0] = fullBar;
        heights[^1] = fullBar;
        int bar = 1;
        for (int i = 0; i <= digits.Length; i++)
        {
            int pattern = Digits[i < digits.Length ? digits[i] - '0' : correction];
            for (int bit = BarsPerDigit - 1; bit >= 0; bit--)
            {
                bool full = (((pattern >> bit) & 1) == 1) == setBitIsFull;
                heights[bar] = full ? fullBar : halfBar;
                tops[bar] = full ? 0F : aboveHalfBar;
                bar++;
            }
        }

        float runUnit = 1F / barPixels;
        symbol.EndLinear(ClearZone * nominal, ClearZone * nominal, VerticalClearZone * nominal, VerticalClearZone * nominal, 0F, runUnit, true);
    }

    /// <summary>
    /// Calculates the correction digit. Section 708.4.2.1: it is "derived from adding the numbers in the ZIP Code (or ZIP+4 or delivery point code) and
    /// determining which single-digit number must be added to that sum to make the total a multiple of 10".
    /// </summary>
    /// <param name="digits">The digits the symbol carries.</param>
    /// <returns>The correction digit.</returns>
    private static int CorrectionDigit(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        foreach (char digit in digits)
        {
            sum += digit - '0';
        }

        return (10 - (sum % 10)) % 10;
    }
}
