// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Marks &amp; Spencer in-house symbology. It is an EAN-8 symbol that holds the centre guard bars at digit bar height and prints the letters M and S in the
/// quiet zones. It has no public specification.
/// <para>
/// The input is seven or eight digits, and the last digit is the EAN-8 check digit. A zero pads a seven digit number to eight. The printed interpretation
/// does not show that leading zero: the seven input digits fill the first seven character cells, and the eighth cell stays empty.
/// </para>
/// </summary>
public sealed class MandsSymbology : BarcodeSymbology
{
    /// <summary>
    /// The quiet zone in modules. Wider than the EAN-8 minimum of seven so the M and S letters, placed twelve modules before and two modules after the symbol,
    /// stay inside the reserved area.
    /// </summary>
    private const int QuietZone = 12;

    /// <summary>
    /// Initializes a new instance of the <see cref="MandsSymbology"/> class.
    /// </summary>
    public MandsSymbology()
    {
    }

    /// <summary>
    /// Gets the indexes of the extended bars: the two bars of each outer guard pattern. The centre guard bars stay at digit bar height.
    /// </summary>
    private static ReadOnlySpan<int> GuardBars => [0, 1, 20, 21];

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (text.Length is not (7 or 8))
        {
            throw new ArgumentException($"An M&S barcode must be 7 or 8 characters; got {text.Length}.", nameof(text));
        }

        // An M&S number is seven or eight characters with the check digit last, and a seven character one carries an implied leading zero, so the padded
        // form fits eight inline characters.
        bool padded = text.Length == 7;
        InlineArray8<char> paddedStorage = default;
        Span<char> paddedBuffer = paddedStorage;
        if (padded)
        {
            paddedBuffer[0] = '0';
            text.CopyTo(paddedBuffer[1..]);
        }
        else
        {
            text.CopyTo(paddedBuffer);
        }

        InlineArray8<char> digitStorage = default;
        ReadOnlySpan<char> digits = EanUpcEncoder.ValidateAndApplyCheckDigit(paddedBuffer, 7, digitStorage);

        InlineArray128<byte> moduleStorage = default;
        Span<byte> modules = ((Span<byte>)moduleStorage)[..EanUpcEncoder.Ean8Width];
        InlineArray128<sbyte> correctionStorage = default;
        Span<sbyte> corrections = ((Span<sbyte>)correctionStorage)[..EanUpcEncoder.Ean8Width];
        EanUpcEncoder.WriteEan8Modules(digits, modules, corrections);

        EanUpcEncoder.BeginRuns(modules, corrections, options.ResolveXDimension(EanUpcEncoder.NominalXDimension), ref symbol, out Span<float> heights);
        float digitHeight = options.ResolveBarHeight(symbol.XDimension, 0, EanUpcEncoder.NominalEan8BarHeight, float.PositiveInfinity);
        EanUpcEncoder.FillGuardedHeights(heights, digitHeight, GuardBars);

        if (options.Font is not null)
        {
            // A padded number hides its leading zero: its seven digits, the check digit included, fill the first seven character cells, and the eighth cell
            // stays empty. The full eight digit form prints as-is.
            float textLine = digitHeight + BarcodeTextPlacement.Clearance;
            ReadOnlySpan<char> shown = padded ? digits[1..] : digits;
            symbol.ReserveText(shown.Length + 2, shown.Length + 2);
            EanUpcEncoder.AddDigits(shown[..4], 3F, 7F, BarcodeTextSide.BelowBars, textLine, ref symbol);
            EanUpcEncoder.AddDigits(shown[4..], 36F, 7F, BarcodeTextSide.BelowBars, textLine, ref symbol);
            symbol.AddText("M", -12F, -5F, BarcodeTextSide.BelowBars, textLine);
            symbol.AddText("S", 69F, 76F, BarcodeTextSide.BelowBars, textLine);
        }

        symbol.EndLinear(QuietZone, QuietZone, 0F, EanUpcEncoder.RunUnit, false);
    }
}
