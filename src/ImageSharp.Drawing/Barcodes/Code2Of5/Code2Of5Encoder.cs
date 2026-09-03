// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the plain 2 of 5 symbologies: Industrial, IATA, Matrix, COOP and Datalogic. No
/// published standard defines them. Every digit has the weights 1, 2, 4 and 7 and a parity element, and
/// two of its five elements are wide. Both reference implementations agree on every pattern below.
/// <para>
/// Industrial and IATA encode a digit in five bars with a narrow space after each bar, so a digit is ten
/// elements. Matrix, COOP and Datalogic encode a digit in three bars and two spaces with a narrow space
/// after them, so a digit is six elements. The check digit is optional in every variant and is the
/// modulo 10 calculation of Appendix C of AIM USS-I 2/5. No document gives a wide element width, a quiet
/// zone or a bar height for these symbologies, so this library draws them with the values of Interleaved
/// 2 of 5.
/// </para>
/// </summary>
internal static class Code2Of5Encoder
{
    /// <summary>
    /// The largest number of digits this library encodes in one symbol. None of the symbologies fixes a
    /// maximum of its own.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The number of elements of one digit of Industrial and IATA 2 of 5: five bars, each with a narrow
    /// space after it.
    /// </summary>
    private const int BarElementsPerDigit = 10;

    /// <summary>
    /// The number of elements of one digit of Matrix, COOP and Datalogic 2 of 5: three bars and two
    /// spaces, with a narrow space after them.
    /// </summary>
    private const int MatrixElementsPerDigit = 6;

    /// <summary>
    /// The width of a wide element in modules, the value this library draws for Interleaved 2 of 5.
    /// </summary>
    private const int WideElement = 3;

    /// <summary>
    /// Gets the element widths of every digit, one bit per element, most significant element first: a
    /// set bit is a wide element and a clear bit a narrow one. Industrial and IATA read the five bits as
    /// bars. Matrix and Datalogic read them as a bar, a space, a bar, a space and a bar. The narrow
    /// spaces that separate the elements and the digits are not in the pattern. The patterns are those
    /// of Table 2 of AIM USS-I 2/5, indexed by digit.
    /// </summary>
    private static ReadOnlySpan<byte> Patterns =>
    [
        0b00110, 0b10001, 0b01001, 0b11000, 0b00101,
        0b10100, 0b01100, 0b00011, 0b10010, 0b01010,
    ];

    /// <summary>
    /// Gets the element widths of every digit of COOP 2 of 5, in the layout of <see cref="Patterns"/>.
    /// COOP assigns the same ten patterns to the digits in a different order.
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
    /// <param name="checkDigit">
    /// Whether the symbol carries the modulo 10 check digit, and whether the encoder calculates it or
    /// validates it.
    /// </param>
    /// <param name="printCheckDigit">
    /// Whether a check digit the symbol carries is part of the human readable interpretation.
    /// </param>
    /// <returns>The encoded symbol.</returns>
    public static LinearBarcodeSymbol Encode(string text, BarcodeOptions options, Code2Of5Variant variant, CheckCharacterMode checkDigit, bool printCheckDigit)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException($"2 of 5 carries only digits; got {current.ToDisplayString()}.", nameof(text));
            }
        }

        ReadOnlySpan<char> data = checkDigit == CheckCharacterMode.Validate ? text.AsSpan(0, text.Length - 1) : text;
        int? check = checkDigit == CheckCharacterMode.None ? null : EanUpcEncoder.ComputeCheckDigit(data);
        if (checkDigit == CheckCharacterMode.Validate && text[^1] - '0' != check)
        {
            throw new ArgumentException($"Incorrect check digit: expected {check}, got {text[^1]}.", nameof(text));
        }

        Span<char> digitBuffer = stackalloc char[Interleaved2Of5Encoder.StackBufferLength];
        ValueStringBuilder digits = new(digitBuffer);
        digits.Append(data);
        if (check is not null)
        {
            digits.Append((char)('0' + check.Value));
        }

        ReadOnlySpan<char> encoded = digits.AsSpan();
        string readable = options.Font is null
            ? string.Empty
            : (check is null || printCheckDigit ? encoded : encoded[..^1]).ToString();

        LinearBarcodeSymbol symbol = Interleaved2Of5Encoder.BuildSymbol(Encode(encoded, variant), readable, options);
        digits.Dispose();
        return symbol;
    }

    /// <summary>
    /// Encodes digits into the alternating bar and space run widths the renderer draws, starting with the
    /// first bar of the start pattern and ending on the last bar of the stop pattern.
    /// </summary>
    /// <param name="digits">The digits to encode, the check digit included, already validated.</param>
    /// <param name="variant">The symbology whose patterns to use.</param>
    /// <returns>The run widths in modules.</returns>
    public static int[] Encode(ReadOnlySpan<char> digits, Code2Of5Variant variant)
    {
        ReadOnlySpan<byte> start = StartPattern(variant);
        ReadOnlySpan<byte> stop = StopPattern(variant);
        ReadOnlySpan<byte> patterns = variant == Code2Of5Variant.Coop ? CoopPatterns : Patterns;
        bool barsOnly = variant is Code2Of5Variant.Industrial or Code2Of5Variant.Iata;
        int elementsPerDigit = barsOnly ? BarElementsPerDigit : MatrixElementsPerDigit;

        int[] runs = new int[(digits.Length * elementsPerDigit) + start.Length + stop.Length];
        int written = 0;

        for (int i = 0; i < start.Length; i++)
        {
            runs[written++] = start[i];
        }

        // Industrial and IATA put a narrow space after every bar. The other variants put one narrow
        // space after the five elements of the digit.
        for (int i = 0; i < digits.Length; i++)
        {
            int pattern = patterns[digits[i] - '0'];
            for (int bit = 4; bit >= 0; bit--)
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

        for (int i = 0; i < stop.Length; i++)
        {
            runs[written++] = stop[i];
        }

        return runs;
    }

    /// <summary>
    /// Gets the start pattern of a variant in bar and space order, as <see cref="Code2Of5Variant"/>
    /// describes it.
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
    /// Gets the stop pattern of a variant in bar and space order, ending on a bar, as
    /// <see cref="Code2Of5Variant"/> describes it.
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
