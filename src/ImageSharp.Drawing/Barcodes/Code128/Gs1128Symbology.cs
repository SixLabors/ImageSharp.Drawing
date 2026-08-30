// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The GS1-128 symbology of section 5.4 of the GS1 General Specifications. A GS1-128 symbol is a Code 128
/// symbol whose start character is followed by a Function 1 character, carrying one or more element
/// strings: a GS1 Application Identifier and the data that belongs to it.
/// <para>
/// Input is the element string syntax the standard prints, an Application Identifier in parentheses
/// followed by its data, repeated: <c>(01)09521234543213(3103)000123</c>. Parentheses are not encoded;
/// section 4.14 rule 2c requires them in the human readable interpretation and rule 2b keeps the
/// separators out of it.
/// </para>
/// </summary>
public sealed class Gs1128Symbology : BarcodeSymbology
{
    /// <summary>
    /// The largest number of data characters section 5.4.1 allows in one symbol.
    /// </summary>
    private const int MaximumDataCharacters = 48;

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1128Symbology"/> class.
    /// </summary>
    public Gs1128Symbology()
    {
    }

    /// <summary>
    /// Gets the total length of an element string whose Application Identifier starts with the given two
    /// digits, or zero when the length is not predefined. Table 7-6 of the GS1 General Specifications
    /// lists these and states that it "is limited to the listed numbers and will remain unchanged", so an
    /// Application Identifier outside the list carries a variable length field.
    /// </summary>
    /// <param name="first">The first digit of the Application Identifier.</param>
    /// <param name="second">The second digit of the Application Identifier.</param>
    /// <returns>The total length in characters, including the Application Identifier, or zero.</returns>
    private static int PredefinedLength(char first, char second) => (first, second) switch
    {
        ('0', '0') => 20,
        ('0', '1') or ('0', '2') or ('0', '3') => 16,
        ('0', '4') => 18,
        ('1', >= '1' and <= '9') => 8,
        ('2', '0') => 4,
        ('3', >= '1' and <= '6') => 10,
        ('4', '1') => 16,
        _ => 0,
    };

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        // Section 5.4.1 caps the data at 48 characters, so the encoded data never outgrows one buffer. The
        // human readable interpretation is the input itself: every character this loop consumes is a
        // parenthesis it drops, an Application Identifier or data, and it re-emits all three in order.
        Span<char> encoded = stackalloc char[MaximumDataCharacters];
        int written = 0;
        int position = 0;
        while (position < text.Length)
        {
            if (text[position] != '(')
            {
                throw new ArgumentException(
                    $"GS1-128 expects an Application Identifier in parentheses at position {position}.",
                    nameof(text));
            }

            int close = text.IndexOf(')', position);
            if (close < 0)
            {
                throw new ArgumentException("A GS1-128 Application Identifier is missing its closing parenthesis.", nameof(text));
            }

            ReadOnlySpan<char> identifier = text.AsSpan(position + 1, close - position - 1);
            if (identifier.Length is < 2 or > 4)
            {
                throw new ArgumentException(
                    $"A GS1 Application Identifier is two to four digits; got '{identifier}'.",
                    nameof(text));
            }

            for (int i = 0; i < identifier.Length; i++)
            {
                if (!char.IsAsciiDigit(identifier[i]))
                {
                    throw new ArgumentException(
                        $"A GS1 Application Identifier is all digits; got '{identifier}'.",
                        nameof(text));
                }
            }

            int dataStart = close + 1;
            int dataEnd = text.IndexOf('(', dataStart);
            if (dataEnd < 0)
            {
                dataEnd = text.Length;
            }

            ReadOnlySpan<char> data = text.AsSpan(dataStart, dataEnd - dataStart);
            if (data.Length == 0)
            {
                throw new ArgumentException($"The GS1 Application Identifier ({identifier}) carries no data.", nameof(text));
            }

            int predefined = PredefinedLength(identifier[0], identifier[1]);
            if (predefined > 0 && identifier.Length + data.Length != predefined)
            {
                throw new ArgumentException(
                    $"Table 7-6 gives the element string ({identifier}) a total length of {predefined}; got {identifier.Length + data.Length}.",
                    nameof(text));
            }

            position = dataEnd;
            int separator = predefined == 0 && position < text.Length ? 1 : 0;

            // Section 5.4.1 allows 48 data characters in a GS1-128 symbol.
            Guard.MustBeLessThanOrEqualTo(written + identifier.Length + data.Length + separator, MaximumDataCharacters, nameof(text));

            identifier.CopyTo(encoded[written..]);
            written += identifier.Length;
            data.CopyTo(encoded[written..]);
            written += data.Length;

            // Section 7.8.6.2: an element string whose first two digits are outside Table 7-6 is
            // terminated by a separator, unless it is the last one in the symbol.
            if (separator == 1)
            {
                encoded[written++] = Code128Encoder.Separator;
            }
        }

        if (written == 0)
        {
            throw new ArgumentException("GS1-128 requires at least one element string.", nameof(text));
        }

        return Code128Encoder.BuildSymbol(
            Code128Encoder.Encode(encoded[..written], true),
            text,
            options);
    }
}
