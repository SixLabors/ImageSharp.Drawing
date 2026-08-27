// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The EAN-8 symbology specified in ISO/IEC 15420. An EAN-8 symbol is 67 modules wide: a normal guard pattern,
/// four symbol characters from number set A, a centre guard pattern, four symbol characters from number set C
/// and a closing normal guard pattern. All eight digits have symbol characters; there is no parity-encoded digit.
/// </summary>
public sealed class Ean8Symbology : BarcodeSymbology
{
    private const int Width = 67;
    private const int BarCount = 22;

    /// <summary>
    /// Initializes a new instance of the <see cref="Ean8Symbology"/> class.
    /// </summary>
    public Ean8Symbology()
    {
    }

    /// <summary>
    /// Gets the indexes of the extended bars: the two bars of each of the left, centre and right guard patterns.
    /// </summary>
    private static ReadOnlySpan<int> GuardBars => [0, 1, 10, 11, 20, 21];

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
        => EncodeDigits(EanUpcEncoder.ValidateAndApplyCheckDigit(text, 7, "EAN-8"), options);

    /// <summary>
    /// Encodes eight verified digits into an EAN-8 symbol.
    /// </summary>
    /// <param name="digits">The eight digits including a verified check digit.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    internal static LinearBarcodeSymbol EncodeDigits(string digits, BarcodeOptions options)
    {
        Span<byte> modules = stackalloc byte[Width];
        int position = 0;
        EanUpcEncoder.AppendPattern(modules, ref position, 0b101, 3);
        for (int i = 0; i < 4; i++)
        {
            EanUpcEncoder.AppendPattern(modules, ref position, EanUpcEncoder.NumberSetA[digits[i] - '0'], 7);
        }

        EanUpcEncoder.AppendPattern(modules, ref position, 0b01010, 5);
        for (int i = 4; i < 8; i++)
        {
            EanUpcEncoder.AppendPattern(modules, ref position, EanUpcEncoder.NumberSetC[digits[i] - '0'], 7);
        }

        EanUpcEncoder.AppendPattern(modules, ref position, 0b101, 3);

        float barHeight = EanUpcEncoder.ResolveBarHeight(options, EanUpcEncoder.NominalEan8BarHeight);
        EanUpcEncoder.BuildGuardedHeights(BarCount, barHeight, GuardBars, options, out float[] heights, out float[] tops);

        // ISO/IEC 15420 prints every digit below its own symbol character. Digits hang one module below the digit
        // bars and flow past the extended guard bars, as in the nominal symbol drawing.
        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null)
        {
            float textLine = barHeight + 1;
            placements = new BarcodeTextPlacement[8];
            EanUpcEncoder.FillDigitPlacements(placements, 0, digits, 0, 4, 3F, 7F, textLine);
            EanUpcEncoder.FillDigitPlacements(placements, 4, digits, 4, 4, 36F, 7F, textLine);
        }

        return new LinearBarcodeSymbol(EanUpcEncoder.ToRuns(modules), heights, tops, placements, 7, 7);
    }
}
