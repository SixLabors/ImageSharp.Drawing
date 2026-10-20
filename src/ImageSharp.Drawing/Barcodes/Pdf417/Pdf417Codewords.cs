// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The high level encodation and the error correction codewords of ISO/IEC 15438, sections 5.4 and 5.10, which PDF417 and MicroPDF417 share. The data is a
/// sequence of byte values: the characters U+0000 to U+00FF are the byte values of the default character set of Annex B.
/// </summary>
internal static class Pdf417Codewords
{
    /// <summary>
    /// The largest number of characters a symbol carries: 2 710 digits in Numeric Compaction mode at error correction level 0, section 5.1.1 c).
    /// </summary>
    public const int MaximumLength = 2710;

    /// <summary>
    /// The largest number of characters a MicroPDF417 symbol carries.
    /// </summary>
    public const int MicroMaximumLength = 366;

    /// <summary>
    /// The number of codewords a caller holds inline for <see cref="Encode"/> to build in. Data this long covers short labels, and longer data rents its buffer
    /// once.
    /// </summary>
    public const int InlineBufferLength = 64;

    /// <summary>
    /// The codeword that pads the data codewords to fill the symbol matrix, section 5.9.2.
    /// </summary>
    public const int Pad = 900;

    /// <summary>
    /// The modulus of the error correction arithmetic, section 5.10.
    /// </summary>
    public const int Modulus = 929;

    /// <summary>
    /// The largest number of error correction codewords, at error correction level 8, Table 9.
    /// </summary>
    public const int MaximumErrorCorrection = 512;

    /// <summary>
    /// The mode latch to Text Compaction mode, Table 2.
    /// </summary>
    private const int TextLatch = 900;

    /// <summary>
    /// The mode latch to Byte Compaction mode for a byte count that is not a multiple of six, Table 2.
    /// </summary>
    public const int ByteLatch = 901;

    /// <summary>
    /// The mode latch to Numeric Compaction mode, Table 2.
    /// </summary>
    private const int NumericLatch = 902;

    /// <summary>
    /// The mode shift to Byte Compaction mode for one byte, Table 2.
    /// </summary>
    private const int ByteShift = 913;

    /// <summary>
    /// The mode latch to Byte Compaction mode for a byte count that is a multiple of six, Table 2.
    /// </summary>
    public const int ByteLatchSix = 924;

    /// <summary>
    /// The MicroPDF417 codeword that stands for the header [)&gt;RS05GS and the trailer RS EOT of a message in format 05, and that puts the encoder in Numeric
    /// Compaction mode.
    /// </summary>
    private const int Macro05 = 916;

    /// <summary>
    /// The MicroPDF417 codeword that stands for the header [)&gt;RS06GS and the trailer RS EOT of a message in format 06, and that puts the encoder in Text
    /// Compaction mode, Mixed sub-mode.
    /// </summary>
    private const int Macro06 = 917;

    /// <summary>
    /// The length of the header of a message in format 05 or 06, [)&gt;RS05GS.
    /// </summary>
    private const int MacroHeaderLength = 7;

    /// <summary>
    /// The length of the trailer of a message in format 05 or 06, RS EOT.
    /// </summary>
    private const int MacroTrailerLength = 2;

    /// <summary>
    /// The number of digits from which a run of digits encodes in Numeric Compaction mode, Annex N step 4.
    /// </summary>
    private const int NumericRun = 13;

    /// <summary>
    /// The number of digits from which data that is all digits encodes in Numeric Compaction mode.
    /// </summary>
    private const int WholeNumericRun = 8;

    /// <summary>
    /// The number of characters from which a run of text characters encodes in Text Compaction mode, Annex N step 11.
    /// </summary>
    private const int TextRun = 5;

    /// <summary>
    /// The largest number of digits one base 10 to base 900 conversion takes, section 5.4.4.2.
    /// </summary>
    private const int NumericGroup = 44;

    /// <summary>
    /// The number of bytes one base 256 to base 900 conversion takes, section 5.4.3.3.
    /// </summary>
    private const int ByteGroup = 6;

    /// <summary>
    /// The number of codewords one conversion of <see cref="ByteGroup"/> bytes gives, section 5.4.3.3.
    /// </summary>
    private const int ByteGroupCodewords = 5;

    /// <summary>
    /// The base of Text Compaction mode values, section 5.4.2.2.
    /// </summary>
    private const int TextBase = 30;

    /// <summary>
    /// The base of the data codewords, section 5.4.
    /// </summary>
    private const int CodewordBase = 900;

    /// <summary>
    /// The number of Text Compaction sub-modes.
    /// </summary>
    private const int SubModes = 4;

    /// <summary>
    /// The number of characters each sub-mode has an entry for in <see cref="TextValues"/>.
    /// </summary>
    private const int TextTableWidth = 128;

    /// <summary>
    /// The base 30 value that pads an odd count of values, section 5.4.2.4: the latch to Alpha in the Punctuation sub-mode, which takes effect, and the shift
    /// to Punctuation in the other sub-modes, which has no character after it. Table 5 gives both the value 29.
    /// </summary>
    private const int PadValue = 29;

    /// <summary>
    /// The cost of a sub-mode that the encoder cannot reach. It leaves room to add a latch or a shift without overflow.
    /// </summary>
    private const int Unreachable = int.MaxValue / 2;

    /// <summary>
    /// The compaction modes of section 5.4.
    /// </summary>
    private enum CompactionMode
    {
        /// <summary>
        /// Text Compaction mode, section 5.4.2.
        /// </summary>
        Text,

        /// <summary>
        /// Numeric Compaction mode, section 5.4.4.
        /// </summary>
        Numeric,

        /// <summary>
        /// Byte Compaction mode, section 5.4.3.
        /// </summary>
        Byte,
    }

    /// <summary>
    /// The sub-modes of Text Compaction mode, Table 5. The values index the sub-mode tables.
    /// </summary>
    private enum SubMode
    {
        /// <summary>
        /// No sub-mode, in the search tables where a sub-mode took no latch or no shift.
        /// </summary>
        None = -1,

        /// <summary>
        /// The Alpha sub-mode: the capital letters and the space.
        /// </summary>
        Alpha,

        /// <summary>
        /// The Lower sub-mode: the small letters and the space.
        /// </summary>
        Lower,

        /// <summary>
        /// The Mixed sub-mode: the digits, the space and some punctuation.
        /// </summary>
        Mixed,

        /// <summary>
        /// The Punctuation sub-mode.
        /// </summary>
        Punctuation,
    }

    /// <summary>
    /// Gets the Text Compaction values of Table 5 of the characters 0 to 127 in each sub-mode, at index <c>(subMode * 128) + character</c>, or -1 where the
    /// sub-mode has no such character. The four blocks of 128 entries are the Alpha, Lower, Mixed and Punctuation sub-modes in turn.
    /// </summary>
    private static ReadOnlySpan<sbyte> TextValues =>
    [
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        26, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
        15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        26, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
        15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, 12, -1, -1, -1, 11, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        26, -1, -1, 15, 18, 21, 10, -1, -1, -1, 22, 20, 13, 16, 17, 19,
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 14, -1, -1, 23, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 24, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, 12, 15, -1, -1, 11, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, 10, 20, -1, 18, -1, -1, 28, 23, 24, 22, -1, 13, 16, 17, 19,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 14, 0, 1, -1, 2, 25,
        3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 4, 5, 6, -1, 7,
        8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 26, 21, 27, 9, -1,
    ];

    /// <summary>
    /// Encodes the data into data codewords, without the Symbol Length Descriptor. Digit runs encode in Numeric Compaction mode, text runs in Text Compaction
    /// mode and the rest in Byte Compaction mode, following the run lengths of Annex N. A text run takes the fewest Text Compaction values its sub-mode latches
    /// and shifts allow.
    /// </summary>
    /// <param name="text">The data, the characters U+0000 to U+00FF.</param>
    /// <param name="codewords">The builder the data codewords are written to.</param>
    /// <exception cref="ArgumentException">The data is empty, too long or carries a character above U+00FF.</exception>
    public static void Encode(ReadOnlySpan<char> text, ref ValueListBuilder<int> codewords)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        // Section 5.4.2.1: every symbol starts in Text Compaction mode, Alpha sub-mode.
        EncodeFrom(text, CompactionMode.Text, 0, ref codewords);
    }

    /// <summary>
    /// Encodes the data into MicroPDF417 data codewords. The encodation is the one of <see cref="Encode"/>, except that a symbol starts in Byte Compaction
    /// mode, so the first run always takes its latch. A message in format 05 or 06, between the header [)&gt;RS05GS or [)&gt;RS06GS and the trailer RS EOT,
    /// takes one codeword for the header and the trailer, which also stands for the latch to the mode the format starts in.
    /// </summary>
    /// <param name="text">The data, the characters U+0000 to U+00FF.</param>
    /// <param name="codewords">The builder the data codewords are written to.</param>
    /// <exception cref="ArgumentException">The data is empty, too long or carries a character above U+00FF.</exception>
    public static void EncodeMicro(ReadOnlySpan<char> text, ref ValueListBuilder<int> codewords)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MicroMaximumLength, nameof(text));

        int macro = 0;
        if (text.Length >= MacroHeaderLength + MacroTrailerLength
            && text.StartsWith("[)>\u001E0")
            && text[5] is '5' or '6'
            && text[6] == '\u001D'
            && text.EndsWith("\u001E\u0004"))
        {
            macro = text[5] == '5' ? Macro05 : Macro06;
            codewords.Append(macro);
            text = text[MacroHeaderLength..^MacroTrailerLength];
        }

        EncodeFrom(text, CompactionMode.Byte, macro, ref codewords);
    }

    /// <summary>
    /// Calculates the error correction codewords of section 5.10 over the data codewords and writes them after the data, the most significant first.
    /// </summary>
    /// <param name="codewords">The data codewords followed by room for the error correction codewords.</param>
    /// <param name="count">The number of error correction codewords, 2 to 512.</param>
    /// <param name="alpha">The scratch for the generator coefficients, at least <paramref name="count"/> plus one long.</param>
    public static void AppendErrorCorrection(Span<int> codewords, int count, Span<int> alpha)
    {
        // The generator polynomial g(x) = (x - 3)(x - 3^2) ... (x - 3^k) of section 5.10, expanded one factor at a time. Multiplying by (x - r) gives each
        // coefficient as the coefficient below it less r times itself, so the update runs from the top down in place. The coefficient of x^k is 1. Each factor
        // reads the coefficient above the product before it writes it, so the coefficients start zero.
        alpha = alpha[..(count + 1)];
        alpha.Clear();
        alpha[0] = 1;
        int root = 1;
        for (int degree = 1; degree <= count; degree++)
        {
            root = (root * 3) % Modulus;
            int negated = Modulus - root;
            for (int j = degree; j > 0; j--)
            {
                alpha[j] = (alpha[j - 1] + (negated * alpha[j])) % Modulus;
            }

            alpha[0] = (negated * alpha[0]) % Modulus;
        }

        // The division of d(x) x^k by g(x) of section 5.10, step 3, with the error correction codewords E(k-1) to E(0) as the register. The register lives in
        // place of the error correction codewords, E(j) at index dataCount + k - 1 - j, so the codewords come out most significant first.
        int dataCount = codewords.Length - count;
        Span<int> register = codewords[dataCount..];
        register.Clear();
        for (int i = 0; i < dataCount; i++)
        {
            int t1 = (codewords[i] + register[0]) % Modulus;
            for (int j = count - 1; j > 0; j--)
            {
                int t3 = Modulus - ((t1 * alpha[j]) % Modulus);
                register[count - 1 - j] = (register[count - j] + t3) % Modulus;
            }

            register[count - 1] = (Modulus - ((t1 * alpha[0]) % Modulus)) % Modulus;
        }

        // Step 4: each codeword is the complement of its register value.
        foreach (ref int value in register)
        {
            if (value != 0)
            {
                value = Modulus - value;
            }
        }
    }

    /// <summary>
    /// Encodes the data into data codewords from the given compaction mode.
    /// </summary>
    /// <param name="text">The data, the characters U+0000 to U+00FF.</param>
    /// <param name="mode">The compaction mode the symbol starts in.</param>
    /// <param name="macro">The MicroPDF417 macro codeword already written in front of the data, or 0.</param>
    /// <param name="codewords">The builder the data codewords are written to.</param>
    /// <exception cref="ArgumentException">The data carries a character above U+00FF.</exception>
    private static void EncodeFrom(ReadOnlySpan<char> text, CompactionMode mode, int macro, ref ValueListBuilder<int> codewords)
    {
        int invalid = text.IndexOfAnyExceptInRange('\0', 'ÿ');
        if (invalid >= 0)
        {
            throw new ArgumentException(
                $"PDF417 carries only the characters U+0000 to U+00FF; got {text.CodePointAt(invalid).ToDisplayString()}.",
                nameof(text));
        }

        // The run tables take length + 1 entries each, the last one the zero that ends every run. The text search takes one step per sub-mode and character,
        // two integers each. The text values take three per character: the cheapest path costs no more than a latch of two values and the character itself for
        // every character. The tables of a short text stay inline, and a longer text rents them once; the text is bounded by the maximum length of the
        // symbology.
        int length = text.Length;
        int runLength = length + 1;
        int searchLength = 2 * SubModes * length;
        int size = (3 * runLength) + searchLength + (3 * length) + 1;
        InlineArray256<int> tableStorage = default;
        PooledBuffer<int> rented = default;
        Span<int> tables = size <= 256
            ? ((Span<int>)tableStorage)[..size]
            : (rented = new PooledBuffer<int>(size)).Span;

        try
        {
            Span<int> digits = tables[..runLength];
            Span<int> texts = tables.Slice(runLength, runLength);
            Span<int> bytes = tables.Slice(runLength * 2, runLength);
            Span<SearchStep> search = MemoryMarshal.Cast<int, SearchStep>(tables.Slice(runLength * 3, searchLength));
            Span<int> path = tables.Slice((runLength * 3) + searchLength, (3 * length) + 1);

            // Each table holds the length of the run that starts at a character, counted from the end so every entry reads the one after it. A text run stops
            // where a run of digits long enough for Numeric Compaction mode starts, and a byte run stops where either of the other runs does.
            digits[length] = 0;
            texts[length] = 0;
            bytes[length] = 0;
            for (int i = length - 1; i >= 0; i--)
            {
                int c = text[i];
                digits[i] = char.IsAsciiDigit((char)c) ? digits[i + 1] + 1 : 0;
                texts[i] = IsText(c) && digits[i] < NumericRun ? texts[i + 1] + 1 : 0;
                bytes[i] = texts[i] < TextRun && digits[i] < NumericRun ? bytes[i + 1] + 1 : 0;
            }

            // Data that is all digits encodes in Numeric Compaction mode from eight digits. After a macro codeword the message is more than its digits, so the
            // rule does not apply.
            int wholeLength = macro == 0 ? length : -1;

            // A latch to Text Compaction mode returns to the Alpha sub-mode, section 5.4.2.1.
            SubMode subMode = SubMode.Alpha;
            int position = 0;
            while (position < length)
            {
                int digitRun = digits[position];
                if (digitRun >= NumericRun || (digitRun == wholeLength && digitRun >= WholeNumericRun))
                {
                    // The format 05 macro codeword stands for the latch of a first numeric run.
                    if (macro != Macro05 || position != 0)
                    {
                        codewords.Append(NumericLatch);
                    }

                    EncodeNumeric(text.Slice(position, digitRun), ref codewords);
                    mode = CompactionMode.Numeric;
                    position += digitRun;
                    continue;
                }

                int textRun = texts[position];
                if (textRun >= TextRun)
                {
                    if (mode != CompactionMode.Text)
                    {
                        // The format 06 macro codeword stands for the latch of a first text run, and leaves the encoder in the Mixed sub-mode.
                        if (macro == Macro06 && position == 0)
                        {
                            subMode = SubMode.Mixed;
                        }
                        else
                        {
                            codewords.Append(TextLatch);
                            subMode = SubMode.Alpha;
                        }
                    }

                    EncodeText(text.Slice(position, textRun), ref subMode, search, path, ref codewords);
                    mode = CompactionMode.Text;
                    position += textRun;
                    continue;
                }

                // Section 5.4.3.1: the mode shift takes a single byte out of Text Compaction mode, and the two latches take a byte count that is or is not a
                // multiple of six.
                int byteRun = bytes[position];
                if (byteRun == 1 && mode == CompactionMode.Text)
                {
                    codewords.Append(ByteShift);
                    codewords.Append(text[position]);
                    position++;
                    continue;
                }

                codewords.Append(byteRun % ByteGroup == 0 ? ByteLatchSix : ByteLatch);
                EncodeBytes(text.Slice(position, byteRun), ref codewords);
                mode = CompactionMode.Byte;
                position += byteRun;
            }
        }
        finally
        {
            rented.Dispose();
        }
    }

    /// <summary>
    /// Encodes a run of digits in Numeric Compaction mode, section 5.4.4.2: each group of up to 44 digits takes a leading 1 and converts from base 10 to base
    /// 900 as Annex D gives.
    /// </summary>
    /// <param name="digits">The digits.</param>
    /// <param name="codewords">The builder the codewords are written to.</param>
    private static void EncodeNumeric(ReadOnlySpan<char> digits, ref ValueListBuilder<int> codewords)
    {
        InlineArray64<byte> numberStorage = default;
        InlineArray16<int> groupStorage = default;
        Span<byte> number = ((Span<byte>)numberStorage)[..(NumericGroup + 1)];
        Span<int> group = ((Span<int>)groupStorage)[..((NumericGroup / 3) + 1)];
        for (int start = 0; start < digits.Length; start += NumericGroup)
        {
            int length = Math.Min(NumericGroup, digits.Length - start);
            number[0] = 1;
            for (int i = 0; i < length; i++)
            {
                number[i + 1] = (byte)(digits[start + i] - '0');
            }

            // Annex D: the codewords are the remainders of repeated division by 900, the least significant first. Each pass divides the decimal digits in place
            // by long division, which keeps a quotient digit of at most 9 in each place, and drops the leading zeros it leaves.
            int count = length + 1;
            int first = 0;
            int produced = 0;
            while (first < count)
            {
                int remainder = 0;
                for (int i = first; i < count; i++)
                {
                    int value = (remainder * 10) + number[i];
                    number[i] = (byte)(value / CodewordBase);
                    remainder = value % CodewordBase;
                }

                group[produced++] = remainder;
                while (first < count && number[first] == 0)
                {
                    first++;
                }
            }

            for (int i = produced - 1; i >= 0; i--)
            {
                codewords.Append(group[i]);
            }
        }
    }

    /// <summary>
    /// Encodes a run of bytes in Byte Compaction mode, section 5.4.3.3: each group of six bytes converts from base 256 to five base 900 codewords as Annex C
    /// gives, and the bytes after the last full group take one codeword each.
    /// </summary>
    /// <param name="bytes">The bytes, each a character U+0000 to U+00FF.</param>
    /// <param name="codewords">The builder the codewords are written to.</param>
    public static void EncodeBytes(ReadOnlySpan<char> bytes, ref ValueListBuilder<int> codewords)
    {
        int position = 0;
        for (; position + ByteGroup <= bytes.Length; position += ByteGroup)
        {
            // Six bytes are at most 2^48 - 1, which a long holds. The five codewords come out of the division the least significant first and are written the
            // most significant first.
            long value = 0;
            foreach (char b in bytes.Slice(position, ByteGroup))
            {
                value = (value << 8) | b;
            }

            Span<int> group = codewords.AppendSpan(ByteGroupCodewords);
            for (int i = ByteGroupCodewords - 1; i >= 0; i--)
            {
                group[i] = (int)(value % CodewordBase);
                value /= CodewordBase;
            }
        }

        foreach (char b in bytes[position..])
        {
            codewords.Append(b);
        }
    }

    /// <summary>
    /// Encodes a run of text characters in Text Compaction mode, section 5.4.2. The search keeps, for each sub-mode, the fewest base 30 values that encode the
    /// characters so far and end in that sub-mode. Before each character every sub-mode may latch to another; the character then extends a sub-mode that holds
    /// it, or a sub-mode that shifts to one that does. The run takes the cheapest end, and an odd count of values takes the pad of section 5.4.2.4.
    /// </summary>
    /// <param name="text">The characters, each one that Table 5 holds.</param>
    /// <param name="subMode">The sub-mode in effect, updated to the sub-mode the run ends in.</param>
    /// <param name="search">The scratch table of the search, one step per sub-mode and character.</param>
    /// <param name="path">The scratch table of the chosen base 30 values, three per character and one more.</param>
    /// <param name="codewords">The builder the codewords are written to.</param>
    private static void EncodeText(
        ReadOnlySpan<char> text,
        ref SubMode subMode,
        scoped Span<SearchStep> search,
        scoped Span<int> path,
        ref ValueListBuilder<int> codewords)
    {
        int length = text.Length;
        InlineArray4<int> costStorage = default;
        InlineArray4<int> nextStorage = default;
        Span<int> cost = costStorage;
        Span<int> next = nextStorage;
        cost.Fill(Unreachable);
        cost[(int)subMode] = 0;

        // A latch or a shift never changes the sub-mode the character leaves the encoder in, so the latch sources and the shift targets of each character are
        // the whole path.
        for (int i = 0; i < length; i++)
        {
            Span<SearchStep> steps = search.Slice(i * SubModes, SubModes);
            steps.Fill(new SearchStep { LatchFrom = SubMode.None, ShiftTo = SubMode.None });

            // A latch can lead to a sub-mode that latches again, so the relaxation repeats until no sub-mode gets cheaper. Each update lowers a cost, so it
            // ends.
            bool improved = true;
            while (improved)
            {
                improved = false;
                for (SubMode from = SubMode.Alpha; from <= SubMode.Punctuation; from++)
                {
                    for (SubMode to = SubMode.Alpha; to <= SubMode.Punctuation; to++)
                    {
                        int latched = cost[(int)from] + LatchLength(from, to);
                        if (latched < cost[(int)to])
                        {
                            cost[(int)to] = latched;
                            steps[(int)to].LatchFrom = from;
                            improved = true;
                        }
                    }
                }
            }

            int c = text[i];
            next.Fill(Unreachable);
            for (SubMode holder = SubMode.Alpha; holder <= SubMode.Punctuation; holder++)
            {
                if (TextValue(holder, c) < 0)
                {
                    continue;
                }

                int direct = cost[(int)holder] + 1;
                if (direct < next[(int)holder])
                {
                    next[(int)holder] = direct;
                    steps[(int)holder].ShiftTo = SubMode.None;
                }

                for (SubMode from = SubMode.Alpha; from <= SubMode.Punctuation; from++)
                {
                    if (from == holder || !CanShift(from, holder))
                    {
                        continue;
                    }

                    int shifted = cost[(int)from] + 2;
                    if (shifted < next[(int)from])
                    {
                        next[(int)from] = shifted;
                        steps[(int)from].ShiftTo = holder;
                    }
                }
            }

            next.CopyTo(cost);
        }

        SubMode best = SubMode.Alpha;
        for (SubMode s = SubMode.Lower; s <= SubMode.Punctuation; s++)
        {
            if (cost[(int)s] < cost[(int)best])
            {
                best = s;
            }
        }

        // Walk the path back from the cheapest end, writing its base 30 values from the end of the table: each character, the shift before it if any, and then
        // the latches taken before it. Table 7 latches directly between most sub-modes. The rest pass through a sub-mode that latches onward: Punctuation
        // through Alpha, and the others through Mixed.
        int count = 0;
        SubMode state = best;
        for (int i = length - 1; i >= 0; i--)
        {
            int c = text[i];
            SearchStep step = search[(i * SubModes) + (int)state];
            if (step.ShiftTo == SubMode.None)
            {
                path[path.Length - 1 - count++] = TextValue(state, c);
            }
            else
            {
                path[path.Length - 1 - count++] = TextValue(step.ShiftTo, c);
                path[path.Length - 1 - count++] = ShiftValue(step.ShiftTo);
            }

            SubMode from = step.LatchFrom;
            while (from != SubMode.None)
            {
                if (LatchLength(from, state) == 2)
                {
                    SubMode via = from == SubMode.Punctuation ? SubMode.Alpha : SubMode.Mixed;
                    path[path.Length - 1 - count++] = LatchValue(via, state);
                    path[path.Length - 1 - count++] = LatchValue(from, via);
                }
                else
                {
                    path[path.Length - 1 - count++] = LatchValue(from, state);
                }

                state = from;
                from = search[(i * SubModes) + (int)state].LatchFrom;
            }
        }

        // Pair the values into codewords, h x 30 + l, section 5.4.2.2. An odd count of values takes the pad, which in the Punctuation sub-mode is the latch to
        // Alpha and takes effect.
        ReadOnlySpan<int> values = path[^count..];
        for (int i = 0; i < count; i += 2)
        {
            int low = i + 1 < count ? values[i + 1] : PadValue;
            codewords.Append((values[i] * TextBase) + low);
        }

        subMode = (count & 1) != 0 && best == SubMode.Punctuation ? SubMode.Alpha : best;
    }

    /// <summary>
    /// Returns the number of base 30 values the latch from one sub-mode to another takes, Table 7, and zero for a sub-mode to itself.
    /// </summary>
    /// <param name="from">The sub-mode the latch starts in.</param>
    /// <param name="to">The sub-mode the latch ends in.</param>
    /// <returns>The number of values.</returns>
    private static int LatchLength(SubMode from, SubMode to)
        => (from, to) switch
        {
            _ when from == to => 0,
            (SubMode.Lower, SubMode.Alpha) or (SubMode.Lower, SubMode.Punctuation) or (SubMode.Alpha, SubMode.Punctuation) => 2,
            (SubMode.Punctuation, SubMode.Lower) or (SubMode.Punctuation, SubMode.Mixed) => 2,
            _ => 1,
        };

    /// <summary>
    /// Returns whether a sub-mode shifts to another for one character, Table 7: Lower shifts to Alpha, and every sub-mode but Punctuation shifts to
    /// Punctuation.
    /// </summary>
    /// <param name="from">The sub-mode the shift starts in.</param>
    /// <param name="to">The sub-mode the character is encoded in.</param>
    /// <returns><see langword="true"/> when the shift exists.</returns>
    private static bool CanShift(SubMode from, SubMode to)
        => (to == SubMode.Punctuation && from != SubMode.Punctuation) || (to == SubMode.Alpha && from == SubMode.Lower);

    /// <summary>
    /// Returns the base 30 value of a direct latch from one sub-mode to another, Table 5. Only the latches of length one in <see cref="LatchLength"/> are
    /// direct.
    /// </summary>
    /// <param name="from">The sub-mode the latch is encoded in.</param>
    /// <param name="to">The sub-mode the latch ends in.</param>
    /// <returns>The value.</returns>
    private static int LatchValue(SubMode from, SubMode to)
        => (from, to) switch
        {
            (SubMode.Mixed, SubMode.Punctuation) => 25,
            (SubMode.Alpha, SubMode.Lower) or (SubMode.Mixed, SubMode.Lower) => 27,
            (SubMode.Alpha, SubMode.Mixed) or (SubMode.Lower, SubMode.Mixed) or (SubMode.Mixed, SubMode.Alpha) => 28,
            _ => 29,
        };

    /// <summary>
    /// Returns the base 30 value of a shift, Table 5: the shift to Alpha, which only the Lower sub-mode has, is 27, and the shift to Punctuation is 29.
    /// </summary>
    /// <param name="to">The sub-mode the shifted character is encoded in.</param>
    /// <returns>The value.</returns>
    private static int ShiftValue(SubMode to) => to == SubMode.Alpha ? 27 : 29;

    /// <summary>
    /// Returns the base 30 value of a character in a sub-mode, Table 5.
    /// </summary>
    /// <param name="subMode">The sub-mode.</param>
    /// <param name="c">The character, U+0000 to U+00FF.</param>
    /// <returns>The value, or -1 when the sub-mode does not hold the character.</returns>
    private static int TextValue(SubMode subMode, int c)
        => c < TextTableWidth ? TextValues[((int)subMode * TextTableWidth) + c] : -1;

    /// <summary>
    /// Returns whether Text Compaction mode holds a character in any of its sub-modes.
    /// </summary>
    /// <param name="c">The character, U+0000 to U+00FF.</param>
    /// <returns><see langword="true"/> when a sub-mode holds the character.</returns>
    private static bool IsText(int c)
        => TextValue(SubMode.Alpha, c) >= 0 || TextValue(SubMode.Lower, c) >= 0 || TextValue(SubMode.Mixed, c) >= 0 || TextValue(SubMode.Punctuation, c) >= 0;

    /// <summary>
    /// One step of the Text Compaction search: how a sub-mode reached a character. The step is two integers, so the search shares the integer tables of the run
    /// lengths.
    /// </summary>
    private struct SearchStep
    {
        /// <summary>
        /// The sub-mode this sub-mode latched from before the character, or <see cref="SubMode.None"/>.
        /// </summary>
        public SubMode LatchFrom;

        /// <summary>
        /// The sub-mode this sub-mode shifted to for the character, or <see cref="SubMode.None"/> when it holds the character itself.
        /// </summary>
        public SubMode ShiftTo;
    }
}
