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
/// Section 1.4 gives the standard dimensions: bars and gaps of 1 mm, the two half bars of 4 mm and the full bar of 8 mm for a 4 mm two-track sensor head. Bars
/// and gaps are 1 module, the X dimension, which section 1.4 allows from 0.8 mm to 2 mm. The full bar is 8 mm high when the caller sets no height, and the
/// half bars are half the full bar. The symbol keeps 6 mm clear on either side, the gap section 2.2.4 gives between the first or last code bar and other
/// print. The guide defines no text line, so a font prints none.
/// </para>
/// </summary>
public sealed class TwoTrackPharmacodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// The smallest value, two lower bars. Section 4.9.2: "coding that is only one bar wide should never be used".
    /// </summary>
    public const int MinimumValue = 4;

    /// <summary>
    /// The largest value, sixteen full bars, the last count of the table of section 4.1.
    /// </summary>
    public const int MaximumValue = 64570080;

    /// <summary>
    /// The nominal X dimension in millimetres: the 1 mm bar of section 1.4.
    /// </summary>
    private const double XDimension = 1;

    /// <summary>
    /// The smallest X dimension in millimetres, the smallest bar width of section 1.4.
    /// </summary>
    private const double MinimumXDimension = 0.8;

    /// <summary>
    /// The largest X dimension in millimetres, the largest bar width of section 1.4.
    /// </summary>
    private const double MaximumXDimension = 2;

    /// <summary>
    /// The clear space in millimetres on each side. Section 2.2.4: "The gap between the first or last code bar and the extraneous print or the edges of the
    /// paper or the start or end bar "D" should be a minimum of 6 mm."
    /// </summary>
    private const double QuietZone = 6;

    /// <summary>
    /// The height of the full bar in millimetres when the caller sets none, the long code bar of section 1.4 for a 4 mm two-track sensor head.
    /// </summary>
    private const double NominalBarHeight = 8;

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

        double xDimension = options.ResolveXDimension(MinimumXDimension, XDimension, MaximumXDimension);

        float barHeight = options.ResolveBarHeight(xDimension, 0, (float)(NominalBarHeight / xDimension), float.PositiveInfinity);
        float halfHeight = barHeight * 0.5F;
        Span<int> runs = symbol.BeginLinear((count * 2) - 1, xDimension, out Span<float> heights, out Span<float> tops);
        runs.Fill(1);
        for (int i = 0; i < count; i++)
        {
            TrackBar bar = bars[count - 1 - i];
            heights[i] = bar == TrackBar.Full ? barHeight : halfHeight;
            tops[i] = bar == TrackBar.Lower ? halfHeight : 0F;
        }

        float quietZone = (float)(QuietZone / xDimension);
        symbol.EndLinear(quietZone, quietZone);
    }
}
