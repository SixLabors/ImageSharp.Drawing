// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Builds the symbol of a height modulated postal symbology from its bar states. The bars are one width and equally spaced, and the four states differ in
/// height and vertical position alone.
/// </summary>
internal static class FourStateEncoder
{
    /// <summary>
    /// The dimensions the Royal Mail 4-state symbologies share, from Table 11 of the Royal Mail Mailmark barcode definition document: a bar width of 0.54 mm,
    /// ascender and descender bars of 1.90 mm, a tracker bar of 1.30 mm, a full bar of 5.10 mm and a pitch of 21.2 bars per inch, with the clear zone of
    /// section 3.5.2, "at least 2mm around all edges". A symbology that prints a text line keeps the same 2 mm between the bars and the text, which the
    /// document does not state. At the 0.54 mm module a run unit of 0.01 mm is 1/54 module, a bar is 54 units and a pitch of 1.20 mm, which is 21.17 bars per
    /// inch, leaves a space of 66 units.
    /// </summary>
    public static readonly FourStateMetrics RoyalMail = new(
        0.54,
        1F / 54F,
        54,
        66,
        (float)(1.90 / 0.54),
        (float)(1.30 / 0.54),
        (float)(1.90 / 0.54),
        (float)(2 / 0.54),
        (float)(2 / 0.54),
        BarcodeTextSide.BelowBars,
        (float)(2 / 0.54),
        BarcodeTextAlignment.Centered);

    /// <summary>
    /// Builds the symbol from bar states.
    /// </summary>
    /// <param name="states">The state of every bar.</param>
    /// <param name="metrics">The dimensions of the symbology.</param>
    /// <param name="readable">The human readable interpretation, or none.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(
        ReadOnlySpan<FourState> states,
        FourStateMetrics metrics,
        ReadOnlySpan<char> readable,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        int barCount = states.Length;
        double xDimension = options.ResolveXDimension(metrics.MinimumXDimension, metrics.XDimension, metrics.MaximumXDimension);

        // The emitter draws every run of these uniform bars at a whole number of pixels, so the runs count pixels: a bar is one module of whole pixels, and
        // the space is the space of the metrics at the nearest whole pixel, at least one, as the emitter rounds a run.
        int barPixels = options.ModulePixels(xDimension);
        double spacePixels = Math.Max(1, Math.Round(metrics.SpaceUnitsAt(xDimension) * (double)metrics.RunUnit * barPixels, MidpointRounding.AwayFromZero));

        // A specification that limits the bars per inch limits the pitch, a bar and a space: a pitch of Dpi / n pixels is n bars per inch. Whole pixels can
        // leave that range, at a bar width at the end of its range or at a low resolution, so the pitch is clamped in whole pixels as ResolveXDimension clamps
        // the module: the largest whole number of pixels at or under the largest pitch, and the smallest at or over the smallest pitch with a space of at
        // least one pixel. The smallest pitch applies last.
        double highest = Math.Floor((options.Dpi / metrics.MinimumBarsPerInch) + BarcodeOptions.LimitTolerance);
        double lowest = Math.Max(barPixels + 1, Math.Ceiling((options.Dpi / metrics.MaximumBarsPerInch) - BarcodeOptions.LimitTolerance));
        int space = (int)Math.Max(Math.Min(barPixels + spacePixels, highest), lowest) - barPixels;
        float runUnit = 1F / barPixels;

        Span<int> runs = symbol.BeginLinear((barCount * 2) - 1, xDimension, out Span<float> heights, out Span<float> tops);
        for (int i = 0; i < runs.Length; i++)
        {
            runs[i] = (i & 1) == 0 ? barPixels : space;
        }

        // The clear zones and the text clearance are millimetres or inches in each specification, given in modules of the nominal bar width. The module the
        // symbol draws at can round away from the nominal width, so the same lengths are this many of its modules. The bar heights are too, unless the
        // specification makes them proportional to the bar width.
        float nominal = (float)(metrics.XDimension / xDimension);
        float barScale = metrics.ProportionalBars ? 1F : nominal;
        float scale = options.ResolveBarHeight(
            xDimension,
            metrics.MinimumFullBar * barScale,
            metrics.FullBar * barScale,
            metrics.MaximumFullBar * barScale) / metrics.FullBar;

        float ascender = metrics.Ascender * scale;
        float tracker = metrics.Tracker * scale;
        float descender = metrics.Descender * scale;
        for (int i = 0; i < barCount; i++)
        {
            FourState state = states[i];
            bool up = (state & FourState.Ascender) != 0;
            bool down = (state & FourState.Descender) != 0;
            tops[i] = up ? 0F : ascender;
            heights[i] = tracker + (up ? ascender : 0F) + (down ? descender : 0F);
        }

        float widthInModules = ((barCount * barPixels) + ((barCount - 1) * space)) * runUnit;
        if (options.Font is not null && readable.Length > 0)
        {
            float textClearance = metrics.TextClearance * nominal;
            float textEdge = metrics.TextSide == BarcodeTextSide.AboveBars
                ? -textClearance
                : ascender + tracker + descender + textClearance;

            symbol.ReserveText(1, readable.Length);
            symbol.AddText(readable, 0F, widthInModules, metrics.TextSide, textEdge, metrics.TextAlignment);
        }

        float quietZone = metrics.QuietZone * nominal;
        float verticalQuietZone = metrics.VerticalQuietZone * nominal;
        symbol.EndLinear(quietZone, quietZone, verticalQuietZone, verticalQuietZone, 0F, runUnit, true);
    }
}
