// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the Code 93 symbology family, defined by ANSI/AIM BC5-1995. Every symbol character is six elements wide, three bars and three spaces,
/// and spans nine modules. A symbol carries a quiet zone, the start character, the data and two check characters. The stop character with its termination bar
/// and a second quiet zone close the symbol. No gap separates the characters, which is what distinguishes the symbology from Code 39.
/// </summary>
internal static class Code93Encoder
{
    /// <summary>
    /// The number of data characters ANSI/AIM BC5-1995 carries. They are the 43 of <see cref="AlphanumericCharacterSet"/>, and the four shift characters take
    /// the values above them.
    /// </summary>
    public const int DataCharacters = 43;

    /// <summary>
    /// The largest number of characters a symbol carries. ANSI/AIM BC5-1995 makes the data string length variable, so the symbology sets no maximum of its own.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The quiet zone in modules on each side. Section 2.6 of ANSI/AIM BC5-1995 measures a symbol as <c>(9 * (C + 4) + 1) * X + 2 * Q</c>, where C is the data
    /// character count and Q is ten modules.
    /// </summary>
    public const int QuietZone = 10;

    /// <summary>
    /// The number of characters a caller holds inline to build symbol data in. Data this long covers the labels the symbology is used for, and longer data
    /// rents its buffer once.
    /// </summary>
    public const int InlineBufferLength = 64;

    /// <summary>
    /// The element count of a symbol character. ANSI/AIM BC5-1995 gives every character three bars and three spaces.
    /// </summary>
    private const int ElementsPerCharacter = 6;

    /// <summary>
    /// The element count a symbol carries beyond its data and check characters: the six of the start character and the seven of the stop character with its
    /// termination bar.
    /// </summary>
    private const int StartStopElements = 13;

    /// <summary>
    /// The number of check characters every symbol carries. ANSI/AIM BC5-1995 fixes both of them, so an application standard cannot make them optional.
    /// </summary>
    private const int CheckCharacters = 2;

    /// <summary>
    /// The modulus ANSI/AIM BC5-1995 takes both check characters over, which is the size of the symbol character set.
    /// </summary>
    private const int CheckModulus = 47;

    /// <summary>
    /// The highest weight the first check character of ANSI/AIM BC5-1995 reaches before it returns to one.
    /// </summary>
    private const int FirstCheckWeights = 20;

    /// <summary>
    /// The highest weight the second check character of ANSI/AIM BC5-1995 reaches before it returns to one.
    /// </summary>
    private const int SecondCheckWeights = 15;

    /// <summary>
    /// The bar height as a fraction of the symbol width, quiet zones included, when the caller sets no height. Section 2.6 of ANSI/AIM BC5-1995 recommends a
    /// minimum of 0.2 inches or 15 per cent of the symbol length, whichever is greater.
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <summary>
    /// The smallest bar height in millimetres when the caller sets none: the 0.2 inches of section 2.6 of ANSI/AIM BC5-1995.
    /// </summary>
    private const float MinimumBarHeightMillimetres = 5.08F;

    /// <summary>
    /// Gets the element widths of every symbol character in modules, six to a character, in bar and space order. These are the patterns of Table 2 of ANSI/AIM
    /// BC5-1995, indexed by character value.
    /// </summary>
    private static ReadOnlySpan<byte> Patterns =>
    [
        1, 3, 1, 1, 1, 2, 1, 1, 1, 2, 1, 3, 1, 1, 1, 3, 1, 2, 1, 1, 1, 4, 1, 1,
        1, 2, 1, 1, 1, 3, 1, 2, 1, 2, 1, 2, 1, 2, 1, 3, 1, 1, 1, 1, 1, 1, 1, 4,
        1, 3, 1, 2, 1, 1, 1, 4, 1, 1, 1, 1, 2, 1, 1, 1, 1, 3, 2, 1, 1, 2, 1, 2,
        2, 1, 1, 3, 1, 1, 2, 2, 1, 1, 1, 2, 2, 2, 1, 2, 1, 1, 2, 3, 1, 1, 1, 1,
        1, 1, 2, 1, 1, 3, 1, 1, 2, 2, 1, 2, 1, 1, 2, 3, 1, 1, 1, 2, 2, 1, 1, 2,
        1, 3, 2, 1, 1, 1, 1, 1, 1, 1, 2, 3, 1, 1, 1, 2, 2, 2, 1, 1, 1, 3, 2, 1,
        1, 2, 1, 1, 2, 2, 1, 3, 1, 1, 2, 1, 2, 1, 2, 1, 1, 2, 2, 1, 2, 2, 1, 1,
        2, 1, 1, 1, 2, 2, 2, 1, 1, 2, 2, 1, 2, 2, 1, 1, 2, 1, 2, 2, 2, 1, 1, 1,
        1, 1, 2, 1, 2, 2, 1, 1, 2, 2, 2, 1, 1, 2, 2, 1, 2, 1, 1, 2, 3, 1, 1, 1,
        1, 2, 1, 1, 3, 1, 3, 1, 1, 1, 1, 2, 3, 1, 1, 2, 1, 1, 3, 2, 1, 1, 1, 1,
        1, 1, 2, 1, 3, 1, 1, 1, 3, 1, 2, 1, 2, 1, 1, 1, 3, 1, 1, 2, 1, 2, 2, 1,
        3, 1, 2, 1, 1, 1, 3, 1, 1, 1, 2, 1, 1, 2, 2, 2, 1, 1,
    ];

    /// <summary>
    /// Gets the element widths of the start and stop characters of ANSI/AIM BC5-1995 in modules. The first six are the start character, and all seven are the
    /// stop character, whose extra bar terminates the symbol.
    /// </summary>
    private static ReadOnlySpan<byte> StartStopPattern => [1, 1, 1, 1, 4, 1, 1];

    /// <summary>
    /// Gets the value ANSI/AIM BC5-1995 assigns the given character, or a negative number when the character is outside the symbol character set.
    /// </summary>
    /// <param name="codePoint">The code point to value.</param>
    /// <returns>The value, or a negative number.</returns>
    public static int Value(int codePoint) => codePoint switch
    {
        >= 'a' and <= 'd' => codePoint - 'a' + DataCharacters,
        _ => AlphanumericCharacterSet.Value(codePoint),
    };

    /// <summary>
    /// Validates the given text against the data character set of ANSI/AIM BC5-1995. Walking code points rather than UTF-16 units reports a surrogate pair as
    /// the one character it is, instead of showing half of it back to the caller.
    /// </summary>
    /// <param name="text">The text to validate.</param>
    /// <exception cref="ArgumentException">The text carries a character outside the set.</exception>
    public static void Validate(ReadOnlySpan<char> text)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            // A value at or beyond the data set length is one of the shift characters, which carries no data and is therefore not input.
            CodePoint current = codePoints.Current;
            int value = Value(current.Value);
            if (value < 0 || value >= DataCharacters)
            {
                throw new ArgumentException(
                    $"Code 93 carries only digits, capital letters, spaces and the symbols -.$/+%; {current.ToDisplayString()} is outside that set.",
                    nameof(text));
            }
        }
    }

    /// <summary>
    /// Encodes symbol characters and builds the symbol. The runs carry the start character, the data, both check characters and the stop character, in the
    /// order ANSI/AIM BC5-1995 gives. That standard fixes both check characters, so the caller cannot turn them off. Code 93 carries no guard bars, so every
    /// bar runs the full height, and the human readable interpretation sits below the symbol.
    /// </summary>
    /// <param name="text">The symbol characters to encode, shift characters included.</param>
    /// <param name="readable">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void BuildSymbol(ReadOnlySpan<char> text, ReadOnlySpan<char> readable, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        int runCount = ((text.Length + CheckCharacters) * ElementsPerCharacter) + StartStopElements;
        double xDimension = options.ResolveXDimension(0, BarcodeSymbology.DefaultXDimension, double.PositiveInfinity);
        Span<int> runs = symbol.BeginLinear(runCount, xDimension, out Span<float> heights, out _);
        int written = 0;

        AppendElements(runs, ref written, StartStopPattern[..ElementsPerCharacter]);
        foreach (char character in text)
        {
            AppendPattern(runs, ref written, Value(character));
        }

        (int first, int second) = CheckValues(text);
        AppendPattern(runs, ref written, first);
        AppendPattern(runs, ref written, second);
        AppendElements(runs, ref written, StartStopPattern);

        int widthInModules = BarcodeSymbol.RunWidth(runs);
        float nominalBarHeight = MathF.Max((widthInModules + (QuietZone * 2)) * NominalBarHeightFraction, (float)(MinimumBarHeightMillimetres / xDimension));
        float barHeight = options.ResolveBarHeight(xDimension, 0, nominalBarHeight, float.PositiveInfinity);
        heights.Fill(barHeight);

        if (options.Font is not null && readable.Length > 0)
        {
            symbol.ReserveText(1, readable.Length);
            symbol.AddText(readable, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance);
        }

        symbol.EndLinear(QuietZone, QuietZone);
    }

    /// <summary>
    /// Calculates both check characters of ANSI/AIM BC5-1995 in one backward pass over the symbol characters. The first, called C, weights the characters one
    /// upward from the last, returns to one after twenty, and takes the sum modulo 47. The second, called K, covers the first check character too: C takes
    /// weight one, the characters take weights from two upward from the last, returning to one after fifteen, and the sum is taken modulo 47. C is not known
    /// until the pass ends, so K adds its weighted term to the sum of the characters at the end.
    /// </summary>
    /// <param name="text">The symbol characters the checks cover.</param>
    /// <returns>The values of the first and second check characters.</returns>
    private static (int First, int Second) CheckValues(ReadOnlySpan<char> text)
    {
        int firstSum = 0;
        int secondSum = 0;
        int firstWeight = 1;
        int secondWeight = 2;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            int value = Value(text[i]);
            firstSum += value * firstWeight;
            secondSum += value * secondWeight;
            if (++firstWeight > FirstCheckWeights)
            {
                firstWeight = 1;
            }

            if (++secondWeight > SecondCheckWeights)
            {
                secondWeight = 1;
            }
        }

        int first = firstSum % CheckModulus;
        return (first, (first + secondSum) % CheckModulus);
    }

    /// <summary>
    /// Writes the six elements of one symbol character.
    /// </summary>
    /// <param name="runs">The run widths being written.</param>
    /// <param name="written">The number of runs written so far.</param>
    /// <param name="value">The value of the character to write.</param>
    private static void AppendPattern(Span<int> runs, ref int written, int value)
        => AppendElements(runs, ref written, Patterns.Slice(value * ElementsPerCharacter, ElementsPerCharacter));

    /// <summary>
    /// Writes element widths to the runs.
    /// </summary>
    /// <param name="runs">The run widths being written.</param>
    /// <param name="written">The number of runs written so far.</param>
    /// <param name="widths">The element widths in modules.</param>
    private static void AppendElements(Span<int> runs, ref int written, ReadOnlySpan<byte> widths)
    {
        foreach (byte width in widths)
        {
            runs[written++] = width;
        }
    }
}
