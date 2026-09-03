// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the Royal Mail 4-State Customer Code and the symbologies that reuse its
/// character set: every character is four bars, of which two carry an ascender and two a descender.
/// </summary>
internal static class RoyalMailEncoder
{
    /// <summary>
    /// The characters the symbology encodes, in pattern order.
    /// </summary>
    public const string Characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>
    /// The largest number of characters a symbol carries. The symbology sets no maximum.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The number of bars in one character.
    /// </summary>
    public const int BarsPerCharacter = 4;

    /// <summary>
    /// The check characters in the order of the checksum calculation table, which the upper half value
    /// selects the row of and the lower half value the column of.
    /// </summary>
    private const string CheckTable = "ZUVWXY501234B6789AHCDEFGNIJKLMTOPQRS";

    /// <summary>
    /// Gets the bar states of every character, four per character in the order of
    /// <see cref="Characters"/>, as <see cref="FourState"/> values.
    /// </summary>
    private static ReadOnlySpan<byte> Patterns =>
    [
        0, 0, 3, 3, 0, 1, 2, 3, 0, 1, 3, 2, 1, 0, 2, 3, 1, 0, 3, 2, 1, 1, 2, 2, 0, 2, 1, 3, 0, 3, 0, 3, 0, 3, 1, 2,
        1, 2, 0, 3, 1, 2, 1, 2, 1, 3, 0, 2, 0, 2, 3, 1, 0, 3, 2, 1, 0, 3, 3, 0, 1, 2, 2, 1, 1, 2, 3, 0, 1, 3, 2, 0,
        2, 0, 1, 3, 2, 1, 0, 3, 2, 1, 1, 2, 3, 0, 0, 3, 3, 0, 1, 2, 3, 1, 0, 2, 2, 0, 3, 1, 2, 1, 2, 1, 2, 1, 3, 0,
        3, 0, 2, 1, 3, 0, 3, 0, 3, 1, 2, 0, 2, 2, 1, 1, 2, 3, 0, 1, 2, 3, 1, 0, 3, 2, 0, 1, 3, 2, 1, 0, 3, 3, 0, 0,
    ];

    /// <summary>
    /// Returns the value of a character, which is its index in <see cref="Characters"/>.
    /// </summary>
    /// <param name="codePoint">The code point to value.</param>
    /// <returns>The value, or -1 when the character is not in the set.</returns>
    public static int Value(int codePoint) => codePoint switch
    {
        >= '0' and <= '9' => codePoint - '0',
        >= 'A' and <= 'Z' => codePoint - 'A' + 10,
        _ => -1,
    };

    /// <summary>
    /// Validates that the text is capital letters and digits alone.
    /// </summary>
    /// <param name="text">The text to validate.</param>
    public static void Validate(string text)
    {
        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (Value(current.Value) < 0)
            {
                throw new ArgumentException($"The symbology carries only digits and capital letters; got {current.ToDisplayString()}.", nameof(text));
            }
        }
    }

    /// <summary>
    /// Writes the four bar states of a character.
    /// </summary>
    /// <param name="value">The character value.</param>
    /// <param name="states">The buffer that receives the states.</param>
    public static void Append(int value, Span<FourState> states)
    {
        for (int i = 0; i < BarsPerCharacter; i++)
        {
            states[i] = (FourState)Patterns[(value * BarsPerCharacter) + i];
        }
    }

    /// <summary>
    /// Calculates the check character. The upper half of a character is the ascenders of its four bars
    /// weighted 4, 2, 1 and 0 from the left, and the lower half its descenders weighted the same. Each
    /// half value of 6 counts as 0. The half values of all the data characters are added, each total is
    /// reduced modulo 6, and the two remainders select the row and the column of the checksum table.
    /// </summary>
    /// <param name="text">The data characters, already validated.</param>
    /// <returns>The check character.</returns>
    public static char CheckCharacter(ReadOnlySpan<char> text)
    {
        int upper = 0;
        int lower = 0;
        Span<FourState> states = stackalloc FourState[BarsPerCharacter];
        for (int i = 0; i < text.Length; i++)
        {
            Append(Value(text[i]), states);
            int characterUpper = 0;
            int characterLower = 0;
            for (int bar = 0; bar < BarsPerCharacter; bar++)
            {
                int weight = 4 >> bar;
                if (states[bar] is FourState.Ascender or FourState.Full)
                {
                    characterUpper += weight;
                }

                if (states[bar] is FourState.Descender or FourState.Full)
                {
                    characterLower += weight;
                }
            }

            upper += characterUpper % 6;
            lower += characterLower % 6;
        }

        return CheckTable[((upper % 6) * 6) + (lower % 6)];
    }
}
