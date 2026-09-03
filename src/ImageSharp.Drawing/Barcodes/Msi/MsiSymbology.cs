// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The MSI symbology, also called Modified Plessey. It carries the digits 0 to 9 between a start
/// character and a stop character, which the printed line does not show.
/// <para>
/// The symbology is not self-checking, and the application chooses the check digits. The modulo 10
/// calculation is the common one, and the modulo 11 calculation exists with the IBM weights 2 to 7 and
/// the NCR weights 2 to 9. Each can be followed by a second modulo 10 check digit over the data and the
/// first. A modulo 11 check value of 10 cannot be carried in one digit, and the encoder rejects data
/// whose check value is 10. The check digits print after the data by default.
/// </para>
/// </summary>
public sealed class MsiSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MsiSymbology"/> class.
    /// </summary>
    public MsiSymbology()
        : this(MsiCheckDigit.None, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MsiSymbology"/> class.
    /// </summary>
    /// <param name="checkDigit">The check digits the symbol carries after its data.</param>
    public MsiSymbology(MsiCheckDigit checkDigit)
        : this(checkDigit, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MsiSymbology"/> class.
    /// </summary>
    /// <param name="checkDigit">The check digits the symbol carries after its data.</param>
    /// <param name="printCheckDigits">
    /// Whether the check digits the symbol carries are part of the human readable interpretation.
    /// </param>
    public MsiSymbology(MsiCheckDigit checkDigit, bool printCheckDigits)
    {
        this.CheckDigit = checkDigit;
        this.PrintCheckDigits = printCheckDigits;
    }

    /// <summary>
    /// Gets the check digits the symbol carries after its data.
    /// </summary>
    public MsiCheckDigit CheckDigit { get; }

    /// <summary>
    /// Gets a value indicating whether the check digits the symbol carries are part of the human
    /// readable interpretation. A symbol that carries none prints none either way.
    /// </summary>
    public bool PrintCheckDigits { get; }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MsiEncoder.MaximumLength, nameof(text));

        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException($"MSI carries only digits; got {current.ToDisplayString()}.", nameof(text));
            }
        }

        Span<char> buffer = stackalloc char[MsiEncoder.StackBufferLength];
        ValueStringBuilder carried = new(buffer);
        carried.Append(text);
        switch (this.CheckDigit)
        {
            case MsiCheckDigit.Modulo10:
                carried.Append((char)('0' + MsiEncoder.Modulo10(text)));
                break;

            case MsiCheckDigit.Modulo1010:
                carried.Append((char)('0' + MsiEncoder.Modulo10(text)));
                carried.Append((char)('0' + MsiEncoder.Modulo10(carried.AsSpan())));
                break;

            case MsiCheckDigit.Modulo11:
            case MsiCheckDigit.Modulo1110:
            case MsiCheckDigit.NcrModulo11:
            case MsiCheckDigit.NcrModulo1110:
                int maximumWeight = this.CheckDigit is MsiCheckDigit.Modulo11 or MsiCheckDigit.Modulo1110
                    ? MsiEncoder.IbmMaximumWeight
                    : MsiEncoder.NcrMaximumWeight;
                int check = MsiEncoder.Modulo11(text, maximumWeight);
                if (check == 10)
                {
                    throw new ArgumentException("The modulo 11 check value of this data is 10, which one digit cannot carry.", nameof(text));
                }

                carried.Append((char)('0' + check));
                if (this.CheckDigit is MsiCheckDigit.Modulo1110 or MsiCheckDigit.NcrModulo1110)
                {
                    carried.Append((char)('0' + MsiEncoder.Modulo10(carried.AsSpan())));
                }

                break;
        }

        string readable = options.Font is null
            ? string.Empty
            : this.PrintCheckDigits
                ? carried.AsSpan().ToString()
                : text;

        LinearBarcodeSymbol symbol = MsiEncoder.BuildSymbol(MsiEncoder.Encode(carried.AsSpan()), readable, options);
        carried.Dispose();
        return symbol;
    }
}
