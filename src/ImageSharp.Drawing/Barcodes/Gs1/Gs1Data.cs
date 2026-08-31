// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The data layer the GS1 symbologies share. A GS1 symbol carries one or more element strings, each an
/// Application Identifier and the data that belongs to it, and the same parsing and separator rules apply
/// whatever symbology draws it.
/// </summary>
internal static class Gs1Data
{
    /// <summary>
    /// The character an element string of variable length is terminated by. Section 7.8.6.2 of the GS1
    /// General Specifications: the separator "is always represented in the transmitted message by the
    /// control character &lt;GS&gt; (ASCII value 29 (decimal), 1D (hexadecimal))".
    /// </summary>
    public const char Separator = (char)29;

    /// <summary>
    /// The number of characters a caller stack allocates for <see cref="Prepare"/> to build in. Section
    /// 5.4.1 caps a GS1-128 symbol at 48 data characters, so every such symbol builds on the stack.
    /// </summary>
    public const int StackBufferLength = 64;

    /// <summary>
    /// Parses the element string syntax the standard prints, an Application Identifier in parentheses
    /// followed by its data, repeated, and writes the data a symbol carries. The symbol does not carry the parentheses.
    /// section 4.14 rule 2c requires them in the human readable interpretation alone.
    /// </summary>
    /// <param name="text">The element strings to parse.</param>
    /// <param name="encoded">
    /// The builder the encoded data is written to: each Application Identifier and its data in turn, with
    /// a <see cref="Separator"/> after every variable length element string but the last.
    /// </param>
    /// <exception cref="ArgumentException">The text is not valid element string syntax.</exception>
    public static void Prepare(ReadOnlySpan<char> text, ref ValueStringBuilder encoded)
    {
        int position = 0;
        while (position < text.Length)
        {
            if (text[position] != '(')
            {
                SpanCodePointEnumerator opening = text[position..].EnumerateCodePoints();
                opening.MoveNext();
                throw new ArgumentException(
                    $"An element string opens with an Application Identifier in parentheses; got {opening.Current.ToDisplayString()} at position {position}.",
                    nameof(text));
            }

            int close = text[position..].IndexOf(')');
            if (close < 0)
            {
                throw new ArgumentException("An Application Identifier is missing its closing parenthesis.", nameof(text));
            }

            close += position;
            ReadOnlySpan<char> identifier = text[(position + 1)..close];
            if (identifier.Length is < 2 or > 4)
            {
                throw new ArgumentException($"An Application Identifier is two to four digits; got '{identifier}'.", nameof(text));
            }

            for (int i = 0; i < identifier.Length; i++)
            {
                if (!char.IsAsciiDigit(identifier[i]))
                {
                    throw new ArgumentException($"An Application Identifier is all digits; got '{identifier}'.", nameof(text));
                }
            }

            int dataStart = close + 1;
            int dataEnd = text[dataStart..].IndexOf('(');
            dataEnd = dataEnd < 0 ? text.Length : dataEnd + dataStart;

            ReadOnlySpan<char> data = text[dataStart..dataEnd];
            if (data.Length == 0)
            {
                throw new ArgumentException($"The Application Identifier ({identifier}) carries no data.", nameof(text));
            }

            int predefined = PredefinedLength(identifier[0], identifier[1]);
            if (predefined > 0 && identifier.Length + data.Length != predefined)
            {
                throw new ArgumentException(
                    $"Table 7-6 gives the element string ({identifier}) a total length of {predefined}; got {identifier.Length + data.Length}.",
                    nameof(text));
            }

            encoded.Append(identifier);
            encoded.Append(data);

            // Section 7.8.6.2: an element string whose first two digits are outside Table 7-6 is
            // terminated by a separator, unless it is the last one in the symbol.
            position = dataEnd;
            if (predefined == 0 && position < text.Length)
            {
                encoded.Append(Separator);
            }
        }

        if (encoded.Length == 0)
        {
            throw new ArgumentException("A GS1 symbol requires at least one element string.", nameof(text));
        }
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
}
