// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The UPC-A symbology specified in ISO/IEC 15420. A UPC-A symbol has the same 95 module structure as an
/// EAN-13 symbol whose leading digit is zero: all six left-half characters use number set A. UPC-A differs
/// only in its human readable layout, where the number system and check digits print in the quiet zones and
/// the bars of their symbol characters are extended.
/// </summary>
public sealed class UpcASymbology : BarcodeSymbology
{
    private const int Width = 95;
    private const int BarCount = 30;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpcASymbology"/> class.
    /// </summary>
    public UpcASymbology()
    {
    }

    /// <summary>
    /// Gets the indexes of the extended bars: the guard pattern bars plus the bars of the first and last
    /// symbol characters, whose digits print in the quiet zones.
    /// </summary>
    private static ReadOnlySpan<int> GuardBars => [0, 1, 2, 3, 14, 15, 26, 27, 28, 29];

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        Span<char> digitBuffer = stackalloc char[12];
        ReadOnlySpan<char> digits = EanUpcEncoder.ValidateAndApplyCheckDigit(text, 11, digitBuffer);

        Span<byte> modules = stackalloc byte[Width];
        int position = 0;
        EanUpcEncoder.AppendPattern(modules, ref position, 0b101, 3);
        for (int i = 0; i < 6; i++)
        {
            EanUpcEncoder.AppendPattern(modules, ref position, EanUpcEncoder.NumberSetA[digits[i] - '0'], 7);
        }

        EanUpcEncoder.AppendPattern(modules, ref position, 0b01010, 5);
        for (int i = 6; i < 12; i++)
        {
            EanUpcEncoder.AppendPattern(modules, ref position, EanUpcEncoder.NumberSetC[digits[i] - '0'], 7);
        }

        EanUpcEncoder.AppendPattern(modules, ref position, 0b101, 3);

        float barHeight = EanUpcEncoder.ResolveBarHeight(options, EanUpcEncoder.NominalBarHeight);
        EanUpcEncoder.BuildGuardedHeights(BarCount, barHeight, GuardBars, options, out float[] heights, out float[] tops);

        // ISO/IEC 15420 prints the number system digit in the leading quiet zone, the check digit in the
        // trailing quiet zone, and every other digit below its own symbol character beside the extended
        // character bars. Digits hang one module below the digit bars and flow past the extended bars,
        // as in the nominal symbol drawing.
        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null)
        {
            float textLine = barHeight;
            placements = new BarcodeTextPlacement[12];
            placements[0] = new BarcodeTextPlacement(EanUpcEncoder.DigitString(digits[0]), -9F, -2F, BarcodeTextSide.BelowBars, textLine, EanUpcEncoder.QuietZoneDigitScale);
            EanUpcEncoder.FillDigitPlacements(placements, 1, digits, 1, 5, 10F, 7F, BarcodeTextSide.BelowBars, textLine);
            EanUpcEncoder.FillDigitPlacements(placements, 6, digits, 6, 5, 50F, 7F, BarcodeTextSide.BelowBars, textLine);
            placements[11] = new BarcodeTextPlacement(EanUpcEncoder.DigitString(digits[11]), 97F, 104F, BarcodeTextSide.BelowBars, textLine, EanUpcEncoder.QuietZoneDigitScale);
        }

        return new LinearBarcodeSymbol(EanUpcEncoder.ToRuns(modules), heights, tops, placements, 9, 9);
    }
}
