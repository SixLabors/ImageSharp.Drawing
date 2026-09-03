// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The one-track Pharmacode symbology, which the Laetus Pharmacode Guide defines. It carries one number
/// from 3 to 131070 in two to sixteen bars, read from the right: a thin bar in position n has the value
/// 2 to the power n - 1 and a thick bar twice that, as the table of section 4.3 gives. Section 4.4 builds
/// the code from the value Z: while Z is not zero, an even Z takes a thick bar in the lowest position
/// left and becomes (Z - 2) / 2, and an odd Z takes a thin bar and becomes (Z - 1) / 2.
/// <para>
/// Section 1.2 gives the standard dimensions: a thin bar of 0.5 mm, a thick bar of 1.5 mm, a gap of
/// 1.0 mm and a height of 8.0 mm, so at the 0.5 mm module a thin bar is 1 module, a thick bar 3, a gap
/// 2 and the height 16. The quiet zone is 6 mm on either side of the symbol, which is 12 modules. The
/// printed line shows the number as given.
/// </para>
/// </summary>
public sealed class PharmacodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// The smallest value, two thin bars.
    /// </summary>
    public const int MinimumValue = 3;

    /// <summary>
    /// The largest value, sixteen thick bars.
    /// </summary>
    public const int MaximumValue = 131070;

    /// <summary>
    /// The nominal X dimension in millimetres: the 0.5 mm thin bar of section 1.2.
    /// </summary>
    private const float XDimension = 0.5F;

    /// <summary>
    /// The quiet zone in modules on each side: 6 mm at the 0.5 mm module.
    /// </summary>
    private const int QuietZone = 12;

    /// <summary>
    /// The largest number of bars a symbol carries.
    /// </summary>
    private const int MaximumBars = 16;

    /// <summary>
    /// The width of a thin bar in modules, 0.5 mm.
    /// </summary>
    private const int ThinBar = 1;

    /// <summary>
    /// The width of a thick bar in modules, 1.5 mm.
    /// </summary>
    private const int ThickBar = 3;

    /// <summary>
    /// The width of the gap between bars in modules, 1.0 mm.
    /// </summary>
    private const int Gap = 2;

    /// <summary>
    /// The bar height in modules when the caller sets none: 8.0 mm at the 0.5 mm module.
    /// </summary>
    private const float NominalBarHeight = 16F;

    /// <inheritdoc/>
    public override float NominalXDimension => XDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        int value = PharmacodeEncoder.ParseValue(text, MinimumValue, MaximumValue);

        // Section 4.4 sets the bars from the lowest position, which is the right end of the symbol.
        Span<int> bars = stackalloc int[MaximumBars];
        int count = 0;
        while (value != 0)
        {
            bool thick = (value & 1) == 0;
            bars[count++] = thick ? ThickBar : ThinBar;
            value = (value - (thick ? 2 : 1)) / 2;
        }

        int[] runs = new int[(count * 2) - 1];
        int widthInModules = 0;
        for (int i = 0; i < count; i++)
        {
            runs[i * 2] = bars[count - 1 - i];
            widthInModules += runs[i * 2];
            if (i < count - 1)
            {
                runs[(i * 2) + 1] = Gap;
                widthInModules += Gap;
            }
        }

        float barHeight = EanUpcEncoder.ResolveBarHeight(options, XDimension, NominalBarHeight);
        float[] heights = new float[count];
        float[] tops = new float[count];
        heights.AsSpan().Fill(barHeight);

        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null)
        {
            placements = [new BarcodeTextPlacement(text, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance)];
        }

        return new LinearBarcodeSymbol(runs, heights, tops, placements, QuietZone, QuietZone);
    }
}
