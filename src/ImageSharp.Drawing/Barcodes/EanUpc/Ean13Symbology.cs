// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The EAN-13 symbology specified in ISO/IEC 15420. An EAN-13 symbol is 95 modules wide: a normal guard
/// pattern, six symbol characters, a centre guard pattern, six more symbol characters and a closing normal
/// guard pattern. The thirteenth (leading) digit has no symbol character; it is conveyed by the number set
/// parity of the six left-half characters.
/// </summary>
public sealed class Ean13Symbology : BarcodeSymbology
{
    private const int Width = 95;
    private const int BarCount = 30;

    /// <summary>
    /// Initializes a new instance of the <see cref="Ean13Symbology"/> class.
    /// </summary>
    public Ean13Symbology()
    {
    }

    /// <summary>
    /// Gets the indexes of the extended bars: the two bars of each of the left, centre and right guard patterns.
    /// </summary>
    private static ReadOnlySpan<int> GuardBars => [0, 1, 14, 15, 28, 29];

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
        => EncodeDigits(EanUpcEncoder.ValidateAndApplyCheckDigit(text, 12, "EAN-13"), options);

    /// <summary>
    /// Encodes thirteen verified digits into an EAN-13 symbol.
    /// </summary>
    /// <param name="digits">The thirteen digits including a verified check digit.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    internal static LinearBarcodeSymbol EncodeDigits(string digits, BarcodeOptions options)
        => EncodeDigits(digits, options, null);

    /// <summary>
    /// Encodes thirteen verified digits into an EAN-13 symbol, optionally with a caption above the bars.
    /// The ISBN, ISMN and ISSN symbologies print their own number above their EAN-13 symbol; the caption
    /// occupies a text band carved from the symbol top, shifting the bars down.
    /// </summary>
    /// <param name="digits">The thirteen digits including a verified check digit.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="caption">The text above the bars, or <see langword="null"/> for none.</param>
    /// <returns>The encoded symbol.</returns>
    internal static LinearBarcodeSymbol EncodeDigits(string digits, BarcodeOptions options, string? caption)
    {
        Span<byte> modules = stackalloc byte[Width];
        int position = 0;
        EanUpcEncoder.AppendPattern(modules, ref position, 0b101, 3);

        int parity = EanUpcEncoder.Ean13LeftParity[digits[0] - '0'];
        for (int i = 1; i <= 6; i++)
        {
            ReadOnlySpan<byte> numberSet = ((parity >> (6 - i)) & 1) == 0 ? EanUpcEncoder.NumberSetA : EanUpcEncoder.NumberSetB;
            EanUpcEncoder.AppendPattern(modules, ref position, numberSet[digits[i] - '0'], 7);
        }

        EanUpcEncoder.AppendPattern(modules, ref position, 0b01010, 5);
        for (int i = 7; i <= 12; i++)
        {
            EanUpcEncoder.AppendPattern(modules, ref position, EanUpcEncoder.NumberSetC[digits[i] - '0'], 7);
        }

        EanUpcEncoder.AppendPattern(modules, ref position, 0b101, 3);

        float barHeight = EanUpcEncoder.ResolveBarHeight(options, EanUpcEncoder.NominalBarHeight);
        EanUpcEncoder.BuildGuardedHeights(BarCount, barHeight, GuardBars, options, out float[] heights, out float[] tops);

        float band = 0;
        if (caption is not null && options.Font is not null)
        {
            band = EanUpcEncoder.MeasureCaptionStrip(caption, Width, options);
            for (int i = 0; i < tops.Length; i++)
            {
                tops[i] += band;
            }
        }

        // ISO/IEC 15420 prints the leading digit in the leading quiet zone and every other digit below its
        // own symbol character. Digits hang one module below the digit bars and flow past the extended
        // guard bars, as in the nominal symbol drawing.
        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null)
        {
            bool hasCaption = caption is not null;
            float textLine = band + barHeight + 1;
            placements = new BarcodeTextPlacement[hasCaption ? 14 : 13];
            placements[0] = new BarcodeTextPlacement(EanUpcEncoder.DigitString(digits[0]), -9F, -2F, textLine);
            EanUpcEncoder.FillDigitPlacements(placements, 1, digits, 1, 6, 3F, 7F, textLine);
            EanUpcEncoder.FillDigitPlacements(placements, 7, digits, 7, 6, 50F, 7F, textLine);
            if (hasCaption)
            {
                placements[13] = new BarcodeTextPlacement(caption!, 0F, Width, 0F, 1F, true);
            }
        }

        return new LinearBarcodeSymbol(EanUpcEncoder.ToRuns(modules), heights, tops, placements, 11, 7);
    }
}
