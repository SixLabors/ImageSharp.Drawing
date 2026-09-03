// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Royal Mail 4-state Mailmark barcode, which the Royal Mail Mailmark barcode C and barcode L
/// encoding and decoding instructions define. The text is the application string: a 22-character
/// string gives barcode C of 66 bars and a 26-character string barcode L of 78 bars. The fields are the
/// format, the version ID, the class, the supply chain ID of 2 or 6 digits, the item ID of 8 digits and
/// the destination post code plus delivery point suffix of 9 characters. The bars are the 0.54 mm bars
/// of Table 11 of the Mailmark barcode definition document, and the printed line shows the application
/// string as given.
/// </summary>
public sealed class FourStateMailmarkSymbology : BarcodeSymbology
{
    /// <inheritdoc/>
    public override float NominalXDimension => FourStateEncoder.RoyalMail.XDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        int barCount = FourStateMailmarkEncoder.BarCount(text.Length);
        if (barCount == 0)
        {
            throw new ArgumentException($"A Mailmark application string is 22 characters for barcode C or 26 for barcode L; got {text.Length}.", nameof(text));
        }

        Span<FourState> states = stackalloc FourState[barCount];
        FourStateMailmarkEncoder.Encode(text, states);
        string readable = options.Font is null ? string.Empty : text;
        return FourStateEncoder.BuildSymbol(states, FourStateEncoder.RoyalMail, readable, options);
    }
}
