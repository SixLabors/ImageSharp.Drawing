// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The BC412 symbology of SEMI T1-95. It carries 7 to 18 characters from the digits and the capital
/// letters without O, for which the digit 0 stands. Every symbol carries the modulo 35 check character
/// after its first data character, and the printed line shows the data with the check character in that
/// place. The start and stop characters are not shown.
/// </summary>
public sealed class Bc412Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Bc412Symbology"/> class.
    /// </summary>
    public Bc412Symbology()
        : this(false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Bc412Symbology"/> class.
    /// </summary>
    /// <param name="validateCheckCharacter">
    /// Whether the second character of the input is the check character, which the encoder validates,
    /// rather than data, for which the encoder calculates it.
    /// </param>
    public Bc412Symbology(bool validateCheckCharacter)
        => this.ValidateCheckCharacter = validateCheckCharacter;

    /// <summary>
    /// Gets a value indicating whether the second character of the input is the check character, which
    /// the encoder validates, rather than data, for which the encoder calculates it.
    /// </summary>
    public bool ValidateCheckCharacter { get; }

    /// <inheritdoc/>
    public override float NominalXDimension => Bc412Encoder.NominalXDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        int supplied = this.ValidateCheckCharacter ? 1 : 0;
        int dataLength = text.Length - supplied;
        if (dataLength < Bc412Encoder.MinimumLength || dataLength > Bc412Encoder.MaximumLength)
        {
            throw new ArgumentException($"BC412 carries {Bc412Encoder.MinimumLength} to {Bc412Encoder.MaximumLength} data characters; got {dataLength}.", nameof(text));
        }

        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (Bc412Encoder.Value(current.Value) < 0)
            {
                throw new ArgumentException($"BC412 carries only digits and capital letters other than O; got {current.ToDisplayString()}.", nameof(text));
            }
        }

        // The data without the check character, then the characters the symbol carries: the first
        // data character, the check character and the remaining data.
        Span<char> data = stackalloc char[Bc412Encoder.MaximumLength];
        data[0] = text[0];
        text.AsSpan(1 + supplied).CopyTo(data[1..]);
        data = data[..dataLength];

        char check = Bc412Encoder.CheckCharacter(data);
        if (this.ValidateCheckCharacter && text[Bc412Encoder.CheckPosition] != check)
        {
            throw new ArgumentException($"Incorrect check character: expected {check}, got {text[Bc412Encoder.CheckPosition]}.", nameof(text));
        }

        Span<char> carried = stackalloc char[Bc412Encoder.MaximumLength + 1];
        carried[0] = data[0];
        carried[Bc412Encoder.CheckPosition] = check;
        data[1..].CopyTo(carried[(Bc412Encoder.CheckPosition + 1)..]);
        carried = carried[..(dataLength + 1)];

        string readable = options.Font is null ? string.Empty : carried.ToString();
        return Bc412Encoder.BuildSymbol(Bc412Encoder.Encode(carried), readable, options);
    }
}
