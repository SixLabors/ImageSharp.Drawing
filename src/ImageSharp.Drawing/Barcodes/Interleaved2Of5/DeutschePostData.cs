// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The data layer the Deutsche Post symbologies share. Both the Leitcode and the Identcode are Interleaved 2 of 5 symbols whose check digit replaces the
/// weights 3 and 1 of the standard calculation with 4 and 9: "Anstelle der bei 2 aus 5 verwendeten Gewichte 3 und 1 werden die Stellen mit den Werten 4 und 9
/// gewichtet."
/// </summary>
internal static class DeutschePostData
{
    /// <summary>
    /// The X dimension in millimetres a symbol takes when the caller sets none: a narrow module of 0.5 mm.
    /// </summary>
    private const double NominalXDimension = 0.5;

    /// <summary>
    /// The bar height in millimetres a symbol takes when the caller sets none.
    /// </summary>
    private const double BarHeight = 25;

    /// <summary>
    /// The quiet zone in millimetres on each side, at least the ten modules of Interleaved 2 of 5.
    /// </summary>
    private const double QuietZone = 5;

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
    /// Validates a Deutsche Post number and writes the digits the symbol carries: the data digits and the check digit. The check digit weights the digits 4 and
    /// 9 alternately from the first, sums the products, and takes the complement of the sum to the next multiple of ten. The documented Leitcode example
    /// 2134807501640 sums to 239 and checks as 1, and the Identcode example 56310243031 sums to 187 and checks as 3.
    /// </summary>
    /// <param name="text"> The data digits on their own, in which case the check digit is calculated, or the data digits and the check digit, in which case the
    /// last is checked against the rest. </param>
    /// <param name="digits">The number of data digits the code carries, excluding the check digit.</param>
    /// <param name="encoded">The buffer the data digits and the check digit are written to, one longer than <paramref name="digits"/>.</param>
    /// <exception cref="ArgumentException">The text is not a valid Deutsche Post number.</exception>
    public static void Prepare(ReadOnlySpan<char> text, int digits, Span<char> encoded)
    {
        Guard.MustBeBetweenOrEqualTo(text.Length, digits, digits + 1, nameof(text));

        BarcodeSymbology.ValidateDigits(text);

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

        text.Slice(0, digits).CopyTo(encoded);
        encoded[digits] = (char)('0' + check);
    }

    /// <summary>
    /// Builds the Interleaved 2 of 5 symbol of a Deutsche Post number. The default bar height and the quiet zones are in millimetres, so they hold at every
    /// module width, and the quiet zone keeps the ten modules of Interleaved 2 of 5 too.
    /// </summary>
    /// <param name="encoded">The data digits and the check digit.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(ReadOnlySpan<char> encoded, ReadOnlySpan<char> readable, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        double xDimension = options.ResolveXDimension(NominalXDimension);
        Interleaved2Of5Layout layout = new()
        {
            XDimension = xDimension,
            NominalBarHeight = (float)(BarHeight / xDimension),
            MinimumBarHeight = 0F,
            MaximumBarHeight = float.PositiveInfinity,
            QuietZone = MathF.Max(Interleaved2Of5Encoder.QuietZone, (float)(QuietZone / xDimension)),
            TextClearance = BarcodeTextPlacement.Clearance,
            NarrowElement = 1,
            WideElement = Interleaved2Of5Encoder.WideElement,
        };

        Interleaved2Of5Encoder.BuildSymbol(encoded, readable, in layout, options, ref symbol);
    }
}
