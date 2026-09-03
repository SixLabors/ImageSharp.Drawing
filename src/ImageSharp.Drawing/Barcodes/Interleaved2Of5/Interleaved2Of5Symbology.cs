// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Interleaved 2 of 5 symbology, which AIM USS-I 2/5 defines and ISO/IEC 16390 succeeded. It carries
/// the digits 0 to 9. Section 2.1 gives every character "two wide elements and three narrow elements",
/// and section 2.2.3 encodes "the more significant digit in the bars and the less significant digit in
/// the spaces", so a symbol carries an even number of digits. A symbol carries a start pattern, the
/// digit pairs and a stop pattern.
/// <para>
/// Section 2.2.1 requires a leading zero for an odd number of digits: "then a leading zero must be
/// added to produce an even number of digits". Section 2.5 makes the check digit optional, and Appendix
/// C recommends the modulo 10 calculation with alternate weights of 1 and 3 that section 7.9 of the GS1
/// General Specifications also uses. Appendix D prints "all numeric characters in the code including
/// leading zeroes", so the printed line shows the leading zero and, by default, the check digit.
/// </para>
/// </summary>
public sealed class Interleaved2Of5Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Interleaved2Of5Symbology"/> class.
    /// </summary>
    public Interleaved2Of5Symbology()
        : this(CheckCharacterMode.None, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Interleaved2Of5Symbology"/> class.
    /// </summary>
    /// <param name="checkDigit">
    /// Whether the symbol carries the modulo 10 check digit, and whether the encoder calculates it or
    /// validates it.
    /// </param>
    public Interleaved2Of5Symbology(CheckCharacterMode checkDigit)
        : this(checkDigit, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Interleaved2Of5Symbology"/> class.
    /// </summary>
    /// <param name="checkDigit">
    /// Whether the symbol carries the modulo 10 check digit, and whether the encoder calculates it or
    /// validates it.
    /// </param>
    /// <param name="printCheckDigit">
    /// Whether a check digit the symbol carries is part of the human readable interpretation.
    /// </param>
    public Interleaved2Of5Symbology(CheckCharacterMode checkDigit, bool printCheckDigit)
    {
        this.CheckDigit = checkDigit;
        this.PrintCheckDigit = printCheckDigit;
    }

    /// <summary>
    /// Gets a value that specifies whether the symbol carries the modulo 10 check digit, and whether
    /// the encoder calculates it or validates it.
    /// </summary>
    public CheckCharacterMode CheckDigit { get; }

    /// <summary>
    /// Gets a value indicating whether a check digit the symbol carries is part of the human readable
    /// interpretation. A symbol that carries none prints none either way.
    /// </summary>
    public bool PrintCheckDigit { get; }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, Interleaved2Of5Encoder.MaximumLength, nameof(text));

        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException($"Interleaved 2 of 5 carries only digits; got {current.ToDisplayString()}.", nameof(text));
            }
        }

        ReadOnlySpan<char> data = this.CheckDigit == CheckCharacterMode.Validate ? text.AsSpan(0, text.Length - 1) : text;
        int? check = this.CheckDigit == CheckCharacterMode.None ? null : EanUpcEncoder.ComputeCheckDigit(data);
        if (this.CheckDigit == CheckCharacterMode.Validate && text[^1] - '0' != check)
        {
            throw new ArgumentException($"Incorrect check digit: expected {check}, got {text[^1]}.", nameof(text));
        }

        // The digits the symbol carries: a leading zero when the count would otherwise be odd, the data
        // and the check digit. Section 2.2.1 of AIM USS-I 2/5 requires the even count, and Appendix C
        // notes that "a leading zero will be required if an even number of data characters are to be
        // appended with one check character".
        int carried = data.Length + (check is null ? 0 : 1);
        Span<char> digitBuffer = stackalloc char[Interleaved2Of5Encoder.StackBufferLength];
        ValueStringBuilder digits = new(digitBuffer);
        if ((carried & 1) == 1)
        {
            digits.Append('0');
        }

        digits.Append(data);
        if (check is not null)
        {
            digits.Append((char)('0' + check.Value));
        }

        ReadOnlySpan<char> encoded = digits.AsSpan();
        string readable = options.Font is null
            ? string.Empty
            : (check is null || this.PrintCheckDigit ? encoded : encoded[..^1]).ToString();

        LinearBarcodeSymbol symbol = Interleaved2Of5Encoder.BuildSymbol(Interleaved2Of5Encoder.Encode(encoded), readable, options);
        digits.Dispose();
        return symbol;
    }
}
