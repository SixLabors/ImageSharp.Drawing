// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Code 39 symbology, defined by ISO/IEC 16388. Section 4.1 a) gives it the digits, the capital
/// letters and the special characters <c>space $ % + - . /</c>, and section 4.2 brackets the data with a
/// start and stop character that share one pattern.
/// <para>
/// Annex A.2 prints the human readable interpretation "of the data characters (and data and symbol check
/// character(s), if used)", and says the start and stop characters "may be printed". Section 4.3.3 depicts
/// that character as an asterisk, so a symbol prints its data between asterisks with the check character
/// behind the data when it carries one.
/// </para>
/// </summary>
public sealed class Code39Symbology : BarcodeSymbology
{
    private const char ReadableDelimiter = '*';

    /// <summary>
    /// Initializes a new instance of the <see cref="Code39Symbology"/> class.
    /// </summary>
    public Code39Symbology()
        : this(Code39CheckCharacter.None, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Code39Symbology"/> class.
    /// </summary>
    /// <param name="checkCharacter">How the symbol treats the modulo 43 check character.</param>
    public Code39Symbology(Code39CheckCharacter checkCharacter)
        : this(checkCharacter, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Code39Symbology"/> class.
    /// </summary>
    /// <param name="checkCharacter">How the symbol treats the modulo 43 check character.</param>
    /// <param name="printCheckCharacter">
    /// Whether a check character the symbol carries is part of the human readable interpretation.
    /// </param>
    public Code39Symbology(Code39CheckCharacter checkCharacter, bool printCheckCharacter)
    {
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
        Code39Encoder.Validate(text);

        ReadOnlySpan<char> data = this.CheckCharacter == Code39CheckCharacter.Validate ? text.AsSpan(0, text.Length - 1) : text;
        char? check = this.CheckCharacter == Code39CheckCharacter.None ? null : Code39Encoder.CheckCharacter(data);
        if (this.CheckCharacter == Code39CheckCharacter.Validate && text[^1] != check)
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
