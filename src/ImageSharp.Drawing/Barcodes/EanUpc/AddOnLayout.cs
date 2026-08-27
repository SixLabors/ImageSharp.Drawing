// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared layout for the two and five digit EAN/UPC add-on symbols. Add-on bars are uniform in height and,
/// unlike the main symbols, the GS1 General Specifications print the human readable interpretation above the
/// bars, so a text band is carved from the top of the symbol when text is enabled.
/// </summary>
internal static class AddOnLayout
{
    /// <summary>
    /// The quiet zone before an add-on symbol in modules: the GS1 General Specifications separate the add-on
    /// from the main symbol by at least seven modules, plus the leading space module folded out of the add-on
    /// guard pattern.
    /// </summary>
    private const int LeadingQuietZone = 8;

    /// <summary>
    /// The quiet zone after an add-on symbol in modules, per the GS1 General Specifications.
    /// </summary>
    private const int TrailingQuietZone = 5;

    /// <summary>
    /// Builds the symbol for an encoded add-on module stream.
    /// </summary>
    /// <param name="modules">The module stream; 1 is a dark module.</param>
    /// <param name="text">The digits of the human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The add-on symbol.</returns>
    public static LinearBarcodeSymbol Build(ReadOnlySpan<byte> modules, string text, BarcodeOptions options)
    {
        int[] runs = EanUpcEncoder.ToRuns(modules);
        int barCount = (runs.Length + 1) / 2;

        float barHeight = EanUpcEncoder.ResolveBarHeight(options, EanUpcEncoder.NominalAddOnBarHeight);
        float band = options.Font is null ? 0 : MathF.Min(EanUpcEncoder.AddOnTextBand, barHeight - 1);

        float[] heights = new float[barCount];
        float[] tops = new float[barCount];
        Array.Fill(heights, barHeight - band);
        if (band > 0)
        {
            Array.Fill(tops, band);
        }

        // Each digit prints above its own symbol character; the character cells start after the guard
        // pattern and advance by the seven character modules plus the two delineator modules.
        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null)
        {
            placements = new BarcodeTextPlacement[text.Length];
            EanUpcEncoder.FillDigitPlacements(placements, 0, text, 0, text.Length, 4F, 9F, 0F);
        }

        return new LinearBarcodeSymbol(runs, heights, tops, placements, LeadingQuietZone, TrailingQuietZone);
    }
}
