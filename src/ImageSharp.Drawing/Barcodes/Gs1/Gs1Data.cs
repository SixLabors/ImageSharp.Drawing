// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The data layer the GS1 symbologies share. A GS1 symbol carries one or more element strings, each an Application Identifier and the data that belongs to it,
/// and the same parsing and separator rules apply whatever symbology draws it.
/// </summary>
internal static class Gs1Data
{
    /// <summary>
    /// The character an element string of variable length is terminated by. Section 7.8.6.2 of the GS1 General Specifications: the separator "is always
    /// represented in the transmitted message by the control character &lt;GS&gt; (ASCII value 29 (decimal), 1D (hexadecimal))".
    /// </summary>
    public const char Separator = (char)29;

    /// <summary>
    /// The number of characters a caller holds inline for <see cref="Prepare"/> to build in. Section 5.4.1 caps a GS1-128 symbol at 48 data characters, so
    /// every such symbol builds inline.
    /// </summary>
    public const int InlineBufferLength = 64;

    /// <summary>
    /// The number of element strings <see cref="Prepare"/> holds inline. Data with more rents its element buffer once.
    /// </summary>
    public const int InlineElementCount = 16;

    /// <summary>
    /// The size in points of a font whose em is <see cref="MinimumTextHeight"/> high. A point is 1/72 inch, so the em of a font is its size times 25.4 / 72
    /// millimetres high at any resolution.
    /// </summary>
    public const float MinimumTextSize = MinimumTextHeight * 72F / 25.4F;

    // The symbol specification tables of section 5.12.3 give each application a range of X dimensions, and most symbologies are in several tables. A
    // symbology clamps the X dimension to the widest range of the tables that list it, so the X dimension of every application those tables define stays
    // possible. The nominal X dimension is the target of the table that Table 2-37 gives to the main application of the symbology.

    /// <summary>
    /// The smallest X dimension in millimetres of a GS1-128 symbol, the smallest minimum of the tables that list GS1-128: 0.170 in Table 5-52 and Table 5-57.
    /// </summary>
    public const double Gs1128MinimumXDimension = 0.170;

    /// <summary>
    /// The largest X dimension in millimetres of a GS1-128 symbol, the largest maximum of the tables that list GS1-128: 1.016 in Table 5-47, Table 5-54 and
    /// Table 5-58.
    /// </summary>
    public const double Gs1128MaximumXDimension = 1.016;

    /// <summary>
    /// The smallest X dimension in millimetres of a GS1-128 symbol that carries an SSCC. Note (*) of Table 5-51: "If the logistic unit is physically too small
    /// to accommodate the minimum X-dimension, the minimum X-dimension is 0.250 millimetre (0.0098 inch)."
    /// </summary>
    public const double SsccMinimumXDimension = 0.250;

    /// <summary>
    /// The largest X dimension in millimetres of a GS1-128 symbol that carries an SSCC, the maximum of the logistic unit rows of Table 5-51 and Table 5-58.
    /// </summary>
    public const double SsccMaximumXDimension = 0.940;

    /// <summary>
    /// The smallest bar height in millimetres of a GS1-128 or ITF-14 symbol. A small item or package can truncate the bars below the minimum symbol height of
    /// its table, but note (****) of Table 5-47 sets "no less than 5.08 millimetres", and the notes of Table 5-50 and Table 5-51: "In no case SHALL the bar
    /// height be less than 5.08 millimetres".
    /// </summary>
    public const double MinimumTruncatedBarHeight = 5.08;

    /// <summary>
    /// The smallest X dimension in millimetres of an ITF-14 symbol, the smallest minimum of the tables that list ITF-14: 0.170 in Table 5-52.
    /// </summary>
    public const double Itf14MinimumXDimension = 0.170;

    /// <summary>
    /// The largest X dimension in millimetres of an ITF-14 symbol, the largest maximum of the tables that list ITF-14: 1.016 in Table 5-47 and Table 5-54.
    /// </summary>
    public const double Itf14MaximumXDimension = 1.016;

    /// <summary>
    /// The nominal X dimension in millimetres of a GS1 DataMatrix or GS1 QR Code symbol, the target of Table 5-45, symbol specification table 1 addendum 1.
    /// Table 2-37 gives that table to "GS1 element string syntax for extended packaging applications for trade items".
    /// </summary>
    public const double Gs1MatrixXDimension = 0.495;

    /// <summary>
    /// The smallest X dimension in millimetres of a GS1 DataMatrix symbol, the smallest minimum of the tables that list GS1 DataMatrix: 0.100 for "GS1
    /// DataMatrix direct part marking - A" in Table 5-53.
    /// </summary>
    public const double Gs1DataMatrixMinimumXDimension = 0.100;

    /// <summary>
    /// The smallest X dimension in millimetres of a GS1 QR Code symbol, the smallest minimum of the tables that list GS1 QR Code: 0.254 in Table 5-53 and
    /// Table 5-57.
    /// </summary>
    public const double Gs1QrCodeMinimumXDimension = 0.254;

    /// <summary>
    /// The largest X dimension in millimetres of a GS1 DataMatrix or GS1 QR Code symbol, the largest maximum of the tables that list them: 3.50 in Table 5-59.
    /// </summary>
    public const double Gs1MatrixMaximumXDimension = 3.50;

    /// <summary>
    /// The smallest X dimension in millimetres of a GS1 Digital Link Data Matrix or QR Code symbol, the minimum of Table 5-46, symbol specification table 1
    /// addendum 2. Table 2-37 gives that table to "GS1 Digital Link URI syntax for extended packaging applications for trade items".
    /// </summary>
    public const double DigitalLinkMinimumXDimension = 0.396;

    /// <summary>
    /// The nominal X dimension in millimetres of a GS1 Digital Link Data Matrix or QR Code symbol, the target of Table 5-46.
    /// </summary>
    public const double DigitalLinkXDimension = 0.495;

    /// <summary>
    /// The largest X dimension in millimetres of a GS1 Digital Link Data Matrix or QR Code symbol, the maximum of Table 5-46 and Table 5-49.
    /// </summary>
    public const double DigitalLinkMaximumXDimension = 0.990;

    /// <summary>
    /// The smallest height in millimetres of the font of the human readable interpretation. Section 4.14 rule 2a.iii of the GS1 General Specifications: "The
    /// font size SHOULD be at least 2 mm (0.08 in) in height".
    /// </summary>
    private const float MinimumTextHeight = 2F;

    /// <summary>
    /// Returns the factor that lifts a font to <see cref="MinimumTextHeight"/>, or 1 for a font that is that high or higher.
    /// </summary>
    /// <param name="font">The font of the human readable interpretation.</param>
    /// <returns>The factor applied to the size of the font.</returns>
    public static float TextScale(Font font) => MathF.Max(1F, MinimumTextSize / font.Size);

    /// <summary>
    /// Adds the human readable interpretation of element strings below a built matrix symbol, one element string on each line, as
    /// <see cref="WriteReadable(ReadOnlySpan{char}, bool, Span{char})"/> writes them. The lines start on the left edge of the symbol, as Example 1 of section
    /// 2.3 of the GS1 DataMatrix Guideline prints them.
    /// </summary>
    /// <param name="text">The element strings, in the element string syntax that <see cref="Prepare"/> accepted.</param>
    /// <param name="font">The font of the caller, which <see cref="TextScale"/> lifts to <see cref="MinimumTextHeight"/>.</param>
    /// <param name="symbol">The built symbol.</param>
    public static void AddMatrixReadable(ReadOnlySpan<char> text, Font font, ref BarcodeSymbol symbol)
    {
        // The text is bounded by the capacity of the symbology, so the buffer is too, and longer text rents it once.
        InlineArray64<char> readableStorage = default;
        PooledBuffer<char> rented = default;
        int capacity = ReadableCapacity(text.Length);
        Span<char> readable = capacity <= InlineBufferLength
            ? ((Span<char>)readableStorage)[..capacity]
            : (rented = new PooledBuffer<char>(capacity)).Span;

        try
        {
            int written = WriteReadable(text, true, readable);
            symbol.AddMatrixText(readable[..written], BarcodeTextAlignment.Left, TextScale(font));
        }
        finally
        {
            rented.Dispose();
        }
    }

    /// <summary>
    /// Returns the number of characters <see cref="Prepare"/> writes at most for the text, after it checks that the text can prepare into no more than the
    /// given number of characters. The prepared data drops the two parentheses of each element string and adds at most one separator between two of them, so it
    /// is never longer than the text. An element string has at least three prepared characters, a two digit Application Identifier and one data character, so
    /// text that prepares into P characters is at most P plus two parentheses for each third of P.
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
    /// Parses the element string syntax the standard prints, an Application Identifier in parentheses followed by its data, repeated, validates the element
    /// strings as one whole symbol with <see cref="ValidateSymbol"/>, and writes the data a symbol carries. The symbol does not carry the parentheses;
    /// section 4.14 rule 2c requires them in the human readable interpretation alone.
    /// </summary>
    /// <param name="text">The element strings to parse.</param>
    /// <param name="encoded"> The builder that receives the encoded data: each Application Identifier and its data in turn, with a <see cref="Separator"/>
    /// after every variable length element string but the last. </param>
    /// <exception cref="ArgumentException">The text is not valid element string syntax, or an element string or a pair of them breaks a rule of the GS1 General
    /// Specifications.</exception>
    public static void Prepare(ReadOnlySpan<char> text, ref ValueStringBuilder encoded)
    {
        // An element string is at least five characters of text, the two parentheses, a two digit Application Identifier and one data character, so the text
        // holds at most a fifth of its length in element strings, and the element buffer never grows.
        InlineArray16<Gs1Element> elementStorage = default;
        int elementCapacity = (text.Length / 5) + 1;
        ValueListBuilder<Gs1Element> elements = elementCapacity <= InlineElementCount ? new(elementStorage) : new(elementCapacity);
        try
        {
            Parse(text, ref elements);
            ValidateSymbol(text, elements.AsSpan());
            AppendElements(text, elements.AsSpan(), ref encoded);
        }
        finally
        {
            elements.Dispose();
        }
    }

    /// <summary>
    /// Parses element strings that the caller validated as part of a whole symbol and writes the data a symbol carries, as <see cref="Prepare"/> writes it.
    /// The linear component of a GS1 Composite symbol takes this step: the caller validates the element strings of both components together, because the
    /// rules of <see cref="ValidateSymbol"/> apply to the whole symbol and not to one component.
    /// </summary>
    /// <param name="text">The validated element strings.</param>
    /// <param name="encoded"> The builder that receives the encoded data: each Application Identifier and its data in turn, with a <see cref="Separator"/>
    /// after every variable length element string but the last. </param>
    /// <exception cref="ArgumentException">The text is not valid element string syntax.</exception>
    public static void Encode(ReadOnlySpan<char> text, ref ValueStringBuilder encoded)
    {
        InlineArray16<Gs1Element> elementStorage = default;
        int elementCapacity = (text.Length / 5) + 1;
        ValueListBuilder<Gs1Element> elements = elementCapacity <= InlineElementCount ? new(elementStorage) : new(elementCapacity);
        try
        {
            Parse(text, ref elements);
            AppendElements(text, elements.AsSpan(), ref encoded);
        }
        finally
        {
            elements.Dispose();
        }
    }

    /// <summary>
    /// Validates element strings: each one against its entry of the GS1 Barcode Syntax Dictionary, and then the invalid pairs of section 4.13.1 of the GS1
    /// General Specifications. A pair in one symbol is a pair on the item that the symbol marks. The mandatory associations of section 4.13.2 are not
    /// validated: its note says that associated element strings need not "appear in the same data carrier", so one symbol cannot break them.
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

            // Data from several carriers on one item can repeat an element string, but never with a different value.
            for (int j = i + 1; j < elements.Length; j++)
            {
                if (elements[j].Identifier(buffer).SequenceEqual(identifier) && !elements[j].Data(buffer).SequenceEqual(elements[i].Data(buffer)))
                {
                    throw new ArgumentException($"The Application Identifier ({identifier}) appears twice with different data.", nameof(elements));
                }
            }

            // Section 4.13.1: an invalid pair. An Application Identifier is never excluded by itself, even when its own pattern matches it.
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
        }

        // Table 4-5 of the GS1 General Specifications puts a digital signature, Application Identifier (8030), with a "Global Returnable Asset Identifier
        // including Serial component", a "Global Document Type Identifier including Serial component" or a "Global Coupon Number including Serial component".
        // So a GRAI (8003), GDTI (253) or GCN (255) in the same symbol as a digital signature carries its optional serial component.
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
    /// Validates the element strings of one whole symbol: the rules of <see cref="Validate"/>, and the rules that tie element strings to one symbol. Section
    /// 3.9.27 of the GS1 General Specifications uses Application Identifier (8200) "in mandatory association with GTIN AI (01) encoded in one symbol". A
    /// component of a GS1 Composite symbol is not a whole symbol, so its own element strings take <see cref="Validate"/> only.
    /// </summary>
    /// <param name="buffer">The buffer the element strings are in.</param>
    /// <param name="elements">The element strings.</param>
    /// <exception cref="ArgumentException">An element string or a pair of them breaks a rule.</exception>
    public static void ValidateSymbol(ReadOnlySpan<char> buffer, ReadOnlySpan<Gs1Element> elements)
    {
        Validate(buffer, elements);
        if (FindPattern(buffer, elements, "8200", []) >= 0 && FindPattern(buffer, elements, "01", []) < 0)
        {
            throw new ArgumentException("The Application Identifier (8200) needs a GTIN, (01), in the same symbol.", nameof(elements));
        }
    }

    /// <summary>
    /// Returns the number of characters <see cref="WriteReadable(ReadOnlySpan{char}, bool, Span{char})"/> writes at most for element strings that
    /// <see cref="Prepare"/> accepted. The interpretation is the text with a line break between two element strings, and an element string is at least five
    /// characters of text, so the text holds at most a fifth of its length in element strings.
    /// </summary>
    /// <param name="textLength">The length of the element strings.</param>
    /// <returns>The capacity the interpretation needs.</returns>
    public static int ReadableCapacity(int textLength) => textLength + (textLength / 5);

    /// <summary>
    /// Writes the human readable interpretation of element strings that <see cref="Prepare"/> accepted, each element string as
    /// <see cref="WriteReadable(ReadOnlySpan{char}, ReadOnlySpan{char}, bool, Span{char}, int)"/> writes it, in the order the symbol carries them.
    /// </summary>
    /// <param name="text">The element strings, in the element string syntax.</param>
    /// <param name="lineBreaks">Whether each element string prints on a line of its own.</param>
    /// <param name="readable"> The buffer the interpretation is written to: <see cref="ReadableCapacity"/> long with line breaks, or as long as the text
    /// without, since the interpretation on one line is never longer than the text. </param>
    /// <returns>The number of characters written.</returns>
    public static int WriteReadable(ReadOnlySpan<char> text, bool lineBreaks, Span<char> readable)
    {
        // The parse took each element string as an Application Identifier in parentheses and the data up to the next opening parenthesis, so the same cuts find
        // the element strings again.
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
    /// Writes one element string of a human readable interpretation. Section 4.14 rule 2c of the GS1 General Specifications puts the Application Identifier in
    /// parentheses before its data. A linear symbol prints the element strings on one line, as Figure 4-7 shows for GS1-128. A two-dimensional symbol prints
    /// each element string on a line of its own, the preferred format of Figure 4-6, so a line break comes before every element string but the first, and rule
    /// 1e keeps an element string on one line. Rule 3b keeps the URL of Application Identifier (8200) out of the interpretation, so that element string writes
    /// nothing.
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
    /// Parses the element string syntax into the positions of its element strings.
    /// </summary>
    /// <param name="text">The element strings to parse.</param>
    /// <param name="elements">The builder the positions are written to.</param>
    /// <exception cref="ArgumentException">The text is not valid element string syntax.</exception>
    public static void Parse(ReadOnlySpan<char> text, ref ValueListBuilder<Gs1Element> elements)
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

            BarcodeSymbology.ValidateDigits(identifier);

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
    /// Computes the GS1 check digit for the given data digits using the standard check digit calculation of the GS1 General Specifications: digits are weighted
    /// 3 and 1 alternately starting with 3 at the rightmost data digit, and the check digit lifts the sum to the next multiple of ten.
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
    /// Reads the element string of GS1 Application Identifier (01) and writes the 14 digit GTIN. The input is <c>(01)</c> and 13 digits, to which the check
    /// digit of section 7.9 of the GS1 General Specifications is added, or 14 digits whose last digit is validated as that check digit.
    /// </summary>
    /// <param name="text">The element string.</param>
    /// <param name="gtin">The buffer the 14 digits are written to.</param>
    /// <exception cref="ArgumentException">The element string is not (01) and 13 or 14 digits, or the check digit is wrong.</exception>
    public static void ReadGtin(ReadOnlySpan<char> text, Span<char> gtin)
    {
        if (text.Length is not (17 or 18))
        {
            throw new ArgumentException(
                $"The element string takes the (01) application identifier and 13 or 14 digits; got {text.Length} characters.",
                nameof(text));
        }

        if (!text.Slice(0, 4).SequenceEqual("(01)"))
        {
            throw new ArgumentException("The element string begins with the (01) application identifier.", nameof(text));
        }

        ReadOnlySpan<char> digits = text.Slice(4);
        BarcodeSymbology.ValidateDigits(digits);

        int check = ComputeCheckDigit(digits[..13]);
        if (digits.Length == 14 && digits[13] - '0' != check)
        {
            throw new ArgumentException($"Incorrect GTIN check digit: expected {check}, got {digits[13]}.", nameof(text));
        }

        digits[..13].CopyTo(gtin);
        gtin[13] = (char)('0' + check);
    }

    /// <summary>
    /// Validates the data of one element string against its entry of the GS1 Barcode Syntax Dictionary: its length, and each component in turn with its
    /// character set and its checks.
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

        // Each component takes as many characters as remain, up to its maximum. Only the last component can vary in length, and an optional component that
        // finds no data left is skipped.
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
    /// Writes the data a symbol carries for parsed element strings: each Application Identifier and its data in turn. Section 7.8.6.2: an element string
    /// without a predefined length is terminated by a separator, unless it is the last one in the symbol. The dictionary marks the predefined lengths of
    /// Table 7-6.
    /// </summary>
    /// <param name="text">The buffer the element strings are in.</param>
    /// <param name="elements">The element strings.</param>
    /// <param name="encoded">The builder that receives the encoded data.</param>
    private static void AppendElements(scoped ReadOnlySpan<char> text, scoped ReadOnlySpan<Gs1Element> elements, ref ValueStringBuilder encoded)
    {
        for (int i = 0; i < elements.Length; i++)
        {
            Gs1Element element = elements[i];
            encoded.Append(element.Identifier(text));
            encoded.Append(element.Data(text));
            if (i < elements.Length - 1 && !element.Entry.PredefinedLength)
            {
                encoded.Append(Separator);
            }
        }
    }

    /// <summary>
    /// Returns the index of the first element string whose Application Identifier matches a pattern of the GS1 Barcode Syntax Dictionary, such as "01" or
    /// "310n", where "n" stands for any digit.
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
            if (identifier.Length != pattern.Length || (!ignore.IsEmpty && identifier.SequenceEqual(ignore)))
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
