// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
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
    /// The number of characters a caller holds inline for <see cref="Prepare"/> to build in. Section 5.4.1
    /// caps a GS1-128 symbol at 48 data characters, so every such symbol builds inline.
    /// </summary>
    public const int InlineBufferLength = 64;

    /// <summary>
    /// The number of element strings <see cref="Prepare"/> holds inline. Data with more rents its element
    /// buffer once.
    /// </summary>
    public const int InlineElementCount = 16;

    /// <summary>
    /// The smallest height in millimetres of the font of the human readable interpretation. Section 4.14
    /// rule 2a.iii of the GS1 General Specifications: "The font size SHOULD be at least 2 mm (0.08 in) in
    /// height".
    /// </summary>
    public const float MinimumTextHeight = 2F;

    /// <summary>
    /// Returns the factor that lifts a font to <see cref="MinimumTextHeight"/>, or 1 for a font that is that
    /// high or higher. A point is 1/72 inch, so the em of a font is its size times 25.4 / 72 millimetres
    /// high at any resolution.
    /// </summary>
    /// <param name="font">The font of the human readable interpretation.</param>
    /// <returns>The factor applied to the size of the font.</returns>
    public static float TextScale(Font font) => MathF.Max(1F, MinimumTextHeight * 72F / 25.4F / font.Size);

    /// <summary>
    /// Returns the number of characters <see cref="Prepare"/> writes at most for the text, after it checks
    /// that the text can prepare into no more than the given number of characters. The prepared data drops
    /// the two parentheses of each element string and adds at most one separator between two of them, so
    /// it is never longer than the text. An element string has at least three prepared characters, a two
    /// digit Application Identifier and one data character, so text that prepares into P characters is at
    /// most P plus two parentheses for each third of P.
    /// </summary>
    /// <param name="text">The element strings.</param>
    /// <param name="maximumPrepared">The largest prepared length the symbology carries.</param>
    /// <returns>The capacity the prepared data needs, the length of the text.</returns>
    /// <exception cref="ArgumentException">The text is longer than any text that prepares into <paramref name="maximumPrepared"/> characters.</exception>
    public static int PreparedCapacity(ReadOnlySpan<char> text, int maximumPrepared)
    {
        int maximumText = maximumPrepared + (2 * (maximumPrepared / 3));
        if (text.Length > maximumText)
        {
            throw new ArgumentException($"The element strings are {text.Length} characters; the symbology carries at most {maximumText}.", nameof(text));
        }

        return text.Length;
    }

    /// <summary>
    /// Parses the element string syntax the standard prints, an Application Identifier in parentheses
    /// followed by its data, repeated, validates each element string and the pairs they form, and writes
    /// the data a symbol carries. The symbol does not carry the parentheses; section 4.14 rule 2c requires
    /// them in the human readable interpretation alone.
    /// </summary>
    /// <param name="text">The element strings to parse.</param>
    /// <param name="encoded">
    /// The builder the encoded data is written to: each Application Identifier and its data in turn, with
    /// a <see cref="Separator"/> after every variable length element string but the last.
    /// </param>
    /// <exception cref="ArgumentException">The text is not valid element string syntax, or an element string or a pair of them breaks a rule of the GS1 General Specifications.</exception>
    public static void Prepare(ReadOnlySpan<char> text, ref ValueStringBuilder encoded)
    {
        // An element string is at least five characters of text, the two parentheses, a two digit
        // Application Identifier and one data character, so the text holds at most a fifth of its length in
        // element strings, and the element buffer never grows.
        InlineArray16<Gs1Element> elementStorage = default;
        int elementCapacity = (text.Length / 5) + 1;
        ValueListBuilder<Gs1Element> elements = elementCapacity <= InlineElementCount ? new(elementStorage) : new(elementCapacity);
        try
        {
            Parse(text, ref elements);
            Validate(text, elements.AsSpan());

            // Section 7.8.6.2: an element string without a predefined length is terminated by a separator,
            // unless it is the last one in the symbol. The dictionary marks the predefined lengths of
            // Table 7-6.
            ReadOnlySpan<Gs1Element> parsed = elements.AsSpan();
            for (int i = 0; i < parsed.Length; i++)
            {
                Gs1Element element = parsed[i];
                encoded.Append(element.Identifier(text));
                encoded.Append(element.Data(text));
                if (i < parsed.Length - 1 && !element.Entry.PredefinedLength)
                {
                    encoded.Append(Separator);
                }
            }
        }
        finally
        {
            elements.Dispose();
        }
    }

    /// <summary>
    /// Validates element strings: each one against its entry of the GS1 Barcode Syntax Dictionary, and then
    /// the rules that relate them, sections 4.13.1 and 4.13.2 of the GS1 General Specifications.
    /// </summary>
    /// <param name="buffer">The buffer the element strings are in.</param>
    /// <param name="elements">The element strings.</param>
    /// <exception cref="ArgumentException">An element string or a pair of them breaks a rule.</exception>
    public static void Validate(ReadOnlySpan<char> buffer, ReadOnlySpan<Gs1Element> elements)
    {
        foreach (Gs1Element element in elements)
        {
            ValidateData(element.Entry, element.Identifier(buffer), element.Data(buffer));
        }

        for (int i = 0; i < elements.Length; i++)
        {
            ReadOnlySpan<char> identifier = elements[i].Identifier(buffer);
            Gs1ApplicationIdentifier entry = elements[i].Entry;

            // Data from several carriers on one item may repeat an element string, but never with a
            // different value.
            for (int j = i + 1; j < elements.Length; j++)
            {
                if (elements[j].Identifier(buffer).SequenceEqual(identifier) && !elements[j].Data(buffer).SequenceEqual(elements[i].Data(buffer)))
                {
                    throw new ArgumentException($"The Application Identifier ({identifier}) appears twice with different data.", nameof(elements));
                }
            }

            // Section 4.13.1: an invalid pair. An Application Identifier is never excluded by itself, even
            // when its own pattern matches it.
            foreach (string pattern in entry.Exclusions)
            {
                int match = FindPattern(buffer, elements, pattern, identifier);
                if (match >= 0)
                {
                    throw new ArgumentException(
                        $"The Application Identifiers ({identifier}) and ({elements[match].Identifier(buffer)}) may not appear together.",
                        nameof(elements));
                }
            }

            // Section 4.13.2: a mandatory association. Each rule holds when every pattern of one of its
            // groups matches another element string.
            foreach (string[][] rule in entry.Requisites)
            {
                bool satisfied = false;
                foreach (string[] group in rule)
                {
                    bool all = true;
                    foreach (string pattern in group)
                    {
                        all &= FindPattern(buffer, elements, pattern, identifier) >= 0;
                    }

                    if (all)
                    {
                        satisfied = true;
                        break;
                    }
                }

                if (!satisfied)
                {
                    string[] alternatives = new string[rule.Length];
                    for (int g = 0; g < rule.Length; g++)
                    {
                        alternatives[g] = "(" + string.Join(") and (", rule[g]) + ")";
                    }

                    throw new ArgumentException(
                        $"The Application Identifier ({identifier}) requires {string.Join(" or ", alternatives)} in the same data.",
                        nameof(elements));
                }
            }
        }

        // A digital signature, Application Identifier (8030), signs a serialised key, so a GDTI (253), GCN
        // (255) or GRAI (8003) with it carries its optional serial component.
        if (FindPattern(buffer, elements, "8030", []) >= 0)
        {
            foreach (string key in (ReadOnlySpan<string>)["253", "255", "8003"])
            {
                int match = FindPattern(buffer, elements, key, []);
                if (match >= 0 && elements[match].DataLength == elements[match].Entry.MinimumLength)
                {
                    throw new ArgumentException(
                        $"The Application Identifier ({key}) carries no serial component, which a digital signature (8030) requires.",
                        nameof(elements));
                }
            }
        }
    }

    /// <summary>
    /// Returns the number of characters <see cref="WriteReadable(ReadOnlySpan{char}, bool, Span{char})"/>
    /// writes at most for element strings that <see cref="Prepare"/> accepted. The interpretation is the
    /// text with a line break between two element strings, and an element string is at least five
    /// characters of text, so the text holds at most a fifth of its length in element strings.
    /// </summary>
    /// <param name="textLength">The length of the element strings.</param>
    /// <returns>The capacity the interpretation needs.</returns>
    public static int ReadableCapacity(int textLength) => textLength + (textLength / 5);

    /// <summary>
    /// Writes the human readable interpretation of element strings that <see cref="Prepare"/> accepted, each
    /// element string as <see cref="WriteReadable(ReadOnlySpan{char}, ReadOnlySpan{char}, bool, Span{char}, int)"/>
    /// writes it, in the order the symbol carries them.
    /// </summary>
    /// <param name="text">The element strings, in the element string syntax.</param>
    /// <param name="lineBreaks">Whether each element string prints on a line of its own.</param>
    /// <param name="readable">
    /// The buffer the interpretation is written to: <see cref="ReadableCapacity"/> long with line breaks, or
    /// as long as the text without, since the interpretation on one line is never longer than the text.
    /// </param>
    /// <returns>The number of characters written.</returns>
    public static int WriteReadable(ReadOnlySpan<char> text, bool lineBreaks, Span<char> readable)
    {
        // The parse took each element string as an Application Identifier in parentheses and the data up to
        // the next opening parenthesis, so the same cuts find the element strings again.
        int written = 0;
        while (text.Length > 0)
        {
            int close = text.IndexOf(')');
            ReadOnlySpan<char> rest = text[(close + 1)..];
            int next = rest.IndexOf('(');
            int dataLength = next < 0 ? rest.Length : next;
            written = WriteReadable(text[1..close], rest[..dataLength], lineBreaks, readable, written);
            text = rest[dataLength..];
        }

        return written;
    }

    /// <summary>
    /// Writes one element string of a human readable interpretation. Section 4.14 rule 2c of the GS1 General
    /// Specifications puts the Application Identifier in parentheses before its data. A linear symbol
    /// prints the element strings on one line, as Figure 4-7 shows for GS1-128. A two-dimensional symbol
    /// prints each element string on a line of its own, the preferred format of Figure 4-6, so a line break
    /// comes before every element string but the first, and rule 1e keeps an element string on one line.
    /// Rule 3b keeps the URL of Application Identifier (8200) out of the interpretation, so that element
    /// string writes nothing.
    /// </summary>
    /// <param name="identifier">The Application Identifier.</param>
    /// <param name="data">The data.</param>
    /// <param name="lineBreaks">Whether each element string prints on a line of its own.</param>
    /// <param name="readable">The buffer the interpretation is written to.</param>
    /// <param name="written">The number of characters of the interpretation written before this element string.</param>
    /// <returns>The number of characters written with this element string.</returns>
    public static int WriteReadable(ReadOnlySpan<char> identifier, ReadOnlySpan<char> data, bool lineBreaks, Span<char> readable, int written)
    {
        if (identifier.SequenceEqual("8200"))
        {
            return written;
        }

        if (lineBreaks && written > 0)
        {
            readable[written++] = BarcodeTextPlacement.LineBreak;
        }

        readable[written++] = '(';
        identifier.CopyTo(readable[written..]);
        written += identifier.Length;
        readable[written++] = ')';
        data.CopyTo(readable[written..]);
        return written + data.Length;
    }

    /// <summary>
    /// Validates the data of one element string against its entry of the GS1 Barcode Syntax Dictionary:
    /// its length, and each component in turn with its character set and its checks.
    /// </summary>
    /// <param name="entry">The dictionary entry of the Application Identifier.</param>
    /// <param name="identifier">The Application Identifier.</param>
    /// <param name="data">The data.</param>
    /// <exception cref="ArgumentException">The data breaks its format.</exception>
    private static void ValidateData(Gs1ApplicationIdentifier entry, ReadOnlySpan<char> identifier, ReadOnlySpan<char> data)
    {
        if (data.Length < entry.MinimumLength || data.Length > entry.MaximumLength)
        {
            string range = entry.MinimumLength == entry.MaximumLength ? $"{entry.MinimumLength}" : $"{entry.MinimumLength} to {entry.MaximumLength}";
            throw new ArgumentException($"The data of Application Identifier ({identifier}) is {range} characters; got {data.Length}.", nameof(data));
        }

        // Each component takes as many characters as remain, up to its maximum. Only the last component may
        // vary in length, and an optional component that finds no data left is skipped.
        foreach (Gs1Component component in entry.Components)
        {
            int length = Math.Min(component.Maximum, data.Length);
            if (length == 0 && component.Optional)
            {
                continue;
            }

            if (length < component.Minimum)
            {
                throw new ArgumentException($"The data of Application Identifier ({identifier}) is too short for its components.", nameof(data));
            }

            ReadOnlySpan<char> part = data[..length];
            Gs1Linters.CheckCharacterSet(component.CharacterSet, identifier, part);
            foreach (Gs1Linter linter in component.Linters)
            {
                Gs1Linters.Check(linter, identifier, part);
            }

            data = data[length..];
        }
    }

    /// <summary>
    /// Parses the element string syntax into the positions of its element strings.
    /// </summary>
    /// <param name="text">The element strings to parse.</param>
    /// <param name="elements">The builder the positions are written to.</param>
    /// <exception cref="ArgumentException">The text is not valid element string syntax.</exception>
    private static void Parse(ReadOnlySpan<char> text, ref ValueListBuilder<Gs1Element> elements)
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

            SpanCodePointEnumerator codePoints = identifier.EnumerateCodePoints();
            while (codePoints.MoveNext())
            {
                CodePoint current = codePoints.Current;
                if (!current.IsAsciiDigit())
                {
                    throw new ArgumentException($"An Application Identifier is all digits; got {current.ToDisplayString()}.", nameof(text));
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

            // The entry is found once here, and every later rule reads it from the element.
            int entryIndex = Gs1SyntaxDictionary.IndexOf(identifier);
            if (entryIndex < 0)
            {
                throw new ArgumentException($"({identifier}) is not an Application Identifier of the GS1 General Specifications.", nameof(text));
            }

            elements.Append(new Gs1Element(position + 1, identifier.Length, dataStart, data.Length, entryIndex));
            position = dataEnd;
        }

        if (elements.Length == 0)
        {
            throw new ArgumentException("A GS1 symbol requires at least one element string.", nameof(text));
        }
    }

    /// <summary>
    /// Computes the GS1 check digit for the given data digits using the standard check digit calculation of
    /// the GS1 General Specifications: digits are weighted 3 and 1 alternately starting with 3 at the rightmost
    /// data digit, and the check digit lifts the sum to the next multiple of ten.
    /// </summary>
    /// <param name="digits">The data digits, excluding the check digit.</param>
    /// <returns>The check digit value.</returns>
    public static int ComputeCheckDigit(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        int weight = 3;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            sum += (digits[i] - '0') * weight;
            weight = 4 - weight;
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>
    /// Reads the element string of GS1 Application Identifier (01) and writes the 14 digit GTIN. The input
    /// is <c>(01)</c> and 13 digits, to which the check digit of section 7.9 of the GS1 General
    /// Specifications is added, or 14 digits whose last digit is validated as that check digit.
    /// </summary>
    /// <param name="text">The element string.</param>
    /// <param name="gtin">The buffer the 14 digits are written to.</param>
    /// <exception cref="ArgumentException">The element string is not (01) and 13 or 14 digits, or the check digit is wrong.</exception>
    public static void ReadGtin(string text, Span<char> gtin)
    {
        Guard.NotNull(text, nameof(text));

        if (text.Length is not (17 or 18))
        {
            throw new ArgumentException(
                $"The element string takes the (01) application identifier and 13 or 14 digits; got {text.Length} characters.",
                nameof(text));
        }

        if (!text.AsSpan(0, 4).SequenceEqual("(01)"))
        {
            throw new ArgumentException("The element string begins with the (01) application identifier.", nameof(text));
        }

        ReadOnlySpan<char> digits = text.AsSpan(4);
        SpanCodePointEnumerator codePoints = digits.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (!current.IsAsciiDigit())
            {
                throw new ArgumentException(
                    $"The GTIN carries only digits; got {current.ToDisplayString()}.",
                    nameof(text));
            }
        }

        int check = ComputeCheckDigit(digits[..13]);
        if (digits.Length == 14 && digits[13] - '0' != check)
        {
            throw new ArgumentException($"Incorrect GTIN check digit: expected {check}, got {digits[13]}.", nameof(text));
        }

        digits[..13].CopyTo(gtin);
        gtin[13] = (char)('0' + check);
    }

    /// <summary>
    /// Returns the index of the first element string whose Application Identifier matches a pattern of the
    /// GS1 Barcode Syntax Dictionary, such as "01" or "310n", where "n" stands for any digit.
    /// </summary>
    /// <param name="buffer">The buffer the element strings are in.</param>
    /// <param name="elements">The element strings.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="ignore">An Application Identifier that does not count as a match, or empty.</param>
    /// <returns>The index of the match, or -1.</returns>
    private static int FindPattern(ReadOnlySpan<char> buffer, ReadOnlySpan<Gs1Element> elements, string pattern, ReadOnlySpan<char> ignore)
    {
        for (int i = 0; i < elements.Length; i++)
        {
            ReadOnlySpan<char> identifier = elements[i].Identifier(buffer);
            if (identifier.Length != pattern.Length || identifier.SequenceEqual(ignore))
            {
                continue;
            }

            bool match = true;
            for (int c = 0; c < pattern.Length && match; c++)
            {
                match = pattern[c] == 'n' || pattern[c] == identifier[c];
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
