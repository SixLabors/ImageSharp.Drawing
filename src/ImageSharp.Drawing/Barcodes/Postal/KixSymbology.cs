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
    public override float NominalXDimension => FourStateEncoder.RoyalMail.XDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, RoyalMailEncoder.MaximumLength, nameof(text));
        RoyalMailEncoder.Validate(text);

        FourState[] states = new FourState[text.Length * RoyalMailEncoder.BarsPerCharacter];
        for (int i = 0; i < text.Length; i++)
        {
            RoyalMailEncoder.Append(RoyalMailEncoder.Value(text[i]), states.AsSpan(i * RoyalMailEncoder.BarsPerCharacter));
        }

        string readable = options.Font is null ? string.Empty : text;
        return FourStateEncoder.BuildSymbol(states, FourStateEncoder.RoyalMail, readable, options);
    }
}
