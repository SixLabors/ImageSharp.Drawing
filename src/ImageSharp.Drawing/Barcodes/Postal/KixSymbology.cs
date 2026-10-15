// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The KIX code of the Dutch post, the Royal Mail 4-State Customer Code without its start bar, stop bar
/// and check character. It carries digits and capital letters, each as four bars of which two have an
/// ascender and two a descender, at the dimensions of Table 11 of the Royal Mail Mailmark barcode
/// definition document. The printed line shows the data as given.
/// </summary>
public sealed class KixSymbology : BarcodeSymbology
{
    /// <inheritdoc/>
    public override double NominalXDimension => FourStateEncoder.RoyalMail.XDimension;

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, RoyalMailEncoder.MaximumLength, nameof(text));
        RoyalMailEncoder.Validate(text);

        // Four bars for each character. The text is at most MaximumLength characters, so a short symbol
        // works inline and a longer one rents its states once.
        int barCount = text.Length * RoyalMailEncoder.BarsPerCharacter;
        InlineArray128<FourState> stateStorage = default;
        PooledBuffer<FourState> rented = default;
        Span<FourState> states = barCount <= 128
            ? ((Span<FourState>)stateStorage)[..barCount]
            : (rented = new PooledBuffer<FourState>(barCount)).Span;
        try
        {
            for (int i = 0; i < text.Length; i++)
            {
                RoyalMailEncoder.Append(RoyalMailEncoder.Value(text[i]), states[(i * RoyalMailEncoder.BarsPerCharacter)..]);
            }

            FourStateEncoder.BuildSymbol(states, FourStateEncoder.RoyalMail, text, options, ref symbol);
        }
        finally
        {
            rented.Dispose();
        }
    }
}
