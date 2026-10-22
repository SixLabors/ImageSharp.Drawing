// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Royal Mail 4-State Customer Code, RM4SCC. It carries digits and capital letters, each as four bars of which two have an ascender and two a descender,
/// between a start bar with an ascender and a stop bar that is full, and it always carries the check character of the checksum calculation table after the
/// data. The dimensions are those of Table 11 of the Royal Mail Mailmark barcode definition document, which the Royal Mail symbologies share, with the 2 mm
/// clear zone of its section 3.5.2. The printed line shows the data as given.
/// </summary>
public sealed class RoyalMailSymbology : BarcodeSymbology
{
    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, RoyalMailEncoder.MaximumLength, nameof(text));
        RoyalMailEncoder.Validate(text);

        // Four bars for each character and the check character, and the start and stop bars. The text is at most MaximumLength characters, so a short symbol
        // works inline and a longer one rents its states once.
        int barCount = ((text.Length + 1) * RoyalMailEncoder.BarsPerCharacter) + 2;
        InlineArray128<FourState> stateStorage = default;
        PooledBuffer<FourState> rented = default;
        Span<FourState> states = barCount <= 128
            ? ((Span<FourState>)stateStorage)[..barCount]
            : (rented = new PooledBuffer<FourState>(barCount)).Span;
        try
        {
            states[0] = FourState.Ascender;
            int bar = 1;
            foreach (char c in text)
            {
                RoyalMailEncoder.Append(RoyalMailEncoder.Value(c), states[bar..]);
                bar += RoyalMailEncoder.BarsPerCharacter;
            }

            RoyalMailEncoder.Append(RoyalMailEncoder.Value(RoyalMailEncoder.CheckCharacter(text)), states[bar..]);
            states[barCount - 1] = FourState.Full;

            FourStateEncoder.BuildSymbol(states, FourStateEncoder.RoyalMail, text, options, ref symbol);
        }
        finally
        {
            rented.Dispose();
        }
    }
}
