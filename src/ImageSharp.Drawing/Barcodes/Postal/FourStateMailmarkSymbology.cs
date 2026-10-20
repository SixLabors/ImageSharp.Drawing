// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Royal Mail 4-state Mailmark barcode, which the Royal Mail Mailmark barcode C and barcode L encoding and decoding instructions define. The text is the
/// application string: a 22-character string gives barcode C of 66 bars and a 26-character string barcode L of 78 bars. The fields are the format, the version
/// ID, the class, the supply chain ID of 2 or 6 digits, the item ID of 8 digits and the destination post code plus delivery point suffix of 9 characters. The
/// bars are the 0.54 mm bars of Table 11 of the Mailmark barcode definition document, and an X dimension outside the acceptable 0.38 to 0.63 mm of that
/// table is clamped to it. The symbol prints no text line, because the Mailmark documents define none.
/// </summary>
public sealed class FourStateMailmarkSymbology : BarcodeSymbology
{
    /// <summary>
    /// The dimensions of Table 11, with the bar width clamped to the acceptable sizes of the same table: "Bar width (all) 0.54 0.38 - 0.63".
    /// </summary>
    private static readonly FourStateMetrics Metrics = FourStateEncoder.RoyalMail with { MinimumXDimension = 0.38, MaximumXDimension = 0.63 };

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        int barCount = FourStateMailmarkEncoder.BarCount(text.Length);
        if (barCount == 0)
        {
            throw new ArgumentException($"A Mailmark application string is 22 characters for barcode C or 26 for barcode L; got {text.Length}.", nameof(text));
        }

        // Barcode L, the larger, has 78 bars.
        InlineArray128<FourState> stateStorage = default;
        Span<FourState> states = ((Span<FourState>)stateStorage)[..barCount];
        FourStateMailmarkEncoder.Encode(text, states);
        FourStateEncoder.BuildSymbol(states, Metrics, [], options, ref symbol);
    }
}
