// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Facing Identification Mark of the United States Postal Service. The input is the pattern letter, A to E. Each pattern is nine positions, a bar or a
/// space, on a pitch of two modules of 1/32 inch: A is 110010011, B is 101101101, C is 110101011, D is 111010111 and E is 101000101. The bars are 1/32 inch
/// wide and 5/8 inch high, so a pattern is 17 modules wide. The mark carries no data and prints no line.
/// </summary>
public sealed class FacingIdentificationMarkSymbology : BarcodeSymbology
{
    /// <summary>
    /// The number of positions in a pattern.
    /// </summary>
    private const int Positions = 9;

    /// <summary>
    /// The distance between positions in modules.
    /// </summary>
    private const int Pitch = 2;

    /// <summary>
    /// The nominal X dimension in millimetres: the 1/32 inch bar width.
    /// </summary>
    private const double XDimension = 25.4 / 32;

    /// <summary>
    /// The smallest X dimension in millimetres. Section 708.9.3 b of the Domestic Mail Manual: the bars must be "1/32 inch (±0.008 inch) wide".
    /// </summary>
    private const double MinimumXDimension = ((1.0 / 32) - 0.008) * 25.4;

    /// <summary>
    /// The largest X dimension in millimetres, from the same tolerance.
    /// </summary>
    private const double MaximumXDimension = ((1.0 / 32) + 0.008) * 25.4;

    /// <summary>
    /// The bar height in millimetres when the caller sets none. Section 708.9.3 b of the Domestic Mail Manual: "The FIM bars must be 5/8 inch (±1/8 inch)
    /// high". The height stays 5/8 inch at every module width, so a module that rounds to whole pixels does not change it.
    /// </summary>
    private const double NominalBarHeight = 25.4 * 5 / 8;

    /// <summary>
    /// The smallest bar height in millimetres, from the tolerance of section 708.9.3 b, "5/8 inch (±1/8 inch)".
    /// </summary>
    private const double MinimumBarHeight = 25.4 * ((5.0 / 8) - (1.0 / 8));

    /// <summary>
    /// The largest bar height in millimetres, from the same tolerance.
    /// </summary>
    private const double MaximumBarHeight = 25.4 * ((5.0 / 8) + (1.0 / 8));

    /// <summary>
    /// Gets the patterns A to E, each as nine bits, most significant first, in which a set bit is a bar.
    /// </summary>
    private static ReadOnlySpan<ushort> Patterns => [0b110010011, 0b101101101, 0b110101011, 0b111010111, 0b101000101];

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (text.Length != 1 || !char.IsBetween(text[0], 'A', 'E'))
        {
            throw new ArgumentException($"A Facing Identification Mark is one of the patterns A to E; got \"{text}\".", nameof(text));
        }

        // Every bar is one module, so the space to the next bar is the pitch less that module, plus one pitch for every empty position between them. A bar and
        // a space for every position bounds the runs, and the symbol keeps those written.
        int pattern = Patterns[text[0] - 'A'];
        double xDimension = options.ResolveXDimension(MinimumXDimension, XDimension, MaximumXDimension);
        Span<int> buffer = symbol.BeginLinear((Positions * 2) - 1, xDimension, out Span<float> heights, out _);
        int written = 0;
        int space = 0;
        for (int position = Positions - 1; position >= 0; position--)
        {
            if (((pattern >> position) & 1) == 0)
            {
                space += Pitch;
                continue;
            }

            if (written > 0)
            {
                buffer[written++] = space + Pitch - 1;
            }

            buffer[written++] = 1;
            space = 0;
        }

        symbol.TrimLastRow(written);
        float barHeight = options.ResolveBarHeight(
            xDimension,
            (float)(MinimumBarHeight / xDimension),
            (float)(NominalBarHeight / xDimension),
            (float)(MaximumBarHeight / xDimension));

        heights[..((written + 1) >> 1)].Fill(barHeight);
        symbol.EndLinear(0, 0);
    }
}
