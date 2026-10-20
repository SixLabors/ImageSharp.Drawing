// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the plain 2 of 5 symbologies: Industrial, IATA, Matrix, COOP and Datalogic. No published standard defines them. Every digit has the
/// weights 1, 2, 4 and 7 and a parity element, and two of its five elements are wide.
/// <para>
/// Industrial and IATA encode a digit in five bars with a narrow space after each bar, so a digit is ten elements. Matrix, COOP and Datalogic encode a digit in
/// three bars and two spaces with a narrow space after them, so a digit is six elements. The check digit is optional in every variant and is the modulo 10
/// calculation of Appendix C of AIM USS-I 2/5. No document gives a wide element width, a quiet zone or a bar height for these symbologies; the values are those
/// of Interleaved 2 of 5.
/// </para>
/// </summary>
internal static class Code2Of5Encoder
{
    /// <summary>
    /// The largest number of digits a symbol carries. None of the symbologies fixes a maximum of its own.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The quiet zone in modules on each side.
    /// </summary>
    private const int QuietZone = 10;

    /// <summary>
    /// The number of digits a symbol holds inline. Data this long covers the labels the symbologies are used for, and longer data rents its buffer once.
    /// </summary>
    private const int InlineBufferLength = 64;

    /// <summary>
    /// The width of a wide element in modules.
    /// </summary>
    private const int WideElement = 3;

    /// <summary>
    /// The number of elements in a digit pattern: the weights 1, 2, 4 and 7 and the parity element.
    /// </summary>
    private const int PatternElements = 5;

    /// <summary>
    /// The bar height as a fraction of the symbol width, quiet zones excluded, when the caller sets no height: 15 per cent, and at least
    /// <see cref="MinimumBarHeightMillimetres"/>.
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <summary>
    /// The smallest bar height in millimetres when the caller sets none.
    /// </summary>
    private const float MinimumBarHeightMillimetres = 6.35F;

    /// <summary>
    /// The number of elements of one digit of Industrial and IATA 2 of 5: five bars, each with a narrow space after it.
    /// </summary>
    private const int BarElementsPerDigit = 10;

    /// <summary>
    /// The number of elements of one digit of Matrix, COOP and Datalogic 2 of 5: three bars and two spaces, with a narrow space after them.
    /// </summary>
    private const int MatrixElementsPerDigit = 6;

    /// <summary>
    /// Gets the element widths of every digit, one bit per element, most significant element first: a set bit is a wide element and a clear bit a narrow one.
    /// Every digit has the weights 1, 2, 4 and 7 and a parity element, and two of its five elements are wide. Industrial and IATA read the five bits as bars.
    /// Matrix and Datalogic read them as a bar, a space, a bar, a space and a bar. The narrow spaces that separate the elements and the digits are not in the
    /// pattern.
    /// </summary>
    private static ReadOnlySpan<byte> Patterns =>
    [
        0b00110, 0b10001, 0b01001, 0b11000, 0b00101,
        0b10100, 0b01100, 0b00011, 0b10010, 0b01010,
    ];

    /// <summary>
    /// Gets the element widths of every digit of COOP 2 of 5, in the layout of <see cref="Patterns"/>. COOP assigns the same ten patterns to the digits in a
    /// different order.
    /// </summary>
    private static ReadOnlySpan<byte> CoopPatterns =>
    [
        0b11000, 0b00011, 0b00101, 0b00110, 0b01001,
        0b01010, 0b01100, 0b10001, 0b10010, 0b10100,
    ];

    /// <summary>
    /// Validates the input, applies the check digit and builds the symbol.
    /// </summary>
    /// <param name="text">The digits to encode.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="variant">The symbology whose patterns to use.</param>
    /// <param name="checkDigit"> Whether the symbol carries the modulo 10 check digit, and whether the encoder calculates it or validates it. </param>
    /// <param name="printCheckDigit"> Whether a check digit the symbol carries is part of the human readable interpretation. </param>
    /// <param name="symbol">The symbol to write.</param>
    public static void Encode(
        ReadOnlySpan<char> text,
        BarcodeOptions options,
        Code2Of5Variant variant,
        CheckCharacterMode checkDigit,
        bool printCheckDigit,
        ref BarcodeSymbol symbol)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        // Validate mode reads the last digit as the check digit, so at least one data digit comes before it.
        if (checkDigit == CheckCharacterMode.Validate && text.Length < 2)
        {
            throw new ArgumentException("A supplied check digit follows at least one data digit.", nameof(text));
        }

        BarcodeSymbology.ValidateDigits(text);

        ReadOnlySpan<char> data = checkDigit == CheckCharacterMode.Validate ? text.Slice(0, text.Length - 1) : text;
        int? check = checkDigit == CheckCharacterMode.None ? null : CheckDigit(data);
        if (checkDigit == CheckCharacterMode.Validate && text[^1] - '0' != check)
        {
            throw new ArgumentException($"Incorrect check digit: expected {check}, got {text[^1]}.", nameof(text));
        }

        // The digits the symbol carries are the data and the check digit. The text is at most MaximumLength digits, so a short symbol works inline and a longer
        // one rents its digits once.
        int carried = data.Length + (check is null ? 0 : 1);
        InlineArray64<char> digitStorage = default;
        PooledBuffer<char> rented = default;
        Span<char> encoded = carried <= InlineBufferLength
            ? ((Span<char>)digitStorage)[..carried]
            : (rented = new PooledBuffer<char>(carried)).Span;
        try
        {
            data.CopyTo(encoded);
            if (check is not null)
            {
                encoded[^1] = (char)('0' + check.Value);
            }

            ReadOnlySpan<char> readable = check is null || printCheckDigit ? encoded : encoded[..^1];
            BuildSymbol(encoded, readable, variant, options, ref symbol);
        }
        finally
        {
            rented.Dispose();
        }
    }

    /// <summary>
    /// Calculates the modulo 10 check digit of Appendix C of AIM USS-I 2/5: the digits are weighted 3 and 1 alternately from the rightmost digit, which takes
    /// 3, and the check digit lifts the sum to the next multiple of ten.
    /// </summary>
    /// <param name="digits">The data digits.</param>
    /// <returns>The check digit value.</returns>
    private static int CheckDigit(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        int weight = 3;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            sum += (digits[i] - '0') * weight;
            weight = 4 - weight;
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>
    /// Encodes digits and builds the symbol. The runs alternate bar and space, start with the first bar of the start pattern and end on the last bar of the
    /// stop pattern. The 2 of 5 symbologies carry no guard bars, so every bar runs the full height, and the human readable interpretation sits below the
    /// symbol.
    /// </summary>
    /// <param name="digits">The digits to encode, the check digit included, already validated.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="variant">The symbology whose patterns to use.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void BuildSymbol(
        ReadOnlySpan<char> digits,
        ReadOnlySpan<char> readable,
        Code2Of5Variant variant,
        BarcodeOptions options,
        ref BarcodeSymbol symbol)
    {
        ReadOnlySpan<byte> start = StartPattern(variant);
        ReadOnlySpan<byte> stop = StopPattern(variant);
        ReadOnlySpan<byte> patterns = variant == Code2Of5Variant.Coop ? CoopPatterns : Patterns;
        bool barsOnly = variant is Code2Of5Variant.Industrial or Code2Of5Variant.Iata;
        int elementsPerDigit = barsOnly ? BarElementsPerDigit : MatrixElementsPerDigit;

        double xDimension = options.ResolveXDimension(BarcodeSymbology.DefaultXDimension);
        int runCount = (digits.Length * elementsPerDigit) + start.Length + stop.Length;
        Span<int> runs = symbol.BeginLinear(runCount, xDimension, out Span<float> heights, out _);
        int written = 0;
        foreach (byte width in start)
        {
            runs[written++] = width;
        }

        // Industrial and IATA put a narrow space after every bar. The other variants put one narrow space after the five elements of the digit.
        foreach (char digit in digits)
        {
            int pattern = patterns[digit - '0'];
            for (int bit = PatternElements - 1; bit >= 0; bit--)
            {
                runs[written++] = ((pattern >> bit) & 1) != 0 ? WideElement : 1;
                if (barsOnly)
                {
                    runs[written++] = 1;
                }
            }

            if (!barsOnly)
            {
                runs[written++] = 1;
            }
        }

        foreach (byte width in stop)
        {
            runs[written++] = width;
        }

        int widthInModules = BarcodeSymbol.RunWidth(runs);

        float nominalBarHeight = MathF.Max(widthInModules * NominalBarHeightFraction, (float)(MinimumBarHeightMillimetres / xDimension));
        float barHeight = options.ResolveBarHeight(xDimension, 0, nominalBarHeight, float.PositiveInfinity);
        heights.Fill(barHeight);

        if (options.Font is not null)
        {
            symbol.ReserveText(1, readable.Length);
            symbol.AddText(readable, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(QuietZone, QuietZone);
    }

    /// <summary>
    /// Gets the start pattern of a variant in bar and space order, as <see cref="Code2Of5Variant"/> describes it.
    /// </summary>
    /// <param name="variant">The symbology.</param>
    /// <returns>The run widths of the start pattern.</returns>
    private static ReadOnlySpan<byte> StartPattern(Code2Of5Variant variant) => variant switch
    {
        Code2Of5Variant.Industrial => [3, 1, 3, 1, 1, 1],
        Code2Of5Variant.Matrix => [3, 1, 1, 1, 1, 1],
        Code2Of5Variant.Coop => [3, 1, 3, 1],
        _ => [1, 1, 1, 1],
    };

    /// <summary>
    /// Gets the stop pattern of a variant in bar and space order, ending on a bar, as <see cref="Code2Of5Variant"/> describes it.
    /// </summary>
    /// <param name="variant">The symbology.</param>
    /// <returns>The run widths of the stop pattern.</returns>
    private static ReadOnlySpan<byte> StopPattern(Code2Of5Variant variant) => variant switch
    {
        Code2Of5Variant.Industrial => [3, 1, 1, 1, 3],
        Code2Of5Variant.Matrix => [3, 1, 1, 1, 1],
        Code2Of5Variant.Coop => [1, 3, 3],
        _ => [3, 1, 1],
    };
}
