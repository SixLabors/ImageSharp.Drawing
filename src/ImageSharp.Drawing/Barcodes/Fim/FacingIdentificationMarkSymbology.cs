// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Facing Identification Mark of the United States Postal Service. The input is the pattern letter,
/// A to E.
/// Each pattern is nine positions, a bar or a space, on a pitch of two modules of 1/32 inch: A is
/// 110010011, B is 101101101, C is 110101011, D is 111010111 and E is 101000101. The bars are 1/32 inch
/// wide and 5/8 inch high, so a pattern is 17 modules wide and 20 modules high. The mark carries no
/// data and prints no line.
/// </summary>
public sealed class FacingIdentificationMarkSymbology : BarcodeSymbology
{
    /// <summary>
    /// The number of positions in a pattern.
    /// </summary>
    private const int Positions = 9;

    /// <summary>
    /// The nominal X dimension in millimetres: the 1/32 inch bar width.
    /// </summary>
    private const double XDimension = 25.4 / 32;

    /// <summary>
    /// The bar height in modules when the caller sets none: 5/8 inch at the 1/32 inch module.
    /// </summary>
    private const float NominalBarHeight = 20F;

    /// <summary>
    /// Gets the patterns A to E, each as nine bits, most significant first, in which a set bit is a bar.
    /// </summary>
    private static ReadOnlySpan<ushort> Patterns => [0b110010011, 0b101101101, 0b110101011, 0b111010111, 0b101000101];

    /// <inheritdoc/>
    public override double NominalXDimension => XDimension;

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));
        if (text.Length != 1 || text[0] < 'A' || text[0] > 'E')
        {
            throw new ArgumentException($"A Facing Identification Mark is one of the patterns A to E; got \"{text}\".", nameof(text));
        }

        // Every bar is one module, and the space to the next bar is one module for every empty
        // position between them plus one for the pitch. A bar and a space for every position bounds the
        // runs, and the symbol keeps those written.
        int pattern = Patterns[text[0] - 'A'];
        Span<int> buffer = symbol.BeginLinear((Positions * 2) - 1, out Span<float> heights, out _);
        int written = 0;
        int space = 0;
        for (int position = Positions - 1; position >= 0; position--)
        {
            if (((pattern >> position) & 1) == 0)
            {
                space += 2;
                continue;
            }

            if (written > 0)
            {
                buffer[written++] = space + 1;
            }

            buffer[written++] = 1;
            space = 0;
        }

        symbol.TrimLastRow(written);
        float barHeight = options.ResolveBarHeight(XDimension, NominalBarHeight);
        heights[..((written + 1) >> 1)].Fill(barHeight);
        symbol.EndLinear(0, 0);
    }
}
