// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The data layer the Deutsche Post symbologies share. Both the Leitcode and the Identcode are
/// Interleaved 2 of 5 symbols whose check digit replaces the weights 3 and 1 of the standard calculation
/// with 4 and 9: "Anstelle der bei 2 aus 5 verwendeten Gewichte 3 und 1 werden die Stellen mit den
/// Werten 4 und 9 gewichtet."
/// </summary>
internal static class DeutschePostData
{
    /// <summary>
    /// The bar height in modules a symbol takes when the caller sets none. Deutsche Post requires a
    /// height of at least 25 mm ("mindestens 25 mm") and a narrow module of 0.375 mm to 0.5 mm, so at the
    /// widest permitted module the minimum height is 50 modules. The 5 mm quiet zone Deutsche Post
    /// requires on each side ("links und rechts von jedem Strichcode mindestens 5 mm") is the ten
    /// modules of Interleaved 2 of 5 at that same module width.
    /// </summary>
    public const float NominalBarHeight = 50F;

    /// <summary>
    /// The modulus of the check digit.
    /// </summary>
    private const int CheckModulus = 10;

    /// <summary>
    /// The weight the first digit and every second digit after it take.
    /// </summary>
    private const int FirstWeight = 4;

    /// <summary>
    /// The weight the second digit and every second digit after it take.
    /// </summary>
    private const int SecondWeight = 9;

    /// <summary>
    /// Validates a Deutsche Post number and writes the digits the symbol carries: the data digits and
    /// the check digit. The check digit weights the digits 4 and 9 alternately from the first, sums the
    /// products, and takes the complement of the sum to the next multiple of ten. The documented Leitcode
    /// example 2134807501640 sums to 239 and checks as 1, and the Identcode example 56310243031 sums to
    /// 187 and checks as 3.
    /// </summary>
    /// <param name="text">
    /// The data digits on their own, in which case the check digit is calculated, or the data digits and
    /// the check digit, in which case the last is checked against the rest.
    /// </param>
    /// <param name="digits">The number of data digits the code carries, excluding the check digit.</param>
    /// <param name="encoded">The buffer the data digits and the check digit are written to, one longer than <paramref name="digits"/>.</param>
    /// <exception cref="ArgumentException">The text is not a valid Deutsche Post number.</exception>
    public static void Prepare(string text, int digits, Span<char> encoded)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeBetweenOrEqualTo(text.Length, digits, digits + 1, nameof(text));

        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException($"A Deutsche Post number carries only digits; got {current.ToDisplayString()}.", nameof(text));
            }
        }

        int sum = 0;
        for (int i = 0; i < digits; i++)
        {
            sum += (text[i] - '0') * ((i & 1) == 0 ? FirstWeight : SecondWeight);
        }

        int check = (CheckModulus - (sum % CheckModulus)) % CheckModulus;
        if (text.Length > digits && text[digits] - '0' != check)
        {
            throw new ArgumentException($"Incorrect check digit: expected {check}, got {text[digits]}.", nameof(text));
        }

        text.AsSpan(0, digits).CopyTo(encoded);
        encoded[digits] = (char)('0' + check);
    }
}
