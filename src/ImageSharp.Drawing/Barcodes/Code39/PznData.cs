// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The data layer the Pharmazentralnummer symbologies share. IFA gives the data structure as a minus sign
/// followed by the number: "The minus sign is internationally standardized as an identifier for the PZN in
/// ISO/IEC 15418 and serves to identify the PZN", and "the last digit of the PZN is the check digit".
/// </summary>
internal static class PznData
{
    /// <summary>
    /// The identifier ISO/IEC 15418 gives the PZN, which opens the encoded data.
    /// </summary>
    private const char Identifier = '-';

    /// <summary>
    /// The term the printed line opens with, with the identifier and the spaces IFA adds for readability.
    /// Neither the term nor the spaces are encoded.
    /// </summary>
    private const string ReadablePrefix = "PZN - ";

    /// <summary>
    /// The bar height in modules a symbol takes when the caller sets none. IFA gives a nominal code
    /// height of 10 mm at the nominal module width of 0.25 mm, and says "the code height of nominally
    /// 10 mm changes proportionately to the nominal module width". IFA also gives a nominal wide to
    /// narrow ratio of 1:2.5 and permits 1:2 to 1:3. A run width is a whole number of modules, so the
    /// symbol draws the permitted 3.
    /// </summary>
    private const float NominalBarHeight = 40F;

    /// <summary>
    /// The modulus of the check digit. IFA: "The check digit of the PZN is calculated based on mod 11."
    /// </summary>
    private const int CheckModulus = 11;

    /// <summary>
    /// The weight the final data digit takes, whatever the length. IFA weights the seven digits of a
    /// PZN8 from one, so the seventh takes seven, and a PZN7 carries one digit fewer and starts at two.
    /// </summary>
    private const int FinalWeight = 7;

    /// <summary>
    /// Encodes a Pharmazentralnummer of the given length into a Code 39 symbol.
    /// </summary>
    /// <param name="text">
    /// The data digits on their own, in which case the check digit is calculated, or the data digits and
    /// the check digit, in which case the last is checked against the rest.
    /// </param>
    /// <param name="digits">The number of data digits the length carries, excluding the check digit.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    /// <exception cref="ArgumentException">The text is not a valid Pharmazentralnummer.</exception>
    public static BarcodeSymbol BuildSymbol(string text, int digits, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeBetweenOrEqualTo(text.Length, digits, digits + 1, nameof(text));

        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException($"A PZN carries only digits; got {current.ToDisplayString()}.", nameof(text));
            }
        }

        // IFA: "The sum is formed across the products and divided by 11. The whole number remainder is the
        // check digit."
        int sum = 0;
        int weight = FinalWeight - digits + 1;
        for (int i = 0; i < digits; i++)
        {
            sum += (text[i] - '0') * (weight + i);
        }

        int check = sum % CheckModulus;
        if (check == 10)
        {
            // IFA: "If the remainder is the number 10, this digit sequence is not used as PZN."
            throw new ArgumentException($"The digits '{text[..digits]}' leave a remainder of 10, which IFA does not issue as a PZN.", nameof(text));
        }

        if (text.Length > digits && text[digits] - '0' != check)
        {
            throw new ArgumentException($"Incorrect PZN check digit: expected {check}, got {text[digits]}.", nameof(text));
        }

        Span<char> encoded = stackalloc char[digits + 2];
        encoded[0] = Identifier;
        text.AsSpan(0, digits).CopyTo(encoded[1..]);
        encoded[^1] = (char)('0' + check);

        return Code39Encoder.BuildSymbol(
            Code39Encoder.Encode(encoded, null),
            options.Font is null ? string.Empty : string.Concat(ReadablePrefix, encoded[1..]),
            options,
            NominalBarHeight);
    }
}
