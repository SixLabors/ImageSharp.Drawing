// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The UPC-A symbology, which the GS1 General Specifications specify. A UPC-A symbol has the same 95 module structure as an EAN-13 symbol with a leading zero,
/// and all six left half characters use number set A.
/// <para>
/// Only the human readable layout differs. The number system digit and the check digit print in the quiet zones, and the bars of their symbol characters extend
/// below the others.
/// </para>
/// </summary>
public sealed class UpcASymbology : BarcodeSymbology
{
    /// <summary>
    /// The quiet zone on each side of the symbol in modules, which holds the number system digit before the bars and the check digit after them.
    /// </summary>
    private const int QuietZone = 9;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpcASymbology"/> class.
    /// </summary>
    public UpcASymbology()
    {
    }

    /// <summary>
    /// Gets the indexes of the extended bars: the guard pattern bars plus the bars of the first and last symbol characters, whose digits print in the quiet
    /// zones.
    /// </summary>
    private static ReadOnlySpan<int> GuardBars => [0, 1, 2, 3, 14, 15, 26, 27, 28, 29];

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray12<char> digitStorage = default;
        ReadOnlySpan<char> digits = EanUpcEncoder.ValidateAndApplyCheckDigit(text, 11, digitStorage);

        // The modules are those of the EAN-13 symbol with a leading zero, whose left half number set sequence is number set A throughout.
        InlineArray128<byte> moduleStorage = default;
        Span<byte> modules = ((Span<byte>)moduleStorage)[..EanUpcEncoder.Ean13Width];
        InlineArray128<sbyte> correctionStorage = default;
        Span<sbyte> corrections = ((Span<sbyte>)correctionStorage)[..EanUpcEncoder.Ean13Width];
        EanUpcEncoder.WriteEan13Modules(EanUpcEncoder.Ean13LeftParity[0], digits, modules, corrections);

        EanUpcEncoder.BeginRuns(
            modules,
            corrections,
            options.ResolveXDimension(EanUpcEncoder.MinimumXDimension, EanUpcEncoder.NominalXDimension, EanUpcEncoder.MaximumXDimension),
            ref symbol,
            out Span<float> heights);

        float barHeight = options.ResolveBarHeight(
            symbol.XDimension,
            (float)(EanUpcEncoder.MinimumBarHeight / symbol.XDimension),
            EanUpcEncoder.NominalBarHeight,
            float.PositiveInfinity);

        EanUpcEncoder.FillGuardedHeights(heights, barHeight, GuardBars);

        // Section 5.2.5 of the GS1 General Specifications: "the size of the first and last digits should be reduced to a maximum width equivalent to four
        // modules". "The right-hand side of the first digit is positioned five module widths to the left of the leftmost guard bar." "The left-hand side of
        // the last digit is positioned five module widths to the right of the rightmost guard bar for UPC-A barcodes". The digits share one baseline, which
        // aligns the bottom edges of the smaller digits with the others. Every other digit prints below its own symbol character. Digits hang one module
        // below the digit bars and flow past the extended bars.
        if (options.Font is not null)
        {
            float textLine = barHeight + BarcodeTextPlacement.Clearance;
            symbol.ReserveText(12, 12);
            symbol.AddTextWithinSpan(digits[..1], -9F, -5F, BarcodeTextSide.BelowBars, textLine, BarcodeTextAlignment.Right);
            EanUpcEncoder.AddDigits(digits[1..6], 10F, 7F, BarcodeTextSide.BelowBars, textLine, ref symbol);
            EanUpcEncoder.AddDigits(digits[6..11], 50F, 7F, BarcodeTextSide.BelowBars, textLine, ref symbol);
            symbol.AddTextWithinSpan(digits[11..12], 100F, 104F, BarcodeTextSide.BelowBars, textLine, BarcodeTextAlignment.Left);
        }

        symbol.EndLinear(QuietZone, QuietZone, 0F, EanUpcEncoder.RunUnit, false);
    }
}
