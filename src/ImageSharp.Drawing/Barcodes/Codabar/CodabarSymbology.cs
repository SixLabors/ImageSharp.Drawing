// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Codabar symbology, which ANSI/AIM BC3-1995 and BS EN 798:1995 define. It carries the digits 0 to 9 and the symbols - $ : / . + between a start character
/// and a stop character, each of which is one of A, B, C and D, or one of their alternative names T, N, * and E. Section 4.2 of BS EN 798 requires at least one
/// data character, and section 4.3.2 permits A, B, C and D as start and stop characters only. The input is the start character, the data and the stop
/// character, and the printed line shows it as given.
/// <para>
/// The symbology is self-checking and Annex A.3 of BS EN 798 leaves the check character to the application. The optional check character is the modulo 16
/// character, placed before the stop character, and by default it prints there too.
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
    /// <param name="checkCharacter"> Whether the symbol carries the modulo 16 check character, and whether the encoder calculates it or validates it. </param>
    public CodabarSymbology(CheckCharacterMode checkCharacter)
        : this(checkCharacter, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodabarSymbology"/> class.
    /// </summary>
    /// <param name="checkCharacter"> Whether the symbol carries the modulo 16 check character, and whether the encoder calculates it or validates it. </param>
    /// <param name="printCheckCharacter"> Whether a check character the symbol carries is part of the human readable interpretation. </param>
    public CodabarSymbology(CheckCharacterMode checkCharacter, bool printCheckCharacter)
    {
        this.CheckCharacter = checkCharacter;
        this.PrintCheckCharacter = printCheckCharacter;
    }

    /// <summary>
    /// Gets a value that specifies whether the symbol carries the modulo 16 check character, and whether the encoder calculates it or validates it.
    /// </summary>
    public CheckCharacterMode CheckCharacter { get; }

    /// <summary>
    /// Gets a value indicating whether a check character the symbol carries is part of the human readable interpretation. A symbol that carries none prints
    /// none either way.
    /// </summary>
    public bool PrintCheckCharacter { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.MustBeLessThanOrEqualTo(text.Length, CodabarEncoder.MaximumLength, nameof(text));

        // A start character, at least one data character, the check character when the caller supplies one, and a stop character.
        int minimum = this.CheckCharacter == CheckCharacterMode.Validate ? 4 : 3;
        if (text.Length < minimum)
        {
            throw new ArgumentException(
                $"Codabar carries a start character, at least one data character and a stop character; got {text.Length} characters.", nameof(text));
        }

        // The first character, the data and the last character are checked in turn, so the message reports the first character outside its set. A surrogate
        // pair at the end fails the data check at its first half, so the message reports the whole pair.
        if (CodabarEncoder.Value(text[0]) < CodabarEncoder.FirstStartStop)
        {
            ThrowStartStop(text, 0);
        }

        int index = text[1..^1].IndexOfAnyExcept(CodabarEncoder.DataCharacterSet);
        if (index >= 0)
        {
            throw new ArgumentException(
                $"Codabar carries only digits and the symbols -$:/.+ between its start and stop characters; got {text.CodePointAt(index + 1).ToDisplayString()}.",
                nameof(text));
        }

        if (CodabarEncoder.Value(text[^1]) < CodabarEncoder.FirstStartStop)
        {
            ThrowStartStop(text, text.Length - 1);
        }

        // The symbol carries the start character and the data, then the check character, then the stop character. A supplied check character stands before the
        // stop character in the text.
        char stop = text[^1];
        ReadOnlySpan<char> head = text.Slice(0, text.Length - 1);
        char? check = null;
        if (this.CheckCharacter == CheckCharacterMode.Validate)
        {
            head = text.Slice(0, text.Length - 2);
            check = CodabarEncoder.CheckCharacter(head, stop);
            if (text[^2] != check)
            {
                throw new ArgumentException($"Incorrect check character: expected {check}, got {text[^2]}.", nameof(text));
            }
        }
        else if (this.CheckCharacter == CheckCharacterMode.Compute)
        {
            check = CodabarEncoder.CheckCharacter(head, stop);
        }

        CodabarEncoder.BuildSymbol(head, stop, check, this.PrintCheckCharacter, options, ref symbol);
    }

    /// <summary>
    /// Throws for a first or last character that is not a start or stop character.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The position of the character.</param>
    /// <exception cref="ArgumentException">Always.</exception>
    private static void ThrowStartStop(ReadOnlySpan<char> text, int index)
        => throw new ArgumentException(
            $"Codabar starts and ends with one of A, B, C and D, or T, N, * and E; got {text.CodePointAt(index).ToDisplayString()}.",
            nameof(text));
}
