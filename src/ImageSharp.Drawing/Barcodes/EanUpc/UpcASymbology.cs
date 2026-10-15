// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The UPC-A symbology, which ISO/IEC 15420 specifies. A UPC-A symbol has the same 95 module structure as
/// an EAN-13 symbol with a leading zero, and all six left half characters use number set A.
/// <para>
/// Only the human readable layout differs. The number system digit and the check digit print in the quiet
/// zones, and the bars of their symbol characters extend below the others.
/// </para>
/// </summary>
public sealed class UpcASymbology : BarcodeSymbology
{
    private const int Width = 95;

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
    public override double NominalXDimension => EanUpcEncoder.NominalXDimension;

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        InlineArray12<char> digitStorage = default;
        Span<char> digitBuffer = digitStorage;
        ReadOnlySpan<char> digits = EanUpcEncoder.ValidateAndApplyCheckDigit(text, 11, digitBuffer);

        InlineArray128<byte> moduleStorage = default;
        Span<byte> modules = ((Span<byte>)moduleStorage)[..Width];
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

        EanUpcEncoder.BeginRuns(modules, ref symbol, out Span<float> heights);
        float barHeight = options.ResolveBarHeight(EanUpcEncoder.NominalXDimension, EanUpcEncoder.NominalBarHeight);
        EanUpcEncoder.FillGuardedHeights(heights, barHeight, GuardBars, options);

        // ISO/IEC 15420 prints the number system digit in the leading quiet zone, the check digit in the
        // trailing quiet zone, and every other digit below its own symbol character beside the extended
        // character bars. Digits hang one module below the digit bars and flow past the extended bars,
        // as in the nominal symbol drawing.
        if (options.Font is not null)
        {
            float textLine = barHeight + BarcodeTextPlacement.Clearance;
            symbol.ReserveText(12, 12);
            symbol.AddText(digits[..1], -9F, -2F, BarcodeTextSide.BelowBars, textLine, EanUpcEncoder.QuietZoneDigitScale);
            EanUpcEncoder.AddDigits(digits[1..6], 10F, 7F, BarcodeTextSide.BelowBars, textLine, ref symbol);
            EanUpcEncoder.AddDigits(digits[6..11], 50F, 7F, BarcodeTextSide.BelowBars, textLine, ref symbol);
            symbol.AddText(digits[11..12], 97F, 104F, BarcodeTextSide.BelowBars, textLine, EanUpcEncoder.QuietZoneDigitScale);
        }

        symbol.EndLinear(9, 9);
    }
}
