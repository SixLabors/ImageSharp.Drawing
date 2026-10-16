// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Flattermarken symbology. The valid characters are "0".."9", the print ratio is 1:1, the module
/// width is 2 to 3 mm and the symbol height is 5 to 10 mm. Every digit takes a window of nine modules. A
/// digit from 1 to 9 is one bar of one module at the position the digit names from the left of its
/// window, and the digit 0 leaves its window blank. The printed line shows the digits as given.
/// <para>
/// A symbol that starts or ends with a blank part of a window begins or ends with a bar of zero width,
/// so the windows keep their width. The quiet zone is "Application dependent" and is zero. The bar
/// height when the caller sets none is 5 modules, the 10 mm height at the 2 mm module.
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
    /// The nominal X dimension in millimetres: the 2 mm bar width.
    /// </summary>
    private const double XDimension = 2;

    /// <summary>
    /// The bar height in modules when the caller sets none.
    /// </summary>
    private const float NominalBarHeight = 5F;

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        BarcodeSymbology.ValidateDigits(text);

        // One bar and one space per digit at most, plus a zero width bar at either end. The symbol reserves
        // that many runs and keeps those written.
        double xDimension = options.XDimension ?? XDimension;
        Span<int> buffer = symbol.BeginLinear((text.Length * 2) + 3, xDimension, out Span<float> heights, out _);
        int written = 0;
        int space = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int digit = text[i] - '0';
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
        float barHeight = options.ResolveBarHeight(xDimension, NominalBarHeight);
        heights[..((written + 1) >> 1)].Fill(barHeight);

        if (options.Font is not null)
        {
            symbol.ReserveText(1, text.Length);
            symbol.AddText(text, 0F, text.Length * Window, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(0, 0);
    }
}
