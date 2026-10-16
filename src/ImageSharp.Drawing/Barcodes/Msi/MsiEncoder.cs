// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the MSI symbology, also called Modified Plessey. Every digit is its four binary coded decimal bits, most significant first, and every bit is
/// a bar and a space three modules wide: a 0 bit is a narrow bar and a wide space, and a 1 bit is a wide bar and a narrow space. The symbol is continuous. A
/// start character, the 1 bit, opens it, and a stop character, two 0 bits without the last space, closes it.
/// </summary>
internal static class MsiEncoder
{
    /// <summary>
    /// The largest number of digits a symbol carries. The symbology sets no maximum.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The quiet zone in modules on each side.
    /// </summary>
    public const int QuietZone = 10;

    /// <summary>
    /// The number of characters a caller holds inline to build symbol data in. Longer data rents its buffer once.
    /// </summary>
    public const int InlineBufferLength = 64;

    /// <summary>
    /// The highest weight of the IBM modulo 11 calculation, after which the weights return to 2.
    /// </summary>
    public const int IbmMaximumWeight = 7;

    /// <summary>
    /// The highest weight of the NCR modulo 11 calculation, after which the weights return to 2.
    /// </summary>
    public const int NcrMaximumWeight = 9;

    /// <summary>
    /// The largest number of check digits a symbol carries after its data: a modulo 10 or modulo 11 check digit and a second modulo 10 check digit.
    /// </summary>
    public const int MaximumCheckDigits = 2;

    /// <summary>
    /// The number of bits in a digit.
    /// </summary>
    private const int BitsPerDigit = 4;

    /// <summary>
    /// The width of a wide bar or space in modules. A bit is three modules, one narrow element of one module and one wide element.
    /// </summary>
    private const int WideElement = 2;

    /// <summary>
    /// The number of runs of the start character: the bar and the space of its one bit.
    /// </summary>
    private const int StartElements = 2;

    /// <summary>
    /// The number of runs of the stop character: the bars and spaces of its two bits without the last space.
    /// </summary>
    private const int StopElements = 3;

    /// <summary>
    /// The bar height as a fraction of the symbol width, quiet zones excluded, when the caller sets no height: 15 per cent.
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <summary>
    /// Calculates the modulo 10 check digit over the digits. Every other digit from the right, the rightmost included, is doubled. The digits of those products
    /// and the digits that were not doubled are added, and the check digit lifts the sum to the next multiple of ten. Doubling the number the selected digits
    /// form and adding the digits of the product gives the same sum, because a doubled digit carries exactly when it is ten or more.
    /// </summary>
    /// <param name="digits">The digits the check digit covers.</param>
    /// <returns>The check digit.</returns>
    public static int Modulo10(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        bool doubled = true;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int digit = digits[i] - '0';
            if (doubled)
            {
                digit *= 2;
                if (digit >= 10)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubled = !doubled;
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>
    /// Calculates the modulo 11 check value over the digits. The digits are weighted from the right, starting at 2 and returning to 2 after the maximum weight,
    /// and the check value lifts the weighted sum to the next multiple of eleven. The value 10 cannot be carried in one digit.
    /// </summary>
    /// <param name="digits">The digits the check value covers.</param>
    /// <param name="maximumWeight">The highest weight, 7 for the IBM calculation and 9 for the NCR calculation.</param>
    /// <returns>The check value, 0 to 10.</returns>
    public static int Modulo11(ReadOnlySpan<char> digits, int maximumWeight)
    {
        int sum = 0;
        int weight = 2;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            sum += weight * (digits[i] - '0');
            weight = weight == maximumWeight ? 2 : weight + 1;
        }

        return (11 - (sum % 11)) % 11;
    }

    /// <summary>
    /// Encodes digits and builds the symbol. The runs start with the bar of the start character and end on the last bar of the stop character. MSI carries no
    /// guard bars, so every bar runs the full height, and the human readable interpretation sits below the symbol.
    /// </summary>
    /// <param name="digits">The digits to encode, the check digits included, already validated.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(ReadOnlySpan<char> digits, ReadOnlySpan<char> readable, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        double xDimension = options.XDimension ?? BarcodeSymbology.PointXDimension;
        Span<int> runs = symbol.BeginLinear(StartElements + (digits.Length * BitsPerDigit * 2) + StopElements, xDimension, out Span<float> heights, out _);

        // The start character is a 1 bit, a wide bar and a narrow space.
        int written = 0;
        runs[written++] = WideElement;
        runs[written++] = 1;

        foreach (char digit in digits)
        {
            int value = digit - '0';
            for (int bit = BitsPerDigit - 1; bit >= 0; bit--)
            {
                bool one = ((value >> bit) & 1) == 1;
                runs[written++] = one ? WideElement : 1;
                runs[written++] = one ? 1 : WideElement;
            }
        }

        // The stop character is two 0 bits without the last space: a narrow bar, a wide space and a narrow bar.
        runs[written++] = 1;
        runs[written++] = WideElement;
        runs[written] = 1;

        int widthInModules = BarcodeSymbol.RunWidth(runs);

        float barHeight = options.ResolveBarHeight(xDimension, widthInModules * NominalBarHeightFraction);
        heights.Fill(barHeight);

        if (options.Font is not null && readable.Length > 0)
        {
            symbol.ReserveText(1, readable.Length);
            symbol.AddText(readable, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(QuietZone, QuietZone);
    }
}
