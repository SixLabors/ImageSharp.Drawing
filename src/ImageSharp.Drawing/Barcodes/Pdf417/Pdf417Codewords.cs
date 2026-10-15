// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The high level encodation and the error correction codewords of ISO/IEC 15438, sections 5.4 and 5.10,
/// which PDF417 and MicroPDF417 share. The data is a sequence of byte values: the characters U+0000 to
/// U+00FF are the byte values of the default character set of Annex B.
/// </summary>
internal static class Pdf417Codewords
{
    /// <summary>
    /// The largest number of characters a symbol carries: 2 710 digits in Numeric Compaction mode at error
    /// correction level 0, section 5.1.1 c).
    /// </summary>
    public const int MaximumLength = 2710;

    /// <summary>
    /// The largest number of characters a MicroPDF417 symbol carries.
    /// </summary>
    public const int MicroMaximumLength = 366;

    /// <summary>
    /// The number of codewords a caller holds inline for <see cref="Encode"/> to build in. Data this long
    /// covers short labels, and longer data rents its buffer once.
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
    private const int ByteLatch = 901;

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
    private const int ByteLatchSix = 924;

    /// <summary>
    /// The MicroPDF417 codeword that stands for the header [)&gt;RS05GS and the trailer RS EOT of a
    /// message in format 05, and that puts the encoder in Numeric Compaction mode.
    /// </summary>
    private const int Macro05 = 916;

    /// <summary>
    /// The MicroPDF417 codeword that stands for the header [)&gt;RS06GS and the trailer RS EOT of a
    /// message in format 06, and that puts the encoder in Text Compaction mode, Mixed sub-mode.
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
    /// The number of characters from which a run of text characters encodes in Text Compaction mode, Annex N
    /// step 11.
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
    /// The Text Compaction mode state of the encoder.
    /// </summary>
    private const int TextState = 0;

    /// <summary>
    /// The Numeric Compaction mode state of the encoder.
    /// </summary>
    private const int NumericState = 1;

    /// <summary>
    /// The Byte Compaction mode state of the encoder.
    /// </summary>
    private const int ByteState = 2;

    /// <summary>
    /// The Alpha sub-mode of Text Compaction mode, Table 5.
    /// </summary>
    private const int Alpha = 0;

    /// <summary>
    /// The Lower sub-mode of Text Compaction mode, Table 5.
    /// </summary>
    private const int Lower = 1;

    /// <summary>
    /// The Mixed sub-mode of Text Compaction mode, Table 5.
    /// </summary>
    private const int Mixed = 2;

    /// <summary>
    /// The Punctuation sub-mode of Text Compaction mode, Table 5.
    /// </summary>
    private const int Punctuation = 3;

    /// <summary>
    /// The number of Text Compaction sub-modes.
    /// </summary>
    private const int SubModes = 4;

    /// <summary>
    /// The text token for the latch to the Alpha sub-mode. Characters are tokens 0 to 255, and the latches
    /// and shifts are negative tokens.
    /// </summary>
    private const int LatchAlpha = -1;

    /// <summary>
    /// The text token for the latch to the Lower sub-mode.
    /// </summary>
    private const int LatchLower = -2;

    /// <summary>
    /// The text token for the latch to the Mixed sub-mode.
    /// </summary>
    private const int LatchMixed = -3;

    /// <summary>
    /// The text token for the latch to the Punctuation sub-mode.
    /// </summary>
    private const int LatchPunctuation = -4;

    /// <summary>
    /// The text token for the shift to the Alpha sub-mode.
    /// </summary>
    private const int ShiftAlpha = -5;

    /// <summary>
    /// The text token for the shift to the Punctuation sub-mode.
    /// </summary>
    private const int ShiftPunctuation = -6;

    /// <summary>
    /// The cost of a sub-mode that the encoder cannot reach. It leaves room to add a latch or a shift
    /// without overflow.
    /// </summary>
    private const int Unreachable = int.MaxValue / 2;

    /// <summary>
    /// The characters of the Mixed sub-mode with the values 0 to 24, Table 5. The space is value 26.
    /// </summary>
    private const string MixedCharacters = "0123456789&\r\t,:#-.$/+%*=^";

    /// <summary>
    /// The characters of the Punctuation sub-mode with the values 0 to 28, Table 5.
    /// </summary>
    private const string PunctuationCharacters = ";<>@[\\]_`~!\r\t,:\n-.$/\"|*()?{}'";

    /// <summary>
    /// The Text Compaction values of the characters 0 to 127 in each sub-mode, at index
    /// <c>(subMode * 128) + character</c>, or -1 where the sub-mode has no such character.
    /// </summary>
    private static readonly sbyte[] TextValues = CreateTextValues();

    /// <summary>
    /// Encodes the data into data codewords, without the Symbol Length Descriptor. Digit runs encode in
    /// Numeric Compaction mode, text runs in Text Compaction mode and the rest in Byte Compaction mode,
    /// following the run lengths of Annex N. A text run takes the fewest Text Compaction values its
    /// sub-mode latches and shifts allow.
    /// </summary>
    /// <param name="text">The data, the characters U+0000 to U+00FF.</param>
    /// <param name="codewords">The builder the data codewords are written to.</param>
    /// <exception cref="ArgumentException">The data is empty, too long or carries a character above U+00FF.</exception>
    public static void Encode(ReadOnlySpan<char> text, ref ValueListBuilder<int> codewords)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        // Section 5.4.2.1: every symbol starts in Text Compaction mode, Alpha sub-mode.
        EncodeFrom(text, TextState, 0, ref codewords);
    }

    /// <summary>
    /// Encodes the data into MicroPDF417 data codewords. The encodation is the one of <see cref="Encode"/>,
    /// except that a symbol starts in Byte Compaction mode, so the first run always takes its latch. A
    /// message in format 05 or 06, between the header [)&gt;RS05GS or [)&gt;RS06GS and the trailer RS EOT,
    /// takes one codeword for the header and the trailer, which also stands for the latch to the mode
    /// the format starts in.
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
            && (text[5] == '5' || text[5] == '6')
            && text[6] == '\u001D'
            && text.EndsWith("\u001E\u0004"))
        {
            macro = text[5] == '5' ? Macro05 : Macro06;
            codewords.Append(macro);
            text = text[MacroHeaderLength..^MacroTrailerLength];
        }

        EncodeFrom(text, ByteState, macro, ref codewords);
    }

    /// <summary>
    /// Encodes the data into data codewords from the given compaction mode.
    /// </summary>
    /// <param name="text">The data, the characters U+0000 to U+00FF.</param>
    /// <param name="state">The compaction mode the symbol starts in.</param>
    /// <param name="macro">The MicroPDF417 macro codeword already written in front of the data, or 0.</param>
    /// <param name="codewords">The builder the data codewords are written to.</param>
    /// <exception cref="ArgumentException">The data carries a character above U+00FF.</exception>
    private static void EncodeFrom(ReadOnlySpan<char> text, int state, int macro, ref ValueListBuilder<int> codewords)
    {
        SpanCodePointEnumerator codePoints = text.EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (current.Value > byte.MaxValue)
            {
                throw new ArgumentException(
                    $"PDF417 carries only the characters U+0000 to U+00FF; got {current.ToDisplayString()}.",
                    nameof(text));
            }
        }

        // The run tables take length + 1 entries each, the last one the zero that ends every run. The
        // text tables take four sub-mode entries per character for the latch sources and the shift
        // targets of the search, and three tokens per character, the most a character can need: a latch
        // of two values and the character itself. The tables of a short text stay inline, and a longer
        // text rents them once; the text is bounded by the maximum length of the symbology.
        int length = text.Length;
        int runLength = length + 1;
        int size = (3 * runLength) + (2 * SubModes * length) + (3 * length) + 1;
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
            Span<int> search = tables.Slice(runLength * 3, 2 * SubModes * length);
            Span<int> tokens = tables.Slice((runLength * 3) + (2 * SubModes * length), (3 * length) + 1);

            // Each table holds the length of the run that starts at a character, counted from the end so
            // every entry reads the one after it. A text run stops where a run of digits long enough for
            // Numeric Compaction mode starts, and a byte run stops where either of the other runs does.
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

            // Data that is all digits encodes in Numeric Compaction mode from eight digits. After a macro
            // codeword the message is more than its digits, so the rule does not apply.
            int wholeLength = macro == 0 ? length : -1;

            // A latch to Text Compaction mode returns to the Alpha sub-mode, section 5.4.2.1.
            int subMode = Alpha;
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
                    state = NumericState;
                    position += digitRun;
                    continue;
                }

                int textRun = texts[position];
                if (textRun >= TextRun)
                {
                    if (state != TextState)
                    {
                        // The format 06 macro codeword stands for the latch of a first text run, and
                        // leaves the encoder in the Mixed sub-mode.
                        if (macro == Macro06 && position == 0)
                        {
                            subMode = Mixed;
                        }
                        else
                        {
                            codewords.Append(TextLatch);
                            subMode = Alpha;
                        }
                    }

                    EncodeText(text.Slice(position, textRun), ref subMode, search, tokens, ref codewords);
                    state = TextState;
                    position += textRun;
                    continue;
                }

                // Section 5.4.3.1: the mode shift takes a single byte out of Text Compaction mode, and the
                // two latches take a byte count that is or is not a multiple of six.
                int byteRun = bytes[position];
                if (byteRun == 1 && state == TextState)
                {
                    codewords.Append(ByteShift);
                    codewords.Append(text[position]);
                    position++;
                    continue;
                }

                codewords.Append(byteRun % ByteGroup == 0 ? ByteLatchSix : ByteLatch);
                EncodeBytes(text.Slice(position, byteRun), ref codewords);
                state = ByteState;
                position += byteRun;
            }
        }
        finally
        {
            rented.Dispose();
        }
    }

    /// <summary>
    /// Calculates the error correction codewords of section 5.10 over the data codewords and writes them
    /// after the data, the most significant first.
    /// </summary>
    /// <param name="codewords">The data codewords followed by room for the error correction codewords.</param>
    /// <param name="count">The number of error correction codewords, 2 to 512.</param>
    public static void AppendErrorCorrection(Span<int> codewords, int count)
    {
        // The generator polynomial g(x) = (x - 3)(x - 3^2) ... (x - 3^k), expanded one factor at a time.
        // Multiplying by (x - r) gives each coefficient as the coefficient below it less r times itself,
        // so the update runs from the top down in place. The coefficients of x^0 to x^(k-1) are the
        // alpha values of Annex F; the coefficient of x^k is 1. Each factor reads the coefficient above the
        // product before it writes it, so the coefficients start zero. Levels 0 to 5 take at most 64
        // coefficients, which stay inline; a higher level rents its coefficients once.
        InlineArray65<int> alphaStorage = default;
        PooledBuffer<int> rented = default;
        bool inline = count < 65;
        Span<int> alpha = inline
            ? ((Span<int>)alphaStorage)[..(count + 1)]
            : (rented = new PooledBuffer<int>(count + 1)).Span;

        try
        {
            // The inline coefficients start zero; a rented array holds the values of its last use.
            if (!inline)
            {
                alpha.Clear();
            }

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

            // The division of d(x) x^k by g(x) of section 5.10, step 3, with the error correction
            // codewords E(k-1) to E(0) as the register. The register lives in place of the error
            // correction codewords, E(j) at index dataCount + k - 1 - j, so the codewords come out most
            // significant first.
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
            for (int j = 0; j < count; j++)
            {
                if (register[j] != 0)
                {
                    register[j] = Modulus - register[j];
                }
            }
        }
        finally
        {
            rented.Dispose();
        }
    }

    /// <summary>
    /// Encodes a run of digits in Numeric Compaction mode, section 5.4.4.2: each group of up to 44 digits
    /// takes a leading 1 and converts from base 10 to base 900 as Annex D gives.
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

            // Annex D: the codewords are the remainders of repeated division by 900, the least
            // significant first. Each pass divides the decimal digits in place by long division, which
            // keeps a quotient digit of at most 9 in each place, and drops the leading zeros it leaves.
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
    /// Encodes a run of bytes in Byte Compaction mode, section 5.4.3.3: each group of six bytes converts
    /// from base 256 to five base 900 codewords as Annex C gives, and the bytes after the last full group
    /// take one codeword each.
    /// </summary>
    /// <param name="bytes">The bytes, each a character U+0000 to U+00FF.</param>
    /// <param name="codewords">The builder the codewords are written to.</param>
    private static void EncodeBytes(ReadOnlySpan<char> bytes, ref ValueListBuilder<int> codewords)
    {
        int position = 0;
        for (; position + ByteGroup <= bytes.Length; position += ByteGroup)
        {
            // Six bytes are at most 2^48 - 1, which a long holds. The five codewords come out of the
            // division the least significant first and are written the most significant first.
            long value = 0;
            for (int i = 0; i < ByteGroup; i++)
            {
                value = (value << 8) | bytes[position + i];
            }

            Span<int> group = codewords.AppendSpan(ByteGroupCodewords);
            for (int i = ByteGroupCodewords - 1; i >= 0; i--)
            {
                group[i] = (int)(value % CodewordBase);
                value /= CodewordBase;
            }
        }

        for (; position < bytes.Length; position++)
        {
            codewords.Append(bytes[position]);
        }
    }

    /// <summary>
    /// Encodes a run of text characters in Text Compaction mode, section 5.4.2. The search keeps, for
    /// each sub-mode, the fewest base 30 values that encode the characters so far and end in that
    /// sub-mode. Before each character every sub-mode may latch to another; the character then extends a
    /// sub-mode that holds it, or a sub-mode that shifts to one that does. The run takes the cheapest
    /// end, and an odd count of values takes the pad of section 5.4.2.4.
    /// </summary>
    /// <param name="text">The characters, each one that Table 5 holds.</param>
    /// <param name="subMode">The sub-mode in effect, updated to the sub-mode the run ends in.</param>
    /// <param name="search">The scratch table of the search, eight entries per character.</param>
    /// <param name="tokens">The scratch table of the chosen tokens, three per character and one more.</param>
    /// <param name="codewords">The builder the codewords are written to.</param>
    private static void EncodeText(
        ReadOnlySpan<char> text,
        ref int subMode,
        scoped Span<int> search,
        scoped Span<int> tokens,
        ref ValueListBuilder<int> codewords)
    {
        int length = text.Length;

        // For character i, latches[(i * 4) + s] is the sub-mode that sub-mode s latched from before the
        // character, or -1, and shifts[(i * 4) + s] is the sub-mode that s shifted to for the character, or
        // -1 when s holds the character itself. A latch or a shift never changes the sub-mode the
        // character leaves the encoder in, so these two tables are the whole path.
        Span<int> latches = search[..(SubModes * length)];
        Span<int> shifts = search.Slice(SubModes * length, SubModes * length);
        InlineArray4<int> costStorage = default;
        InlineArray4<int> nextStorage = default;
        Span<int> cost = costStorage;
        Span<int> next = nextStorage;
        cost.Fill(Unreachable);
        cost[subMode] = 0;

        for (int i = 0; i < length; i++)
        {
            Span<int> latchFrom = latches.Slice(i * SubModes, SubModes);
            Span<int> shiftTo = shifts.Slice(i * SubModes, SubModes);
            latchFrom.Fill(-1);

            // A latch can lead to a sub-mode that latches again, so the relaxation repeats until no
            // sub-mode gets cheaper. Each update lowers a cost, so it ends.
            bool improved = true;
            while (improved)
            {
                improved = false;
                for (int from = 0; from < SubModes; from++)
                {
                    for (int to = 0; to < SubModes; to++)
                    {
                        int latched = cost[from] + LatchLength(from, to);
                        if (latched < cost[to])
                        {
                            cost[to] = latched;
                            latchFrom[to] = from;
                            improved = true;
                        }
                    }
                }
            }

            int c = text[i];
            next.Fill(Unreachable);
            for (int holder = 0; holder < SubModes; holder++)
            {
                if (TextValue(holder, c) < 0)
                {
                    continue;
                }

                int direct = cost[holder] + 1;
                if (direct < next[holder])
                {
                    next[holder] = direct;
                    shiftTo[holder] = -1;
                }

                for (int from = 0; from < SubModes; from++)
                {
                    if (from == holder || !CanShift(from, holder))
                    {
                        continue;
                    }

                    int shifted = cost[from] + 2;
                    if (shifted < next[from])
                    {
                        next[from] = shifted;
                        shiftTo[from] = holder;
                    }
                }
            }

            next.CopyTo(cost);
        }

        int best = 0;
        for (int s = 1; s < SubModes; s++)
        {
            if (cost[s] < cost[best])
            {
                best = s;
            }
        }

        // Walk the path back from the cheapest end, writing the tokens from the end of the table: each
        // character, the shift before it if any, and then the latches taken before it.
        int count = 0;
        int state = best;
        for (int i = length - 1; i >= 0; i--)
        {
            tokens[tokens.Length - 1 - count++] = text[i];
            int target = shifts[(i * SubModes) + state];
            if (target >= 0)
            {
                tokens[tokens.Length - 1 - count++] = target == Alpha ? ShiftAlpha : ShiftPunctuation;
            }

            int from = latches[(i * SubModes) + state];
            while (from >= 0)
            {
                count = PrependLatch(tokens, count, from, state);
                state = from;
                from = latches[(i * SubModes) + state];
            }
        }

        // Pair the values into codewords, h x 30 + l, section 5.4.2.2.
        int high = -1;
        int current = subMode;
        ReadOnlySpan<int> path = tokens[^count..];
        for (int i = 0; i < path.Length; i++)
        {
            int token = path[i];
            if (token == ShiftAlpha || token == ShiftPunctuation)
            {
                AppendValue(TokenValue(current, token), ref high, ref codewords);
                AppendValue(TextValue(token == ShiftAlpha ? Alpha : Punctuation, path[++i]), ref high, ref codewords);
            }
            else if (token >= 0)
            {
                AppendValue(TextValue(current, token), ref high, ref codewords);
            }
            else
            {
                AppendValue(TokenValue(current, token), ref high, ref codewords);
                current = LatchTarget(token);
            }
        }

        // Section 5.4.2.4: an odd count of values takes a pad. The Punctuation sub-mode has only the
        // latch to Alpha, which takes effect; every other sub-mode pads with the shift to Punctuation,
        // which has no character after it.
        if (high >= 0)
        {
            if (current == Punctuation)
            {
                AppendValue(TokenValue(current, LatchAlpha), ref high, ref codewords);
                current = Alpha;
            }
            else
            {
                AppendValue(TokenValue(current, ShiftPunctuation), ref high, ref codewords);
            }
        }

        subMode = current;
    }

    /// <summary>
    /// Adds one base 30 value, completing a codeword when it is the second of a pair.
    /// </summary>
    /// <param name="value">The value, 0 to 29.</param>
    /// <param name="high">The first value of the open pair, or -1 when no pair is open.</param>
    /// <param name="codewords">The builder the codewords are written to.</param>
    private static void AppendValue(int value, ref int high, ref ValueListBuilder<int> codewords)
    {
        if (high < 0)
        {
            high = value;
            return;
        }

        codewords.Append((high * TextBase) + value);
        high = -1;
    }

    /// <summary>
    /// Writes the latch tokens from one sub-mode to another in front of the tokens written so far.
    /// </summary>
    /// <param name="tokens">The token table, filled from the end.</param>
    /// <param name="count">The number of tokens written so far.</param>
    /// <param name="from">The sub-mode the latch starts in.</param>
    /// <param name="to">The sub-mode the latch ends in.</param>
    /// <returns>The number of tokens written.</returns>
    private static int PrependLatch(Span<int> tokens, int count, int from, int to)
    {
        // Table 7 latches directly between most sub-modes. The rest pass through a sub-mode that latches
        // onward: Lower reaches Alpha and Punctuation through Mixed, Alpha reaches Punctuation through
        // Mixed, and Punctuation reaches Lower and Mixed through Alpha.
        int second = to switch
        {
            Alpha => LatchAlpha,
            Lower => LatchLower,
            Mixed => LatchMixed,
            _ => LatchPunctuation,
        };

        tokens[tokens.Length - 1 - count++] = second;
        if (LatchLength(from, to) == 2)
        {
            tokens[tokens.Length - 1 - count++] = from == Punctuation ? LatchAlpha : LatchMixed;
        }

        return count;
    }

    /// <summary>
    /// Returns the number of base 30 values the latch from one sub-mode to another takes, Table 7, and
    /// zero for a sub-mode to itself.
    /// </summary>
    /// <param name="from">The sub-mode the latch starts in.</param>
    /// <param name="to">The sub-mode the latch ends in.</param>
    /// <returns>The number of values.</returns>
    private static int LatchLength(int from, int to)
        => (from, to) switch
        {
            _ when from == to => 0,
            (Lower, Alpha) or (Lower, Punctuation) or (Alpha, Punctuation) => 2,
            (Punctuation, Lower) or (Punctuation, Mixed) => 2,
            _ => 1,
        };

    /// <summary>
    /// Returns whether a sub-mode shifts to another for one character, Table 7: Lower shifts to Alpha,
    /// and every sub-mode but Punctuation shifts to Punctuation.
    /// </summary>
    /// <param name="from">The sub-mode the shift starts in.</param>
    /// <param name="to">The sub-mode the character is encoded in.</param>
    /// <returns><see langword="true"/> when the shift exists.</returns>
    private static bool CanShift(int from, int to)
        => (to == Punctuation && from != Punctuation) || (to == Alpha && from == Lower);

    /// <summary>
    /// Returns the sub-mode a latch token ends in.
    /// </summary>
    /// <param name="token">The latch token.</param>
    /// <returns>The sub-mode.</returns>
    private static int LatchTarget(int token)
        => token switch
        {
            LatchAlpha => Alpha,
            LatchLower => Lower,
            LatchMixed => Mixed,
            _ => Punctuation,
        };

    /// <summary>
    /// Returns the base 30 value of a latch or shift token in a sub-mode, Table 5.
    /// </summary>
    /// <param name="subMode">The sub-mode the token is encoded in.</param>
    /// <param name="token">The latch or shift token.</param>
    /// <returns>The value.</returns>
    private static int TokenValue(int subMode, int token)
        => (subMode, token) switch
        {
            (Mixed, LatchPunctuation) => 25,
            (Alpha, LatchLower) or (Lower, ShiftAlpha) or (Mixed, LatchLower) => 27,
            (Alpha, LatchMixed) or (Lower, LatchMixed) or (Mixed, LatchAlpha) => 28,
            _ => 29,
        };

    /// <summary>
    /// Returns the base 30 value of a character in a sub-mode, Table 5.
    /// </summary>
    /// <param name="subMode">The sub-mode.</param>
    /// <param name="c">The character, U+0000 to U+00FF.</param>
    /// <returns>The value, or -1 when the sub-mode does not hold the character.</returns>
    private static int TextValue(int subMode, int c)
        => c < 128 ? TextValues[(subMode << 7) + c] : -1;

    /// <summary>
    /// Returns whether Text Compaction mode holds a character in any of its sub-modes.
    /// </summary>
    /// <param name="c">The character, U+0000 to U+00FF.</param>
    /// <returns><see langword="true"/> when a sub-mode holds the character.</returns>
    private static bool IsText(int c)
        => TextValue(Alpha, c) >= 0 || TextValue(Lower, c) >= 0 || TextValue(Mixed, c) >= 0 || TextValue(Punctuation, c) >= 0;

    /// <summary>
    /// Builds the table of Text Compaction values from Table 5.
    /// </summary>
    /// <returns>The table.</returns>
    private static sbyte[] CreateTextValues()
    {
        sbyte[] values = new sbyte[SubModes * 128];
        values.AsSpan().Fill(-1);
        for (int i = 0; i < 26; i++)
        {
            values[(Alpha << 7) + 'A' + i] = (sbyte)i;
            values[(Lower << 7) + 'a' + i] = (sbyte)i;
        }

        values[(Alpha << 7) + ' '] = 26;
        values[(Lower << 7) + ' '] = 26;
        values[(Mixed << 7) + ' '] = 26;
        for (int i = 0; i < MixedCharacters.Length; i++)
        {
            values[(Mixed << 7) + MixedCharacters[i]] = (sbyte)i;
        }

        for (int i = 0; i < PunctuationCharacters.Length; i++)
        {
            values[(Punctuation << 7) + PunctuationCharacters[i]] = (sbyte)i;
        }

        return values;
    }
}
