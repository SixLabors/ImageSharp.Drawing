// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Builds the 2D component of a GS1 Composite symbol, ISO/IEC 24723. The element strings become one bit stream, clause 5: an encodation method field, a
/// compressed field for some leading Application Identifiers, and a general purpose field, padded to the capacity of the smallest symbol that holds it. CC-A
/// carries the bit stream in base 928, clause 8. CC-B and CC-C carry it in PDF417 Byte Compaction mode after the codeword 920, clauses 9 and 10.
/// </summary>
internal static class Gs1CompositeComponentEncoder
{
    /// <summary>
    /// The fewest data columns of CC-A and CC-B.
    /// </summary>
    public const int MinimumColumns = 2;

    /// <summary>
    /// The most data columns of CC-A and CC-B.
    /// </summary>
    public const int MaximumColumns = 4;

    /// <summary>
    /// The fewest data columns of CC-C, section 5.11.2.3 of the GS1 General Specifications: "1 to 30 data/EC codeword columns".
    /// </summary>
    public const int MinimumCcCColumns = 1;

    /// <summary>
    /// The most data columns of CC-C.
    /// </summary>
    public const int MaximumCcCColumns = 30;

    /// <summary>
    /// The row height of CC-A and CC-B in modules. Section 11.3 of ISO/IEC 24723 gives a minimum of 2X, and Table 9 sizes the symbols at that height.
    /// </summary>
    private const float RowHeight = 2F;

    /// <summary>
    /// The fewest rows of CC-C, section 5.11.2.3 of the GS1 General Specifications: "from 3 to 30 rows".
    /// </summary>
    private const int MinimumCcCRows = 3;

    /// <summary>
    /// The most rows of CC-C.
    /// </summary>
    private const int MaximumCcCRows = 30;

    /// <summary>
    /// The columns of a CC-C row that carry no data: the start pattern, the two row indicators and the stop pattern.
    /// </summary>
    private const int CcCOverheadColumns = 4;

    /// <summary>
    /// The most bits of a bit stream: the byte capacity of the largest CC-C symbol, 30 columns of 30 rows at error correction level 4, eight bits each.
    /// </summary>
    private const int MaximumBits = 8304;

    /// <summary>
    /// The longest prepared data a 2D component can hold. No encodation of ISO/IEC 24723 takes fewer than 2 bits for a character of prepared data, so data
    /// longer than half of <see cref="MaximumBits"/> never fits.
    /// </summary>
    public const int MaximumPrepared = MaximumBits / 2;

    /// <summary>
    /// The number of bits section 8.5 converts into one group of base 928 codewords.
    /// </summary>
    private const int Base928GroupBits = 69;

    /// <summary>
    /// The number of base 928 codewords of a full group of <see cref="Base928GroupBits"/> bits.
    /// </summary>
    private const int Base928GroupCodewords = 7;

    /// <summary>
    /// The base of the CC-A codewords, section 8.5.
    /// </summary>
    private const int Base928 = 928;

    /// <summary>
    /// The codeword that starts the data of CC-B, clause 9 a), and follows the Symbol Length Descriptor of CC-C, clause 10 a).
    /// </summary>
    private const int CompositeCodeword = 920;

    /// <summary>
    /// The modes of the general purpose field. The class of a character is the mode with the fewest bits that carries it.
    /// </summary>
    private enum FieldMode : byte
    {
        /// <summary>
        /// The class of a character the field cannot carry.
        /// </summary>
        None,

        /// <summary>
        /// Digits and the separator, two per seven bits, section 5.4.1.
        /// </summary>
        Numeric,

        /// <summary>
        /// Digits, capital letters and five punctuation characters, section 5.4.2.
        /// </summary>
        Alphanumeric,

        /// <summary>
        /// The characters of Table 7, section 5.4.3.
        /// </summary>
        Iso646,

        /// <summary>
        /// Capital letters and digits, section 5.3.3. Only the rest of Application Identifier (90) starts in it, and no character class is alpha.
        /// </summary>
        Alpha,
    }

    /// <summary>
    /// Gets the 17 CC-A sizes, six values each: the data columns, the rows, the error correction codewords of Table 9, and the left, centre and right row
    /// address patterns of the first row from Table 10 and Table 11. The sizes of one column count stand in order of capacity. A 3-column CC-A has no left row
    /// address pattern, clause 8.2 d), and its left value gives the row of Table 11 that sets the cluster of its first row.
    /// </summary>
    private static ReadOnlySpan<byte> CcASizes =>
    [
        2, 5, 4, 39, 0, 19,
        2, 6, 4, 1, 0, 33,
        2, 7, 5, 32, 0, 12,
        2, 8, 5, 8, 0, 40,
        2, 9, 6, 14, 0, 46,
        2, 10, 6, 43, 0, 23,
        2, 12, 7, 20, 0, 52,
        3, 4, 4, 11, 43, 23,
        3, 5, 5, 1, 33, 13,
        3, 6, 6, 5, 37, 17,
        3, 7, 7, 15, 47, 27,
        3, 8, 7, 21, 1, 33,
        4, 3, 4, 40, 20, 52,
        4, 4, 5, 43, 23, 3,
        4, 5, 6, 46, 26, 6,
        4, 6, 7, 34, 14, 46,
        4, 7, 8, 29, 9, 41,
    ];

    /// <summary>
    /// Gets the CC-B sizes of Table 5-41 of the GS1 General Specifications, which "lists all the possible column and row combinations for CC-B", two values
    /// each: the data columns and the rows. The sizes of one column count stand in order of capacity.
    /// </summary>
    private static ReadOnlySpan<byte> CcBSizes =>
    [
        2, 17, 2, 20, 2, 23, 2, 26,
        3, 15, 3, 20, 3, 26, 3, 32, 3, 38, 3, 44,
        4, 10, 4, 12, 4, 15, 4, 20, 4, 26, 4, 32, 4, 38, 4, 44,
    ];

    /// <summary>
    /// Gets the letters of Table 3, whose four bit code is their index.
    /// </summary>
    private static ReadOnlySpan<char> Table3Letters => "BDHIJKLNPQRSTVWZ";

    /// <summary>
    /// Gets the punctuation characters ISO/IEC 646 mode encodes, in the order of their values from 232 in Table 7.
    /// </summary>
    private static ReadOnlySpan<char> Iso646Punctuation => "!\"%&'()*+,-./:;<=>?_ ";

    /// <summary>
    /// Encodes the element strings and builds the 2D component.
    /// </summary>
    /// <param name="text">The element strings, in the element string syntax.</param>
    /// <param name="component">The smallest 2D component the symbol takes.</param>
    /// <param name="columns">The data columns: 2 to 4 for CC-A and CC-B, 1 to 30 for CC-C.</param>
    /// <param name="ccCColumns">The data columns of CC-C when a CC-B symbol cannot hold the data, or 0 when the symbol cannot take CC-C.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <returns>The 2D component the symbol took.</returns>
    /// <exception cref="ArgumentException">The text is not valid element string syntax, carries a character the bit stream cannot encode, or does not fit the
    /// largest symbol.</exception>
    public static Gs1CompositeComponent Build(
        ReadOnlySpan<char> text,
        Gs1CompositeComponent component,
        int columns,
        int ccCColumns,
        double xDimension,
        ref BarcodeSymbol symbol)
    {
        // The length check runs before the buffers take their size from the text.
        _ = Gs1Data.PreparedCapacity(text, MaximumPrepared);

        // An element string is at least five characters of text, so the text holds at most a fifth of its length in element strings. The general purpose field
        // drops the parentheses and adds at most one separator after each element string, so it is never longer than the text.
        InlineArray16<Gs1Element> elementStorage = default;
        int elementCapacity = (text.Length / 5) + 1;
        ValueListBuilder<Gs1Element> elements = elementCapacity <= Gs1Data.InlineElementCount ? new(elementStorage) : new(elementCapacity);
        PooledBuffer<char> field = new(text.Length);
        PooledBuffer<byte> stream = new(MaximumBits / 8);
        try
        {
            Gs1Data.Parse(text, ref elements);
            Gs1Data.Validate(text, elements.AsSpan());

            stream.Span.Clear();
            BitStream bits = new(stream.Span);
            int fieldLength = EncodeLeadingFields(text, elements.AsSpan(), ref bits, field.Span, out FieldMode mode);
            mode = EncodeGeneralField(field.Span[..fieldLength], mode, ref bits, out char lastDigit);

            int c = columns;
            int rows;
            int level;
            if (lastDigit != '\0')
            {
                // Section 5.4.1 c) covers a single digit left at the end. The remaining bit count comes from the smallest symbol that holds the bit stream so
                // far. When four to six bits remain, the digit takes four bits as its value plus one. The fifth and sixth bits are zero. Otherwise, the digit
                // takes seven bits as a pair with a separator.
                int remaining = Capacity(bits.Position, ref component, ref c, ccCColumns, out _, out _) - bits.Position;
                if (remaining is >= 4 and <= 6)
                {
                    bits.Append(lastDigit - '0' + 1, 4);
                    bits.Append(0, remaining - 4);
                }
                else
                {
                    bits.Append(((lastDigit - '0') * 11) + 10 + 8, 7);
                }
            }

            int capacity = Capacity(bits.Position, ref component, ref c, ccCColumns, out rows, out level);
            if (capacity < 0)
            {
                throw new ArgumentException(
                    $"The element strings take {bits.Position} bits; the 2D component of a GS1 Composite symbol holds fewer.",
                    nameof(text));
            }

            // Section 5.4.4: the unused bits repeat 00100, the last copy cut on the right. A field that ends in numeric mode first latches out of it with 0000,
            // and one that ends in alpha mode with the separator 11111 and 0000, each cut short when fewer bits remain.
            int pad = capacity - bits.Position;
            if (mode == FieldMode.Numeric)
            {
                bits.Append(0, Math.Min(4, pad));
            }
            else if (mode == FieldMode.Alpha)
            {
                int latch = Math.Min(9, pad);
                bits.Append(0b111110000 >> (9 - latch), latch);
            }

            for (int k = 0; bits.Position < capacity; k = (k + 1) % 5)
            {
                bits.Append(k == 2 ? 1 : 0, 1);
            }

            BuildSymbol(ref bits, component, c, rows, level, xDimension, ref symbol);
            return component;
        }
        finally
        {
            elements.Dispose();
            field.Dispose();
            stream.Dispose();
        }
    }

    /// <summary>
    /// Writes the encodation method field of section 5.2 and the compressed field of section 5.3, and writes the characters the general purpose field carries:
    /// what the compressed field leaves of the element strings, each Application Identifier and its data, with a separator after every variable length element
    /// string but the last.
    /// </summary>
    /// <param name="text">The element strings.</param>
    /// <param name="elements">The parsed element strings.</param>
    /// <param name="bits">The bit stream.</param>
    /// <param name="field">The buffer the characters of the general purpose field are written to, as long as the text.</param>
    /// <param name="mode">The mode the general purpose field starts in.</param>
    /// <returns>The number of characters of the general purpose field.</returns>
    private static int EncodeLeadingFields(
        scoped ReadOnlySpan<char> text,
        scoped ReadOnlySpan<Gs1Element> elements,
        ref BitStream bits,
        scoped Span<char> field,
        out FieldMode mode)
    {
        int written = 0;
        int next = 0;
        mode = FieldMode.Numeric;
        ReadOnlySpan<char> first = elements[0].Identifier(text);
        ReadOnlySpan<char> firstData = elements[0].Data(text);
        if (first.SequenceEqual("10") || first.SequenceEqual("11") || first.SequenceEqual("17"))
        {
            // Section 5.3.1, method "10": a production date (11) or an expiration date (17) takes 16 bits and a bit that names its Application Identifier,
            // and "11" stands for no date.
            bits.Append(0b10, 2);
            if (first[1] != '0')
            {
                int year = ((firstData[0] - '0') * 10) + (firstData[1] - '0');
                int month = ((firstData[2] - '0') * 10) + (firstData[3] - '0');
                int day = ((firstData[4] - '0') * 10) + (firstData[5] - '0');
                bits.Append((year * 384) + ((month - 1) * 32) + day, 16);
                bits.Append(first[1] == '7' ? 1 : 0, 1);
                next = 1;
            }
            else
            {
                bits.Append(0b11, 2);
            }

            // A batch or lot number (10) next to the date carries its data alone, and a separator ends it when more data follows. Section 5.3.1: when no lot
            // number follows the date directly, a separator follows the date, also when no other data follows. The separator is not transmitted.
            if (next < elements.Length && elements[next].Identifier(text).SequenceEqual("10"))
            {
                written = Append(field, written, elements[next].Data(text));
                next++;
                if (next < elements.Length)
                {
                    field[written++] = Gs1Data.Separator;
                }
            }
            else
            {
                field[written++] = Gs1Data.Separator;
            }
        }
        else if (first.SequenceEqual("90") && Ai90Prefix(firstData, out int digits))
        {
            // Section 5.3.2, method "11": the data of (90) starts with up to three digits and a capital letter. The rest of it takes the scheme that suits it
            // best, and a serial number (21) or an (8004) right after it carries its data alone.
            bits.Append(0b11, 2);
            ReadOnlySpan<char> rest = firstData[(digits + 1)..];
            int letters = 0;
            int numbers = 0;
            foreach (char c in rest)
            {
                letters += char.IsAsciiLetterUpper(c) ? 1 : 0;
                numbers += char.IsAsciiDigit(c) ? 1 : 0;
            }

            // Section 5.3.3: alpha mode suits a rest of capital letters and digits with more letters than digits. A rest of digits alone takes numeric mode,
            // and any other rest alphanumeric mode.
            if (letters + numbers == rest.Length && letters > numbers)
            {
                mode = FieldMode.Alpha;
                bits.Append(0b11, 2);
            }
            else if (letters + numbers == rest.Length && letters == 0)
            {
                bits.Append(0b10, 2);
            }
            else
            {
                mode = FieldMode.Alphanumeric;
                bits.Append(0, 1);
            }

            ReadOnlySpan<char> second = elements.Length > 1 ? elements[1].Identifier(text) : [];
            bool implied = second.SequenceEqual("21") || second.SequenceEqual("8004");
            if (implied)
            {
                bits.Append(second.SequenceEqual("21") ? 0b10 : 0b11, 2);
            }
            else
            {
                bits.Append(0, 1);
            }

            // The digits and the letter take nine bits when the number is under 31 and Table 3 holds the letter, and otherwise 11111, ten bits and five.
            int number = 0;
            for (int i = 0; i < digits; i++)
            {
                number = (number * 10) + (firstData[i] - '0');
            }

            char letter = firstData[digits];
            int code = Table3Letters.IndexOf(letter);
            if (number < 31 && code >= 0)
            {
                bits.Append(number, 5);
                bits.Append(code, 4);
            }
            else
            {
                bits.Append(0b11111, 5);
                bits.Append(number, 10);
                bits.Append(letter - 'A', 5);
            }

            written = Append(field, written, rest);
            next = 1;
            if (next < elements.Length)
            {
                field[written++] = Gs1Data.Separator;
            }

            if (implied)
            {
                written = Append(field, written, elements[1].Data(text));
                next = 2;
                if (next < elements.Length)
                {
                    field[written++] = Gs1Data.Separator;
                }
            }
        }
        else
        {
            // Section 5.2, method "0": the general purpose field carries all the data.
            bits.Append(0, 1);
        }

        for (int i = next; i < elements.Length; i++)
        {
            written = Append(field, written, elements[i].Identifier(text));
            written = Append(field, written, elements[i].Data(text));
            if (i < elements.Length - 1 && !elements[i].Entry.PredefinedLength)
            {
                field[written++] = Gs1Data.Separator;
            }
        }

        return written;
    }

    /// <summary>
    /// Returns whether the data of Application Identifier (90) starts as method "11" of section 5.3.2 requires: zero to three digits without a leading zero,
    /// then a capital letter.
    /// </summary>
    /// <param name="data">The data of (90).</param>
    /// <param name="digits">The number of leading digits.</param>
    /// <returns><see langword="true"/> when the data starts that way.</returns>
    private static bool Ai90Prefix(ReadOnlySpan<char> data, out int digits)
    {
        digits = 0;
        while (digits < 3 && digits < data.Length && char.IsAsciiDigit(data[digits]) && !(digits == 0 && data[0] == '0'))
        {
            digits++;
        }

        return digits < data.Length && char.IsAsciiLetterUpper(data[digits]);
    }

    /// <summary>
    /// Copies characters into the general purpose field.
    /// </summary>
    /// <param name="field">The general purpose field.</param>
    /// <param name="written">The number of characters written before.</param>
    /// <param name="characters">The characters.</param>
    /// <returns>The number of characters written after.</returns>
    private static int Append(Span<char> field, int written, ReadOnlySpan<char> characters)
    {
        characters.CopyTo(field[written..]);
        return written + characters.Length;
    }

    /// <summary>
    /// Encodes the general purpose field of section 5.4 from a mode. Numeric mode carries two digits or a digit and a separator per seven bits, alphanumeric
    /// mode a character per five or six bits, and ISO/IEC 646 mode a character per five, seven or eight bits. Each mode latches to another when the characters
    /// ahead favour it. Alpha mode, section 5.3.3, carries the rest of (90) up to its separator, which latches to numeric mode.
    /// </summary>
    /// <param name="text">The characters of the general purpose field.</param>
    /// <param name="mode">The mode at the start of the field.</param>
    /// <param name="bits">The bit stream.</param>
    /// <param name="lastDigit">A single digit left at the end in numeric mode, or zero.</param>
    /// <returns>The mode at the end of the field.</returns>
    /// <exception cref="ArgumentException">The field holds a character the bit stream cannot carry.</exception>
    private static FieldMode EncodeGeneralField(ReadOnlySpan<char> text, FieldMode mode, ref BitStream bits, out char lastDigit)
    {
        // The latch decisions look up to ten characters ahead, so every character is classified once. The field is bounded by the text, so a short field
        // works inline and a longer one rents once.
        InlineArray128<FieldMode> classStorage = default;
        using PooledBuffer<FieldMode> rented = text.Length <= 128 ? default : new(text.Length);
        Span<FieldMode> classes = text.Length <= 128 ? ((Span<FieldMode>)classStorage)[..text.Length] : rented.Span[..text.Length];
        for (int k = 0; k < text.Length; k++)
        {
            classes[k] = ClassOf(text[k]);
            if (classes[k] == FieldMode.None)
            {
                throw new ArgumentException($"The 2D component of a GS1 Composite symbol cannot carry the character U+{(int)text[k]:X4}.", nameof(text));
            }
        }

        lastDigit = '\0';
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            FieldMode type = classes[i];
            switch (mode)
            {
                case FieldMode.Numeric:
                    if (i < text.Length - 1)
                    {
                        // Table 5: two characters take V = 11 x D1 + D2 + 8, the separator counting as 10. Anything else latches with 0000.
                        if (type != FieldMode.Numeric || classes[i + 1] != FieldMode.Numeric)
                        {
                            bits.Append(0b0000, 4);
                            mode = FieldMode.Alphanumeric;
                        }
                        else
                        {
                            bits.Append((11 * NumericValue(c)) + NumericValue(text[i + 1]) + 8, 7);
                            i += 2;
                        }
                    }
                    else if (!char.IsAsciiDigit(c))
                    {
                        bits.Append(0b0000, 4);
                        mode = FieldMode.Alphanumeric;
                    }
                    else
                    {
                        lastDigit = c;
                        i++;
                    }

                    break;

                case FieldMode.Alpha:
                    // Table 4: a capital letter is its code less 65 in five bits, a digit its code plus 4 in six bits, and the separator 31 in five bits, which
                    // latches to numeric mode.
                    if (c == Gs1Data.Separator)
                    {
                        bits.Append(0b11111, 5);
                        mode = FieldMode.Numeric;
                    }
                    else if (char.IsAsciiDigit(c))
                    {
                        bits.Append(c + 4, 6);
                    }
                    else
                    {
                        bits.Append(c - 'A', 5);
                    }

                    i++;
                    break;

                case FieldMode.Alphanumeric:
                    // Section 5.4.2: a separator encodes here, an ISO/IEC 646 character latches to that mode, and six numeric characters ahead, or four or more
                    // that end the field, latch to numeric mode.
                    if (c == Gs1Data.Separator)
                    {
                        bits.Append(0b01111, 5);
                        mode = FieldMode.Numeric;
                        i++;
                    }
                    else if (type == FieldMode.Iso646)
                    {
                        bits.Append(0b00100, 5);
                        mode = FieldMode.Iso646;
                    }
                    else if (Next(classes, i, 6, FieldMode.Numeric) || EndsNumeric(classes, i))
                    {
                        bits.Append(0b000, 3);
                        mode = FieldMode.Numeric;
                    }
                    else if (char.IsAsciiDigit(c))
                    {
                        bits.Append(c - 43, 5);
                        i++;
                    }
                    else if (char.IsAsciiLetterUpper(c))
                    {
                        bits.Append(c - 33, 6);
                        i++;
                    }
                    else
                    {
                        bits.Append("*,-./".IndexOf(c) + 58, 6);
                        i++;
                    }

                    break;

                default:
                    // Section 5.4.3: a separator encodes here. With no ISO/IEC 646 only character in the next ten, four numeric characters ahead latch to
                    // numeric mode and five alphanumeric ones to alphanumeric mode.
                    if (c == Gs1Data.Separator)
                    {
                        bits.Append(0b01111, 5);
                        mode = FieldMode.Numeric;
                        i++;
                        break;
                    }

                    bool noIso646Ahead = NoneAhead(classes, i, 10, FieldMode.Iso646);
                    if (noIso646Ahead && Next(classes, i, 4, FieldMode.Numeric))
                    {
                        bits.Append(0b000, 3);
                        mode = FieldMode.Numeric;
                    }
                    else if (noIso646Ahead && Next(classes, i, 5, FieldMode.Alphanumeric, FieldMode.Numeric))
                    {
                        bits.Append(0b00100, 5);
                        mode = FieldMode.Alphanumeric;
                    }
                    else if (char.IsAsciiDigit(c))
                    {
                        bits.Append(c - 43, 5);
                        i++;
                    }
                    else if (char.IsAsciiLetterUpper(c))
                    {
                        bits.Append(c - 1, 7);
                        i++;
                    }
                    else if (char.IsAsciiLetterLower(c))
                    {
                        bits.Append(c - 7, 7);
                        i++;
                    }
                    else
                    {
                        bits.Append(Iso646Punctuation.IndexOf(c) + 232, 8);
                        i++;
                    }

                    break;
            }
        }

        return mode;
    }

    /// <summary>
    /// Returns the bit capacity of the smallest symbol that holds a number of bits, and takes the next larger 2D component when none of the current one does:
    /// CC-B after CC-A, as section 4.4 requires, and CC-C after CC-B when the symbol can take it.
    /// </summary>
    /// <param name="used">The number of bits.</param>
    /// <param name="component">The 2D component, updated when it grows.</param>
    /// <param name="columns">The data columns, updated when the symbol takes CC-C.</param>
    /// <param name="ccCColumns">The data columns of CC-C, or 0 when the symbol cannot take CC-C.</param>
    /// <param name="rows">The rows of the symbol.</param>
    /// <param name="level">The error correction level of CC-C.</param>
    /// <returns>The bit capacity, or -1 when no symbol holds the bits.</returns>
    private static int Capacity(int used, ref Gs1CompositeComponent component, ref int columns, int ccCColumns, out int rows, out int level)
    {
        level = 0;
        while (true)
        {
            int capacity = component switch
            {
                Gs1CompositeComponent.CcA => CcACapacity(used, columns, out rows),
                Gs1CompositeComponent.CcB => CcBCapacity(used, columns, out rows),
                _ => CcCCapacity(used, ref columns, out rows, out level),
            };

            if (capacity >= 0)
            {
                return capacity;
            }

            if (component == Gs1CompositeComponent.CcA)
            {
                component = Gs1CompositeComponent.CcB;
            }
            else if (component == Gs1CompositeComponent.CcB && ccCColumns > 0)
            {
                component = Gs1CompositeComponent.CcC;
                columns = ccCColumns;
            }
            else
            {
                return -1;
            }
        }
    }

    /// <summary>
    /// Returns the bit capacity of the smallest CC-A of a column count that holds a number of bits. Table 12 converts the data codewords of a size into bits:
    /// each full group of seven codewords carries 69 bits, and k more codewords carry 10k - 1.
    /// </summary>
    /// <param name="used">The number of bits.</param>
    /// <param name="columns">The data columns.</param>
    /// <param name="rows">The rows of the size.</param>
    /// <returns>The bit capacity, or -1 when no size holds the bits.</returns>
    private static int CcACapacity(int used, int columns, out int rows)
    {
        ReadOnlySpan<byte> sizes = CcASizes;
        for (int offset = 0; offset < sizes.Length; offset += MicroPdf417Encoder.SymbolSize.Length)
        {
            MicroPdf417Encoder.SymbolSize size = new(sizes.Slice(offset, MicroPdf417Encoder.SymbolSize.Length));
            if (size.Columns != columns)
            {
                continue;
            }

            int data = (size.Columns * size.Rows) - size.ErrorCorrection;
            int remainder = data % Base928GroupCodewords;
            int capacity = (Base928GroupBits * (data / Base928GroupCodewords)) + (remainder == 0 ? 0 : (10 * remainder) - 1);
            if (capacity >= used)
            {
                rows = size.Rows;
                return capacity;
            }
        }

        rows = 0;
        return -1;
    }

    /// <summary>
    /// Returns the bit capacity of the smallest CC-B of a column count that holds a number of bits. The data codewords of a size less the codeword 920 and the
    /// byte latch carry six bytes per five codewords and one byte per codeword after the last group.
    /// </summary>
    /// <param name="used">The number of bits.</param>
    /// <param name="columns">The data columns.</param>
    /// <param name="rows">The rows of the size.</param>
    /// <returns>The bit capacity, or -1 when no size holds the bits.</returns>
    private static int CcBCapacity(int used, int columns, out int rows)
    {
        ReadOnlySpan<byte> sizes = CcBSizes;
        for (int offset = 0; offset < sizes.Length; offset += 2)
        {
            if (sizes[offset] != columns)
            {
                continue;
            }

            int capacity = 8 * Pdf417Codewords.ByteCapacity(MicroPdf417Encoder.DataCodewords(columns, sizes[offset + 1]) - 2);
            if (capacity >= used)
            {
                rows = sizes[offset + 1];
                return capacity;
            }
        }

        rows = 0;
        return -1;
    }

    /// <summary>
    /// Returns the bit capacity of the CC-C that holds a number of bits. The error correction level is the recommended minimum of Table E.1 of ISO/IEC 15438
    /// for the data codewords, clause 10 g), lowered where the largest symbol leaves too little room. The symbol takes the fewest rows of its columns. It takes
    /// more columns when it needs more than 30 rows, and fewer columns when all its columns reach four times its rows.
    /// </summary>
    /// <param name="used">The number of bits.</param>
    /// <param name="columns">The data columns, updated when more columns are needed.</param>
    /// <param name="rows">The rows of the symbol.</param>
    /// <param name="level">The error correction level.</param>
    /// <returns>The bit capacity, or -1 when no symbol holds the bits.</returns>
    private static int CcCCapacity(int used, ref int columns, out int rows, out int level)
    {
        // The data codewords are the codeword 920, the byte latch and the codewords of the bytes. With the Symbol Length Descriptor and the error correction
        // codewords they fill the matrix.
        int bytes = (used + 7) / 8;
        int data = 2 + Pdf417Codewords.ByteCodewords(bytes);
        int largest = MaximumCcCColumns * MaximumCcCRows;
        level = Pdf417Encoder.RecommendedErrorCorrectionLevel(data);
        while (level > 0 && data + 1 + (2 << level) > largest)
        {
            level--;
        }

        int total = data + 1 + (2 << level);
        while (columns < MaximumCcCColumns && (total + columns - 1) / columns > MaximumCcCRows)
        {
            columns++;
        }

        rows = Math.Max(MinimumCcCRows, (total + columns - 1) / columns);
        if (rows > MaximumCcCRows)
        {
            return -1;
        }

        // Clause 10: "the total number of CC-C columns shall be less than four times the number of rows in the component". The total counts the start and
        // stop patterns and the two row indicators. Fewer columns give more rows, so the symbol drops columns until the rule holds.
        while (columns > MinimumCcCColumns && columns + CcCOverheadColumns >= 4 * rows)
        {
            columns--;
            rows = Math.Max(MinimumCcCRows, (total + columns - 1) / columns);
        }

        return 8 * Pdf417Codewords.ByteCapacity((columns * rows) - (2 << level) - 3);
    }

    /// <summary>
    /// Converts the padded bit stream into codewords and builds the symbol of the chosen size.
    /// </summary>
    /// <param name="bits">The padded bit stream.</param>
    /// <param name="component">The 2D component.</param>
    /// <param name="columns">The data columns.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="level">The error correction level of CC-C.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="symbol">The symbol to write.</param>
    private static void BuildSymbol(
        ref BitStream bits,
        Gs1CompositeComponent component,
        int columns,
        int rows,
        int level,
        double xDimension,
        ref BarcodeSymbol symbol)
    {
        if (component == Gs1CompositeComponent.CcA)
        {
            MicroPdf417Encoder.SymbolSize size = MicroPdf417Encoder.FindSize(CcASizes, columns, rows);

            // Section 8.5: each group of up to 69 bits, read as one number with its first bit the most significant, converts to base 928, the most significant
            // codeword first. A group of b bits takes b / 10 + 1 codewords. A CC-A holds at most 28 codewords, so they fit inline.
            int k = size.ErrorCorrection;
            InlineArray32<int> codewordStorage = default;
            Span<int> codewords = ((Span<int>)codewordStorage)[..(columns * rows)];
            int written = 0;
            for (int start = 0; start < bits.Position; start += Base928GroupBits)
            {
                int count = Math.Min(Base928GroupBits, bits.Position - start);
                UInt128 value = 0;
                for (int b = start; b < start + count; b++)
                {
                    value = (value << 1) | (uint)bits.Bit(b);
                }

                int groupCodewords = (count / 10) + 1;
                for (int i = groupCodewords - 1; i >= 0; i--)
                {
                    codewords[written + i] = (int)(value % Base928);
                    value /= Base928;
                }

                written += groupCodewords;
            }

            InlineArray16<int> alphaStorage = default;
            Pdf417Codewords.AppendErrorCorrection(codewords, k, alphaStorage);

            MicroPdf417Encoder.AddressPatterns patterns = new(size.LeftPattern, size.CentrePattern, size.RightPattern, columns != 3);
            MicroPdf417Encoder.BuildRows(codewords, columns, rows, patterns, xDimension, RowHeight, 0F, ref symbol);
            return;
        }

        // Clauses 9 and 10: the codeword 920, the latch to Byte Compaction mode, and the bytes of the bit stream. Byte Compaction mode takes the latch 924 for
        // a byte count that is a multiple of six and 901 otherwise.
        int byteCount = bits.Position / 8;
        ValueListBuilder<int> data = new(byteCount + 2);
        try
        {
            data.Append(CompositeCodeword);
            data.Append(Pdf417Codewords.ByteLatchFor(byteCount));
            Pdf417Codewords.EncodeBytes(bits.Bytes[..byteCount], ref data);

            if (component == Gs1CompositeComponent.CcB)
            {
                MicroPdf417Encoder.BuildSymbol(data.AsSpan(), columns, rows, xDimension, RowHeight, 0F, ref symbol);
            }
            else
            {
                Pdf417Encoder.BuildSymbol(data.AsSpan(), columns, level, xDimension, 0F, ref symbol);
            }
        }
        finally
        {
            data.Dispose();
        }
    }

    /// <summary>
    /// Returns the numeric mode value of a digit, or 10 for the separator.
    /// </summary>
    /// <param name="c">The digit or the separator.</param>
    /// <returns>The value.</returns>
    private static int NumericValue(char c)
        => c == Gs1Data.Separator ? 10 : c - '0';

    /// <summary>
    /// Returns the character class of a general purpose field character: numeric for a digit or the separator, alphanumeric for a capital letter or one of <c>*
    /// , - . /</c>, ISO/IEC 646 for a small letter, a space or the other punctuation of Table 7, and none for a character the field cannot carry.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>The class.</returns>
    private static FieldMode ClassOf(char c)
    {
        if (c == Gs1Data.Separator || char.IsAsciiDigit(c))
        {
            return FieldMode.Numeric;
        }

        if (char.IsAsciiLetterUpper(c) || c is '*' or ',' or '-' or '.' or '/')
        {
            return FieldMode.Alphanumeric;
        }

        return char.IsAsciiLetterLower(c) || Iso646Punctuation.Contains(c) ? FieldMode.Iso646 : FieldMode.None;
    }

    /// <summary>
    /// Returns whether the next characters, from the given one on, are all of one class.
    /// </summary>
    /// <param name="classes">The class of every character of the field.</param>
    /// <param name="start">The first character.</param>
    /// <param name="count">The number of characters.</param>
    /// <param name="type">The class.</param>
    /// <returns>Whether that many characters remain and all are of the class.</returns>
    private static bool Next(ReadOnlySpan<FieldMode> classes, int start, int count, FieldMode type)
        => Next(classes, start, count, type, type);

    /// <summary>
    /// Returns whether the next characters, from the given one on, are all of one of two classes.
    /// </summary>
    /// <param name="classes">The class of every character of the field.</param>
    /// <param name="start">The first character.</param>
    /// <param name="count">The number of characters.</param>
    /// <param name="type">The class.</param>
    /// <param name="alternative">The second class.</param>
    /// <returns>Whether that many characters remain and each is of one of the classes.</returns>
    private static bool Next(ReadOnlySpan<FieldMode> classes, int start, int count, FieldMode type, FieldMode alternative)
    {
        if (start + count > classes.Length)
        {
            return false;
        }

        foreach (FieldMode c in classes.Slice(start, count))
        {
            if (c != type && c != alternative)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether four or five numeric characters end the field from the given one on.
    /// </summary>
    /// <param name="classes">The class of every character of the field.</param>
    /// <param name="start">The first character.</param>
    /// <returns>Whether the rest of the field is four or five numeric characters.</returns>
    private static bool EndsNumeric(ReadOnlySpan<FieldMode> classes, int start)
        => classes.Length - start is 4 or 5 && Next(classes, start, classes.Length - start, FieldMode.Numeric);

    /// <summary>
    /// Returns whether none of the next characters, or the characters to the end, are of a class.
    /// </summary>
    /// <param name="classes">The class of every character of the field.</param>
    /// <param name="start">The first character.</param>
    /// <param name="count">The number of characters.</param>
    /// <param name="type">The class.</param>
    /// <returns>Whether none of them is of the class.</returns>
    private static bool NoneAhead(ReadOnlySpan<FieldMode> classes, int start, int count, FieldMode type)
        => !classes[start..Math.Min(start + count, classes.Length)].Contains(type);

    /// <summary>
    /// A bit stream packed eight bits to a byte, the first bit in the most significant bit of the first byte.
    /// </summary>
    private ref struct BitStream
    {
        /// <summary>
        /// The bytes of the stream, cleared before the first bit.
        /// </summary>
        private readonly Span<byte> bytes;

        /// <summary>
        /// Initializes a new instance of the <see cref="BitStream"/> struct.
        /// </summary>
        /// <param name="bytes">The bytes of the stream, cleared.</param>
        public BitStream(Span<byte> bytes) => this.bytes = bytes;

        /// <summary>
        /// Gets the number of bits written.
        /// </summary>
        public int Position { get; private set; }

        /// <summary>
        /// Gets the bytes of the stream, the first bit in the most significant bit of the first byte.
        /// </summary>
        public readonly ReadOnlySpan<byte> Bytes => this.bytes;

        /// <summary>
        /// Appends a value as a number of bits, the most significant first.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="count">The number of bits.</param>
        /// <exception cref="ArgumentException">The stream is longer than the largest symbol holds.</exception>
        public void Append(int value, int count)
        {
            if (this.Position + count > this.bytes.Length * 8)
            {
                throw new ArgumentException($"The data takes more than {MaximumBits} bits, the most a GS1 Composite 2D component holds.");
            }

            for (int i = count - 1; i >= 0; i--)
            {
                if (((value >> i) & 1) != 0)
                {
                    this.bytes[this.Position >> 3] |= (byte)(0x80 >> (this.Position & 7));
                }

                this.Position++;
            }
        }

        /// <summary>
        /// Returns one bit of the stream.
        /// </summary>
        /// <param name="position">The index of the bit.</param>
        /// <returns>The bit, 0 or 1.</returns>
        public readonly int Bit(int position) => (this.bytes[position >> 3] >> (7 - (position & 7))) & 1;
    }
}
