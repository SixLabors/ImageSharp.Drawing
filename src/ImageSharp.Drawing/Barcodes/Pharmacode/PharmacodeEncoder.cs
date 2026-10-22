// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The input handling the one-track and two-track Pharmacode symbologies share. Both carry one number, given in decimal. The Laetus Pharmacode Guide counts
/// the codes of up to sixteen bars in section 4.1, and section 4.9.2 says that "coding that is only one bar wide should never be used", which sets the
/// range.
/// </summary>
internal static class PharmacodeEncoder
{
    /// <summary>
    /// Parses the number a Pharmacode symbol carries and checks its range.
    /// </summary>
    /// <param name="text">The digits of the number.</param>
    /// <param name="minimum">The smallest value the symbology carries.</param>
    /// <param name="maximum">The largest value the symbology carries.</param>
    /// <param name="maximumDigits">The number of digits of <paramref name="maximum"/>.</param>
    /// <returns>The value.</returns>
    public static int ParseValue(ReadOnlySpan<char> text, int minimum, int maximum, int maximumDigits)
    {
        Guard.MustBeBetweenOrEqualTo(text.Length, 1, maximumDigits, nameof(text));

        BarcodeSymbology.ValidateDigits(text);
        int value = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);

        Guard.MustBeBetweenOrEqualTo(value, minimum, maximum, nameof(text));

        return value;
    }
}
