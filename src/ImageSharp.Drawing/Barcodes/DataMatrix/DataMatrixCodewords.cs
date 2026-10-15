// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The data encodation of Data Matrix ECC 200, ISO/IEC 16022. The data is a sequence of message values:
/// 0 to 255 are byte values, and the negative values <see cref="Fnc1"/>, <see cref="Macro05"/> and
/// <see cref="Macro06"/> are function characters. The encoder starts in ASCII encodation and moves between
/// the ASCII, C40, Text, X12, EDIFACT and Base 256 encodation schemes by a look-ahead test that compares
/// the codewords each scheme needs for the data that follows.
/// </summary>
internal static class DataMatrixCodewords
{
    /// <summary>
    /// The message value of the Function 1 Symbol Character.
    /// </summary>
    public const int Fnc1 = -1;

    /// <summary>
    /// The message value of the 05 Macro, which stands for the header [)&gt;RS05GS and the trailer RS EOT.
    /// </summary>
    public const int Macro05 = -3;

    /// <summary>
    /// The message value of the 06 Macro, which stands for the header [)&gt;RS06GS and the trailer RS EOT.
    /// </summary>
    public const int Macro06 = -4;

    /// <summary>
    /// The largest number of data codewords, which the 144 x 144 symbol holds.
    /// </summary>
    public const int MaximumCodewords = 1558;

    /// <summary>
    /// The number of values the encoder can collect in one run of C40, Text, X12 or EDIFACT encodation.
    /// Three C40, Text or X12 values pack into two codewords and four EDIFACT values into three, so a run
    /// of more than three halves of <see cref="MaximumCodewords"/> values cannot fit any symbol. The four
    /// places beyond hold the values of one more character.
    /// </summary>
    private const int MaximumValues = (MaximumCodewords * 3 / 2) + 4;

    /// <summary>
    /// The ASCII codeword of the pad character.
    /// </summary>
    public const int Pad = 129;

    /// <summary>
    /// The ASCII codeword that latches to C40 encodation, ISO/IEC 16022 Table 2.
    /// </summary>
    private const int LatchC40 = 230;

    /// <summary>
    /// The ASCII codeword that latches to Base 256 encodation, Table 2.
    /// </summary>
    private const int LatchBase256 = 231;

    /// <summary>
    /// The ASCII codeword of the Function 1 Symbol Character, Table 2.
    /// </summary>
    private const int Fnc1Codeword = 232;

    /// <summary>
    /// The ASCII codeword of the Upper Shift, which adds 128 to the next character, Table 2.
    /// </summary>
    private const int UpperShift = 235;

    /// <summary>
    /// The ASCII codeword of the 05 Macro, Table 2.
    /// </summary>
    private const int Macro05Codeword = 236;

    /// <summary>
    /// The ASCII codeword of the 06 Macro, Table 2.
    /// </summary>
    private const int Macro06Codeword = 237;

    /// <summary>
    /// The ASCII codeword that latches to X12 encodation, Table 2.
    /// </summary>
    private const int LatchX12 = 238;

    /// <summary>
    /// The ASCII codeword that latches to Text encodation, Table 2.
    /// </summary>
    private const int LatchText = 239;

    /// <summary>
    /// The ASCII codeword that latches to EDIFACT encodation, Table 2.
    /// </summary>
    private const int LatchEdifact = 240;

    /// <summary>
    /// The ASCII codeword of the first digit pair, 00. The pair nn is this value plus nn, Table 2.
    /// </summary>
    private const int DigitPairBase = 130;

    /// <summary>
    /// The codeword that unlatches C40, Text and X12 encodation back to ASCII encodation.
    /// </summary>
    private const int Unlatch = 254;

    /// <summary>
    /// The EDIFACT value that unlatches back to ASCII encodation.
    /// </summary>
    private const int EdifactUnlatch = 31;

    /// <summary>
    /// The value of a first character that is not in the basic set of C40 or Text encodation: Shift 1
    /// for the control characters, Shift 2 for the punctuation and the function characters, and Shift 3
    /// for the rest.
    /// </summary>
    private const int Shift1 = 0;

    /// <summary>
    /// The Shift 2 value.
    /// </summary>
    private const int Shift2 = 1;

    /// <summary>
    /// The Shift 3 value.
    /// </summary>
    private const int Shift3 = 2;

    /// <summary>
    /// The Shift 2 set value of the Function 1 Symbol Character in C40 and Text encodation.
    /// </summary>
    private const int ShiftedFnc1 = 27;

    /// <summary>
    /// The Shift 2 set value of the Upper Shift in C40 and Text encodation.
    /// </summary>
    private const int ShiftedUpperShift = 30;

    /// <summary>
    /// The scale of the look-ahead counts. The counts step in halves, thirds and quarters, so twelve
    /// units to the codeword keep every count a whole number and every comparison exact.
    /// </summary>
    private const int Unit = 12;

    /// <summary>
    /// The number of codewords left when no allowed symbol holds one more codeword.
    /// </summary>
    private const int NoSymbol = int.MaxValue;

    /// <summary>
    /// The number of characters before the look-ahead test may end early, while it still reads data.
    /// </summary>
    private const int MinimumLookAhead = 4;

    /// <summary>
    /// The result of the early test of the look-ahead test when no scheme wins at that position.
    /// </summary>
    private const int NoChoice = -1;

    /// <summary>
    /// The ASCII encodation scheme.
    /// </summary>
    private const int Ascii = 0;

    /// <summary>
    /// The C40 encodation scheme.
    /// </summary>
    private const int C40 = 1;

    /// <summary>
    /// The Text encodation scheme.
    /// </summary>
    private const int Text = 2;

    /// <summary>
    /// The X12 encodation scheme.
    /// </summary>
    private const int X12 = 3;

    /// <summary>
    /// The EDIFACT encodation scheme.
    /// </summary>
    private const int Edifact = 4;

    /// <summary>
    /// The Base 256 encodation scheme.
    /// </summary>
    private const int Base256 = 5;

    /// <summary>
    /// Encodes the message into data codewords, without pad codewords.
    /// </summary>
    /// <param name="message">The message values.</param>
    /// <param name="capacities">The data codeword capacities of the symbols the data may take.</param>
    /// <param name="c40Prefix">The number of message values at the start of the message that encode in C40 encodation, or 0.</param>
    /// <param name="codewords">The buffer the data codewords are written to, <see cref="MaximumCodewords"/> long.</param>
    /// <returns>The number of data codewords written.</returns>
    /// <exception cref="ArgumentException">The data needs more codewords than the largest symbol holds.</exception>
    public static int Encode(ReadOnlySpan<int> message, ReadOnlySpan<int> capacities, int c40Prefix, Span<int> codewords)
    {
        // The look-ahead test reads three runs from each position: the digits that start there, the
        // distance to the next X12 terminator, carriage return, asterisk or greater than, and the distance
        // to the next character X12 cannot encode. The entry past the end stops each run. A distance with
        // no end stands past the end of the data, so where neither character follows, the two distances
        // are equal. The same rented block also holds the six rows of look-ahead sums and the values that
        // a run of C40, Text, X12 or EDIFACT encodation collects, so the encoder rents once. The message
        // length is at most DataMatrixEncoder.MaximumLength values, so the block size is bounded.
        int length = message.Length;
        int stride = length + 1;
        PooledBuffer<int> runs = new((9 * stride) + MaximumValues);
        try
        {
            Span<int> digits = runs.Span[..stride];
            Span<int> terminator = runs.Span.Slice(stride, stride);
            Span<int> nonX12 = runs.Span.Slice(2 * stride, stride);
            digits[length] = 0;
            terminator[length] = length + 1;
            nonX12[length] = length + 1;
            for (int i = length - 1; i >= 0; i--)
            {
                int value = message[i];
                digits[i] = IsDigit(value) ? digits[i + 1] + 1 : 0;
                terminator[i] = value is '\r' or '*' or '>' ? 0 : terminator[i + 1] + 1;
                nonX12[i] = IsX12(value) ? nonX12[i + 1] + 1 : 0;
            }

            // The look-ahead test also reads the cost of each scheme as a running sum, in units, of the
            // characters before each position. Each scheme has one row of sums, at its scheme number. The cost
            // of a scheme for a run of characters is then the difference of two sums, so the test can read the
            // counts at many positions at once.
            //
            // A digit costs half a codeword in ASCII encodation and a character above 127 two whole codewords;
            // any other character rounds the count up first, because it cannot share a codeword with a digit
            // before it. A basic set character costs two thirds of a codeword in C40, Text and X12 encodation,
            // one of three values in two codewords, and three quarters in EDIFACT encodation, one of four
            // values in three codewords. A shifted character costs two values and an Upper Shift two more. X12
            // costs a character it cannot hold 10/3 codewords, or 13/3 above 127, and EDIFACT 13/4 or 17/4.
            // Base 256 encodation pays one codeword a byte and four for a function character it cannot hold.
            Span<int> sums = runs.Span.Slice(3 * stride, 6 * stride);
            Span<int> asciiSums = sums.Slice(Ascii * stride, stride);
            Span<int> c40Sums = sums.Slice(C40 * stride, stride);
            Span<int> textSums = sums.Slice(Text * stride, stride);
            Span<int> x12Sums = sums.Slice(X12 * stride, stride);
            Span<int> edifactSums = sums.Slice(Edifact * stride, stride);
            Span<int> base256Sums = sums.Slice(Base256 * stride, stride);
            asciiSums[0] = 0;
            c40Sums[0] = 0;
            textSums[0] = 0;
            x12Sums[0] = 0;
            edifactSums[0] = 0;
            base256Sums[0] = 0;
            for (int i = 0; i < length; i++)
            {
                int value = message[i];
                bool extended = value > 127;
                asciiSums[i + 1] = AddAscii(asciiSums[i], value);
                c40Sums[i + 1] = c40Sums[i] + (IsC40Basic(value) ? 2 * Unit / 3 : extended ? 8 * Unit / 3 : 4 * Unit / 3);
                textSums[i + 1] = textSums[i] + (IsTextBasic(value) ? 2 * Unit / 3 : extended ? 8 * Unit / 3 : 4 * Unit / 3);
                x12Sums[i + 1] = x12Sums[i] + (IsX12(value) ? 2 * Unit / 3 : extended ? 13 * Unit / 3 : 10 * Unit / 3);
                edifactSums[i + 1] = edifactSums[i] + (IsEdifact(value) ? 3 * Unit / 4 : extended ? 17 * Unit / 4 : 13 * Unit / 4);
                base256Sums[i + 1] = base256Sums[i] + (value < 0 ? 4 * Unit : Unit);
            }

            // The C40 prefix cannot run past the data.
            Span<int> values = runs.Span.Slice(9 * stride, MaximumValues);
            Encoder encoder = new(message, capacities, digits, terminator, nonX12, sums, codewords, values, Math.Min(c40Prefix, length));

            // A Function 1 Symbol Character in the first position always encodes in ASCII encodation,
            // because a reader identifies GS1 data by codeword 232 at the start of the symbol. The C40
            // prefix counts message values, so it moves one place to stay over the same characters.
            if (message[0] == Fnc1)
            {
                encoder.AppendCodeword(Fnc1Codeword);
                encoder.Position = 1;
                if (encoder.C40Prefix > 0)
                {
                    encoder.C40Prefix++;
                }
            }

            // Each step encodes in the current scheme until that scheme ends, either by a latch to another
            // scheme or by the end of the data. C40, Text and X12 share one method, because they share the
            // packing of three values into two codewords.
            while (encoder.Position < length)
            {
                switch (encoder.Mode)
                {
                    case Ascii:
                        encoder.EncodeAscii();
                        break;

                    case Edifact:
                        encoder.EncodeEdifact();
                        break;

                    case Base256:
                        encoder.EncodeBase256();
                        break;

                    default:
                        encoder.EncodeTriplets();
                        break;
                }
            }

            return encoder.Written;
        }
        finally
        {
            runs.Dispose();
        }
    }

    /// <summary>
    /// Returns whether the message value is a digit.
    /// </summary>
    /// <param name="value">The message value.</param>
    /// <returns><see langword="true"/> for the digits 0 to 9.</returns>
    private static bool IsDigit(int value) => value is >= '0' and <= '9';

    /// <summary>
    /// Returns whether the message value is in the basic set of C40 encodation: space, the digits and the
    /// capital letters.
    /// </summary>
    /// <param name="value">The message value.</param>
    /// <returns><see langword="true"/> for a basic set character.</returns>
    private static bool IsC40Basic(int value) => value is ' ' or (>= '0' and <= '9') or (>= 'A' and <= 'Z');

    /// <summary>
    /// Returns whether the message value is in the basic set of Text encodation: space, the digits and the
    /// small letters.
    /// </summary>
    /// <param name="value">The message value.</param>
    /// <returns><see langword="true"/> for a basic set character.</returns>
    private static bool IsTextBasic(int value) => value is ' ' or (>= '0' and <= '9') or (>= 'a' and <= 'z');

    /// <summary>
    /// Returns whether X12 encodation holds the message value: carriage return, asterisk, greater than,
    /// space, the digits and the capital letters.
    /// </summary>
    /// <param name="value">The message value.</param>
    /// <returns><see langword="true"/> for an X12 character.</returns>
    private static bool IsX12(int value) => value is '\r' or '*' or '>' || IsC40Basic(value);

    /// <summary>
    /// Returns whether EDIFACT encodation holds the message value, the characters 32 to 94.
    /// </summary>
    /// <param name="value">The message value.</param>
    /// <returns><see langword="true"/> for an EDIFACT character.</returns>
    private static bool IsEdifact(int value) => value is >= 32 and <= 94;

    /// <summary>
    /// Rounds a look-ahead count up to a whole codeword.
    /// </summary>
    /// <param name="count">The count in units.</param>
    /// <returns>The count rounded up to a multiple of <see cref="Unit"/>.</returns>
    private static int Ceiling(int count) => (count + Unit - 1) / Unit * Unit;

    /// <summary>
    /// Adds the ASCII cost of one message value to a look-ahead count.
    /// </summary>
    /// <param name="count">The count in units.</param>
    /// <param name="value">The message value.</param>
    /// <returns>The count with the cost of the value added.</returns>
    private static int AddAscii(int count, int value)
        => IsDigit(value) ? count + (Unit / 2) : Ceiling(count) + (value > 127 ? 2 * Unit : Unit);

    /// <summary>
    /// Throws the exception for data that needs more codewords than the largest symbol holds.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowTooLong()
        => throw new ArgumentException($"The data needs more than the {MaximumCodewords} data codewords of the largest Data Matrix symbol.");

    /// <summary>
    /// Writes the C40, Text or X12 values of one message value.
    /// </summary>
    /// <param name="mode">The encodation scheme, <see cref="C40"/>, <see cref="Text"/> or <see cref="X12"/>.</param>
    /// <param name="value">The message value.</param>
    /// <param name="values">The buffer of at least four values that receives the values.</param>
    /// <returns>The number of values written, or 0 when the scheme cannot encode the message value.</returns>
    private static int TripletValues(int mode, int value, Span<int> values)
    {
        // X12 has one set of 40 values and no shifts: carriage return 0, asterisk 1, greater than 2,
        // space 3, the digits 4 to 13 and the capital letters 14 to 39. Any other character ends X12.
        if (mode == X12)
        {
            int x12 = value switch
            {
                '\r' => 0,
                '*' => 1,
                '>' => 2,
                ' ' => 3,
                >= '0' and <= '9' => value - 44,
                >= 'A' and <= 'Z' => value - 51,
                _ => -1,
            };

            if (x12 < 0)
            {
                return 0;
            }

            values[0] = x12;
            return 1;
        }

        // A character above 127 takes the Upper Shift in the Shift 2 set, then the values of the
        // character 128 below it.
        int count = 0;
        if (value > 127)
        {
            values[0] = Shift2;
            values[1] = ShiftedUpperShift;
            count = 2;
            value -= 128;
        }

        // The Macro characters exist only in ASCII encodation, so they end C40 and Text encodation.
        Span<int> rest = values[count..];
        if (value is Macro05 or Macro06)
        {
            return 0;
        }

        // The Function 1 Symbol Character is value 27 of the Shift 2 set.
        if (value == Fnc1)
        {
            rest[0] = Shift2;
            rest[1] = ShiftedFnc1;
            return count + 2;
        }

        // The basic set: space is 3 and the digits 0 to 9 are 4 to 13, so a digit is its character code
        // less 44.
        if (value == ' ')
        {
            rest[0] = 3;
            return count + 1;
        }

        if (IsDigit(value))
        {
            rest[0] = value - 44;
            return count + 1;
        }

        // The basic set holds the capital letters in C40 encodation and the small letters in Text
        // encodation, as the values 14 to 39, so A is 65 - 51 and a is 97 - 83. The other case moves to
        // the Shift 3 set, where C40 puts the small letters at 1 to 26 after the grave accent at 0, and
        // Text puts the capital letters there.
        if (value is >= 'A' and <= 'Z')
        {
            if (mode == C40)
            {
                rest[0] = value - 51;
                return count + 1;
            }

            rest[0] = Shift3;
            rest[1] = value - 64;
            return count + 2;
        }

        if (value is >= 'a' and <= 'z')
        {
            if (mode == Text)
            {
                rest[0] = value - 83;
                return count + 1;
            }

            rest[0] = Shift3;
            rest[1] = value - 96;
            return count + 2;
        }

        // The rest of the characters to 127 take a shift and a value in that set. The control characters
        // 0 to 31 are the Shift 1 set in order. The Shift 2 set holds the punctuation in three runs:
        // 33 to 47 as 0 to 14, 58 to 64 as 15 to 21 and 91 to 95 as 22 to 26. The Shift 3 set holds 96 to
        // 127 as 0 to 31; the letters of that range were handled above.
        (int shift, int shifted) = value switch
        {
            < 32 => (Shift1, value),
            <= 47 => (Shift2, value - 33),
            <= 64 => (Shift2, value - 43),
            <= 95 => (Shift2, value - 69),
            _ => (Shift3, value - 96),
        };

        rest[0] = shift;
        rest[1] = shifted;
        return count + 2;
    }

    /// <summary>
    /// The state of the encodation: the position in the message, the current encodation scheme and the
    /// runs the look-ahead test reads, with the codewords written so far and the scratch for the values of
    /// a run of C40, Text, X12 or EDIFACT encodation.
    /// </summary>
    private ref struct Encoder
    {
        private readonly ReadOnlySpan<int> message;
        private readonly ReadOnlySpan<int> capacities;
        private readonly ReadOnlySpan<int> digits;
        private readonly ReadOnlySpan<int> terminator;
        private readonly ReadOnlySpan<int> nonX12;
        private readonly ReadOnlySpan<int> sums;
        private readonly Span<int> codewords;
        private readonly Span<int> values;

        /// <summary>
        /// Initializes a new instance of the <see cref="Encoder"/> struct.
        /// </summary>
        /// <param name="message">The message values.</param>
        /// <param name="capacities">The data codeword capacities of the symbols the data may take.</param>
        /// <param name="digits">The number of digits that start at each position.</param>
        /// <param name="terminator">The distance from each position to the next X12 terminator.</param>
        /// <param name="nonX12">The distance from each position to the next character X12 cannot encode.</param>
        /// <param name="sums">The running sums of the look-ahead costs, one row for each scheme at its scheme number.</param>
        /// <param name="codewords">The buffer the data codewords are written to, <see cref="MaximumCodewords"/> long.</param>
        /// <param name="values">The scratch for the values of one run, <see cref="MaximumValues"/> long.</param>
        /// <param name="c40Prefix">The number of message values at the start that encode in C40 encodation.</param>
        public Encoder(
            ReadOnlySpan<int> message,
            ReadOnlySpan<int> capacities,
            ReadOnlySpan<int> digits,
            ReadOnlySpan<int> terminator,
            ReadOnlySpan<int> nonX12,
            ReadOnlySpan<int> sums,
            Span<int> codewords,
            Span<int> values,
            int c40Prefix)
        {
            this.message = message;
            this.capacities = capacities;
            this.digits = digits;
            this.terminator = terminator;
            this.nonX12 = nonX12;
            this.sums = sums;
            this.codewords = codewords;
            this.values = values;
            this.C40Prefix = c40Prefix;
            this.Position = 0;
            this.Mode = Ascii;
            this.Written = 0;
        }

        /// <summary>
        /// Gets or sets the position of the next message value to encode.
        /// </summary>
        public int Position { get; set; }

        /// <summary>
        /// Gets or sets the current encodation scheme.
        /// </summary>
        public int Mode { get; set; }

        /// <summary>
        /// Gets or sets the number of message values at the start that encode in C40 encodation, or -1
        /// once the encoder has left C40 encodation.
        /// </summary>
        public int C40Prefix { get; set; }

        /// <summary>
        /// Gets the number of data codewords written.
        /// </summary>
        public int Written { get; private set; }

        /// <summary>
        /// Appends one data codeword. No symbol holds more than <see cref="MaximumCodewords"/>, so the
        /// encodation stops there.
        /// </summary>
        /// <param name="codeword">The codeword.</param>
        /// <exception cref="ArgumentException">The data needs more codewords than the largest symbol holds.</exception>
        public void AppendCodeword(int codeword)
        {
            int written = this.Written;
            if (written == this.codewords.Length)
            {
                ThrowTooLong();
            }

            this.codewords[written] = codeword;
            this.Written = written + 1;
        }

        /// <summary>
        /// Encodes in ASCII encodation: a digit pair as one codeword, or the latch the look-ahead test
        /// chooses, or one character.
        /// </summary>
        public void EncodeAscii()
        {
            // Two digits take one codeword in ASCII encodation, which no other scheme does better, so a
            // digit pair encodes before the look-ahead test runs. Digits in the C40 prefix stay in C40.
            int position = this.Position;
            if (position >= this.C40Prefix && this.digits[position] >= 2)
            {
                this.AppendDigitPair();
                return;
            }

            // A latch writes its codeword and changes the scheme. The next step then encodes the same
            // character in the new scheme.
            int next = this.LookAhead();
            if (next != this.Mode)
            {
                this.AppendCodeword(next switch
                {
                    C40 => LatchC40,
                    Text => LatchText,
                    X12 => LatchX12,
                    Edifact => LatchEdifact,
                    _ => LatchBase256,
                });

                this.Mode = next;
                return;
            }

            this.AppendAscii(this.message[position]);
            this.Position = position + 1;
        }

        /// <summary>
        /// Encodes in C40, Text or X12 encodation until the look-ahead test chooses another scheme at a
        /// group boundary, the data ends, or a character the scheme cannot encode follows.
        /// </summary>
        public void EncodeTriplets()
        {
            // The values collect until the scheme ends, because the end of the scheme can step back over
            // characters already read. A character takes one to four values, so the groups of three do not
            // line up with the characters. The values collect in the scratch of the encoder, which holds
            // more values than the codewords of the largest symbol can pack, so a run that fills it cannot
            // fit any symbol.
            ReadOnlySpan<int> message = this.message;
            int mode = this.Mode;
            Span<int> values = this.values;
            int valueCount = 0;
            InlineArray4<int> characterStorage = default;
            Span<int> character = characterStorage;
            while (this.Position < message.Length)
            {
                int position = this.Position;
                int count = TripletValues(mode, message[position], character);
                if (count == 0)
                {
                    break;
                }

                // The scheme can change and the data can end only between whole groups of three values,
                // because a group is the unit that packs into codewords.
                if (valueCount % 3 == 0)
                {
                    // At a group boundary the look-ahead test may choose another scheme. The scheme ends
                    // with an unlatch, and a latch follows unless the next scheme is ASCII.
                    if (valueCount > 0)
                    {
                        int next = this.LookAhead();
                        if (next != mode)
                        {
                            this.AppendTriplets(values[..valueCount]);
                            this.AppendCodeword(Unlatch);
                            if (next != Ascii)
                            {
                                this.AppendCodeword(next switch
                                {
                                    C40 => LatchC40,
                                    Text => LatchText,
                                    X12 => LatchX12,
                                    Edifact => LatchEdifact,
                                    _ => LatchBase256,
                                });
                            }

                            this.Mode = next;
                            return;
                        }
                    }

                    // Near the end of the data the last characters may fill the symbol exactly, so the
                    // unlatch is left out and the end of the symbol returns to ASCII encodation.
                    int left = message.Length - position;
                    if (left <= 3 && this.EndTriplets(mode, values[..valueCount]))
                    {
                        return;
                    }
                }

                if (valueCount + count > values.Length)
                {
                    ThrowTooLong();
                }

                character[..count].CopyTo(values[valueCount..]);
                valueCount += count;
                this.Position = position + 1;
            }

            // The scheme ends inside a group or before a character it cannot encode. The encoder steps
            // back to the last whole group, unlatches and encodes the characters it stepped back over in
            // ASCII encodation. Each step back removes the values of one character, and a group boundary
            // falls between characters, so the loop stops on one.
            int length = valueCount;
            while (length % 3 != 0)
            {
                this.Position--;
                length -= TripletValues(mode, message[this.Position], character);
            }

            this.AppendTriplets(values[..length]);
            this.AppendCodeword(Unlatch);
            this.LeaveToAscii();
        }

        /// <summary>
        /// Encodes in EDIFACT encodation until the look-ahead test chooses another scheme at a group
        /// boundary, the data ends, or a character EDIFACT cannot encode follows.
        /// </summary>
        public void EncodeEdifact()
        {
            // The values collect in the scratch of the encoder. Four values pack into three codewords, so
            // the scratch holds more values than the largest symbol can pack, and a run that fills it cannot
            // fit any symbol.
            ReadOnlySpan<int> message = this.message;
            Span<int> values = this.values;
            int valueCount = 0;
            while (this.Position < message.Length)
            {
                int position = this.Position;
                int value = message[position];
                if (!IsEdifact(value))
                {
                    break;
                }

                if (valueCount % 4 == 0)
                {
                    // With two characters or fewer left, the symbol may have room for them in ASCII
                    // encodation, which the end of the symbol returns to without an unlatch.
                    if (message.Length - position <= 2)
                    {
                        // The codewords the whole groups take are counted as written, because the groups
                        // so far are whole. A character above 127 takes two ASCII codewords.
                        int remaining = this.Remaining(this.Written + (valueCount / 4 * 3));
                        int asciiCount = 0;
                        for (int i = position; i < message.Length; i++)
                        {
                            asciiCount += message[i] > 127 ? 2 : 1;
                        }

                        if ((remaining == 1 || remaining == 2) && asciiCount <= remaining)
                        {
                            this.AppendEdifact(values[..valueCount]);
                            for (int i = position; i < message.Length; i++)
                            {
                                this.AppendAscii(message[i]);
                            }

                            this.Mode = Ascii;
                            this.Position = message.Length;
                            return;
                        }
                    }

                    if (this.LookAhead() != Edifact)
                    {
                        break;
                    }
                }

                // The unlatch value takes one more place, so a run stops one value short of the end of the
                // scratch.
                if (valueCount == values.Length - 1)
                {
                    ThrowTooLong();
                }

                // EDIFACT keeps the low six bits: the characters 64 to 94 become 0 to 30, and 32 to 63 keep
                // their value.
                values[valueCount++] = value & 0x3F;
                this.Position = position + 1;
            }

            // The unlatch value ends the scheme, unless the data ended on a whole group and fewer than three
            // codewords are left, which the end of the symbol returns to ASCII encodation. The count is the
            // codewords left after the whole groups, in the smallest symbol that holds them.
            int left = this.Remaining(this.Written + (valueCount / 4 * 3) - 1) - 1;
            if (valueCount % 4 != 0 || this.Position != message.Length || left >= 3)
            {
                values[valueCount++] = EdifactUnlatch;
            }

            this.AppendEdifact(values[..valueCount]);
            this.LeaveToAscii();
        }

        /// <summary>
        /// Encodes in Base 256 encodation until the look-ahead test chooses another scheme. A length field
        /// leads the bytes, and every codeword of the field and the bytes takes the 255-state randomising.
        /// </summary>
        public void EncodeBase256()
        {
            // The length field comes before the bytes, so the run is measured first. The look-ahead test
            // runs at each byte, and the run ends where it chooses another scheme. Base 256 has no unlatch;
            // the length field tells the decoder where the run ends.
            ReadOnlySpan<int> message = this.message;
            int start = this.Position;
            while (this.Position < message.Length && this.LookAhead() == Base256)
            {
                this.Position++;
            }

            // The length field is one codeword below 250 bytes and two codewords from 250: the first is 249
            // plus the hundreds of 250, the second the rest. A field of 0 means that the bytes run to the
            // end of the symbol, which applies when the field and the bytes fill it exactly.
            int first = this.Written;
            int count = this.Position - start;
            if (this.Remaining(first + count) - 1 == 0 && this.Position == message.Length)
            {
                this.AppendCodeword(0);
            }
            else if (count < 250)
            {
                this.AppendCodeword(count);
            }
            else
            {
                this.AppendCodeword((count / 250) + 249);
                this.AppendCodeword(count % 250);
            }

            for (int i = start; i < this.Position; i++)
            {
                this.AppendCodeword(message[i]);
            }

            // The 255-state algorithm adds a pseudo-random number from the position of the codeword in the
            // symbol, counted from 1, so a run of equal bytes does not repeat one module pattern. The
            // number is ((149 x position) mod 255) + 1, and the sum wraps at 256.
            Span<int> codewords = this.codewords;
            for (int i = first; i < this.Written; i++)
            {
                int randomised = codewords[i] + ((149 * (i + 1)) % 255) + 1;
                codewords[i] = randomised <= 255 ? randomised : randomised - 256;
            }

            this.Mode = Ascii;
        }

        /// <summary>
        /// Encodes the last one to three characters of C40, Text or X12 encodation when they fill the
        /// symbol with no unlatch, or when one character is left and ASCII encodation fits it.
        /// </summary>
        /// <param name="mode">The encodation scheme.</param>
        /// <param name="values">The values of the whole groups so far, not yet written as codewords.</param>
        /// <returns><see langword="true"/> when the data ended here.</returns>
        private bool EndTriplets(int mode, ReadOnlySpan<int> values)
        {
            // The end cases apply only when one or two codewords are left after the whole groups, in the
            // smallest symbol that holds them. With more room the scheme ends with an unlatch as usual.
            ReadOnlySpan<int> message = this.message;
            int position = this.Position;
            int remaining = this.Remaining(this.Written + (values.Length / 3 * 2));
            if (remaining > 2)
            {
                return false;
            }

            // A character the scheme cannot encode counts as four values, which no end case takes. At most
            // three characters are left, each of at most four values.
            InlineArray12<int> restStorage = default;
            InlineArray4<int> characterStorage = default;
            Span<int> rest = restStorage;
            Span<int> character = characterStorage;
            int count = 0;
            for (int i = position; i < message.Length; i++)
            {
                int n = TripletValues(mode, message[i], character);
                if (n == 0)
                {
                    count += 4;
                }
                else
                {
                    character[..n].CopyTo(rest[count..]);
                    count += n;
                }
            }

            // Two codewords hold one more group. Three values fill it. Two values take Shift 1 as the third,
            // which a decoder discards at the end of the symbol; X12 has no shifts, so it does not take
            // this case. One value does not fill a group, so it goes to ASCII: after an unlatch when two
            // codewords are left, or straight after the groups when one is left, which the end of the
            // symbol returns to ASCII encodation.
            if (remaining == 2 && (count == 3 || (count == 2 && mode != X12)))
            {
                this.AppendTriplets(values);
                this.AppendTriplet(rest[0], rest[1], count == 3 ? rest[2] : Shift1);
            }
            else if (count == 1 && remaining == 2)
            {
                this.AppendTriplets(values);
                this.AppendCodeword(Unlatch);
                this.AppendAscii(message[position]);
            }
            else if (count == 1 && remaining == 1)
            {
                this.AppendTriplets(values);
                this.AppendAscii(message[position]);
            }
            else
            {
                return false;
            }

            this.Mode = Ascii;
            this.Position = message.Length;
            return true;
        }

        /// <summary>
        /// Returns to ASCII encodation after an unlatch and encodes the next character in it, so the
        /// look-ahead test does not latch straight back.
        /// </summary>
        private void LeaveToAscii()
        {
            // The C40 prefix ends here, so a later look-ahead test is free to choose any scheme.
            this.Mode = Ascii;
            this.C40Prefix = -1;
            if (this.Position == this.message.Length)
            {
                return;
            }

            if (this.digits[this.Position] >= 2)
            {
                this.AppendDigitPair();
                return;
            }

            this.AppendAscii(this.message[this.Position]);
            this.Position++;
        }

        /// <summary>
        /// Appends the digit pair at the current position as one ASCII codeword.
        /// </summary>
        private void AppendDigitPair()
        {
            // Table 2: the pairs 00 to 99 are the codewords 130 to 229.
            int position = this.Position;
            this.AppendCodeword(DigitPairBase + (10 * (this.message[position] - '0')) + this.message[position + 1] - '0');
            this.Position = position + 2;
        }

        /// <summary>
        /// Appends the ASCII codewords of one message value.
        /// </summary>
        /// <param name="value">The message value.</param>
        private void AppendAscii(int value)
        {
            // Table 2: the characters 0 to 127 are their value plus 1. A character from 128 to 255 is the
            // Upper Shift followed by the codeword of the character 128 below it, (value - 128) + 1.
            switch (value)
            {
                case Fnc1:
                    this.AppendCodeword(Fnc1Codeword);
                    break;

                case Macro05:
                    this.AppendCodeword(Macro05Codeword);
                    break;

                case Macro06:
                    this.AppendCodeword(Macro06Codeword);
                    break;

                case < 128:
                    this.AppendCodeword(value + 1);
                    break;

                default:
                    this.AppendCodeword(UpperShift);
                    this.AppendCodeword(value - 127);
                    break;
            }
        }

        /// <summary>
        /// Appends the codewords of whole groups of three C40, Text or X12 values. Each group packs into two
        /// codewords as 1600 x C1 + 40 x C2 + C3 + 1.
        /// </summary>
        /// <param name="values">The values, a multiple of three.</param>
        private void AppendTriplets(ReadOnlySpan<int> values)
        {
            for (int i = 0; i < values.Length; i += 3)
            {
                this.AppendTriplet(values[i], values[i + 1], values[i + 2]);
            }
        }

        /// <summary>
        /// Appends the two codewords of one group of three C40, Text or X12 values, packed as
        /// 1600 x C1 + 40 x C2 + C3 + 1.
        /// </summary>
        /// <param name="c1">The first value.</param>
        /// <param name="c2">The second value.</param>
        /// <param name="c3">The third value.</param>
        private void AppendTriplet(int c1, int c2, int c3)
        {
            // The three base 40 values form one number from 1 to 64000, which fits in 16 bits and is
            // written as two codewords, the high byte first.
            int packed = (1600 * c1) + (40 * c2) + c3 + 1;
            this.AppendCodeword(packed >> 8);
            this.AppendCodeword(packed & 0xFF);
        }

        /// <summary>
        /// Appends the codewords of EDIFACT values. Each group of four six-bit values packs into three
        /// codewords, and a group of fewer values packs into the codewords its bits need.
        /// </summary>
        /// <param name="values">The six-bit values.</param>
        private void AppendEdifact(ReadOnlySpan<int> values)
        {
            // A group of n values carries 6 x n bits and takes the whole codewords that hold them. The values
            // missing from a short group are zero.
            for (int i = 0; i < values.Length; i += 4)
            {
                // The four values fill a 24 bit number, the first value in the top six bits, so the three
                // codewords are bits 23 to 16, 15 to 8 and 7 to 0.
                int count = Math.Min(4, values.Length - i);
                int packed = 0;
                for (int j = 0; j < 4; j++)
                {
                    packed = (packed << 6) | (j < count ? values[i + j] : 0);
                }

                // One value takes 1 codeword, two values 2 and three or four values 3.
                int bytes = ((count * 6) + 7) / 8;
                for (int j = 0; j < bytes; j++)
                {
                    this.AppendCodeword((packed >> (16 - (8 * j))) & 0xFF);
                }
            }
        }

        /// <summary>
        /// Returns the number of codewords left in the smallest allowed symbol that holds more than the
        /// given number of codewords, counted from that number.
        /// </summary>
        /// <param name="written">The number of codewords.</param>
        /// <returns>The codewords left, or <see cref="NoSymbol"/> when no allowed symbol holds more.</returns>
        /// <exception cref="ArgumentException">The count reaches the capacity of the largest symbol.</exception>
        private readonly int Remaining(int written)
        {
            if (written >= MaximumCodewords)
            {
                ThrowTooLong();
            }

            // The symbol that the data ends in is the smallest one with room for one more codeword than is
            // written. The end rules compare against the room left in it.
            int best = NoSymbol;
            foreach (int capacity in this.capacities)
            {
                if (capacity > written && capacity < best)
                {
                    best = capacity;
                }
            }

            return best == NoSymbol ? NoSymbol : best - written;
        }

        /// <summary>
        /// The look-ahead test. Each scheme keeps a count of the codewords it needs for the data read so
        /// far, starting with the cost of the latch to it. After at least five characters one scheme can
        /// win early by a clear codeword, and at the end of the data the smallest whole count wins.
        /// </summary>
        /// <returns>The encodation scheme for the data at the current position.</returns>
        private readonly int LookAhead()
        {
            // The C40 prefix fixes the scheme for its characters, whatever the counts say.
            int position = this.Position;
            if (position < this.C40Prefix)
            {
                return C40;
            }

            // Counts are in twelfths of a codeword. A scheme other than the current one starts one codeword
            // dearer for its latch, and leaving any scheme but ASCII costs one more for the unlatch. Base 256
            // starts a quarter codeword dearer again, for its length field.
            int ascii = Unit;
            int c40 = 2 * Unit;
            int text = 2 * Unit;
            int x12 = 2 * Unit;
            int edifact = 2 * Unit;
            int base256 = (2 * Unit) + (Unit / 4);
            if (this.Mode == Ascii)
            {
                ascii = 0;
                c40 = Unit;
                text = Unit;
                x12 = Unit;
                edifact = Unit;
                base256 = Unit + (Unit / 4);
            }

            switch (this.Mode)
            {
                case C40:
                    c40 = 0;
                    break;

                case Text:
                    text = 0;
                    break;

                case X12:
                    x12 = 0;
                    break;

                case Edifact:
                    edifact = 0;
                    break;

                case Base256:
                    base256 = 0;
                    break;
            }

            // Base 256 cannot carry a Function 1 Symbol Character, so where the test would choose Base 256
            // for one, ASCII takes it instead.
            ReadOnlySpan<int> message = this.message;
            bool fnc1 = message[position] == Fnc1;

            // The count of each scheme but ASCII is its start value plus the costs of the characters from the
            // position. So the count after the characters before index n is an offset plus the running sum
            // at n, and the offsets are fixed for the whole test.
            int length = message.Length;
            int stride = length + 1;
            ReadOnlySpan<int> asciiSums = this.sums.Slice(Ascii * stride, stride);
            ReadOnlySpan<int> c40Sums = this.sums.Slice(C40 * stride, stride);
            ReadOnlySpan<int> textSums = this.sums.Slice(Text * stride, stride);
            ReadOnlySpan<int> x12Sums = this.sums.Slice(X12 * stride, stride);
            ReadOnlySpan<int> edifactSums = this.sums.Slice(Edifact * stride, stride);
            ReadOnlySpan<int> base256Sums = this.sums.Slice(Base256 * stride, stride);
            int c40Offset = c40 - c40Sums[position];
            int textOffset = text - textSums[position];
            int x12Offset = x12 - x12Sums[position];
            int edifactOffset = edifact - edifactSums[position];
            int base256Offset = base256 - base256Sums[position];

            // The test reads the data in three stages. Each stage ends the test when a scheme wins early. The
            // index is the number of characters read, so the counts stand after the character before it.
            //
            // Stage 1 reads one character at a time. The test cannot end early before the fifth character.
            // The ASCII count does not follow its running sum where the position is inside a digit run of odd
            // length, because ASCII rounds its count up to a whole codeword before a character that is not a
            // digit. So this stage goes on until the ASCII count and its running sum stand at the same part
            // of a codeword. Rounding up keeps a difference of whole codewords, so from there the ASCII count
            // is also an offset plus its running sum.
            int index = position;
            do
            {
                ascii = AddAscii(ascii, message[index]);
                index++;
                if (index - position > MinimumLookAhead)
                {
                    int choice = this.EarlyChoice(
                        ascii,
                        c40Offset + c40Sums[index],
                        textOffset + textSums[index],
                        x12Offset + x12Sums[index],
                        edifactOffset + edifactSums[index],
                        base256Offset + base256Sums[index],
                        index,
                        fnc1);

                    if (choice != NoChoice)
                    {
                        return choice;
                    }
                }
            }
            while (index < length && (index - position <= MinimumLookAhead || (ascii - asciiSums[index]) % Unit != 0));

            // Where stage 1 read to the end of the data, the offset gives the ASCII count at the end only, which
            // is the one count that the end of the test reads.
            int asciiOffset = ascii - asciiSums[index];

            // Stage 2 reads a vector of positions at a time. Lane i holds the counts after the character at
            // index + i, which are the offsets plus the running sums at index + i + 1. The vectors a, c, t, x, e
            // and b hold the ASCII, C40, Text, X12, EDIFACT and Base 256 counts. A mask lane is all ones where
            // the early test of that scheme holds, with the same comparisons as EarlyChoice. The C40 mask also
            // holds where the C40 and X12 counts are equal, because the X12 terminator decides that case. So
            // EarlyChoice runs again for each lane that a mask holds, in lane order, and gives the choice.
            if (Vector.IsHardwareAccelerated)
            {
                int lanes = Vector<int>.Count;
                Vector<int> unit = new(Unit);
                Vector<int> asciiOffsets = new(asciiOffset);
                Vector<int> c40Offsets = new(c40Offset);
                Vector<int> textOffsets = new(textOffset);
                Vector<int> x12Offsets = new(x12Offset);
                Vector<int> edifactOffsets = new(edifactOffset);
                Vector<int> base256Offsets = new(base256Offset);
                for (; index + lanes <= length; index += lanes)
                {
                    int next = index + 1;
                    Vector<int> a = asciiOffsets + new Vector<int>(asciiSums[next..]);
                    Vector<int> c = c40Offsets + new Vector<int>(c40Sums[next..]);
                    Vector<int> t = textOffsets + new Vector<int>(textSums[next..]);
                    Vector<int> x = x12Offsets + new Vector<int>(x12Sums[next..]);
                    Vector<int> e = edifactOffsets + new Vector<int>(edifactSums[next..]);
                    Vector<int> b = base256Offsets + new Vector<int>(base256Sums[next..]);

                    // Each count plus one codeword, for the margin that a winning scheme needs.
                    Vector<int> au = a + unit;
                    Vector<int> cu = c + unit;
                    Vector<int> tu = t + unit;
                    Vector<int> xu = x + unit;
                    Vector<int> eu = e + unit;
                    Vector<int> bu = b + unit;

                    Vector<int> asciiWins = Vector.LessThanOrEqual(au, c) & Vector.LessThanOrEqual(au, t)
                        & Vector.LessThanOrEqual(au, x) & Vector.LessThanOrEqual(au, e) & Vector.LessThanOrEqual(au, b);
                    Vector<int> base256Wins = Vector.LessThanOrEqual(bu, a)
                        | (Vector.LessThan(bu, c) & Vector.LessThan(bu, t) & Vector.LessThan(bu, x) & Vector.LessThan(bu, e));
                    Vector<int> edifactWins = Vector.LessThan(eu, a) & Vector.LessThan(eu, c) & Vector.LessThan(eu, t)
                        & Vector.LessThan(eu, x) & Vector.LessThan(eu, b);
                    Vector<int> textWins = Vector.LessThan(tu, a) & Vector.LessThan(tu, c) & Vector.LessThan(tu, x)
                        & Vector.LessThan(tu, e) & Vector.LessThan(tu, b);
                    Vector<int> x12Wins = Vector.LessThan(xu, a) & Vector.LessThan(xu, c) & Vector.LessThan(xu, t)
                        & Vector.LessThan(xu, e) & Vector.LessThan(xu, b);
                    Vector<int> c40Wins = Vector.LessThan(cu, a) & Vector.LessThan(cu, t) & Vector.LessThan(cu, e)
                        & Vector.LessThan(cu, b) & Vector.LessThanOrEqual(c, x);

                    Vector<int> wins = asciiWins | base256Wins | edifactWins | textWins | x12Wins | c40Wins;
                    if (wins == Vector<int>.Zero)
                    {
                        continue;
                    }

                    for (int lane = 0; lane < lanes; lane++)
                    {
                        if (wins[lane] == 0)
                        {
                            continue;
                        }

                        int read = next + lane;
                        int choice = this.EarlyChoice(
                            asciiOffset + asciiSums[read],
                            c40Offset + c40Sums[read],
                            textOffset + textSums[read],
                            x12Offset + x12Sums[read],
                            edifactOffset + edifactSums[read],
                            base256Offset + base256Sums[read],
                            read,
                            fnc1);

                        if (choice != NoChoice)
                        {
                            return choice;
                        }
                    }
                }
            }

            // Stage 3 reads the characters that stage 2 left, one at a time. Without vector hardware this is
            // all the characters after stage 1.
            while (index < length)
            {
                index++;
                int choice = this.EarlyChoice(
                    asciiOffset + asciiSums[index],
                    c40Offset + c40Sums[index],
                    textOffset + textSums[index],
                    x12Offset + x12Sums[index],
                    edifactOffset + edifactSums[index],
                    base256Offset + base256Sums[index],
                    index,
                    fnc1);

                if (choice != NoChoice)
                {
                    return choice;
                }
            }

            // At the end of the data every count rounds up to whole codewords, because a part of a codeword
            // still takes a codeword. ASCII wins a tie, then each scheme wins only by being strictly smallest,
            // in the order Base 256, EDIFACT, Text and X12, and C40 takes what is left.
            ascii = Ceiling(asciiOffset + asciiSums[length]);
            c40 = Ceiling(c40Offset + c40Sums[length]);
            text = Ceiling(textOffset + textSums[length]);
            x12 = Ceiling(x12Offset + x12Sums[length]);
            edifact = Ceiling(edifactOffset + edifactSums[length]);
            base256 = Ceiling(base256Offset + base256Sums[length]);
            if (ascii <= c40 && ascii <= text && ascii <= x12 && ascii <= edifact && ascii <= base256)
            {
                return Ascii;
            }

            if (base256 < ascii && base256 < c40 && base256 < text && base256 < x12 && base256 < edifact)
            {
                return fnc1 ? Ascii : Base256;
            }

            if (edifact < ascii && edifact < c40 && edifact < text && edifact < x12 && edifact < base256)
            {
                return Edifact;
            }

            if (text < ascii && text < c40 && text < x12 && text < edifact && text < base256)
            {
                return Text;
            }

            if (x12 < ascii && x12 < c40 && x12 < text && x12 < edifact && x12 < base256)
            {
                return X12;
            }

            return C40;
        }

        /// <summary>
        /// The early test of the look-ahead test, from the fifth character: a scheme wins when it is at least
        /// one whole codeword cheaper than every other scheme. ASCII and Base 256 win with a margin of exactly
        /// one codeword; the others need more than one.
        /// </summary>
        /// <param name="ascii">The ASCII count.</param>
        /// <param name="c40">The C40 count.</param>
        /// <param name="text">The Text count.</param>
        /// <param name="x12">The X12 count.</param>
        /// <param name="edifact">The EDIFACT count.</param>
        /// <param name="base256">The Base 256 count.</param>
        /// <param name="read">The number of characters read: the counts stand after the character before it.</param>
        /// <param name="fnc1">Whether the test started at a Function 1 Symbol Character, which Base 256 cannot carry.</param>
        /// <returns>The winning scheme, or <see cref="NoChoice"/> when no scheme wins yet.</returns>
        private readonly int EarlyChoice(int ascii, int c40, int text, int x12, int edifact, int base256, int read, bool fnc1)
        {
            if (ascii + Unit <= c40 && ascii + Unit <= text && ascii + Unit <= x12 && ascii + Unit <= edifact && ascii + Unit <= base256)
            {
                return Ascii;
            }

            if (base256 + Unit <= ascii
                || (base256 + Unit < c40 && base256 + Unit < text && base256 + Unit < x12 && base256 + Unit < edifact))
            {
                return fnc1 ? Ascii : Base256;
            }

            if (edifact + Unit < ascii && edifact + Unit < c40 && edifact + Unit < text && edifact + Unit < x12 && edifact + Unit < base256)
            {
                return Edifact;
            }

            if (text + Unit < ascii && text + Unit < c40 && text + Unit < x12 && text + Unit < edifact && text + Unit < base256)
            {
                return Text;
            }

            if (x12 + Unit < ascii && x12 + Unit < c40 && x12 + Unit < text && x12 + Unit < edifact && x12 + Unit < base256)
            {
                return X12;
            }

            if (c40 + Unit < ascii && c40 + Unit < text && c40 + Unit < edifact && c40 + Unit < base256)
            {
                if (c40 < x12)
                {
                    return C40;
                }

                // With equal counts X12 wins when an X12 terminator comes before a character X12 cannot
                // encode.
                if (c40 == x12)
                {
                    return this.terminator[read] < this.nonX12[read] ? X12 : C40;
                }
            }

            return NoChoice;
        }
    }
}
