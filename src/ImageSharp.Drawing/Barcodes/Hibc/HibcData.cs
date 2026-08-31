// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The data layer the Health Industry Bar Code symbologies share. Every HIBC symbol carries the Code 39
/// character set, the same flag character in front of the data and the same modulo 43 check character
/// behind it, whatever symbology draws it.
/// </summary>
internal static class HibcData
{
    /// <summary>
    /// The largest number of characters a HIBC symbol carries.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The number of characters a caller stack allocates for <see cref="Prepare"/> to build in. Data this
    /// long covers the labels the standard is used for, and anything longer grows into a pooled array.
    /// </summary>
    public const int StackBufferLength = 64;

    /// <summary>
    /// The character that begins the encoded data.
    /// </summary>
    private const char FlagCharacter = '+';

    /// <summary>
    /// The value the flag character holds in the Code 39 character set.
    /// </summary>
    private const int FlagCharacterValue = 41;

    /// <summary>
    /// The delimiter that opens the human readable interpretation, with the flag character behind it.
    /// </summary>
    private const string ReadableOpening = "*+";

    /// <summary>
    /// The delimiter that closes the human readable interpretation.
    /// </summary>
    private const char ReadableClosing = '*';

    /// <summary>
    /// The character that stands in for a check character that is a space, which cannot be read as one.
    /// </summary>
    private const char PrintedSpace = '_';

    /// <summary>
    /// Validates the given text against the HIBC character set, calculates the check character over the
    /// data and writes the encoded data from both.
    /// </summary>
    /// <param name="text">
    /// The data to carry. When <paramref name="validateCheckCharacter"/> is <see langword="true"/> the
    /// last character is the check character rather than data.
    /// </param>
    /// <param name="validateCheckCharacter">
    /// Whether the check character at the end of <paramref name="text"/> is validated.
    /// </param>
    /// <param name="encoded">
    /// The builder the encoded data is written to: the flag character, the data and the check character.
    /// </param>
    /// <exception cref="ArgumentException">The text is not valid HIBC data.</exception>
    public static void Prepare(ReadOnlySpan<char> text, bool validateCheckCharacter, ref ValueStringBuilder encoded)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        // The check character covers the flag character the encoder puts in front of the data, so the
        // sum starts at the value that character holds in the set.
        int sum = FlagCharacterValue;
        int dataLength = validateCheckCharacter ? text.Length - 1 : text.Length;
        int index = 0;
        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            int value = current.IsAscii ? Code39Encoder.Value((char)current.Value) : -1;
            if (value < 0)
            {
                throw new ArgumentException(
                    $"HIBC carries only digits, capital letters, spaces and the symbols -.$/+%; got {current.ToDisplayString()}.",
                    nameof(text));
            }

            if (index < dataLength)
            {
                sum += value;
            }

            index++;
        }

        char check = Code39Encoder.Characters[sum % Code39Encoder.Characters.Length];
        if (validateCheckCharacter && text[dataLength] != check)
        {
            throw new ArgumentException(
                $"Incorrect check character: expected '{check}', got '{text[dataLength]}'.",
                nameof(text));
        }

        encoded.Append(FlagCharacter);
        encoded.Append(text[..dataLength]);
        encoded.Append(check);
    }

    /// <summary>
    /// Builds the human readable interpretation of encoded data: the same string between delimiters, with
    /// a check character that is a space shown as an underscore.
    /// </summary>
    /// <param name="encoded">The encoded data, as <see cref="Prepare"/> wrote it.</param>
    /// <returns>The human readable interpretation.</returns>
    public static string BuildReadable(ReadOnlySpan<char> encoded)
    {
        char check = encoded[^1];
        Span<char> tail = stackalloc char[2] { check == ' ' ? PrintedSpace : check, ReadableClosing };
        return string.Concat(ReadableOpening, encoded[1..^1], tail);
    }
}
