// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The one-track Pharmacode symbology, which the Laetus Pharmacode Guide defines. It carries one number from 3 to 131070 in two to sixteen bars, read from the
/// right: a thin bar in position n has the value 2 to the power n - 1 and a thick bar twice that, as the table of section 4.3 gives. Section 4.4 builds the
/// code from the value Z: while Z is not zero, an even Z takes a thick bar in the lowest position left and becomes (Z - 2) / 2, and an odd Z takes a thin bar
/// and becomes (Z - 1) / 2.
/// <para>
/// Section 1.2 gives the standard dimensions: a thin bar of 0.5 mm, a thick bar of 1.5 mm, a gap of 1.0 mm and a height of 8.0 mm, so a thin bar is 1
/// module, a thick bar 3 and a gap 2. The X dimension is the width of the thin bar, from 0.3 mm to 0.7 mm. From 0.45 mm to 0.7 mm the bars and gaps keep
/// the limits of the standard Pharmacode of section 1.2, and from 0.3 mm to 0.45 mm the limits of the miniature Pharmacode of section 1.3. The bars are
/// 8.0 mm high when the caller sets no height, and the symbol keeps 6 mm clear on either side, the gap section 2.2.4 gives between the first or last code bar
/// and other print. The guide defines no text line, so a font prints none.
/// </para>
/// </summary>
public sealed class PharmacodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// The smallest value, two thin bars. Section 4.9.2: "coding that is only one bar wide should never be used".
    /// </summary>
    public const int MinimumValue = 3;

    /// <summary>
    /// The largest value, sixteen thick bars, the last count of the table of section 4.1.
    /// </summary>
    public const int MaximumValue = 131070;

    /// <summary>
    /// The nominal X dimension in millimetres: the 0.5 mm thin bar of section 1.2.
    /// </summary>
    private const double XDimension = 0.5;

    /// <summary>
    /// The smallest X dimension in millimetres, the smallest thin bar of the miniature Pharmacode of section 1.3. A thick bar of three modules and a gap of two
    /// stay inside the limits of section 1.3 up to 0.45 mm.
    /// </summary>
    private const double MinimumXDimension = 0.3;

    /// <summary>
    /// The largest X dimension in millimetres, the largest thin bar of the standard Pharmacode of section 1.2. A thick bar of three modules and a gap of two
    /// stay inside the limits of section 1.2 from 0.45 mm.
    /// </summary>
    private const double MaximumXDimension = 0.7;

    /// <summary>
    /// The clear space in millimetres on each side. Section 2.2.4: "The gap between the first or last code bar and the extraneous print or the edges of the
    /// paper or the start or end bar "D" should be a minimum of 6 mm."
    /// </summary>
    private const double QuietZone = 6;

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
    /// The bar height in millimetres when the caller sets none, the standard code bar height of section 1.2.
    /// </summary>
    private const double NominalBarHeight = 8.0;

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        int value = PharmacodeEncoder.ParseValue(text, MinimumValue, MaximumValue);

        // Section 4.4 sets the bars from the lowest position, which is the right end of the symbol.
        InlineArray16<int> barStorage = default;
        Span<int> bars = barStorage;
        int count = 0;
        while (value != 0)
        {
            bool thick = (value & 1) == 0;
            bars[count++] = thick ? ThickBar : ThinBar;
            value = (value - (thick ? 2 : 1)) / 2;
        }

        double xDimension = options.ResolveXDimension(MinimumXDimension, XDimension, MaximumXDimension);

        Span<int> runs = symbol.BeginLinear((count * 2) - 1, xDimension, out Span<float> heights, out _);

        // The bars were found from the right end, so they are written in reverse, with a gap between each pair.
        runs.Fill(Gap);
        for (int i = 0; i < count; i++)
        {
            runs[i * 2] = bars[count - 1 - i];
        }

        float barHeight = options.ResolveBarHeight(xDimension, (float)(NominalBarHeight / xDimension));
        heights.Fill(barHeight);

        float quietZone = (float)(QuietZone / xDimension);
        symbol.EndLinear(quietZone, quietZone);
    }
}
