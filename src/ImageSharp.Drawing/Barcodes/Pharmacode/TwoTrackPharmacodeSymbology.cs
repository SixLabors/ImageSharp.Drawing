// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The two-track Pharmacode symbology, which the Laetus Pharmacode Guide defines. It carries one number
/// from 4 to 64570080 in two to sixteen bars of one width, read from the right, each of which is a lower
/// half bar, an upper half bar or a full bar. The table of section 4.5 gives a bar in position n the
/// value 3 to the power n - 1 for a lower bar, twice that for an upper bar and three times that for a
/// full bar. The code is built as in section 4.4: while the value is not zero, its remainder modulo 3
/// picks the bar in the lowest position left, 1 a lower bar, 2 an upper bar and 0 a full bar, and the
/// value less the bar's weight is divided by 3.
/// <para>
/// Section 1.4 gives the standard dimensions: bars and gaps of 1 mm, the two half bars of 4 mm and the
/// full bar of 8 mm, so at the 1 mm module bars and gaps are 1 module and the heights 4 and 8 modules,
/// or half and the whole of the height the caller sets. The quiet zone of 6 mm is 6 modules. The printed
/// line shows the number as given.
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
    private const float XDimension = 1F;

    /// <summary>
    /// The quiet zone in modules on each side: 6 mm at the 1 mm module.
    /// </summary>
    private const int QuietZone = 6;

    /// <summary>
    /// The largest number of bars a symbol carries.
    /// </summary>
    private const int MaximumBars = 16;

    /// <summary>
    /// The height of the full bar in modules when the caller sets none: 8 mm at the 1 mm module.
    /// </summary>
    private const float NominalBarHeight = 8F;

    /// <inheritdoc/>
    public override float NominalXDimension => XDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        int value = PharmacodeEncoder.ParseValue(text, MinimumValue, MaximumValue);

        // The weight of each bar from the lowest position: 1 for a lower bar, 2 for an upper bar and 3
        // for a full bar.
        Span<int> weights = stackalloc int[MaximumBars];
        int count = 0;
        while (value != 0)
        {
            int remainder = value % 3;
            int weight = remainder == 0 ? 3 : remainder;
            weights[count++] = weight;
            value = (value - weight) / 3;
        }

        float barHeight = EanUpcEncoder.ResolveBarHeight(options, XDimension, NominalBarHeight);
        float halfHeight = barHeight * 0.5F;
        int[] runs = new int[(count * 2) - 1];
        runs.AsSpan().Fill(1);
        float[] heights = new float[count];
        float[] tops = new float[count];
        for (int i = 0; i < count; i++)
        {
            int weight = weights[count - 1 - i];
            heights[i] = weight == 3 ? barHeight : halfHeight;
            tops[i] = weight == 1 ? halfHeight : 0F;
        }

        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null)
        {
            placements = [new BarcodeTextPlacement(text, 0F, runs.Length, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance)];
        }

        return new LinearBarcodeSymbol(runs, heights, tops, placements, QuietZone, QuietZone);
    }
}
