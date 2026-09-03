// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Codabar symbology, which ANSI/AIM BC3-1995 and BS EN 798:1995 define. It carries the digits 0 to
/// 9 and the symbols - $ : / . + between a start character and a stop character, each of which is one of
/// A, B, C and D, or one of their alternative names T, N, * and E. Section 4.2 of BS EN 798 requires at
/// least one data character, and section 4.3.2 permits A, B, C and D as start and stop characters only.
/// The input is the start character, the data and the stop character, and the printed line shows it as
/// given.
/// <para>
/// The symbology is self-checking and Annex A.3 of BS EN 798 leaves the check character to the
/// application. The optional check character is the modulo 16 character, placed before the stop
/// character, and by default it prints there too.
/// </para>
/// </summary>
public sealed class CodabarSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodabarSymbology"/> class.
    /// </summary>
    public CodabarSymbology()
        : this(CheckCharacterMode.None, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodabarSymbology"/> class.
    /// </summary>
    /// <param name="checkCharacter">
    /// Whether the symbol carries the modulo 16 check character, and whether the encoder calculates it
    /// or validates it.
    /// </param>
    public CodabarSymbology(CheckCharacterMode checkCharacter)
        : this(checkCharacter, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodabarSymbology"/> class.
    /// </summary>
    /// <param name="checkCharacter">
    /// Whether the symbol carries the modulo 16 check character, and whether the encoder calculates it
    /// or validates it.
    /// </param>
    /// <param name="printCheckCharacter">
    /// Whether a check character the symbol carries is part of the human readable interpretation.
    /// </param>
    public CodabarSymbology(CheckCharacterMode checkCharacter, bool printCheckCharacter)
    {
        this.CheckCharacter = checkCharacter;
        this.PrintCheckCharacter = printCheckCharacter;
    }

    /// <summary>
    /// Gets a value that specifies whether the symbol carries the modulo 16 check character, and whether
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
        Guard.MustBeLessThanOrEqualTo(text.Length, CodabarEncoder.MaximumLength, nameof(text));

        // A start character, at least one data character, the check character when the caller supplies
        // one, and a stop character.
        int minimum = this.CheckCharacter == CheckCharacterMode.Validate ? 4 : 3;
        if (text.Length < minimum)
        {
            throw new ArgumentException($"Codabar carries a start character, at least one data character and a stop character; got {text.Length} characters.", nameof(text));
        }

        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        int index = 0;
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            int value = CodabarEncoder.Value(current.Value);
            bool startStop = index == 0 || index == text.Length - 1;
            if (value < 0 || (value >= CodabarEncoder.FirstStartStop) != startStop)
            {
                throw new ArgumentException(
                    startStop
                        ? $"Codabar starts and ends with one of A, B, C and D, or T, N, * and E; got {current.ToDisplayString()}."
                        : $"Codabar carries only digits and the symbols -$:/.+ between its start and stop characters; got {current.ToDisplayString()}.",
                    nameof(text));
            }

            index += current.Utf16SequenceLength;
        }

        string body = text;
        char? check = null;
        if (this.CheckCharacter == CheckCharacterMode.Validate)
        {
            body = string.Concat(text.AsSpan(0, text.Length - 2), text.AsSpan(text.Length - 1));
            check = CodabarEncoder.CheckCharacter(body);
            if (text[^2] != check)
            {
                throw new ArgumentException($"Incorrect check character: expected {check}, got {text[^2]}.", nameof(text));
            }
        }
        else if (this.CheckCharacter == CheckCharacterMode.Compute)
        {
            check = CodabarEncoder.CheckCharacter(body);
        }

        string readable = options.Font is null
            ? string.Empty
            : check is null || !this.PrintCheckCharacter
                ? body
                : $"{body.AsSpan(0, body.Length - 1)}{check.Value}{body.AsSpan(body.Length - 1)}";

        return CodabarEncoder.BuildSymbol(CodabarEncoder.Encode(body, check), readable, options);
    }
}
