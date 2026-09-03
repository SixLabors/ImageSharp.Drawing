// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A custom linear symbology whose input is the symbol itself: the digits 1 to 9 give the widths in
/// modules of the bars and spaces in turn, starting with a bar. An even number of digits ends on a
/// space, which the symbol keeps behind a bar of zero width. The symbol has no quiet zones and prints no
/// line.
/// </summary>
public sealed class RawLinearSymbology : BarcodeSymbology
{
    /// <summary>
    /// The largest number of widths a symbol carries.
    /// </summary>
    public const int MaximumLength = 2500;

    /// <summary>
    /// The bar height as a fraction of the symbol width when the caller sets no height: the 15 per cent
    /// of Code 39 and Codabar.
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        bool endsOnSpace = (text.Length & 1) == 0;
        int[] runs = new int[text.Length + (endsOnSpace ? 1 : 0)];
        int widthInModules = 0;
        int index = 0;
        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (current.Value is < '1' or > '9')
            {
                throw new ArgumentException($"A raw symbol carries only the digits 1 to 9; got {current.ToDisplayString()}.", nameof(text));
            }

            runs[index++] = current.Value - '0';
            widthInModules += current.Value - '0';
        }

        float barHeight = EanUpcEncoder.ResolveBarHeight(options, PointXDimension, widthInModules * NominalBarHeightFraction);
        int barCount = (runs.Length + 1) / 2;
        float[] heights = new float[barCount];
        float[] tops = new float[barCount];
        heights.AsSpan().Fill(barHeight);
        return new LinearBarcodeSymbol(runs, heights, tops, [], 0, 0);
    }
}
