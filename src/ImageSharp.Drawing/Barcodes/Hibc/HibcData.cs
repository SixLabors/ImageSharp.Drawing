// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The data layer the Health Industry Bar Code symbologies share. Every HIBC symbol carries the Code 39
/// character set, the same flag character in front of the data and the same modulo 43 check character
/// after it, whatever symbology draws it.
/// </summary>
internal static class HibcData
{
    /// <summary>
    /// The largest number of characters of HIBC text: 498. The flag character and the check character make
    /// the encoded data two characters longer, 500, the most the Code 39 and Code 128 carriers take.
    /// </summary>
    public const int MaximumLength = 498;

    /// <summary>
    /// The number of characters a caller holds inline for <see cref="Prepare"/> to build in. Data this
    /// long covers the labels the standard is used for, and longer data rents its buffer once.
    /// </summary>
    public const int InlineBufferLength = 64;

    /// <summary>
    /// The delimiter that opens the human readable interpretation, with the flag character after it.
    /// </summary>
    public const string ReadableOpening = "*+";

    /// <summary>
    /// The delimiter that closes the human readable interpretation.
    /// </summary>
    public const char ReadableClosing = '*';

    /// <summary>
    /// The character that begins the encoded data.
    /// </summary>
    private const char FlagCharacter = '+';

    /// <summary>
    /// The character that stands in for a check character that is a space, which cannot be read as one.
    /// </summary>
    private const char PrintedSpace = '_';

    /// <summary>
    /// The character that joins primary and secondary data in one symbol, section 2.2.1.1 of ANSI/HIBC 2.6.
    /// </summary>
    private const char ConcatenationCharacter = '/';

    /// <summary>
    /// Returns the number of characters <see cref="Prepare"/> writes at most for the text: the flag
    /// character, the data and the check character. <see cref="Prepare"/> rejects text longer than
    /// <see cref="MaximumLength"/>, so the capacity never passes that length plus two.
    /// </summary>
    /// <param name="textLength">The length of the text.</param>
    /// <returns>The capacity the prepared data needs.</returns>
    public static int PreparedCapacity(int textLength) => Math.Min(textLength, MaximumLength) + 2;

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
        int sum = AlphanumericCharacterSet.Value(FlagCharacter);
        int dataLength = validateCheckCharacter ? text.Length - 1 : text.Length;
        int index = 0;
        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            int value = AlphanumericCharacterSet.Value(current.Value);
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

        char check = AlphanumericCharacterSet.CheckCharacter(sum);
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
    /// Returns the body of the human readable interpretation of encoded data: the data between the flag
    /// character and the check character. The interpretation is <see cref="ReadableOpening"/>, the body,
    /// <see cref="PrintedCheck"/> and <see cref="ReadableClosing"/>.
    /// </summary>
    /// <param name="encoded">The encoded data, as <see cref="Prepare"/> wrote it.</param>
    /// <returns>The body.</returns>
    public static ReadOnlySpan<char> ReadableBody(ReadOnlySpan<char> encoded) => encoded[1..^1];

    /// <summary>
    /// Returns the check character as the human readable interpretation prints it: a space shows as an
    /// underscore.
    /// </summary>
    /// <param name="encoded">The encoded data, as <see cref="Prepare"/> wrote it.</param>
    /// <returns>The printed check character.</returns>
    public static char PrintedCheck(ReadOnlySpan<char> encoded)
    {
        char check = encoded[^1];
        return check == ' ' ? PrintedSpace : check;
    }

    /// <summary>
    /// Returns the number of characters <see cref="WriteMatrixReadable"/> writes at most for encoded data:
    /// the encoded data, the two delimiters and one line break.
    /// </summary>
    /// <param name="encodedLength">The length of the encoded data.</param>
    /// <returns>The capacity the interpretation needs.</returns>
    public static int MatrixReadableCapacity(int encodedLength) => encodedLength + 3;

    /// <summary>
    /// Writes the human readable interpretation of encoded data that a two-dimensional symbol carries.
    /// Section 3.0 of ANSI/HIBC 2.6 gives every symbology the same interpretation:
    /// <see cref="ReadableOpening"/>, the body, <see cref="PrintedCheck"/> and <see cref="ReadableClosing"/>.
    /// The figure of section 4.3.3 prints primary and secondary data that one symbol concatenates on two
    /// lines, broken after the character that joins them.
    /// </summary>
    /// <param name="encoded">The encoded data, as <see cref="Prepare"/> wrote it.</param>
    /// <param name="readable">The buffer the interpretation is written to, <see cref="MatrixReadableCapacity"/> long.</param>
    /// <returns>The number of characters written.</returns>
    public static int WriteMatrixReadable(ReadOnlySpan<char> encoded, Span<char> readable)
    {
        ReadOnlySpan<char> body = ReadableBody(encoded);
        ReadableOpening.CopyTo(readable);
        int written = ReadableOpening.Length;

        // Section 3.0 uses the join character only between the primary and the secondary data.
        int join = body.IndexOf(ConcatenationCharacter);
        if (join >= 0)
        {
            body[..(join + 1)].CopyTo(readable[written..]);
            written += join + 1;
            readable[written++] = BarcodeTextPlacement.LineBreak;
            body = body[(join + 1)..];
        }

        body.CopyTo(readable[written..]);
        written += body.Length;
        readable[written++] = PrintedCheck(encoded);
        readable[written++] = ReadableClosing;
        return written;
    }

    /// <summary>
    /// Adds the human readable interpretation of encoded data below a built matrix symbol, as
    /// <see cref="WriteMatrixReadable"/> writes it, centered on the symbol as the figures of sections 4.3.1
    /// and 4.3.3 of ANSI/HIBC 2.6 print it.
    /// </summary>
    /// <param name="encoded">The encoded data, as <see cref="Prepare"/> wrote it.</param>
    /// <param name="symbol">The built symbol.</param>
    public static void AddMatrixReadable(ReadOnlySpan<char> encoded, ref BarcodeSymbol symbol)
    {
        // Prepare bounds the encoded data, so the buffer is bounded too, and longer data rents it once.
        InlineArray64<char> readableStorage = default;
        PooledBuffer<char> rented = default;
        int capacity = MatrixReadableCapacity(encoded.Length);
        Span<char> readable = capacity <= InlineBufferLength
            ? ((Span<char>)readableStorage)[..capacity]
            : (rented = new PooledBuffer<char>(capacity)).Span;
        try
        {
            int written = WriteMatrixReadable(encoded, readable);
            symbol.AddMatrixText(readable[..written], BarcodeTextAlignment.Centered, 1F);
        }
        finally
        {
            rented.Dispose();
        }
    }
}
