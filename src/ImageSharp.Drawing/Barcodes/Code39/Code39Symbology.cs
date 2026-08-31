// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Code 39 symbology, defined by ISO/IEC 16388. Section 4.1 a) gives it the digits, the capital
/// letters and the special characters <c>space $ % + - . /</c>, and section 4.2 brackets the data with a
/// start and stop character that share one pattern.
/// <para>
/// Section 4.3.3 says that character "is usually depicted in human-readable form by a * (asterisk)", so
/// the human readable interpretation shows one on each side of the data. Section 4.1 g) makes the check
/// character optional, and it is not part of the printed interpretation.
/// </para>
/// </summary>
public sealed class Code39Symbology : BarcodeSymbology
{
    private const char ReadableDelimiter = '*';

    /// <summary>
    /// Initializes a new instance of the <see cref="Code39Symbology"/> class.
    /// </summary>
    public Code39Symbology()
        : this(Code39CheckCharacter.None)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Code39Symbology"/> class.
    /// </summary>
    /// <param name="checkCharacter">How the symbol treats the modulo 43 check character.</param>
    public Code39Symbology(Code39CheckCharacter checkCharacter)
        => this.CheckCharacter = checkCharacter;

    /// <summary>
    /// Gets a value indicating how the symbol treats the modulo 43 check character.
    /// </summary>
    public Code39CheckCharacter CheckCharacter { get; }

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
            options.Font is null ? string.Empty : BuildReadable(data),
            options);
    }

    /// <summary>
    /// Builds the human readable interpretation: the data between the delimiters that stand for the start
    /// and stop character.
    /// </summary>
    /// <param name="data">The data the symbol carries, without the check character.</param>
    /// <returns>The human readable interpretation.</returns>
    private static string BuildReadable(ReadOnlySpan<char> data)
    {
        Span<char> opening = stackalloc char[1] { ReadableDelimiter };
        Span<char> closing = stackalloc char[1] { ReadableDelimiter };
        return string.Concat(opening, data, closing);
    }
}
