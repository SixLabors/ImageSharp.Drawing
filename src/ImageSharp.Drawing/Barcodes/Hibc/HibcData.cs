// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The data layer the Health Industry Bar Code symbologies share, from ANSI/HIBC 2.6. Every HIBC symbol carries the Code 39 character set, the same flag
/// character in front of the data and the same modulo 43 check character after it, whatever symbology draws it.
/// </summary>
internal static class HibcData
{
    /// <summary>
    /// The number of characters a caller holds inline for <see cref="Prepare"/> to build in. Data this long covers the labels the standard is used for, and
    /// longer data rents its buffer once.
    /// </summary>
    public const int InlineBufferLength = 64;

    /// <summary>
    /// The smallest bar height of a Code 128 or Code 39 carrier as a fraction of the symbol length. Section 5.1: "The height of the bars should be at least 15%
    /// of the symbol length."
    /// </summary>
    public const float MinimumBarHeightFraction = 0.15F;

    /// <summary>
    /// The smallest X dimension in millimetres of an Aztec Code, Data Matrix or QR Code symbol, 0.010 inch. Section 5.2: "Any X-dimension greater than 0.010
    /// (0.25mm) inches is allowable if the print quality requirement is met." The section gives no largest X dimension.
    /// </summary>
    public const double MinimumMatrixXDimension = 0.010 * 25.4;

    /// <summary>
    /// The smallest X dimension in millimetres of a Code 128 or Code 39 symbol, 0.0067 inch. Section 5.1: "Any X-dimension greater than 0.0067 inches is
    /// allowable if the print quality requirement is met." The section gives no largest X dimension.
    /// </summary>
    public const double MinimumLinearXDimension = 0.0067 * 25.4;

    /// <summary>
    /// The default X dimension in millimetres of a Code 128 or Code 39 symbol: 0.010 inch. Section 5.1: "Labelers should use an X-dimension of 0.010 inches
    /// (0.25 mm)."
    /// </summary>
    public const double NominalLinearXDimension = 0.010 * 25.4;

    /// <summary>
    /// The default X dimension in millimetres of an Aztec Code, Data Matrix or QR Code symbol: 0.015 inch. Section 5.2: "Labelers should use an X-dimension
    /// of 0.015 inches (0.37 mm)."
    /// </summary>
    public const double NominalMatrixXDimension = 0.015 * 25.4;

    /// <summary>
    /// The delimiter that opens and closes the human readable interpretation. Section 4.1: the interpretation "should be bounded in the beginning and at the
    /// end of the data string by an asterisk".
    /// </summary>
    private const char ReadableDelimiter = '*';

    /// <summary>
    /// The character that begins the encoded data, the HIBC Supplier Labeling Flag Character of Table 1.
    /// </summary>
    private const char FlagCharacter = '+';

    /// <summary>
    /// The character that stands in for a space in the human readable interpretation. Section 4.1: "The Check Character or Link Character in the symbol will
    /// sometimes be a space character. In this case, the human-readable interpretation shall use an "underscore" to represent the space character."
    /// </summary>
    private const char PrintedSpace = '_';

    /// <summary>
    /// The character that stands in for a zero in the human readable interpretation. Section 4.1: "It is the recommendation of HIBCC that the human-readable
    /// interpretation of zero be represented as "Ø"."
    /// </summary>
    private const char PrintedZero = 'Ø';

    /// <summary>
    /// The character that joins primary and secondary data in one symbol, section 2.2.1.1, and opens each supplemental data field, section 2.3.1.
    /// </summary>
    private const char Delimiter = '/';

    /// <summary>
    /// The longest Product or Catalog Number. Table 1: "P 1-18 V Labelers Product or Catalog Number (PCN)."
    /// </summary>
    private const int MaximumProductLength = 18;

    /// <summary>
    /// The longest Lot/Batch or Serial Number. Appendix E1.2: "The Lot/Batch or Serial Number field can be alphanumeric and vary in length up to a maximum of
    /// 18 characters."
    /// </summary>
    private const int MaximumLotLength = 18;

    /// <summary>
    /// The longest quantity. Section 2.3.2.4: "Field Length - an1+n1...n5 Q Quantity".
    /// </summary>
    private const int MaximumQuantityLength = 5;

    /// <summary>
    /// Returns the number of characters <see cref="Prepare"/> writes at most for the text: the flag character, the data and the check character. The prepared
    /// data is never shorter than the text, so text longer than the carrier holds fails here, before the caller sizes a buffer from it.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="maximumPrepared">The largest number of characters the carrier symbology encodes.</param>
    /// <returns>The capacity the prepared data needs.</returns>
    /// <exception cref="ArgumentException">The text is longer than <paramref name="maximumPrepared"/>.</exception>
    public static int PreparedCapacity(ReadOnlySpan<char> text, int maximumPrepared)
    {
        if (text.Length > maximumPrepared)
        {
            throw new ArgumentException($"The text is {text.Length} characters; the symbology carries at most {maximumPrepared}.", nameof(text));
        }

        return text.Length + 2;
    }

    /// <summary>
    /// Validates the given text against the HIBC character set and the data structures of ANSI/HIBC 2.6, calculates the check character over the data and
    /// writes the encoded data from both.
    /// </summary>
    /// <param name="text"> The data to carry, without the flag character. When <paramref name="validateCheckCharacter"/> is <see langword="true"/> the last
    /// character is the check character rather than data. </param>
    /// <param name="validateCheckCharacter"> Whether the method validates the check character at the end of <paramref name="text"/>. </param>
    /// <param name="encoded"> The builder that receives the encoded data: the flag character, the data and the check character. </param>
    /// <exception cref="ArgumentException">The text is not valid HIBC data.</exception>
    public static void Prepare(ReadOnlySpan<char> text, bool validateCheckCharacter, ref ValueStringBuilder encoded)
    {
        // The data is at least one character, and a check character to validate follows it.
        Guard.MustBeGreaterThan(text.Length, validateCheckCharacter ? 1 : 0, nameof(text));

        // The check character covers the flag character the encoder puts in front of the data, so the sum starts at the value that character holds in the set.
        int sum = AlphanumericCharacterSet.Value(FlagCharacter);
        int dataLength = validateCheckCharacter ? text.Length - 1 : text.Length;
        int index = 0;
        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            int value = AlphanumericCharacterSet.Value(current.Value);
            if (value < 0)
            {
                throw new ArgumentException(
                    $"HIBC carries only digits, capital letters, spaces and the symbols -.$/+%; got {current.ToDisplayString()}.",
                    nameof(text));
            }

            if (index < dataLength)
            {
                sum += value;
            }

            index++;
        }

        ReadOnlySpan<char> data = text[..dataLength];
        ValidateStructure(data);

        char check = AlphanumericCharacterSet.CheckCharacter(sum);
        if (validateCheckCharacter && text[dataLength] != check)
        {
            throw new ArgumentException($"Incorrect check character: expected '{check}', got '{text[dataLength]}'.", nameof(text));
        }

        encoded.Append(FlagCharacter);
        encoded.Append(data);
        encoded.Append(check);
    }

    /// <summary>
    /// Returns the number of characters <see cref="WriteReadable"/> and <see cref="WriteMatrixReadable"/> write at most for encoded data: the encoded data, the
    /// two delimiters and one line break.
    /// </summary>
    /// <param name="encodedLength">The length of the encoded data.</param>
    /// <returns>The capacity the interpretation needs.</returns>
    public static int ReadableCapacity(int encodedLength) => encodedLength + 3;

    /// <summary>
    /// Writes the human readable interpretation of encoded data on one line, as a linear symbol prints it. Section 4.1: the interpretation represents "all
    /// encoded characters" between asterisks, a space prints as an underscore and a zero as "Ø".
    /// </summary>
    /// <param name="encoded">The encoded data, as <see cref="Prepare"/> wrote it.</param>
    /// <param name="readable">The buffer the interpretation is written to, <see cref="ReadableCapacity"/> long.</param>
    /// <returns>The number of characters written.</returns>
    public static int WriteReadable(ReadOnlySpan<char> encoded, Span<char> readable)
    {
        readable[0] = ReadableDelimiter;
        int written = 1 + WritePrinted(encoded, readable[1..]);
        readable[written++] = ReadableDelimiter;
        return written;
    }

    /// <summary>
    /// Adds the human readable interpretation of encoded data below a built matrix symbol, as <see cref="WriteMatrixReadable"/> writes it, centered on the
    /// symbol as the figures of sections 4.3.1 and 4.3.3 print it.
    /// </summary>
    /// <param name="encoded">The encoded data, as <see cref="Prepare"/> wrote it.</param>
    /// <param name="symbol">The built symbol.</param>
    public static void AddMatrixReadable(ReadOnlySpan<char> encoded, ref BarcodeSymbol symbol)
    {
        InlineArray64<char> readableStorage = default;
        PooledBuffer<char> rented = default;
        int capacity = ReadableCapacity(encoded.Length);
        Span<char> readable = capacity <= InlineBufferLength
            ? ((Span<char>)readableStorage)[..capacity]
            : (rented = new PooledBuffer<char>(capacity)).Span;
        try
        {
            int written = WriteMatrixReadable(encoded, readable);
            symbol.AddMatrixText(readable[..written], BarcodeTextAlignment.Centered, 1F);
        }
        finally
        {
            rented.Dispose();
        }
    }

    /// <summary>
    /// Adds the human readable interpretation of encoded data below a built stacked symbol, as <see cref="WriteMatrixReadable"/> writes it, centered on the
    /// symbol. Section 3.0: "The data structure and human-readable interpretation is identical regardless of symbology used."
    /// </summary>
    /// <param name="encoded">The encoded data, as <see cref="Prepare"/> wrote it.</param>
    /// <param name="symbol">The built symbol.</param>
    public static void AddStackedReadable(ReadOnlySpan<char> encoded, ref BarcodeSymbol symbol)
    {
        InlineArray64<char> readableStorage = default;
        PooledBuffer<char> rented = default;
        int capacity = ReadableCapacity(encoded.Length);
        Span<char> readable = capacity <= InlineBufferLength
            ? ((Span<char>)readableStorage)[..capacity]
            : (rented = new PooledBuffer<char>(capacity)).Span;
        try
        {
            int written = WriteMatrixReadable(encoded, readable);
            symbol.AddStackedText(readable[..written], BarcodeTextAlignment.Centered, 1F);
        }
        finally
        {
            rented.Dispose();
        }
    }

    /// <summary>
    /// Writes the human readable interpretation of encoded data that a two-dimensional symbol carries. Section 3.0 gives every symbology the same
    /// interpretation as <see cref="WriteReadable"/>. The figure of section 4.3.3 prints primary and secondary data that one symbol concatenates on two lines,
    /// broken after the character that joins them.
    /// </summary>
    /// <param name="encoded">The encoded data, as <see cref="Prepare"/> wrote it.</param>
    /// <param name="readable">The buffer the interpretation is written to, <see cref="ReadableCapacity"/> long.</param>
    /// <returns>The number of characters written.</returns>
    private static int WriteMatrixReadable(ReadOnlySpan<char> encoded, Span<char> readable)
    {
        readable[0] = ReadableDelimiter;
        int written = 1;

        // Note 1 of section 2.2.1: primary data starts with a letter after the flag character. Primary data holds no "/", so the first "/" after it is the
        // join of section 2.2.1.1. Secondary data alone has no join, and its "/" characters open supplemental data fields. The check character at the end can
        // be "/" too, so the search stops before it.
        int join = char.IsAsciiLetterUpper(encoded[1]) ? encoded[..^1].IndexOf(Delimiter) : -1;
        if (join >= 0)
        {
            written += WritePrinted(encoded[..(join + 1)], readable[written..]);
            readable[written++] = BarcodeTextPlacement.LineBreak;
            encoded = encoded[(join + 1)..];
        }

        written += WritePrinted(encoded, readable[written..]);
        readable[written++] = ReadableDelimiter;
        return written;
    }

    /// <summary>
    /// Writes characters of the encoded data as the interpretation prints them: a space as <see cref="PrintedSpace"/> and a zero as <see cref="PrintedZero"/>.
    /// </summary>
    /// <param name="encoded">The characters of the encoded data.</param>
    /// <param name="destination">The buffer the characters are written to.</param>
    /// <returns>The number of characters written.</returns>
    private static int WritePrinted(ReadOnlySpan<char> encoded, Span<char> destination)
    {
        for (int i = 0; i < encoded.Length; i++)
        {
            char character = encoded[i];
            destination[i] = character switch
            {
                ' ' => PrintedSpace,
                '0' => PrintedZero,
                _ => character,
            };
        }

        return encoded.Length;
    }

    /// <summary>
    /// Validates the data against the data structures of ANSI/HIBC 2.6. Note 1 of section 2.2.1: "the Primary Data Structure has an alphabetic character
    /// following the HIBC Supplier Labeling Flag Character "+", while the Secondary Data Structure has a numeric character or a "$" following" it. Primary
    /// data can carry secondary data after the "/" of section 2.2.1.1, without its flag character and its link character. Secondary data alone, in its own
    /// symbol, ends with the link character of Table 2.
    /// </summary>
    /// <param name="data">The data, without the flag character and the check character.</param>
    /// <exception cref="ArgumentException">The data does not follow a data structure.</exception>
    private static void ValidateStructure(ReadOnlySpan<char> data)
    {
        char first = data[0];
        if (char.IsAsciiLetterUpper(first))
        {
            int join = data.IndexOf(Delimiter);
            ValidatePrimary(join < 0 ? data : data[..join]);
            if (join >= 0)
            {
                ValidateSecondary(data[(join + 1)..], false);
            }

            return;
        }

        if (char.IsAsciiDigit(first) || first == '$')
        {
            ValidateSecondary(data, true);
            return;
        }

        throw new ArgumentException("HIBC data starts with a letter for primary data, or with a digit or '$' for secondary data.", nameof(data));
    }

    /// <summary>
    /// Validates primary data against Table 1: a 4-character Labeler Identification Code "with the first character always being alphabetic", a 1 to 18
    /// character Product or Catalog Number of "A through Z and 0 through 9", and a one digit Unit of Measure.
    /// </summary>
    /// <param name="primary">The primary data, without the flag character.</param>
    /// <exception cref="ArgumentException">The data does not follow Table 1.</exception>
    private static void ValidatePrimary(ReadOnlySpan<char> primary)
    {
        // Section 3.0: "No special characters (-, ., $, /, +, %, and space) are used in the Primary data structure".
        for (int i = 0; i < primary.Length; i++)
        {
            if (!char.IsAsciiLetterUpper(primary[i]) && !char.IsAsciiDigit(primary[i]))
            {
                throw new ArgumentException("HIBC primary data carries only capital letters and digits.", nameof(primary));
            }
        }

        int productLength = primary.Length - 5;
        if (productLength < 1 || productLength > MaximumProductLength)
        {
            throw new ArgumentException(
                "HIBC primary data is a 4-character labeler code, a 1 to 18 character product number and a one digit unit of measure.",
                nameof(primary));
        }

        if (!char.IsAsciiDigit(primary[^1]))
        {
            throw new ArgumentException("The unit of measure of HIBC primary data is a digit, 0 through 9.", nameof(primary));
        }
    }

    /// <summary>
    /// Validates secondary data against Table 2 and Appendix F, then any supplemental data fields of section 2.3. A numeric start is a 5-digit Julian date
    /// alone. "$" starts a lot number, "$+" a serial number, and "$$" and "$$+" a date of the format the next digit gives, then a lot or a serial number.
    /// </summary>
    /// <param name="secondary">The secondary data, without the flag character.</param>
    /// <param name="linked">Whether the data stands in its own symbol and so ends with a link character.</param>
    /// <exception cref="ArgumentException">The data does not follow Table 2 or section 2.3.</exception>
    private static void ValidateSecondary(ReadOnlySpan<char> secondary, bool linked)
    {
        int position;
        bool requireField;
        if (secondary.Length > 0 && char.IsAsciiDigit(secondary[0]))
        {
            // Table 2: "If the first character is numeric, then R is a fixed 5-digit Julian date. No Lot/Batch or Serial Number is present".
            ValidateDate(secondary, 0, "YYJJJ");
            position = 5;
            ValidateLinkAndSupplements(secondary, position, linked);
            return;
        }

        if (secondary.StartsWith("$$"))
        {
            // Appendix E1.1: after "$$" or "$$+" "the first digit following will specify the Date Field formats".
            position = secondary.StartsWith("$$+") ? 3 : 2;
            if (position >= secondary.Length)
            {
                throw new ArgumentException("HIBC secondary data gives a date format digit after \"$$\" or \"$$+\".", nameof(secondary));
            }

            position += secondary[position] switch
            {
                '0' or '1' => ValidateDate(secondary, position, "MMYY"),
                '2' => 1 + ValidateDate(secondary, position + 1, "MMDDYY"),
                '3' => 1 + ValidateDate(secondary, position + 1, "YYMMDD"),
                '4' => 1 + ValidateDate(secondary, position + 1, "YYMMDDHH"),
                '5' => 1 + ValidateDate(secondary, position + 1, "YYJJJ"),
                '6' => 1 + ValidateDate(secondary, position + 1, "YYJJJHH"),
                '7' => 1,
                _ => throw new ArgumentException("The HIBC date format digit is 0 through 7.", nameof(secondary)),
            };

            requireField = false;
        }
        else if (secondary.StartsWith("$+"))
        {
            // Table 2: "If the first two characters are "$+" and the third character is alphanumeric then a serial number only follows."
            position = 2;
            requireField = true;
        }
        else if (secondary.StartsWith("$"))
        {
            // Table 2: "If the first character is a "$" and the second character is alphanumeric, then the Date fields are not used."
            position = 1;
            requireField = true;
        }
        else
        {
            throw new ArgumentException("HIBC secondary data starts with a digit or '$'.", nameof(secondary));
        }

        // Section 3.0 permits "." and "-" in the Lot and Serial fields. A required field starts with a letter or a digit.
        int fieldStart = position;
        while (position < secondary.Length && IsLotCharacter(secondary[position]))
        {
            position++;
        }

        // In a symbol of its own the link character follows the field, and it can be any character of the set. A link character that is a letter, a digit, "."
        // or "-" ends the run of field characters. Any other link character stands after the run.
        int fieldEnd = position;
        if (linked && !EndsWithLinkAfterRun(secondary, fieldEnd))
        {
            if (fieldEnd == fieldStart)
            {
                throw new ArgumentException("HIBC secondary data in its own symbol ends with the link character.", nameof(secondary));
            }

            fieldEnd--;
        }

        int fieldLength = fieldEnd - fieldStart;
        if (fieldLength > MaximumLotLength)
        {
            throw new ArgumentException("An HIBC lot or serial number is at most 18 characters.", nameof(secondary));
        }

        if (requireField && (fieldLength < 1 || !(char.IsAsciiLetterUpper(secondary[fieldStart]) || char.IsAsciiDigit(secondary[fieldStart]))))
        {
            throw new ArgumentException("An HIBC lot or serial number after \"$\" or \"$+\" starts with a letter or a digit.", nameof(secondary));
        }

        ValidateLinkAndSupplements(secondary, fieldEnd, linked);
    }

    /// <summary>
    /// Returns whether the link character of secondary data in its own symbol stands after the run of field characters that ends at a position, rather than
    /// being the last character of that run. A link character that is not a field character ends the run, and only "/" can also open a supplemental field.
    /// </summary>
    /// <param name="secondary">The secondary data.</param>
    /// <param name="runEnd">The end of the run of field characters.</param>
    /// <returns><see langword="true"/> when the link character is the character at <paramref name="runEnd"/>.</returns>
    private static bool EndsWithLinkAfterRun(ReadOnlySpan<char> secondary, int runEnd)
    {
        if (runEnd >= secondary.Length)
        {
            return false;
        }

        // A "/" after the run is the link character when the data ends after it or the next field opens after it. Otherwise it opens the next field.
        return secondary[runEnd] != Delimiter || runEnd + 1 == secondary.Length || secondary[runEnd + 1] == Delimiter;
    }

    /// <summary>
    /// Validates the link character of secondary data in its own symbol and the supplemental data fields after it. Section 2.3.1: "The Secondary Supplemental
    /// Data field is constructed with a "/" character followed by a Data Identifier (DI), followed by data." Sections 2.3.2.1 to 2.3.2.4 give the fields:
    /// "S" and a serial number, "16D" and a date of manufacture, "14D" and an expiry date, and "Q" and a quantity, which "must be the last field".
    /// </summary>
    /// <param name="secondary">The secondary data.</param>
    /// <param name="position">The position after the secondary fields.</param>
    /// <param name="linked">Whether the data stands in its own symbol and so carries a link character.</param>
    /// <exception cref="ArgumentException">The data does not follow section 2.3.</exception>
    private static void ValidateLinkAndSupplements(ReadOnlySpan<char> secondary, int position, bool linked)
    {
        if (linked)
        {
            // Table 2: "L 1 Link Character (Check Character from primary data field.)"
            if (position >= secondary.Length)
            {
                throw new ArgumentException("HIBC secondary data in its own symbol ends with the link character.", nameof(secondary));
            }

            position++;
        }

        bool quantity = false;
        while (position < secondary.Length)
        {
            if (secondary[position] != Delimiter || quantity)
            {
                throw new ArgumentException("HIBC supplemental data fields each start with '/', and the quantity is the last field.", nameof(secondary));
            }

            ReadOnlySpan<char> field = secondary[(position + 1)..];
            int end = field.IndexOf(Delimiter);
            ReadOnlySpan<char> value = end < 0 ? field : field[..end];
            if (value.StartsWith("16D") || value.StartsWith("14D"))
            {
                // Sections 2.3.2.2 and 2.3.2.3: "Field Length - an3+n8".
                if (value.Length != 11)
                {
                    throw new ArgumentException("An HIBC date of manufacture or expiry date is YYYYMMDD.", nameof(secondary));
                }

                ValidateDate(value, 3, "YYYYMMDD");
            }
            else if (value.StartsWith("S"))
            {
                // Section 2.3.2.1: "Field Length - an1 + an18".
                if (value.Length < 2 || value.Length - 1 > MaximumLotLength)
                {
                    throw new ArgumentException("An HIBC serial number is 1 to 18 characters.", nameof(secondary));
                }

                foreach (char character in value[1..])
                {
                    if (!IsLotCharacter(character))
                    {
                        throw new ArgumentException("An HIBC serial number carries letters, digits, '.' and '-'.", nameof(secondary));
                    }
                }
            }
            else if (value.StartsWith("Q"))
            {
                // Section 2.3.2.4: "Field Length - an1+n1...n5".
                if (value.Length < 2 || value.Length - 1 > MaximumQuantityLength || value[1..].ContainsAnyExceptInRange('0', '9'))
                {
                    throw new ArgumentException("An HIBC quantity is 1 to 5 digits.", nameof(secondary));
                }

                quantity = true;
            }
            else
            {
                throw new ArgumentException("An HIBC supplemental data field starts with the data identifier S, 16D, 14D or Q.", nameof(secondary));
            }

            position += 1 + value.Length;
        }
    }

    /// <summary>
    /// Validates a date field of the format the field descriptions of Appendix F give: MM a month, DD a day, YY or YYYY a year, HH an hour and JJJ a Julian
    /// day, each of fixed length in digits.
    /// </summary>
    /// <param name="data">The data that holds the date.</param>
    /// <param name="start">The position of the date.</param>
    /// <param name="format">The format, from the letters Y, M, D, H and J.</param>
    /// <returns>The length of the date.</returns>
    /// <exception cref="ArgumentException">The date does not follow the format.</exception>
    private static int ValidateDate(ReadOnlySpan<char> data, int start, string format)
    {
        if (start + format.Length > data.Length || data.Slice(start, format.Length).ContainsAnyExceptInRange('0', '9'))
        {
            throw new ArgumentException($"An HIBC date of the format {format} is {format.Length} digits.", nameof(data));
        }

        ReadOnlySpan<char> date = data.Slice(start, format.Length);
        int month = Field(date, format, 'M');
        int day = Field(date, format, 'D');
        int hour = Field(date, format, 'H');
        int julian = Field(date, format, 'J');
        if ((month >= 0 && month is < 1 or > 12) || (day >= 0 && day is < 1 or > 31) || hour > 23 || (julian >= 0 && julian is < 1 or > 366))
        {
            throw new ArgumentException($"The HIBC date {date} is not a date of the format {format}.", nameof(data));
        }

        return format.Length;
    }

    /// <summary>
    /// Returns the value of the field of one letter in a date, or -1 when the format has no such field.
    /// </summary>
    /// <param name="date">The date digits.</param>
    /// <param name="format">The format.</param>
    /// <param name="letter">The letter of the field.</param>
    /// <returns>The value.</returns>
    private static int Field(ReadOnlySpan<char> date, string format, char letter)
    {
        int start = format.IndexOf(letter);
        if (start < 0)
        {
            return -1;
        }

        int value = 0;
        for (int i = start; i < format.Length && format[i] == letter; i++)
        {
            value = (value * 10) + (date[i] - '0');
        }

        return value;
    }

    /// <summary>
    /// Returns whether a character can stand in a lot or serial number. Section 3.0: "The use of the special characters "." and "-" are permitted in the Lot
    /// and Serial fields of the Secondary data structure."
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> for a capital letter, a digit, "." or "-".</returns>
    private static bool IsLotCharacter(char character) => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character is '.' or '-';
}
