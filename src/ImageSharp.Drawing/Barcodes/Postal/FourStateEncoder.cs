// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Builds the symbol of a height modulated postal symbology from its bar states. The bars are one width
/// and equally spaced, and the four states differ in height and vertical position alone.
/// </summary>
internal static class FourStateEncoder
{
    /// <summary>
    /// The dimensions the Royal Mail 4-state symbologies share, from Table 11 of the Royal Mail Mailmark
    /// barcode definition document: a bar width of 0.54 mm, ascender and descender bars of 1.90 mm, a
    /// tracker bar of 1.30 mm, a full bar of 5.10 mm and a pitch of 21.2 bars per inch, with the clear
    /// zone of section 3.5.2, "at least 2mm around all edges", which also stands between the bars and
    /// the human readable interpretation. At the 0.54 mm module a run unit of 0.01 mm is 1/54 module, a
    /// bar is 54 units and a pitch of 1.20 mm, which is 21.17 bars per inch, leaves a space of 66 units.
    /// </summary>
    public static readonly FourStateMetrics RoyalMail = new(0.54F, 1F / 54F, 54, 66, 1.90F / 0.54F, 1.30F / 0.54F, 1.90F / 0.54F, 2F / 0.54F, BarcodeTextSide.BelowBars, 2F / 0.54F);

    /// <summary>
    /// Builds the symbol from bar states.
    /// </summary>
    /// <param name="states">The state of every bar.</param>
    /// <param name="metrics">The dimensions of the symbology.</param>
    /// <param name="text">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    public static LinearBarcodeSymbol BuildSymbol(ReadOnlySpan<FourState> states, FourStateMetrics metrics, string text, BarcodeOptions options)
    {
        int barCount = states.Length;
        int[] runs = new int[(barCount * 2) - 1];
        for (int i = 0; i < runs.Length; i++)
        {
            runs[i] = (i & 1) == 0 ? metrics.BarUnits : metrics.SpaceUnits;
        }

        float scale = EanUpcEncoder.ResolveBarHeight(options, metrics.XDimension, metrics.FullBar) / metrics.FullBar;
        float ascender = metrics.Ascender * scale;
        float tracker = metrics.Tracker * scale;
        float descender = metrics.Descender * scale;
        float[] heights = new float[barCount];
        float[] tops = new float[barCount];
        for (int i = 0; i < barCount; i++)
        {
            FourState state = states[i];
            bool up = state is FourState.Ascender or FourState.Full;
            bool down = state is FourState.Descender or FourState.Full;
            tops[i] = up ? 0F : ascender;
            heights[i] = tracker + (up ? ascender : 0F) + (down ? descender : 0F);
        }

        float widthInModules = ((barCount * metrics.BarUnits) + ((barCount - 1) * metrics.SpaceUnits)) * metrics.RunUnit;
        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null && text.Length > 0)
        {
            float textEdge = metrics.TextSide == BarcodeTextSide.AboveBars
                ? -metrics.TextClearance
                : ascender + tracker + descender + metrics.TextClearance;
            placements = [new BarcodeTextPlacement(text, 0F, widthInModules, metrics.TextSide, textEdge, metrics.TextAlignment)];
        }

        return new LinearBarcodeSymbol(runs, heights, tops, placements, metrics.QuietZone, metrics.QuietZone, 0F, metrics.RunUnit, true);
    }
}
