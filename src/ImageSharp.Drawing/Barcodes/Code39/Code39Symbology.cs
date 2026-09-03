// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Code 39 symbology, which ISO/IEC 16388 defines. Section 4.1 a) gives it the digits, the capital
/// letters and the special characters <c>space $ % + - . /</c>. Section 4.2 puts a start and a stop
/// character around the data, and both share one pattern.
/// <para>
/// Annex A.2 prints the human readable interpretation "of the data characters (and data and symbol check
/// character(s), if used)". It also says the start and stop characters "may be printed". Section 4.3.3
/// shows that character as an asterisk. A symbol therefore prints its data between asterisks, with the
/// check character after the data when it carries one.
/// </para>
/// </summary>
public sealed class Code39Symbology : BarcodeSymbology
{
    private const char ReadableDelimiter = '*';

    /// <summary>
    /// Initializes a new instance of the <see cref="Code39Symbology"/> class.
    /// </summary>
    public Code39Symbology()
        : this(CheckCharacterMode.None, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Code39Symbology"/> class.
    /// </summary>
    /// <param name="checkCharacter">
    /// Whether the symbol carries the modulo 43 check character, and whether the encoder calculates it
    /// or validates it.
    /// </param>
    public Code39Symbology(CheckCharacterMode checkCharacter)
        : this(checkCharacter, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Code39Symbology"/> class.
    /// </summary>
    /// <param name="checkCharacter">
    /// Whether the symbol carries the modulo 43 check character, and whether the encoder calculates it
    /// or validates it.
    /// </param>
    /// <param name="printCheckCharacter">
    /// Whether a check character the symbol carries is part of the human readable interpretation.
    /// </param>
    public Code39Symbology(CheckCharacterMode checkCharacter, bool printCheckCharacter)
    {
        this.CheckCharacter = checkCharacter;
        this.PrintCheckCharacter = printCheckCharacter;
    }

    /// <summary>
    /// Gets a value that specifies whether the symbol carries the modulo 43 check character, and whether
    /// the encoder calculates it or validates it.
    /// </summary>
    public CheckCharacterMode CheckCharacter { get; }

    /// <summary>
    /// Gets a value indicating whether a check character the symbol carries is part of the human readable
    /// interpretation. A symbol that carries none prints none either way.
    /// </summary>
    public bool PrintCheckCharacter { get; }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Code39Encoder.Validate(text);

        ReadOnlySpan<char> data = this.CheckCharacter == CheckCharacterMode.Validate ? text.AsSpan(0, text.Length - 1) : text;
        char? check = this.CheckCharacter == CheckCharacterMode.None ? null : Code39Encoder.CheckCharacter(data);
        if (this.CheckCharacter == CheckCharacterMode.Validate && text[^1] != check)
        {
            throw new ArgumentException(
                $"Incorrect check character: expected '{check}', got '{text[^1]}'.",
                nameof(text));
        }

        return Code39Encoder.BuildSymbol(
            Code39Encoder.Encode(data, check),
            options.Font is null ? string.Empty : this.BuildReadable(data, check),
            options);
    }

    /// <summary>
    /// Builds the human readable interpretation: the data, then the check character the symbol carries,
    /// between the delimiters that stand for the start and stop character.
    /// </summary>
    /// <param name="data">The data the symbol carries, without the check character.</param>
    /// <param name="check">The check character the symbol carries, or <see langword="null"/>.</param>
    /// <returns>The human readable interpretation.</returns>
    private string BuildReadable(ReadOnlySpan<char> data, char? check)
    {
        Span<char> opening = stackalloc char[1] { ReadableDelimiter };
        if (check is null || !this.PrintCheckCharacter)
        {
            return string.Concat(opening, data, opening);
        }

        Span<char> closing = stackalloc char[2] { check.Value, ReadableDelimiter };
        return string.Concat(opening, data, closing);
    }
}
