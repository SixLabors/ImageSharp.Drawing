// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The MOD 97-10 check character system of ISO/IEC 7064, which the International Bank Account Number of
/// ISO 13616 and the Creditor Reference of ISO 11649 share: the first four characters, a two-letter prefix and
/// two check digits, move to the end, each letter reads as the two digits 10 to 35, and the number that
/// gives must leave 1 when divided by 97.
/// </summary>
internal static class Iso7064
{
    /// <summary>
    /// Returns the remainder modulo 97 of the characters read with the first four moved to the end and each
    /// letter as the two digits 10 to 35, a small letter as its capital letter.
    /// </summary>
    /// <param name="text">The characters, at least four.</param>
    /// <returns>The remainder, 0 to 96, or -1 when a character is not a digit or a letter of A to Z.</returns>
    public static int Mod97(ReadOnlySpan<char> text)
    {
        // The remainder is kept below 97 after each character, so the long number is never held.
        int remainder = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[(i + 4) % text.Length];
            if (char.IsAsciiDigit(c))
            {
                remainder = ((remainder * 10) + (c - '0')) % 97;
            }
            else if (char.IsAsciiLetter(c))
            {
                remainder = ((remainder * 100) + (char.ToUpperInvariant(c) - 'A' + 10)) % 97;
            }
            else
            {
                return -1;
            }
        }

        return remainder;
    }
}
