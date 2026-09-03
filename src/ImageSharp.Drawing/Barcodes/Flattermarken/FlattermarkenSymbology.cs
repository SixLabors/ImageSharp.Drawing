// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

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
    private const float XDimension = 2F;

    /// <summary>
    /// The bar height in modules when the caller sets none.
    /// </summary>
    private const float NominalBarHeight = 5F;

    /// <inheritdoc/>
    public override float NominalXDimension => XDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException($"Flattermarken carries only digits; got {current.ToDisplayString()}.", nameof(text));
            }
        }

        // One bar and one space per digit at most, plus a zero width bar at either end.
        int[] buffer = new int[(text.Length * 2) + 3];
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

        int[] runs = buffer[..written];
        float barHeight = EanUpcEncoder.ResolveBarHeight(options, XDimension, NominalBarHeight);
        int barCount = (runs.Length + 1) / 2;
        float[] heights = new float[barCount];
        float[] tops = new float[barCount];
        heights.AsSpan().Fill(barHeight);

        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null)
        {
            placements = [new BarcodeTextPlacement(text, 0F, text.Length * Window, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance)];
        }

        return new LinearBarcodeSymbol(runs, heights, tops, placements, 0, 0);
    }
}
