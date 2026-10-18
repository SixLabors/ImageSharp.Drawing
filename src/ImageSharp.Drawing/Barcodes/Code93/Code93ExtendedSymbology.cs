// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Code 93 Extended, the full ASCII mode of ANSI/AIM BC5-1995. Table 3 encodes the 128 characters of ASCII as either one symbol character or a shift character
/// followed by one more.
/// <para>
/// The symbol is an ordinary Code 93 symbol, so only a decoder in full ASCII mode reads back what went in. The human readable interpretation shows the ASCII
/// characters the caller gave rather than the symbol characters that stand for them, and prints a space where a character has no printed form.
/// </para>
/// </summary>
public sealed class Code93ExtendedSymbology : BarcodeSymbology
{
    /// <summary>
    /// Table 3, two characters for every ASCII value in order. A value encoded by a single symbol character has a space in front of it, and a space is never a
    /// shift character, so the first character of a pair says which of the two forms it is.
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
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));

        // Each character substitutes to one or two symbol characters, and the symbol carries at most Code93Encoder.MaximumLength of them, so longer text cannot
        // fit. A pair per character bounds the substitution, and the readable interpretation matches the input character for character, so neither builder
        // grows: short text stays inline and longer text rents once.
        Guard.MustBeLessThanOrEqualTo(text.Length, Code93Encoder.MaximumLength, nameof(text));

        // The message decodes the code point at the first unit outside ASCII, so a surrogate pair reports as the one character it is.
        int invalid = text.IndexOfAnyExceptInRange('\0', '\u007F');
        if (invalid >= 0)
        {
            throw new ArgumentException(
                $"Code 93 Extended encodes ASCII 0 to 127; {text.CodePointAt(invalid).ToDisplayString()} is outside that range.",
                nameof(text));
        }

        InlineArray64<char> encodedStorage = default;
        InlineArray64<char> readableStorage = default;
        int encodedCapacity = text.Length * 2;
        ValueStringBuilder encoded = encodedCapacity <= Code93Encoder.InlineBufferLength ? new(encodedStorage) : new(encodedCapacity);
        ValueStringBuilder readable = text.Length <= Code93Encoder.InlineBufferLength ? new(readableStorage) : new(text.Length);
        try
        {
            foreach (char character in text)
            {
                ReadOnlySpan<char> pair = Substitutions.AsSpan(character * 2, 2);
                encoded.Append(pair[0] == ' ' ? pair[1..] : pair);

                // A control character has no printed form, so the interpretation shows a space instead.
                readable.Append(char.IsControl(character) ? ' ' : character);
            }

            Guard.MustBeLessThanOrEqualTo(encoded.Length, Code93Encoder.MaximumLength, nameof(text));

            Code93Encoder.BuildSymbol(encoded.AsSpan(), readable.AsSpan(), options, ref symbol);
        }
        finally
        {
            encoded.Dispose();
            readable.Dispose();
        }
    }
}
