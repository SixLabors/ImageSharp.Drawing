// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Code 93 Extended, the full ASCII mode of ANSI/AIM BC5-1995. Table 3 encodes the 128 characters of
/// ASCII as either one symbol character or a shift character followed by one more.
/// <para>
/// The symbol is an ordinary Code 93 symbol, so only a decoder in full ASCII mode reads back what went
/// in. The human readable interpretation shows the ASCII characters the caller gave rather than the
/// symbol characters that stand for them, and prints a space where a character has no printed form.
/// </para>
/// </summary>
public sealed class Code93ExtendedSymbology : BarcodeSymbology
{
    /// <summary>
    /// Table 3, two characters for every ASCII value in order. A value encoded by a single symbol
    /// character has a space in front of it, and a space is never a shift character, so the first
    /// character of a pair says which of the two forms it is.
    /// </summary>
    private const string Substitutions =
        "bUaAaBaCaDaEaFaG" +
        "aHaIaJaKaLaMaNaO" +
        "aPaQaRaSaTaUaVaW" +
        "aXaYaZbAbBbCbDbE" +
        "  cAcBcC $ %cFcG" +
        "cHcIcJ +cL - . /" +
        " 0 1 2 3 4 5 6 7" +
        " 8 9cZbFbGbHbIbJ" +
        "bV A B C D E F G" +
        " H I J K L M N O" +
        " P Q R S T U V W" +
        " X Y ZbKbLbMbNbO" +
        "bWdAdBdCdDdEdFdG" +
        "dHdIdJdKdLdMdNdO" +
        "dPdQdRdSdTdUdVdW" +
        "dXdYdZbPbQbRbSbT";

    /// <summary>
    /// Initializes a new instance of the <see cref="Code93ExtendedSymbology"/> class.
    /// </summary>
    public Code93ExtendedSymbology()
    {
    }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));

        // A pair per character bounds the substitution, and the readable interpretation matches the input
        // character for character.
        Span<char> encodedBuffer = stackalloc char[Code93Encoder.StackBufferLength];
        Span<char> readableBuffer = stackalloc char[Code93Encoder.StackBufferLength];
        ValueStringBuilder encoded = new(encodedBuffer);
        ValueStringBuilder readable = new(readableBuffer);
        try
        {
            // Walking code points rather than UTF-16 units reports a surrogate pair as the one character
            // it is, instead of showing half of it back to the caller.
            SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
            while (codePoints.MoveNext())
            {
                CodePoint current = codePoints.Current;
                if (!current.IsAscii)
                {
                    throw new ArgumentException(
                        $"Code 93 Extended encodes ASCII 0 to 127; {current.ToDisplayString()} is outside that range.",
                        nameof(text));
                }

                ReadOnlySpan<char> pair = Substitutions.AsSpan(current.Value * 2, 2);
                encoded.Append(pair[0] == ' ' ? pair[1..] : pair);

                // A control character has no printed form, so the interpretation shows a space instead.
                readable.Append(CodePoint.IsControl(current) ? ' ' : (char)current.Value);
            }

            Guard.MustBeLessThanOrEqualTo(encoded.Length, Code93Encoder.MaximumLength, nameof(text));

            return Code93Encoder.BuildSymbol(
                Code93Encoder.Encode(encoded.AsSpan()),
                options.Font is null ? string.Empty : readable.AsSpan().ToString(),
                options);
        }
        finally
        {
            encoded.Dispose();
            readable.Dispose();
        }
    }
}
