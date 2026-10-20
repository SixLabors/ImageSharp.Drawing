// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation and layout for the two and five digit EAN/UPC add-on symbols. Add-on bars are uniform in height and, unlike the main symbols, the GS1
/// General Specifications print the human readable interpretation above the bars, so the digits face the bar tops. The room they need belongs to the renderer,
/// which is what knows the font.
/// </summary>
internal static class AddOnLayout
{
    /// <summary>
    /// The quiet zone before an add-on symbol in modules. Table 5-44 of the GS1 General Specifications separates an add-on from its main symbol by 7X to 12X
    /// for EAN-13 and UPC-E and by 9X to 12X for UPC-A. The add-on symbol does not know its main symbol, so it takes the smallest separation.
    /// </summary>
    private const int LeadingQuietZone = 7;

    /// <summary>
    /// The quiet zone after an add-on symbol in modules. Table 5-11 of the GS1 General Specifications gives the right quiet zone of an add-on as 5 modules.
    /// </summary>
    private const int TrailingQuietZone = 5;

    /// <summary>
    /// The add-on guard bar pattern of Table 5-4 of the GS1 General Specifications: 4 modules, a bar, a space, then a bar two modules wide.
    /// </summary>
    private const int AddOnGuard = 0b1011;

    /// <summary>
    /// The width of <see cref="AddOnGuard"/> in modules.
    /// </summary>
    private const int AddOnGuardWidth = 4;

    /// <summary>
    /// The add-on delineator of Table 5-4 of the GS1 General Specifications between two add-on characters: a space, then a bar.
    /// </summary>
    private const int Delineator = 0b01;

    /// <summary>
    /// The width of <see cref="Delineator"/> in modules.
    /// </summary>
    private const int DelineatorWidth = 2;

    /// <summary>
    /// Encodes the digits of an add-on and builds the symbol: the guard pattern, then one symbol character per digit with a delineator between each pair.
    /// </summary>
    /// <param name="digits">The two or five digits, already validated.</param>
    /// <param name="parity">The number set sequence, the first digit in the highest of one bit per digit. A set bit selects number set B.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void Build(ReadOnlySpan<char> digits, int parity, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        // Five digits take 4 + (5 * 7) + (4 * 2) = 47 modules, so the stream fits the inline buffer.
        InlineArray64<byte> moduleStorage = default;
        Span<byte> modules = moduleStorage;
        InlineArray64<sbyte> correctionStorage = default;
        Span<sbyte> corrections = correctionStorage;
        int position = 0;
        EanUpcEncoder.AppendPattern(modules, ref position, AddOnGuard, AddOnGuardWidth);
        for (int i = 0; i < digits.Length; i++)
        {
            if (i > 0)
            {
                EanUpcEncoder.AppendPattern(modules, ref position, Delineator, DelineatorWidth);
            }

            EanUpcNumberSet numberSet = ((parity >> (digits.Length - 1 - i)) & 1) == 0 ? EanUpcNumberSet.A : EanUpcNumberSet.B;
            EanUpcEncoder.AppendCharacter(modules, corrections, ref position, numberSet, digits[i]);
        }

        double xDimension = options.ResolveXDimension(EanUpcEncoder.MinimumXDimension, EanUpcEncoder.NominalXDimension, EanUpcEncoder.MaximumXDimension);
        EanUpcEncoder.BeginRuns(modules[..position], corrections[..position], xDimension, ref symbol, out Span<float> heights);

        // Section 5.2.3.2 of the GS1 General Specifications: the add-on "must not extend outside the symbol height dimensions of the main symbol". The main
        // symbol resolves the same bar height with a minimum and no maximum, so an add-on without limits is never taller than it.
        float barHeight = options.ResolveBarHeight(symbol.XDimension, 0, EanUpcEncoder.NominalAddOnBarHeight, float.PositiveInfinity);
        heights.Fill(barHeight);

        // Section 5.2.5 of the GS1 General Specifications prints the add-on interpretation above the bars. Each digit prints above its own symbol character;
        // the character cells start after the guard pattern and advance by the seven character modules plus the two delineator modules.
        if (options.Font is not null)
        {
            symbol.ReserveText(digits.Length, digits.Length);
            EanUpcEncoder.AddDigits(digits, 4F, 9F, BarcodeTextSide.AboveBars, -BarcodeTextPlacement.Clearance, ref symbol);
        }

        symbol.EndLinear(LeadingQuietZone, TrailingQuietZone, 0F, EanUpcEncoder.RunUnit, false);
    }
}
