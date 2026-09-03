// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The IATA 2 of 5 symbology, also called Computer Identics 2 of 5. No published standard defines it.
/// It carries the digits 0 to 9 in the five bar patterns of Industrial 2 of 5, and the spaces carry no
/// data. A symbol carries the start pattern of Interleaved 2 of 5, the digits, an optional check digit
/// and the stop pattern of Interleaved 2 of 5.
/// <para>
/// The check digit is the modulo 10 calculation of Appendix C of AIM USS-I 2/5, which both reference
/// implementations apply. The printed line shows the digits the symbol carries and, by default, the
/// check digit.
/// </para>
/// </summary>
public sealed class Iata2Of5Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Iata2Of5Symbology"/> class.
    /// </summary>
    public Iata2Of5Symbology()
        : this(CheckCharacterMode.None, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Iata2Of5Symbology"/> class.
    /// </summary>
    /// <param name="checkDigit">
    /// Whether the symbol carries the modulo 10 check digit, and whether the encoder calculates it or
    /// validates it.
    /// </param>
    public Iata2Of5Symbology(CheckCharacterMode checkDigit)
        : this(checkDigit, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Iata2Of5Symbology"/> class.
    /// </summary>
    /// <param name="checkDigit">
    /// Whether the symbol carries the modulo 10 check digit, and whether the encoder calculates it or
    /// validates it.
    /// </param>
    /// <param name="printCheckDigit">
    /// Whether a check digit the symbol carries is part of the human readable interpretation.
    /// </param>
    public Iata2Of5Symbology(CheckCharacterMode checkDigit, bool printCheckDigit)
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
        => Code2Of5Encoder.Encode(text, options, Code2Of5Variant.Iata, this.CheckDigit, this.PrintCheckDigit);
}
