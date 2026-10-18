// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The two-track Pharmacode symbology, which the Laetus Pharmacode Guide defines. It carries one number from 4 to 64570080 in two to sixteen bars of one width,
/// read from the right, each of which is a lower half bar, an upper half bar or a full bar. The table of section 4.5 gives a bar in position n the value 3 to
/// the power n - 1 for a lower bar, twice that for an upper bar and three times that for a full bar. The code is built as in section 4.4: while the value is
/// not zero, its remainder modulo 3 picks the bar in the lowest position left, 1 a lower bar, 2 an upper bar and 0 a full bar, and the value less the bar's
/// weight is divided by 3.
/// <para>
/// Section 1.4 gives the standard dimensions: bars and gaps of 1 mm, the two half bars of 4 mm and the full bar of 8 mm, so at the 1 mm module bars and gaps
/// are 1 module and the heights 4 and 8 modules, or half and the whole of the height the caller sets. The quiet zone of 6 mm is 6 modules. The printed line
/// shows the number as given.
/// </para>
/// </summary>
public sealed class TwoTrackPharmacodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// The smallest value, two lower bars.
    /// </summary>
    public const int MinimumValue = 4;

    /// <summary>
    /// The largest value, sixteen full bars.
    /// </summary>
    public const int MaximumValue = 64570080;

    /// <summary>
    /// The nominal X dimension in millimetres: the 1 mm bar of section 1.4.
    /// </summary>
    private const double XDimension = 1;

    /// <summary>
    /// The quiet zone in modules on each side: 6 mm at the 1 mm module.
    /// </summary>
    private const int QuietZone = 6;

    /// <summary>
    /// The height of the full bar in modules when the caller sets none: 8 mm at the 1 mm module.
    /// </summary>
    private const float NominalBarHeight = 8F;

    /// <summary>
    /// The radix of the bar weights: a bar in position n weighs 3 to the power n - 1 times its kind.
    /// </summary>
    private const int Radix = 3;

    /// <summary>
    /// The kinds of bar, each valued at the weight the table of section 4.5 gives it in the lowest position.
    /// </summary>
    private enum TrackBar
    {
        /// <summary>
        /// The lower half bar, weight 1.
        /// </summary>
        Lower = 1,

        /// <summary>
        /// The upper half bar, weight 2.
        /// </summary>
        Upper = 2,

        /// <summary>
        /// The full bar, weight 3.
        /// </summary>
        Full = 3,
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        int value = PharmacodeEncoder.ParseValue(text, MinimumValue, MaximumValue);

        // The bars from the lowest position. A remainder of 0 picks the full bar, whose weight is the radix itself.
        InlineArray16<TrackBar> barStorage = default;
        Span<TrackBar> bars = barStorage;
        int count = 0;
        while (value != 0)
        {
            int remainder = value % Radix;
            TrackBar bar = remainder == 0 ? TrackBar.Full : (TrackBar)remainder;
            bars[count++] = bar;
            value = (value - (int)bar) / Radix;
        }

        double xDimension = options.XDimension ?? XDimension;
        float barHeight = options.ResolveBarHeight(xDimension, NominalBarHeight);
        float halfHeight = barHeight * 0.5F;
        Span<int> runs = symbol.BeginLinear((count * 2) - 1, xDimension, out Span<float> heights, out Span<float> tops);
        runs.Fill(1);
        for (int i = 0; i < count; i++)
        {
            TrackBar bar = bars[count - 1 - i];
            heights[i] = bar == TrackBar.Full ? barHeight : halfHeight;
            tops[i] = bar == TrackBar.Lower ? halfHeight : 0F;
        }

        if (options.Font is not null)
        {
            symbol.ReserveText(1, text.Length);
            symbol.AddText(text, 0F, runs.Length, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(QuietZone, QuietZone);
    }
}
