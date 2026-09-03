// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the Code 93 symbology family, defined by ANSI/AIM BC5-1995. Every symbol
/// character is six elements wide, three bars and three spaces, and spans nine modules. A symbol carries
/// a quiet zone, the start character, the data and two check characters. The stop character with its
/// termination bar and a second quiet zone close the symbol. No gap separates the characters, which is
/// what distinguishes the symbology from Code 39.
/// </summary>
internal static class Code93Encoder
{
    /// <summary>
    /// The number of data characters ANSI/AIM BC5-1995 carries. They are the 43 of
    /// <see cref="AlphanumericCharacterSet"/>, and the four shift characters take the values above them.
    /// </summary>
    public const int DataCharacters = 43;

    /// <summary>
    /// The largest number of characters this library encodes in one symbol. ANSI/AIM BC5-1995 makes the
    /// data string length variable, so the symbology sets no maximum of its own.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The quiet zone in modules on each side. Section 2.6 of ANSI/AIM BC5-1995 measures a symbol as
    /// <c>(9 * (C + 4) + 1) * X + 2 * Q</c>, where C is the data character count and Q is ten modules.
    /// </summary>
    public const int QuietZone = 10;

    /// <summary>
    /// The number of characters a caller stack allocates to build symbol data in. Data this long covers
    /// the labels the symbology is used for, and anything longer grows into a pooled array.
    /// </summary>
    public const int StackBufferLength = 64;

    /// <summary>
    /// The element count of a symbol character. ANSI/AIM BC5-1995 gives every character three bars and
    /// three spaces.
    /// </summary>
    private const int ElementsPerCharacter = 6;

    /// <summary>
    /// The element count a symbol carries beyond its data and check characters: the six of the start
    /// character and the seven of the stop character with its termination bar.
    /// </summary>
    private const int StartStopElements = 13;

    /// <summary>
    /// The number of check characters every symbol carries. ANSI/AIM BC5-1995 fixes both of them, so an
    /// application standard cannot make them optional.
    /// </summary>
    private const int CheckCharacters = 2;

    /// <summary>
    /// The modulus ANSI/AIM BC5-1995 takes both check characters over, which is the size of the symbol
    /// character set.
    /// </summary>
    private const int CheckModulus = 47;

    /// <summary>
    /// The highest weight the first check character of ANSI/AIM BC5-1995 reaches before it returns to
    /// one.
    /// </summary>
    private const int FirstCheckWeights = 20;

    /// <summary>
    /// The highest weight the second check character of ANSI/AIM BC5-1995 reaches before it returns to
    /// one.
    /// </summary>
    private const int SecondCheckWeights = 15;

    /// <summary>
    /// The bar height as a fraction of the symbol width, quiet zones included, when the caller sets no
    /// height. Section 2.6 of ANSI/AIM BC5-1995 recommends a minimum of 0.2 inches or 15 per cent of the
    /// symbol length, whichever is greater.
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <summary>
    /// The smallest bar height in millimetres when the caller sets none: the 0.2 inches of section 2.6 of
    /// ANSI/AIM BC5-1995.
    /// </summary>
    private const float MinimumBarHeightMillimetres = 5.08F;

    /// <summary>
    /// Gets the element widths of every symbol character in modules, six to a character, in bar and space
    /// order. These are the patterns of Table 2 of ANSI/AIM BC5-1995, indexed by character value.
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
    /// Gets the element widths of the start and stop characters of ANSI/AIM BC5-1995 in modules. The
    /// first six are the start character, and all seven are the stop character, whose extra bar
    /// terminates the symbol.
    /// </summary>
    private static ReadOnlySpan<byte> StartStopPattern => [1, 1, 1, 1, 4, 1, 1];

    /// <summary>
    /// Gets the value ANSI/AIM BC5-1995 assigns the given character, or a negative number when the
    /// character is outside the symbol character set.
    /// </summary>
    /// <param name="codePoint">The code point to value.</param>
    /// <returns>The value, or a negative number.</returns>
    public static int Value(int codePoint) => codePoint switch
    {
        >= 'a' and <= 'd' => codePoint - 'a' + DataCharacters,
        _ => AlphanumericCharacterSet.Value(codePoint),
    };

    /// <summary>
    /// Validates the given text against the data character set of ANSI/AIM BC5-1995. Walking code points
    /// rather than UTF-16 units reports a surrogate pair as the one character it is, instead of showing
    /// half of it back to the caller.
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
            // A value at or beyond the data set length is one of the shift characters, which carries no
            // data and is therefore not input.
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
    /// Encodes symbol characters into the alternating bar and space run widths the renderer draws,
    /// starting with a bar. The runs carry the start character, the data, both check characters and the
    /// stop character, in the order ANSI/AIM BC5-1995 gives. That standard fixes both check characters,
    /// so the caller cannot turn them off.
    /// </summary>
    /// <param name="text">The symbol characters to encode, shift characters included.</param>
    /// <returns>The run widths in modules.</returns>
    public static int[] Encode(ReadOnlySpan<char> text)
    {
        int[] runs = new int[((text.Length + CheckCharacters) * ElementsPerCharacter) + StartStopElements];
        int written = 0;

        for (int i = 0; i < ElementsPerCharacter; i++)
        {
            runs[written++] = StartStopPattern[i];
        }

        for (int i = 0; i < text.Length; i++)
        {
            AppendPattern(runs, ref written, Value(text[i]));
        }

        int first = FirstCheckValue(text);
        AppendPattern(runs, ref written, first);
        AppendPattern(runs, ref written, SecondCheckValue(text, first));

        for (int i = 0; i < StartStopPattern.Length; i++)
        {
            runs[written++] = StartStopPattern[i];
        }

        return runs;
    }

    /// <summary>
    /// Calculates the first check character of ANSI/AIM BC5-1995, called C. It runs over the data
    /// characters from the last backwards. It weights them one upward and returns to one after twenty,
    /// then takes the sum modulo 47.
    /// </summary>
    /// <param name="text">The symbol characters the check covers.</param>
    /// <returns>The check character value.</returns>
    public static int FirstCheckValue(ReadOnlySpan<char> text)
    {
        int sum = 0;
        int weight = 1;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            sum += Value(text[i]) * weight;
            if (++weight > FirstCheckWeights)
            {
                weight = 1;
            }
        }

        return sum % CheckModulus;
    }

    /// <summary>
    /// Calculates the second check character of ANSI/AIM BC5-1995, called K. It runs the same way as the
    /// first, but it starts at the first check character and returns to one after fifteen. The first
    /// check character is therefore part of what the second covers.
    /// </summary>
    /// <param name="text">The symbol characters the check covers.</param>
    /// <param name="first">The value of the first check character.</param>
    /// <returns>The check character value.</returns>
    public static int SecondCheckValue(ReadOnlySpan<char> text, int first)
    {
        int sum = first;
        int weight = 2;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            sum += Value(text[i]) * weight;
            if (++weight > SecondCheckWeights)
            {
                weight = 1;
            }
        }

        return sum % CheckModulus;
    }

    /// <summary>
    /// Builds the symbol from encoded run widths. Code 93 carries no guard bars, so every bar runs the
    /// full height, and the human readable interpretation sits below the symbol.
    /// </summary>
    /// <param name="runs">The alternating bar and space run widths in modules.</param>
    /// <param name="text">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    public static LinearBarcodeSymbol BuildSymbol(int[] runs, string text, BarcodeOptions options)
    {
        int widthInModules = WidthInModules(runs);
        float xDimension = options.XDimension ?? BarcodeSymbology.PointXDimension;
        float nominalBarHeight = MathF.Max((widthInModules + (QuietZone * 2)) * NominalBarHeightFraction, MinimumBarHeightMillimetres / xDimension);
        float barHeight = EanUpcEncoder.ResolveBarHeight(options, BarcodeSymbology.PointXDimension, nominalBarHeight);
        int barCount = (runs.Length + 1) / 2;
        float[] heights = new float[barCount];
        float[] tops = new float[barCount];
        for (int i = 0; i < barCount; i++)
        {
            heights[i] = barHeight;
        }

        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null && text.Length > 0)
        {
            placements = [new BarcodeTextPlacement(text, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance)];
        }

        return new LinearBarcodeSymbol(runs, heights, tops, placements, QuietZone, QuietZone);
    }

    /// <summary>
    /// Returns the width of the runs in modules.
    /// </summary>
    /// <param name="runs">The alternating bar and space run widths in modules.</param>
    /// <returns>The width in modules.</returns>
    private static int WidthInModules(int[] runs)
    {
        int width = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            width += runs[i];
        }

        return width;
    }

    /// <summary>
    /// Writes the six elements of one symbol character.
    /// </summary>
    /// <param name="runs">The run widths being written.</param>
    /// <param name="written">The number of runs written so far.</param>
    /// <param name="value">The value of the character to write.</param>
    private static void AppendPattern(int[] runs, ref int written, int value)
    {
        ReadOnlySpan<byte> pattern = Patterns.Slice(value * ElementsPerCharacter, ElementsPerCharacter);
        for (int i = 0; i < pattern.Length; i++)
        {
            runs[written++] = pattern[i];
        }
    }
}
