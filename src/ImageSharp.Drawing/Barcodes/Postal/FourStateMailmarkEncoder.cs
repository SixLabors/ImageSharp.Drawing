// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the Royal Mail 4-state Mailmark barcodes C and L, from the Royal Mail Mailmark barcode
/// C and barcode L encoding and decoding instructions, release 1b. Section 2.2 converts the application
/// string to six external user fields, each to an integer internal user field, the six to one
/// consolidated data value, the value to data numbers of 30 or 32 values, adds Reed-Solomon check
/// numbers over a Galois field of 32 values, converts the numbers to six-bit symbols, reorders the
/// symbols into extender groups and maps the bits of every group to the ascenders and descenders of
/// three bars.
/// </summary>
internal static class FourStateMailmarkEncoder
{
    /// <summary>
    /// The length of the application string of a Mailmark barcode C.
    /// </summary>
    public const int LengthC = 22;

    /// <summary>
    /// The length of the application string of a Mailmark barcode L.
    /// </summary>
    public const int LengthL = 26;

    /// <summary>
    /// The number of bars in a group, which carries one six-bit extender group.
    /// </summary>
    public const int BarsPerGroup = 3;

    /// <summary>
    /// The allowed characters of the Format field, Table 2, in value order.
    /// </summary>
    public const string FormatCharacters = "01234";

    /// <summary>
    /// The allowed characters of the Version ID field, Table 2, in value order. Section 2.2.1: the
    /// encoding process "is valid only for cases in which the value of the Version ID External User
    /// Field is 1".
    /// </summary>
    public const string VersionCharacters = "1234";

    /// <summary>
    /// The allowed characters of the Class field, Table 2, in value order.
    /// </summary>
    public const string ClassCharacters = "0123456789ABCDE";

    /// <summary>
    /// The full alphabetic character type of a domestic sorting code, Table 3.
    /// </summary>
    public const string FullAlphabetic = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>
    /// The limited alphabetic character type of a domestic sorting code, Table 3.
    /// </summary>
    public const string LimitedAlphabetic = "ABDEFGHJLNPQRSTUWXYZ";

    /// <summary>
    /// The international designation of the destination field, section 2.2.2: "a nine-character string
    /// with five trailing spaces", whose internal user field value is zero.
    /// </summary>
    public const string International = "XY11     ";

    /// <summary>
    /// The primitive polynomial of the Galois field, section 2.2.5: x^5 + x^2 + 1.
    /// </summary>
    private const int PrimitivePolynomial = 0b100101;

    private static readonly byte[] Exp = BuildExp();

    private static readonly byte[] Log = BuildLog();

    /// <summary>
    /// Table 5, the symbols of the numbers with 32 values and of the check numbers: the six-bit values
    /// with an odd number of binary 1s, in ascending order.
    /// </summary>
    private static readonly byte[] OddSymbols = BuildSymbols(true);

    /// <summary>
    /// Table 5, the symbols of the numbers with 30 values: the six-bit values with a non-zero, even
    /// number of binary 1s, in ascending order.
    /// </summary>
    private static readonly byte[] EvenSymbols = BuildSymbols(false);

    /// <summary>
    /// The six character patterns of a domestic sorting code, section 2.2.2, in the order the steps test
    /// them: F for full alphabetic, N for numeric, L for limited alphabetic and S for a space.
    /// </summary>
    private static readonly string[] PostcodePatterns =
    [
        "FNFNLLNLS",
        "FFNNLLNLS",
        "FFNNNLLNL",
        "FFNFNLLNL",
        "FNNLLNLSS",
        "FNNNLLNLS",
    ];

    /// <summary>
    /// Gets the value the accumulator holds before the characters of each pattern are added: 1 for the
    /// first pattern, and the steps 6, 8, 10, 12 and 14 add 5,408,000,000, 5,408,000,000, 54,080,000,000,
    /// 140,608,000,000 and 208,000,000 before the next.
    /// </summary>
    private static ReadOnlySpan<ulong> PostcodeOffsets =>
    [
        1UL,
        1UL + 5_408_000_000UL,
        1UL + 5_408_000_000UL + 5_408_000_000UL,
        1UL + 5_408_000_000UL + 5_408_000_000UL + 54_080_000_000UL,
        1UL + 5_408_000_000UL + 5_408_000_000UL + 54_080_000_000UL + 140_608_000_000UL,
        1UL + 5_408_000_000UL + 5_408_000_000UL + 54_080_000_000UL + 140_608_000_000UL + 208_000_000UL,
    ];

    /// <summary>
    /// Gets the generator polynomial of barcode C, section 2.2.5, from the x^6 term down:
    /// x^6 + 17x^5 + 26x^4 + 30x^3 + 27x^2 + 30x + 24.
    /// </summary>
    private static ReadOnlySpan<byte> GeneratorC => [1, 17, 26, 30, 27, 30, 24];

    /// <summary>
    /// Gets the generator polynomial of barcode L, section 2.2.5, from the x^7 term down:
    /// x^7 + 5x^6 + 9x^5 + 5x^4 + 26x^3 + 17x^2 + 25x + 22.
    /// </summary>
    private static ReadOnlySpan<byte> GeneratorL => [1, 5, 9, 5, 26, 17, 25, 22];

    /// <summary>
    /// Gets Table 6 of barcode C: the extender group of each data symbol D0 to D15 and check symbol C0
    /// to C5.
    /// </summary>
    private static ReadOnlySpan<byte> ExtenderGroupsC => [3, 5, 7, 11, 13, 14, 16, 17, 19, 0, 1, 2, 4, 6, 8, 9, 10, 12, 15, 18, 20, 21];

    /// <summary>
    /// Gets Table 6 of barcode L: the extender group of each data symbol D0 to D18 and check symbol C0
    /// to C6.
    /// </summary>
    private static ReadOnlySpan<byte> ExtenderGroupsL => [2, 5, 7, 8, 13, 14, 15, 16, 21, 22, 23, 0, 1, 3, 4, 6, 9, 10, 11, 12, 17, 18, 19, 20, 24, 25];

    /// <summary>
    /// Returns the number of bars of the barcode an application string selects by its length.
    /// </summary>
    /// <param name="length">The length of the application string.</param>
    /// <returns>66 for barcode C, 78 for barcode L, or 0 for any other length.</returns>
    public static int BarCount(int length) => length switch
    {
        LengthC => 66,
        LengthL => 78,
        _ => 0,
    };

    /// <summary>
    /// Encodes an application string into bar states.
    /// </summary>
    /// <param name="text">The application string, of 22 or 26 characters.</param>
    /// <param name="states">The buffer that receives the bar states.</param>
    public static void Encode(string text, Span<FourState> states)
    {
        bool isL = text.Length == LengthL;
        int supplyChainLength = isL ? 6 : 2;
        int itemIdLength = 8;

        int format = FieldValue(text, 0, 1, FormatCharacters, "Format");
        int version = FieldValue(text, 1, 1, VersionCharacters, "Version ID");
        if (text[1] != '1')
        {
            throw new ArgumentException($"The encoding is defined for Version ID 1 alone; got {text[1]}.", nameof(text));
        }

        int mailClass = FieldValue(text, 2, 1, ClassCharacters, "Class");
        int supplyChain = FieldValue(text, 3, supplyChainLength, "0123456789", "Supply Chain ID");
        int itemId = FieldValue(text, 3 + supplyChainLength, itemIdLength, "0123456789", "Item ID");
        ulong destination = DestinationValue(text.AsSpan(3 + supplyChainLength + itemIdLength, 9));

        UInt128 consolidated = destination;
        consolidated = (consolidated * 100_000_000U) + (uint)itemId;
        consolidated = (consolidated * (isL ? 1_000_000U : 100U)) + (uint)supplyChain;
        consolidated = (consolidated * 15U) + (uint)mailClass;
        consolidated = (consolidated * 5U) + (uint)format;
        consolidated = (consolidated * 4U) + (uint)version;

        int dataCount = isL ? 19 : 16;
        int checkCount = isL ? 7 : 6;
        int thirtyValued = isL ? 11 : 9;
        Span<byte> numbers = stackalloc byte[dataCount + checkCount];
        DataNumbers(consolidated, numbers[..dataCount], thirtyValued);
        CheckNumbers(numbers[..dataCount], isL ? GeneratorL : GeneratorC, numbers[dataCount..]);

        ReadOnlySpan<byte> groups = isL ? ExtenderGroupsL : ExtenderGroupsC;
        Span<byte> extenderGroups = stackalloc byte[numbers.Length];
        for (int i = 0; i < numbers.Length; i++)
        {
            extenderGroups[groups[i]] = i < thirtyValued ? EvenSymbols[numbers[i]] : OddSymbols[numbers[i]];
        }

        Bars(extenderGroups, states);
    }

    /// <summary>
    /// Section 2.2.2: converts the Destination Post Code plus DPS field to its internal user field value.
    /// The international designation is 0, and a domestic sorting code is the offset of its character
    /// pattern plus the mixed radix value of its non-space characters.
    /// </summary>
    /// <param name="field">The nine characters of the field.</param>
    /// <returns>The internal user field value.</returns>
    public static ulong DestinationValue(ReadOnlySpan<char> field)
    {
        if (field.SequenceEqual(International))
        {
            return 0;
        }

        for (int pattern = 0; pattern < PostcodePatterns.Length; pattern++)
        {
            string types = PostcodePatterns[pattern];
            ulong value = 0;
            bool matches = true;
            for (int i = 0; i < types.Length && matches; i++)
            {
                char c = field[i];
                switch (types[i])
                {
                    case 'F':
                        matches = c is >= 'A' and <= 'Z';
                        value = (value * 26) + (ulong)(c - 'A');
                        break;
                    case 'N':
                        matches = char.IsAsciiDigit(c);
                        value = (value * 10) + (ulong)(c - '0');
                        break;
                    case 'L':
                        int limited = LimitedAlphabetic.IndexOf(c);
                        matches = limited >= 0;
                        value = (value * 20) + (ulong)limited;
                        break;
                    default:
                        matches = c == ' ';
                        break;
                }
            }

            if (matches)
            {
                return PostcodeOffsets[pattern] + value;
            }
        }

        throw new ArgumentException($"The Destination Post Code plus DPS field is XY11 with five spaces or one of the six domestic patterns; got {field.ToString()}.", nameof(field));
    }

    /// <summary>
    /// Section 2.2.4: divides the consolidated data value into data numbers, the least significant
    /// first, by 32 for the numbers with 32 values and by 30 for the rest; the most significant number
    /// is the last quotient.
    /// </summary>
    /// <param name="consolidated">The consolidated data value.</param>
    /// <param name="numbers">The buffer that receives the data numbers, D0 first.</param>
    /// <param name="thirtyValued">The number of leading data numbers with 30 values.</param>
    public static void DataNumbers(UInt128 consolidated, Span<byte> numbers, int thirtyValued)
    {
        UInt128 x = consolidated;
        for (int i = numbers.Length - 1; i > 0; i--)
        {
            uint radix = i < thirtyValued ? 30U : 32U;
            numbers[i] = (byte)(x % radix);
            x /= radix;
        }

        numbers[0] = (byte)x;
    }

    /// <summary>
    /// Section 2.2.5: the check numbers are the remainder of the data polynomial, whose first number is
    /// its highest term, multiplied by x to the number of check numbers and divided by the generator
    /// polynomial over the Galois field of 32 values.
    /// </summary>
    /// <param name="data">The data numbers.</param>
    /// <param name="generator">The generator polynomial coefficients, highest term first.</param>
    /// <param name="check">The buffer that receives the check numbers, C0 first.</param>
    public static void CheckNumbers(ReadOnlySpan<byte> data, ReadOnlySpan<byte> generator, Span<byte> check)
    {
        int checkCount = generator.Length - 1;
        Span<byte> buffer = stackalloc byte[data.Length + checkCount];
        data.CopyTo(buffer);
        for (int i = 0; i < data.Length; i++)
        {
            byte coefficient = buffer[i];
            if (coefficient == 0)
            {
                continue;
            }

            for (int j = 0; j <= checkCount; j++)
            {
                buffer[i + j] ^= Multiply(generator[j], coefficient);
            }
        }

        buffer[data.Length..].CopyTo(check);
    }

    /// <summary>
    /// Section 2.2.8, Table 7: every extender group fills three bars. Its high three bits are the
    /// ascenders and its low three bits the descenders of an even numbered group, and the reverse of an
    /// odd numbered group, with the most significant bit at the leftmost bar.
    /// </summary>
    /// <param name="extenderGroups">The extender groups in physical order.</param>
    /// <param name="states">The buffer that receives the bar states.</param>
    public static void Bars(ReadOnlySpan<byte> extenderGroups, Span<FourState> states)
    {
        for (int group = 0; group < extenderGroups.Length; group++)
        {
            int high = extenderGroups[group] >> 3;
            int low = extenderGroups[group] & 7;
            int ascenders = (group & 1) == 0 ? high : low;
            int descenders = (group & 1) == 0 ? low : high;
            for (int bar = 0; bar < BarsPerGroup; bar++)
            {
                int bit = 2 - bar;
                bool ascender = ((ascenders >> bit) & 1) != 0;
                bool descender = ((descenders >> bit) & 1) != 0;
                states[(group * BarsPerGroup) + bar] = ascender && descender
                    ? FourState.Full
                    : ascender
                        ? FourState.Ascender
                        : descender
                            ? FourState.Descender
                            : FourState.Tracker;
            }
        }
    }

    /// <summary>
    /// Section 2.2.2: converts a field to its internal user field value, in which each allowed character
    /// has its index in the array of allowed characters as its value.
    /// </summary>
    private static int FieldValue(string text, int start, int length, string allowed, string fieldName)
    {
        int value = 0;
        for (int i = start; i < start + length; i++)
        {
            int index = allowed.IndexOf(text[i]);
            if (index < 0)
            {
                throw new ArgumentException($"The {fieldName} field carries [{allowed}]; got {text[i]}.", nameof(text));
            }

            value = (value * allowed.Length) + index;
        }

        return value;
    }

    private static byte Multiply(byte a, byte b)
        => a == 0 || b == 0 ? (byte)0 : Exp[(Log[a] + Log[b]) % 31];

    private static byte[] BuildExp()
    {
        byte[] exp = new byte[31];
        int value = 1;
        for (int i = 0; i < exp.Length; i++)
        {
            exp[i] = (byte)value;
            value <<= 1;
            if ((value & 32) != 0)
            {
                value ^= PrimitivePolynomial;
            }
        }

        return exp;
    }

    private static byte[] BuildLog()
    {
        byte[] log = new byte[32];
        for (int i = 0; i < Exp.Length; i++)
        {
            log[Exp[i]] = (byte)i;
        }

        return log;
    }

    private static byte[] BuildSymbols(bool odd)
    {
        byte[] symbols = new byte[odd ? 32 : 30];
        int count = 0;
        for (int value = 1; count < symbols.Length; value++)
        {
            if ((BitOperations.PopCount((uint)value) & 1) == (odd ? 1 : 0))
            {
                symbols[count++] = (byte)value;
            }
        }

        return symbols;
    }
}
