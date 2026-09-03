// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Code 11 symbology, also called USD-8. It carries the digits 0 to 9 and the dash between a start
/// character and a stop character, which the printed line does not show.
/// <para>
/// The check characters are optional. A symbol with fewer than ten data characters carries the C check
/// character, and a symbol with ten or more carries the C and K check characters. C is the weighted sum
/// of the data modulo 11, and K is the weighted sum of the data and C modulo 11. The check characters
/// print after the data by default.
/// </para>
/// </summary>
public sealed class Code11Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Code11Symbology"/> class.
    /// </summary>
    public Code11Symbology()
        : this(CheckCharacterMode.None, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Code11Symbology"/> class.
    /// </summary>
    /// <param name="checkCharacters">
    /// Whether the symbol carries the C and K check characters, and whether the encoder calculates them
    /// or validates them.
    /// </param>
    public Code11Symbology(CheckCharacterMode checkCharacters)
        : this(checkCharacters, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Code11Symbology"/> class.
    /// </summary>
    /// <param name="checkCharacters">
    /// Whether the symbol carries the C and K check characters, and whether the encoder calculates them
    /// or validates them.
    /// </param>
    /// <param name="printCheckCharacters">
    /// Whether the check characters the symbol carries are part of the human readable interpretation.
    /// </param>
    public Code11Symbology(CheckCharacterMode checkCharacters, bool printCheckCharacters)
    {
        this.CheckCharacters = checkCharacters;
        this.PrintCheckCharacters = printCheckCharacters;
    }

    /// <summary>
    /// Gets a value that specifies whether the symbol carries the C and K check characters, and whether
    /// the encoder calculates them or validates them.
    /// </summary>
    public CheckCharacterMode CheckCharacters { get; }

    /// <summary>
    /// Gets a value indicating whether the check characters the symbol carries are part of the human
    /// readable interpretation. A symbol that carries none prints none either way.
    /// </summary>
    public bool PrintCheckCharacters { get; }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, Code11Encoder.MaximumLength, nameof(text));

        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (Code11Encoder.Value(current.Value) < 0)
            {
                throw new ArgumentException($"Code 11 carries only digits and the dash; got {current.ToDisplayString()}.", nameof(text));
            }
        }

        ReadOnlySpan<char> data = text;
        Span<char> checks = stackalloc char[2];
        int checkCount = 0;
        if (this.CheckCharacters == CheckCharacterMode.Validate)
        {
            // Nine data characters and one check character make ten, and ten data characters and two
            // check characters make twelve, so eleven characters cannot carry a valid check.
            if (text.Length == Code11Encoder.TwoCheckCharactersFrom + 1)
            {
                throw new ArgumentException($"Code 11 cannot carry {text.Length} characters with its check characters: nine data characters take one and ten take two.", nameof(text));
            }

            checkCount = text.Length <= Code11Encoder.TwoCheckCharactersFrom ? 1 : 2;
            if (text.Length <= checkCount)
            {
                throw new ArgumentException("Code 11 carries at least one data character before its check characters.", nameof(text));
            }

            data = text.AsSpan(0, text.Length - checkCount);
        }
        else if (this.CheckCharacters == CheckCharacterMode.Compute)
        {
            checkCount = data.Length >= Code11Encoder.TwoCheckCharactersFrom ? 2 : 1;
        }

        if (checkCount > 0)
        {
            int checkC = Code11Encoder.CheckC(data);
            checks[0] = Code11Encoder.Characters[checkC];
            if (checkCount == 2)
            {
                checks[1] = Code11Encoder.Characters[Code11Encoder.CheckK(data, checkC)];
            }

            if (this.CheckCharacters == CheckCharacterMode.Validate && !text.AsSpan(data.Length).SequenceEqual(checks[..checkCount]))
            {
                throw new ArgumentException($"Incorrect check characters: expected {checks[..checkCount]}, got {text.AsSpan(data.Length)}.", nameof(text));
            }
        }

        string readable = options.Font is null
            ? string.Empty
            : this.PrintCheckCharacters
                ? $"{data}{checks[..checkCount]}"
                : data.ToString();

        return Code11Encoder.BuildSymbol(Code11Encoder.Encode(data, checks[..checkCount]), readable, options);
    }
}
