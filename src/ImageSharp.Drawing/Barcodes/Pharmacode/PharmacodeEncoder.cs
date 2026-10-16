// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The input handling the one-track and two-track Pharmacode symbologies share. Both carry one number, given in decimal, whose range the Laetus Pharmacode
/// Guide sets in section 4.1.
/// </summary>
internal static class PharmacodeEncoder
{
    /// <summary>
    /// Parses the number a Pharmacode symbol carries and checks its range.
    /// </summary>
    /// <param name="text">The digits of the number.</param>
    /// <param name="minimum">The smallest value the symbology carries.</param>
    /// <param name="maximum">The largest value the symbology carries.</param>
    /// <returns>The value.</returns>
    public static int ParseValue(string text, int minimum, int maximum)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));

        int digits = 1;
        for (int remaining = maximum; remaining >= 10; remaining /= 10)
        {
            digits++;
        }

        if (text.Length > digits)
        {
            throw new ArgumentException($"Pharmacode carries a number of up to {digits} digits; got {text.Length} characters.", nameof(text));
        }

        BarcodeSymbology.ValidateDigits(text);
        int value = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);

        if (value < minimum || value > maximum)
        {
            throw new ArgumentException($"Pharmacode carries a number from {minimum} to {maximum}; got {value}.", nameof(text));
        }

        return value;
    }
}
