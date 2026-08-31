// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Code 39 Extended, the full ASCII transmission mode of Annex A.3.1 of ISO/IEC 16388. The 128 character
/// set of ISO 646 IRV "may be encoded using either one or combinations of two symbol characters, made up
/// of one of the four characters ($ + % /) followed by one of the 26 alphabetic characters", and Table A.2
/// gives the combinations.
/// <para>
/// A.3 warns that the mode needs a decoder programmed for it, so the symbol is an ordinary Code 39 symbol
/// and only a decoder in full ASCII mode reads back what was encoded. Annex A.2 prints an interpretation
/// "of the data characters", which here are the ASCII characters the caller gave, not the symbol
/// characters they were substituted into, so the interpretation shows the text as given with a space
/// where a character has no printed form.
/// </para>
/// </summary>
public sealed class Code39ExtendedSymbology : BarcodeSymbology
{
    private const char ReadableDelimiter = '*';

    /// <summary>
    /// Table A.2, two characters for every ASCII value in order. A value encoded by a single symbol
    /// character has a space in front of it, which is never a prefix, so the first character of a pair
    /// says which of the two forms it is.
    /// </summary>
    private const string Substitutions =
        "%U$A$B$C$D$E$F$G" +
        "$H$I$J$K$L$M$N$O" +
        "$P$Q$R$S$T$U$V$W" +
        "$X$Y$Z%A%B%C%D%E" +
        "  /A/B/C/D/E/F/G" +
        "/H/I/J/K/L - ./O" +
        " 0 1 2 3 4 5 6 7" +
        " 8 9/Z%F%G%H%I%J" +
        "%V A B C D E F G" +
        " H I J K L M N O" +
        " P Q R S T U V W" +
        " X Y Z%K%L%M%N%O" +
        "%W+A+B+C+D+E+F+G" +
        "+H+I+J+K+L+M+N+O" +
        "+P+Q+R+S+T+U+V+W" +
        "+X+Y+Z%P%Q%R%S%T";

    /// <summary>
    /// The characters Table A.2 puts in front of an alphabetic character to make a pair.
    /// </summary>
    private const string Prefixes = "$%/+";

    /// <summary>
    /// Initializes a new instance of the <see cref="Code39ExtendedSymbology"/> class.
    /// </summary>
    public Code39ExtendedSymbology()
        : this(Code39CheckCharacter.None, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Code39ExtendedSymbology"/> class.
    /// </summary>
    /// <param name="checkCharacter">How the symbol treats the modulo 43 check character.</param>
    public Code39ExtendedSymbology(Code39CheckCharacter checkCharacter)
        : this(checkCharacter, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Code39ExtendedSymbology"/> class.
    /// </summary>
    /// <param name="checkCharacter">
    /// How the symbol treats the modulo 43 check character. <see cref="Code39CheckCharacter.Validate"/>
    /// is rejected: the check character covers the substituted symbol characters, which a caller working
    /// in ASCII does not have.
    /// </param>
    /// <param name="printCheckCharacter">
    /// Whether a check character the symbol carries is part of the human readable interpretation.
    /// </param>
    public Code39ExtendedSymbology(Code39CheckCharacter checkCharacter, bool printCheckCharacter)
    {
        Guard.IsFalse(
            checkCharacter == Code39CheckCharacter.Validate,
            nameof(checkCharacter),
            "Code 39 Extended works out the check character over the substituted symbol characters, so a supplied one cannot be validated against the input.");

        this.CheckCharacter = checkCharacter;
        this.PrintCheckCharacter = printCheckCharacter;
    }

    /// <summary>
    /// Gets a value indicating how the symbol treats the modulo 43 check character.
    /// </summary>
    public Code39CheckCharacter CheckCharacter { get; }

    /// <summary>
    /// Gets a value indicating whether a check character the symbol carries is part of the human readable
    /// interpretation. A symbol that carries none prints none either way.
    /// </summary>
    public bool PrintCheckCharacter { get; }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));

        // A pair per character bounds the substitution, and the readable interpretation matches it
        // character for character.
        Span<char> encodedBuffer = stackalloc char[Code39Encoder.StackBufferLength];
        Span<char> readableBuffer = stackalloc char[Code39Encoder.StackBufferLength];
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
                        $"Code 39 Extended encodes ASCII 0 to 127; {current.ToDisplayString()} is outside that range.",
                        nameof(text));
                }

                char character = (char)current.Value;
                ReadOnlySpan<char> pair = Substitutions.AsSpan(character * 2, 2);
                encoded.Append(Prefixes.Contains(pair[0], StringComparison.Ordinal) ? pair : pair[1..]);

                // A control character has no printed form, so the interpretation shows a space instead.
                readable.Append(char.IsControl(character) ? ' ' : character);
            }

            Code39Encoder.Validate(encoded.AsSpan());
            char? check = this.CheckCharacter == Code39CheckCharacter.None ? null : Code39Encoder.CheckCharacter(encoded.AsSpan());

            return Code39Encoder.BuildSymbol(
                Code39Encoder.Encode(encoded.AsSpan(), check),
                options.Font is null ? string.Empty : this.BuildReadable(readable.AsSpan(), check),
                options);
        }
        finally
        {
            readable.Dispose();
            encoded.Dispose();
        }
    }

    /// <summary>
    /// Builds the human readable interpretation: the text as given, then the check character the symbol
    /// carries, between the delimiters that stand for the start and stop character.
    /// </summary>
    /// <param name="text">The text the symbol carries, spaced to line up with the symbol characters.</param>
    /// <param name="check">The check character the symbol carries, or <see langword="null"/>.</param>
    /// <returns>The human readable interpretation.</returns>
    private string BuildReadable(ReadOnlySpan<char> text, char? check)
    {
        Span<char> opening = stackalloc char[1] { ReadableDelimiter };
        if (check is null || !this.PrintCheckCharacter)
        {
            return string.Concat(opening, text, opening);
        }

        Span<char> closing = stackalloc char[2] { check.Value, ReadableDelimiter };
        return string.Concat(opening, text, closing);
    }
}
