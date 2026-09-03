// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Royal Mail 4-State Customer Code, RM4SCC. It carries digits and capital letters, each as four
/// bars of which two have an ascender and two a descender, between a start bar with an ascender and a
/// stop bar that is full, and it always carries the check character of the checksum calculation table
/// after the data. The dimensions are those of Table 11 of the Royal Mail Mailmark barcode definition
/// document, which the Royal Mail symbologies share, with the 2 mm clear zone of its section 3.5.2. The
/// printed line shows the data as given.
/// </summary>
public sealed class RoyalMailSymbology : BarcodeSymbology
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

        int barCount = ((text.Length + 1) * RoyalMailEncoder.BarsPerCharacter) + 2;
        FourState[] states = new FourState[barCount];
        states[0] = FourState.Ascender;
        int bar = 1;
        for (int i = 0; i < text.Length; i++)
        {
            RoyalMailEncoder.Append(RoyalMailEncoder.Value(text[i]), states.AsSpan(bar));
            bar += RoyalMailEncoder.BarsPerCharacter;
        }

        RoyalMailEncoder.Append(RoyalMailEncoder.Value(RoyalMailEncoder.CheckCharacter(text)), states.AsSpan(bar));
        states[barCount - 1] = FourState.Full;

        string readable = options.Font is null ? string.Empty : text;
        return FourStateEncoder.BuildSymbol(states, FourStateEncoder.RoyalMail, readable, options);
    }
}
