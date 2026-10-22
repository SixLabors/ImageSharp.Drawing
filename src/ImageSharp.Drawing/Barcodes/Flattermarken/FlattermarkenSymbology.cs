// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Flattermarken symbology. The valid characters are "0".."9", the print ratio is 1:1, the module width is 2 to 3 mm and the symbol height is 5 to 10 mm.
/// Every digit takes a window of nine modules. A digit from 1 to 9 is one bar of one module at the position the digit names from the left of its window, and
/// the digit 0 leaves its window blank. The printed line shows the digits as given.
/// <para>
/// A symbol that starts or ends with a blank part of a window begins or ends with a bar of zero width, so the windows keep their width. The quiet zone is
/// "Application dependent" and is zero. When the caller sets no X dimension and no bar height, each bar is 2 mm wide and 10 mm high, the size print shops
/// most often use.
/// </para>
/// </summary>
public sealed class FlattermarkenSymbology : BarcodeSymbology
{
    /// <summary>
    /// The largest number of digits a symbol carries. The symbology sets no maximum.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The width of a digit's window in modules.
    /// </summary>
    private const int Window = 9;

    /// <summary>
    /// The nominal X dimension in millimetres: a bar 2 mm wide, the width of the usual 2 by 10 mm mark.
    /// </summary>
    private const double XDimension = 2;

    /// <summary>
    /// The smallest X dimension in millimetres, from the "Module width: 2-3 mm" of section 6.1.34 of the TEC-IT Barcode Reference.
    /// </summary>
    private const double MinimumXDimension = 2;

    /// <summary>
    /// The largest X dimension in millimetres, from the same module width.
    /// </summary>
    private const double MaximumXDimension = 3;

    /// <summary>
    /// The bar height in millimetres when the caller sets none: 10 mm, the height of the usual 2 by 10 mm mark.
    /// </summary>
    private const double NominalBarHeight = 10;

    /// <summary>
    /// The smallest bar height in millimetres, from the "Symbol height between 5 and 10mm" of section 6.1.34 of the TEC-IT Barcode Reference.
    /// </summary>
    private const double MinimumBarHeight = 5;

    /// <summary>
    /// The largest bar height in millimetres, from the same symbol height.
    /// </summary>
    private const double MaximumBarHeight = 10;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlattermarkenSymbology"/> class.
    /// </summary>
    public FlattermarkenSymbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        BarcodeSymbology.ValidateDigits(text);

        // One bar and one space per digit at most, plus a zero width bar at either end. The symbol reserves that many runs and keeps those written.
        double xDimension = options.ResolveXDimension(MinimumXDimension, XDimension, MaximumXDimension);
        Span<int> buffer = symbol.BeginLinear((text.Length * 2) + 3, xDimension, out Span<float> heights, out _);
        int written = 0;
        int space = 0;
        foreach (char c in text)
        {
            int digit = c - '0';
            if (digit == 0)
            {
                space += Window;
                continue;
            }

            space += digit - 1;
            if (written == 0 && space > 0)
            {
                buffer[written++] = 0;
            }

            if (written > 0)
            {
                buffer[written++] = space;
            }

            buffer[written++] = 1;
            space = Window - digit;
        }

        if (written == 0)
        {
            buffer[written++] = 0;
        }

        if (space > 0)
        {
            buffer[written++] = space;
            buffer[written++] = 0;
        }

        symbol.TrimLastRow(written);
        float barHeight = options.ResolveBarHeight(
            xDimension,
            (float)(MinimumBarHeight / xDimension),
            (float)(NominalBarHeight / xDimension),
            (float)(MaximumBarHeight / xDimension));

        heights[..((written + 1) >> 1)].Fill(barHeight);

        if (options.Font is not null)
        {
            symbol.ReserveText(1, text.Length);
            symbol.AddText(text, 0F, text.Length * Window, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(0, 0);
    }
}
