// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Plessey Code symbology. It carries the hexadecimal digits 0 to 9 and A to F between a start code
/// and a termination bar with the reversed start code, which the printed line does not show.
/// <para>
/// Every symbol carries two check characters, which hold the eight bit remainder of the data divided
/// by the generator polynomial x^8 + x^7 + x^6 + x^5 + x^3 + 1. The encoder calculates them, or
/// validates the two the input ends with, and by default it prints them after the data.
/// </para>
/// </summary>
public sealed class PlesseySymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlesseySymbology"/> class.
    /// </summary>
    public PlesseySymbology()
        : this(false, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlesseySymbology"/> class.
    /// </summary>
    /// <param name="validateCheckCharacters">
    /// Whether the input ends with the two check characters, which the encoder validates, rather than
    /// with data, for which the encoder calculates them.
    /// </param>
    public PlesseySymbology(bool validateCheckCharacters)
        : this(validateCheckCharacters, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlesseySymbology"/> class.
    /// </summary>
    /// <param name="validateCheckCharacters">
    /// Whether the input ends with the two check characters, which the encoder validates, rather than
    /// with data, for which the encoder calculates them.
    /// </param>
    /// <param name="printCheckCharacters">
    /// Whether the two check characters are part of the human readable interpretation.
    /// </param>
    public PlesseySymbology(bool validateCheckCharacters, bool printCheckCharacters)
    {
        this.ValidateCheckCharacters = validateCheckCharacters;
        this.PrintCheckCharacters = printCheckCharacters;
    }

    /// <summary>
    /// Gets a value indicating whether the input ends with the two check characters, which the encoder
    /// validates, rather than with data, for which the encoder calculates them.
    /// </summary>
    public bool ValidateCheckCharacters { get; }

    /// <summary>
    /// Gets a value indicating whether the two check characters are part of the human readable
    /// interpretation.
    /// </summary>
    public bool PrintCheckCharacters { get; }

    /// <inheritdoc/>
    public override float NominalXDimension => PlesseyEncoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, PlesseyEncoder.MaximumLength, nameof(text));

        int minimum = this.ValidateCheckCharacters ? PlesseyEncoder.CheckCharacterCount + 1 : 1;
        if (text.Length < minimum)
        {
            throw new ArgumentException($"Plessey carries at least one data character; got {text.Length} characters.", nameof(text));
        }

        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (PlesseyEncoder.Value(current.Value) < 0)
            {
                throw new ArgumentException($"Plessey carries only the digits 0 to 9 and the letters A to F; got {current.ToDisplayString()}.", nameof(text));
            }
        }

        ReadOnlySpan<char> data = this.ValidateCheckCharacters ? text.AsSpan(0, text.Length - PlesseyEncoder.CheckCharacterCount) : text;
        Span<char> checks = stackalloc char[PlesseyEncoder.CheckCharacterCount];
        PlesseyEncoder.CheckCharacters(data, checks);
        if (this.ValidateCheckCharacters && !text.AsSpan(data.Length).SequenceEqual(checks))
        {
            throw new ArgumentException($"Incorrect check characters: expected {checks}, got {text.AsSpan(data.Length)}.", nameof(text));
        }

        string carried = this.ValidateCheckCharacters ? text : $"{text}{checks}";
        string readable = options.Font is null
            ? string.Empty
            : this.PrintCheckCharacters
                ? carried
                : data.ToString();

        return PlesseyEncoder.BuildSymbol(PlesseyEncoder.Encode(carried), readable, options);
    }
}
