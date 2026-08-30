// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// SSCC-18, a GS1-128 symbol carrying one element string: the Serial Shipping Container Code of GS1
/// Application Identifier (00). Section 3.3.1 of the GS1 General Specifications gives that identifier an
/// 18 digit SSCC, Table 7-6 gives the element string a total length of 20, and section 7.9 defines the
/// check digit.
/// <para>
/// Input is the element string syntax, <c>(00)</c> followed by the SSCC, with spaces ignored. The check
/// digit is optional: 17 digits have it computed, 18 have it verified. The human readable interpretation
/// separates a computed check digit with a space, as the printed number is grouped.
/// </para>
/// </summary>
public sealed class Sscc18Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Sscc18Symbology"/> class.
    /// </summary>
    public Sscc18Symbology()
    {
    }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        // The element string is "(00)" and 18 digits, so the longest input is 22 characters once the
        // spaces the caller may group the number with are dropped.
        Span<char> compact = stackalloc char[22];
        int length = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
            {
                continue;
            }

            if (length == compact.Length)
            {
                throw new ArgumentException("SSCC-18 takes the (00) application identifier and 17 or 18 digits.", nameof(text));
            }

            compact[length++] = text[i];
        }

        if (length is not (21 or 22))
        {
            throw new ArgumentException(
                $"SSCC-18 takes the (00) application identifier and 17 or 18 digits; got {length} characters.",
                nameof(text));
        }

        if (!compact[..4].SequenceEqual("(00)"))
        {
            throw new ArgumentException("SSCC-18 begins with the (00) application identifier.", nameof(text));
        }

        ReadOnlySpan<char> supplied = compact[4..length];
        for (int i = 0; i < supplied.Length; i++)
        {
            if (supplied[i] is < '0' or > '9')
            {
                throw new ArgumentException($"SSCC-18 carries only digits after its application identifier; got '{supplied[i]}'.", nameof(text));
            }
        }

        int check = EanUpcEncoder.ComputeCheckDigit(supplied[..17]);
        if (supplied.Length == 18 && supplied[17] - '0' != check)
        {
            throw new ArgumentException($"Incorrect SSCC-18 check digit: expected {check}, got {supplied[17]}.", nameof(text));
        }

        // The symbol carries the identifier and the eighteen digits with no separator, because Table 7-6
        // gives this element string a predefined length.
        Span<char> encoded = stackalloc char[20];
        "00".CopyTo(encoded);
        supplied[..17].CopyTo(encoded[2..]);
        encoded[19] = (char)('0' + check);

        string caption = options.Font is null
            ? string.Empty
            : supplied.Length == 18 ? text : $"{text} {(char)('0' + check)}";

        return Code128Encoder.BuildSymbol(
            Code128Encoder.Encode(new string(encoded), true, "SSCC-18"),
            caption,
            options);
    }
}
