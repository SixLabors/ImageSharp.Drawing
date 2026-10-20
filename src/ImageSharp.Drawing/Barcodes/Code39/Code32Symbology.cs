// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Code 32 is the Italian pharmaceutical code. Allegato A of the decree in Gazzetta Ufficiale della Repubblica Italiana, Serie generale n. 165 of 18 July 2014,
/// defines it.
/// <para>
/// The AIC code is nine digits. The first digit is zero and the last digit is the check digit. Six base 32 characters carry those nine digits, and a Code 39
/// symbol carries those six characters.
/// </para>
/// <para>
/// The input is the eight data digits, or those digits and the check digit. This class always calculates the check digit. When the input carries one, this
/// class compares the two.
/// </para>
/// <para>
/// The printed line is the letter A and then the nine digits. That letter is the field identifier for automatic reading equipment. The symbol does not carry
/// the letter or the digits.
/// </para>
/// <para>
/// The symbol draws its wide elements at the nominal ratio of 2.5 that Allegato A gives.
/// </para>
/// </summary>
public sealed class Code32Symbology : BarcodeSymbology
{
    /// <summary>
    /// The base 32 characters in value order, from Table 1 of Allegato A. Section 3 gives the rule: "l'uso delle cifre da 0 a 9 e delle lettere dell'alfabeto
    /// inglese ad eccezione delle lettere A, E, I, O".
    /// </summary>
    private const string Base32Characters = "0123456789BCDFGHJKLMNPQRSTUVWXYZ";

    /// <summary>
    /// The letter that opens the printed line. Section 3 of Allegato A prints the code "preceduto dalla lettera A, avente funzione di identificatore di campo
    /// per apparecchiature di lettura automatica".
    /// </summary>
    private const char FieldIdentifier = 'A';

    /// <summary>
    /// The number of data digits, excluding the check digit.
    /// </summary>
    private const int Digits = 8;

    /// <summary>
    /// The number of base 32 characters the symbol carries. Section 3: the numbering system "consente di rappresentare le nove cifre del codice con sei
    /// caratteri alfanumerici".
    /// </summary>
    private const int Base32Length = 6;

    /// <summary>
    /// The X dimension in millimetres a symbol takes when the caller sets none. Allegato A: "lo spessore nominale delle barre e degli spazi stretti è di mm
    /// 0,250".
    /// </summary>
    private const double NominalXDimension = 0.25;

    /// <summary>
    /// The width of a narrow element in run units, so a run unit is half a module.
    /// </summary>
    private const int NarrowElement = 2;

    /// <summary>
    /// The width of a wide element in run units. Allegato A: the wide elements are such "che il rapporto tra S ed M risulti nominalmente uguale a 2,5".
    /// </summary>
    private const int WideElement = 5;

    /// <summary>
    /// The width of the symbol in modules, quiet zones excluded. The start character, the six base 32 characters and the stop character each have three wide
    /// and six narrow elements, every character but the stop character has a narrow gap, and a run unit is half a module.
    /// </summary>
    private const float WidthInModules = (((Base32Length + 2) * ((3 * WideElement) + (7 * NarrowElement))) - NarrowElement) / (float)NarrowElement;

    /// <summary>
    /// Initializes a new instance of the <see cref="Code32Symbology"/> class.
    /// </summary>
    public Code32Symbology()
    {
    }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.MustBeBetweenOrEqualTo(text.Length, Digits, Digits + 1, nameof(text));

        BarcodeSymbology.ValidateDigits(text);

        // Section 3: the code "è composto da nove cifre di cui la prima è la cifra zero".
        if (text[0] != '0')
        {
            throw new ArgumentException($"An AIC code starts with the digit zero; got '{text[0]}'.", nameof(text));
        }

        // Section 3 doubles the second, fourth, sixth and eighth digits, and sums the quotient and the remainder of each product divided by ten. That sum joins
        // the sum of the first, third, fifth and seventh digits, and the check digit is the remainder of the total divided by ten.
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

        InlineArray10<char> printedStorage = default;
        Span<char> printed = ((Span<char>)printedStorage)[..(Digits + 1)];
        text.Slice(0, Digits).CopyTo(printed);
        printed[Digits] = (char)('0' + check);

        int number = 0;
        foreach (char digit in printed)
        {
            number = (number * 10) + (digit - '0');
        }

        InlineArray6<char> encodedStorage = default;
        Span<char> encoded = encodedStorage;
        for (int i = Base32Length - 1; i >= 0; i--)
        {
            encoded[i] = Base32Characters[number % Base32Characters.Length];
            number /= Base32Characters.Length;
        }

        double xDimension = options.ResolveXDimension(NominalXDimension);

        // The runs count pixels, so every element draws at a whole number of pixels. A wide element is WideElement / NarrowElement modules, rounded half up
        // to a whole pixel: at an odd number of pixels a module the ratio of 2.5 becomes 3.0 at one pixel and 2.67 at three, and every wide element takes the
        // same width.
        int pixels = options.ModulePixels(xDimension);
        Code39Layout layout = new()
        {
            XDimension = xDimension,
            NominalBarHeight = Code39Encoder.ProportionalBarHeight(WidthInModules, xDimension),
            NarrowElement = pixels,
            WideElement = ((WideElement * pixels) + (NarrowElement / 2)) / NarrowElement,
        };

        // The interpretation is the field identifier, then the nine digits of the code.
        Code39Encoder.BuildSymbol(encoded, null, [FieldIdentifier], printed, [], in layout, options, ref symbol);
    }
}
