// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Code 32 is the Italian pharmaceutical code. Allegato A of the decree in Gazzetta Ufficiale della
/// Repubblica Italiana, Serie generale n. 165 of 18 July 2014, defines it.
/// <para>
/// The AIC code is nine digits. The first digit is zero and the last digit is the check digit. Six
/// base 32 characters carry those nine digits, and a Code 39 symbol carries those six characters.
/// </para>
/// <para>
/// The input is the eight data digits, or those digits and the check digit. This class always
/// calculates the check digit. When the input carries one, this class compares the two.
/// </para>
/// <para>
/// The printed line is the letter A and then the nine digits. That letter is the field identifier for
/// automatic reading equipment. The symbol does not carry the letter or the digits.
/// </para>
/// </summary>
public sealed class Code32Symbology : BarcodeSymbology
{
    /// <summary>
    /// The base 32 characters in value order, from Table 1 of Allegato A. Section 3 gives the rule: "l'uso
    /// delle cifre da 0 a 9 e delle lettere dell'alfabeto inglese ad eccezione delle lettere A, E, I, O".
    /// </summary>
    private const string Base32Characters = "0123456789BCDFGHJKLMNPQRSTUVWXYZ";

    /// <summary>
    /// The letter that opens the printed line. Area 3 of Allegato A prints the code "preceduto dalla
    /// lettera A, avente funzione di identificatore di campo per apparecchiature di lettura automatica".
    /// </summary>
    private const char FieldIdentifier = 'A';

    /// <summary>
    /// The number of data digits, excluding the check digit.
    /// </summary>
    private const int Digits = 8;

    /// <summary>
    /// The number of base 32 characters the symbol carries. Section 3: the numbering system "consente di
    /// rappresentare le nove cifre del codice con sei caratteri alfanumerici".
    /// </summary>
    private const int Base32Length = 6;

    /// <summary>
    /// Initializes a new instance of the <see cref="Code32Symbology"/> class.
    /// </summary>
    public Code32Symbology()
    {
    }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeBetweenOrEqualTo(text.Length, Digits, Digits + 1, nameof(text));

        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException($"An AIC code carries only digits; got {current.ToDisplayString()}.", nameof(text));
            }
        }

        // Section 3: the code "è composto da nove cifre di cui la prima è la cifra zero".
        if (text[0] != '0')
        {
            throw new ArgumentException($"An AIC code starts with the digit zero; got '{text[0]}'.", nameof(text));
        }

        // Section 3 doubles the second, fourth, sixth and eighth digits, and sums the quotient and the
        // remainder of each product divided by ten. That sum joins the sum of the first, third, fifth and
        // seventh digits, and the check digit is the remainder of the total divided by ten.
        int sum = 0;
        for (int i = 0; i < Digits; i++)
        {
            int value = text[i] - '0';
            if ((i & 1) == 1)
            {
                value *= 2;
                value = (value / 10) + (value % 10);
            }

            sum += value;
        }

        int check = sum % 10;
        if (text.Length > Digits && text[Digits] - '0' != check)
        {
            throw new ArgumentException($"Incorrect AIC check digit: expected {check}, got {text[Digits]}.", nameof(text));
        }

        Span<char> printed = stackalloc char[Digits + 1];
        text.AsSpan(0, Digits).CopyTo(printed);
        printed[Digits] = (char)('0' + check);

        int number = 0;
        for (int i = 0; i < printed.Length; i++)
        {
            number = (number * 10) + (printed[i] - '0');
        }

        Span<char> encoded = stackalloc char[Base32Length];
        for (int i = Base32Length - 1; i >= 0; i--)
        {
            encoded[i] = Base32Characters[number % Base32Characters.Length];
            number /= Base32Characters.Length;
        }

        return Code39Encoder.BuildSymbol(
            Code39Encoder.Encode(encoded, null),
            options.Font is null ? string.Empty : string.Concat(stackalloc char[1] { FieldIdentifier }, printed),
            options);
    }
}
