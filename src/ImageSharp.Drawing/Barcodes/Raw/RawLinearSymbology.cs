// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A custom linear symbology whose input is the symbol itself: the digits 1 to 9 give the widths in modules of the bars and spaces in turn, starting with a
/// bar. An even number of digits ends on a space, which the symbol keeps behind a bar of zero width. The symbol has no quiet zones and prints no line.
/// </summary>
public sealed class RawLinearSymbology : BarcodeSymbology
{
    /// <summary>
    /// The largest number of widths a symbol carries.
    /// </summary>
    public const int MaximumLength = 2500;

    /// <summary>
    /// The bar height as a fraction of the symbol width when the caller sets no height: 15 per cent.
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        // An even count of widths ends on a space, so a zero width bar closes the runs. The storage starts zero, so that bar needs no write.
        bool endsOnSpace = (text.Length & 1) == 0;
        double xDimension = options.XDimension ?? PointXDimension;
        Span<int> runs = symbol.BeginLinear(text.Length + (endsOnSpace ? 1 : 0), xDimension, out Span<float> heights, out _);
        int widthInModules = 0;
        int index = 0;
        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (current.Value is < '1' or > '9')
            {
                throw new ArgumentException($"A raw symbol carries only the digits 1 to 9; got {current.ToDisplayString()}.", nameof(text));
            }

            int width = current.Value - '0';
            runs[index++] = width;
            widthInModules += width;
        }

        float barHeight = options.ResolveBarHeight(xDimension, widthInModules * NominalBarHeightFraction);
        heights.Fill(barHeight);
        symbol.EndLinear(0, 0);
    }
}
