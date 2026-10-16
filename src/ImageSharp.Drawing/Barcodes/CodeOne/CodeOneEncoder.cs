// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodes Code One symbols. Versions A to H and T carry 8-bit codewords in the ASCII, C40, Text, X12, Decimal and Byte modes, chosen by a look-ahead over the
/// data, with Reed-Solomon error correction over GF(256). Versions S carry a number as 5-bit codewords with error correction over GF(32). The codewords fill
/// the data columns in 2-row bricks around the centre pattern, the risers and the black dots.
/// </summary>
internal static class CodeOneEncoder
{
    /// <summary>
    /// The longest input: version H holds 1480 data codewords, and Decimal mode, the densest, writes three digits in 10 bits, so at most 3550 characters fit.
    /// </summary>
    public const int MaximumLength = 3550;

    /// <summary>
    /// The ASCII codeword that pads the data codewords to the capacity of the version.
    /// </summary>
    private const int Pad = 129;

    /// <summary>
    /// The ASCII codeword of the first digit pair, 00. A pair of digits is this codeword plus its value.
    /// </summary>
    private const int DigitPairBase = 130;

    /// <summary>
    /// The ASCII codeword that latches to C40 mode.
    /// </summary>
    private const int LatchC40 = 230;

    /// <summary>
    /// The ASCII codeword that latches to Byte mode.
    /// </summary>
    private const int LatchByte = 231;

    /// <summary>
    /// The ASCII codeword of the Upper Shift, which adds 128 to the next character.
    /// </summary>
    private const int UpperShift = 235;

    /// <summary>
    /// The ASCII codeword that latches to X12 mode.
    /// </summary>
    private const int LatchX12 = 238;

    /// <summary>
    /// The ASCII codeword that latches to Text mode.
    /// </summary>
    private const int LatchText = 239;

    /// <summary>
    /// The C40 and Text value of Shift 1, the set of the control characters.
    /// </summary>
    private const int Shift1 = 0;

    /// <summary>
    /// The C40 and Text value of Shift 2, the set of the punctuation and the Upper Shift.
    /// </summary>
    private const int Shift2 = 1;

    /// <summary>
    /// The C40 and Text value of Shift 3, the set of the other case and the characters 96 to 127.
    /// </summary>
    private const int Shift3 = 2;

    /// <summary>
    /// The Shift 2 set value of the Upper Shift in C40 and Text mode.
    /// </summary>
    private const int ShiftedUpperShift = 30;

    /// <summary>
    /// The scale of the look-ahead counts. The counts step in halves and thirds, so twelve units to the codeword keep every count a whole number and every
    /// comparison exact.
    /// </summary>
    private const int Unit = 12;

    /// <summary>
    /// The data codewords of version H, the most any version holds.
    /// </summary>
    private const int MaximumCodewords = 1480;

    /// <summary>
    /// The check codewords of version H, the most any version holds.
    /// </summary>
    private const int MaximumCheckCodewords = 560;

    /// <summary>
    /// The cells of version H, 148 rows of 134 columns, the most any version holds.
    /// </summary>
    private const int MaximumCells = 148 * 134;

    /// <summary>
    /// The number of values one run of C40, Text or X12 mode can gather: three values pack into two codewords, so a run of more than three halves of
    /// <see cref="MaximumCodewords"/> cannot fit. A character takes at most four values, and the twelve places beyond hold the values of the last three
    /// characters.
    /// </summary>
    private const int MaximumValues = (MaximumCodewords * 3 / 2) + 12;

    /// <summary>
    /// The codeword that unlatches C40, Text and X12 mode back to ASCII mode.
    /// </summary>
    private const int Unlatch = 255;

    /// <summary>
    /// The four bits that latch from ASCII mode into Decimal mode, the high bits of the first Decimal codeword.
    /// </summary>
    private const int DecimalLatch = 0b1111;

    /// <summary>
    /// The six bits that unlatch Decimal mode back to ASCII mode.
    /// </summary>
    private const int DecimalUnlatch = 0b111111;

    /// <summary>
    /// The number of characters the look-ahead reads before a mode can win early.
    /// </summary>
    private const int MinimumLookAhead = 4;

    /// <summary>
    /// The number of entries a version takes in <see cref="VersionTable"/>.
    /// </summary>
    private const int VersionStride = 13;

    /// <summary>
    /// The field of the 8-bit codewords of versions A to H and T: GF(256) with the polynomial x^8 + x^5 + x^3 + x^2 + 1, 301 decimal.
    /// </summary>
    private static readonly GaloisField Field8 = new(256, 301);

    /// <summary>
    /// The field of the 5-bit codewords of the S versions: GF(32) with the polynomial x^5 + x^2 + 1, 37 decimal.
    /// </summary>
    private static readonly GaloisField Field5 = new(32, 37);

    /// <summary>
    /// The modes of versions A to H and T.
    /// </summary>
    private enum Mode
    {
        Ascii,
        C40,
        Text,
        X12,
        Byte,
        Decimal,
    }

    /// <summary>
    /// The kinds of the rows of the centre pattern.
    /// </summary>
    private enum CentreRow : byte
    {
        /// <summary>
        /// Every module light.
        /// </summary>
        Light = 1,

        /// <summary>
        /// Every module dark.
        /// </summary>
        Dark = 2,

        /// <summary>
        /// Light ends around a dark run.
        /// </summary>
        DarkWithLightEnds = 3,

        /// <summary>
        /// Light, with a dark module one in from each end.
        /// </summary>
        LightWithDarkInner = 4,

        /// <summary>
        /// A single dark module in the middle. The other cells take data.
        /// </summary>
        DarkCentre = 5,

        /// <summary>
        /// A single light module in the middle. The other cells take data.
        /// </summary>
        LightCentre = 6,

        /// <summary>
        /// Dark ends around a light run.
        /// </summary>
        LightWithDarkEnds = 7,

        /// <summary>
        /// Dark, with a light module one in from each end.
        /// </summary>
        DarkWithLightInner = 8,
    }

    /// <summary>
    /// Gets the dimensions and fixed patterns of each version in the order of <see cref="CodeOneVersion"/>, <see cref="VersionStride"/> entries a version: the
    /// rows, the columns, the columns the codeword bricks fill, the data codewords, the check codewords, the Reed-Solomon blocks, the column of the first
    /// riser, the columns from one riser to the next, the rows of a riser, the start and the length of the centre pattern in <see cref="CentreRows"/>, and the
    /// start and the count of the black dots in <see cref="BlackDots"/>.
    /// </summary>
    private static ReadOnlySpan<short> VersionTable =>
    [
        16, 18, 16, 10, 10, 1, 4, 18, 6, 0, 6, 0, 1,
        22, 22, 20, 19, 16, 1, 4, 22, 8, 6, 8, 1, 1,
        28, 32, 28, 44, 26, 1, 4, 22, 11, 14, 8, 2, 1,
        40, 42, 36, 91, 44, 1, 4, 16, 16, 22, 10, 3, 0,
        52, 54, 48, 182, 70, 1, 4, 22, 22, 32, 10, 3, 1,
        70, 76, 68, 370, 140, 2, 4, 22, 31, 42, 10, 4, 4,
        104, 98, 88, 732, 280, 4, 6, 21, 47, 52, 12, 8, 2,
        148, 134, 120, 1480, 560, 8, 6, 20, 69, 64, 12, 10, 6,
        8, 11, 10, 4, 4, 1, 0, 0, 0, 76, 8, 16, 0,
        8, 21, 20, 8, 8, 1, 0, 0, 0, 76, 8, 16, 1,
        8, 31, 30, 12, 12, 1, 0, 0, 0, 76, 8, 17, 2,
        16, 17, 16, 10, 10, 1, 0, 0, 0, 84, 16, 19, 1,
        16, 33, 32, 24, 16, 1, 0, 0, 0, 84, 16, 20, 2,
        16, 49, 48, 38, 22, 1, 0, 0, 0, 84, 16, 22, 3,
    ];

    /// <summary>
    /// Gets the centre patterns of the versions as <see cref="CentreRow"/> kinds, top row first: versions A to H in turn, then the pattern the S versions share
    /// and the pattern the T versions share.
    /// </summary>
    private static ReadOnlySpan<byte> CentreRows =>
    [
        1, 2, 1, 3, 4, 3,
        1, 2, 1, 3, 4, 3, 4, 3,
        1, 2, 1, 2, 1, 3, 4, 3,
        1, 2, 1, 3, 4, 3, 4, 3, 4, 3,
        1, 2, 1, 2, 1, 3, 4, 3, 4, 3,
        1, 2, 1, 2, 1, 2, 1, 3, 4, 3,
        1, 2, 1, 2, 1, 3, 4, 3, 4, 3, 4, 3,
        1, 2, 1, 2, 1, 2, 1, 3, 4, 3, 4, 3,
        5, 6, 6, 6, 1, 2, 7, 8,
        5, 6, 6, 6, 6, 6, 6, 6, 6, 6, 1, 2, 7, 8, 7, 8,
    ];

    /// <summary>
    /// Gets the black dots of the versions as column and row pairs, the versions in the order of <see cref="CodeOneVersion"/>.
    /// </summary>
    private static ReadOnlySpan<byte> BlackDots =>
    [
        12, 5,
        16, 7,
        26, 12,
        26, 23,
        26, 32, 70, 32, 26, 34, 70, 34,
        27, 48, 69, 48,
        26, 70, 66, 70, 106, 70, 26, 72, 66, 72, 106, 72,
        10, 4,
        15, 4, 15, 6,
        8, 10,
        16, 10, 16, 12,
        24, 10, 24, 12, 24, 14,
    ];

    /// <summary>
    /// Builds a Code One symbol.
    /// </summary>
    /// <param name="text">The text, characters U+0000 to U+00FF, or digits for an S version.</param>
    /// <param name="version">The version, or <see langword="null"/> for the smallest of versions A to H.</param>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    /// <param name="symbol">The symbol to write.</param>
    /// <exception cref="ArgumentException">The text is empty, has a character the version cannot carry, or does not fit.</exception>
    public static void BuildSymbol(ReadOnlySpan<char> text, CodeOneVersion? version, double xDimension, ref BarcodeSymbol symbol)
    {
        if (text.IsEmpty)
        {
            throw new ArgumentException("Code One needs at least one character.", nameof(text));
        }

        bool number = version is CodeOneVersion.S10 or CodeOneVersion.S20 or CodeOneVersion.S30;
        if (!number)
        {
            Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));
            int invalid = text.IndexOfAnyExceptInRange('\0', '\u00FF');
            if (invalid >= 0)
            {
                throw new ArgumentException($"Code One encodes U+0000 to U+00FF; got {text.CodePointAt(invalid).ToDisplayString()}.", nameof(text));
            }
        }

        // One rented block holds the codewords and the cells of the largest version, and, for the data of versions A to H and T, the scratch of the encodation.
        // The text is at most MaximumLength characters, so the block is bounded.
        const int codewordLength = MaximumCodewords + MaximumCheckCodewords;
        const int cellLength = MaximumCells / sizeof(int);
        int scratchLength = number ? 0 : ScratchLength(text.Length);
        using PooledBuffer<int> block = new(codewordLength + cellLength + scratchLength);
        Span<int> codewords = block.Span[..codewordLength];
        Span<sbyte> cells = MemoryMarshal.Cast<int, sbyte>(block.Span.Slice(codewordLength, cellLength));
        CodeOneVersion chosen;
        if (number)
        {
            chosen = version!.Value;
            EncodeNumber(text, new VersionInfo(chosen).DataCodewords, codewords);
        }
        else
        {
            int count = EncodeData(text, version, codewords, block.Span.Slice(codewordLength + cellLength, scratchLength));
            if (count < 0 || !TryChoose(version, count, out chosen))
            {
                throw new ArgumentException("The text does not fit in a Code One symbol of the requested version.", nameof(text));
            }

            codewords[count..new VersionInfo(chosen).DataCodewords].Fill(Pad);
        }

        VersionInfo info = new(chosen);
        Span<int> all = codewords[..(info.DataCodewords + info.CheckCodewords)];
        AddCheckCodewords(all, info.DataCodewords, info.CheckBlocks, info.IsNumeric);

        // Versions A to H need no quiet zone. Versions S and T have one module of quiet zone on the left, the right and the bottom.
        Span<bool> modules = chosen >= CodeOneVersion.S10
            ? symbol.BeginMatrix(info.Columns, info.Rows, 1, 1, 0, 1, xDimension)
            : symbol.BeginMatrix(info.Columns, info.Rows, 0, xDimension);

        Layout(info, all, cells[..(info.Rows * info.Columns)], modules);
    }

    /// <summary>
    /// Returns the length in ints of the scratch <see cref="EncodeData"/> takes: one <see cref="LookAheadEntry"/> for each position of the text and one past
    /// its end, then the values of a C40, Text or X12 run.
    /// </summary>
    /// <param name="length">The length of the text.</param>
    /// <returns>The scratch length.</returns>
    private static int ScratchLength(int length)
        => (LookAheadEntry.IntCount * (length + 1)) + MaximumValues;

    /// <summary>
    /// Finds the smallest permitted version that holds the codewords.
    /// </summary>
    /// <param name="version">The version, or <see langword="null"/> for versions A to H.</param>
    /// <param name="count">The number of data codewords.</param>
    /// <param name="chosen">The version found.</param>
    /// <returns><see langword="true"/> when a version holds the codewords; otherwise, <see langword="false"/>.</returns>
    private static bool TryChoose(CodeOneVersion? version, int count, out CodeOneVersion chosen)
    {
        for (CodeOneVersion v = CodeOneVersion.A; v <= CodeOneVersion.T48; v++)
        {
            if (IsPermitted(version, v) && count <= new VersionInfo(v).DataCodewords)
            {
                chosen = v;
                return true;
            }
        }

        chosen = default;
        return false;
    }

    /// <summary>
    /// Gets a value indicating whether a version is open to data of the requested version: versions A to H when none is requested, or the requested version
    /// itself. The S versions carry numbers only.
    /// </summary>
    /// <param name="requested">The requested version, or <see langword="null"/>.</param>
    /// <param name="candidate">The candidate version.</param>
    /// <returns><see langword="true"/> when the candidate is open.</returns>
    private static bool IsPermitted(CodeOneVersion? requested, CodeOneVersion candidate)
        => !new VersionInfo(candidate).IsNumeric && (requested is null ? candidate <= CodeOneVersion.H : candidate == requested.Value);

    /// <summary>
    /// Encodes the digits of an S version as one number plus one in 5-bit codewords, the most significant first, padded at the front with zero codewords to the
    /// data codeword count.
    /// </summary>
    /// <param name="text">The digits.</param>
    /// <param name="dataCount">The data codeword count of the version.</param>
    /// <param name="codewords">The buffer the codewords are written to.</param>
    private static void EncodeNumber(ReadOnlySpan<char> text, int dataCount, Span<int> codewords)
    {
        if (text.Length > 18 || text.ContainsAnyExceptInRange('0', '9'))
        {
            throw new ArgumentException("A Code One S version carries 1 to 18 digits.", nameof(text));
        }

        ulong value = ulong.Parse(text, System.Globalization.CultureInfo.InvariantCulture) + 1;
        if (value >> (5 * dataCount) != 0)
        {
            throw new ArgumentException("The number does not fit in the requested Code One S version.", nameof(text));
        }

        for (int i = dataCount - 1; i >= 0; i--)
        {
            codewords[i] = (int)(value & 31);
            value >>= 5;
        }
    }

    /// <summary>
    /// Encodes the data of versions A to H and T into codewords.
    /// </summary>
    /// <param name="text">The data.</param>
    /// <param name="version">The version, or <see langword="null"/> for versions A to H.</param>
    /// <param name="codewords">The buffer the codewords are written to.</param>
    /// <param name="scratch">The scratch of the encodation, <see cref="ScratchLength"/> long.</param>
    /// <returns>The number of codewords, or -1 when no permitted version holds them.</returns>
    private static int EncodeData(ReadOnlySpan<char> text, CodeOneVersion? version, Span<int> codewords, Span<int> scratch)
    {
        // The entries are plain ints, so the int scratch holds them in place.
        int tableLength = LookAheadEntry.IntCount * (text.Length + 1);
        Span<LookAheadEntry> table = MemoryMarshal.Cast<int, LookAheadEntry>(scratch[..tableLength]);
        PrepareLookAhead(text, table);
        Encoder encoder = new(text, version, codewords, table, scratch.Slice(tableLength, MaximumValues));
        return encoder.Run();
    }

    /// <summary>
    /// Fills the entries the look-ahead reads, one for each position of the message and one past its end. Each entry holds three runs from its position: the
    /// digits that start there, the distance to the next X12 terminator, carriage return, asterisk or greater than, and the distance to the next character X12
    /// cannot carry. The entry past the end stops each run, and a distance with no end stands past the end. Each entry also holds the running sums of the costs
    /// of the ASCII, C40, Text, X12 and Byte modes before its position.
    /// <para>
    /// A digit costs half a codeword in ASCII mode and a character above 127 two whole codewords; any other character rounds the count up first, because it
    /// cannot share a codeword with a digit before it. A basic set character costs two thirds of a codeword in C40, Text and X12 mode, one of three values in
    /// two codewords. A shifted character costs two values and an Upper Shift two more. X12 costs a character it cannot carry 10/3 codewords, or 13/3 above
    /// 127. Byte mode costs one codeword a byte.
    /// </para>
    /// </summary>
    /// <param name="message">The message, characters U+0000 to U+00FF.</param>
    /// <param name="table">The entries, the message length plus one.</param>
    private static void PrepareLookAhead(ReadOnlySpan<char> message, Span<LookAheadEntry> table)
    {
        int length = message.Length;
        ref LookAheadEntry end = ref table[length];
        end.Digits = 0;
        end.Terminator = length + 1;
        end.NonX12 = length + 1;
        for (int i = length - 1; i >= 0; i--)
        {
            char value = message[i];
            ref LookAheadEntry entry = ref table[i];
            ref readonly LookAheadEntry next = ref table[i + 1];
            entry.Digits = IsDigit(value) ? next.Digits + 1 : 0;
            entry.Terminator = value is '\r' or '*' or '>' ? 0 : next.Terminator + 1;
            entry.NonX12 = IsX12(value) ? next.NonX12 + 1 : 0;
        }

        table[0].Sums = default;
        for (int i = 0; i < length; i++)
        {
            char value = message[i];
            bool extended = value > 127;
            ref readonly ModeCounts sum = ref table[i].Sums;
            ref ModeCounts next = ref table[i + 1].Sums;
            next.Ascii = AddAsciiCost(sum.Ascii, value);
            next.C40 = sum.C40 + (IsC40Basic(value) ? 2 * Unit / 3 : extended ? 8 * Unit / 3 : 4 * Unit / 3);
            next.Text = sum.Text + (IsTextBasic(value) ? 2 * Unit / 3 : extended ? 8 * Unit / 3 : 4 * Unit / 3);
            next.X12 = sum.X12 + (IsX12(value) ? 2 * Unit / 3 : extended ? 13 * Unit / 3 : 10 * Unit / 3);
            next.Byte = sum.Byte + Unit;
        }
    }

    /// <summary>
    /// Returns whether the message value is a digit.
    /// </summary>
    /// <param name="value">The message value.</param>
    /// <returns><see langword="true"/> for the digits 0 to 9.</returns>
    private static bool IsDigit(int value) => value is >= '0' and <= '9';

    /// <summary>
    /// Returns whether the message value is in the basic set of C40 mode: space, the digits and the capital letters.
    /// </summary>
    /// <param name="value">The message value.</param>
    /// <returns><see langword="true"/> for a basic set character.</returns>
    private static bool IsC40Basic(int value) => value is ' ' or (>= '0' and <= '9') or (>= 'A' and <= 'Z');

    /// <summary>
    /// Returns whether the message value is in the basic set of Text mode: space, the digits and the small letters.
    /// </summary>
    /// <param name="value">The message value.</param>
    /// <returns><see langword="true"/> for a basic set character.</returns>
    private static bool IsTextBasic(int value) => value is ' ' or (>= '0' and <= '9') or (>= 'a' and <= 'z');

    /// <summary>
    /// Returns whether X12 mode carries the message value: carriage return, asterisk, greater than, space, the digits and the capital letters.
    /// </summary>
    /// <param name="value">The message value.</param>
    /// <returns><see langword="true"/> for an X12 character.</returns>
    private static bool IsX12(int value) => value is '\r' or '*' or '>' || IsC40Basic(value);

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
    private static int AddAsciiCost(int count, int value)
        => IsDigit(value) ? count + (Unit / 2) : Ceiling(count) + (value > 127 ? 2 * Unit : Unit);

    /// <summary>
    /// Writes the C40, Text or X12 values of one message value.
    /// </summary>
    /// <param name="mode">The mode, <see cref="Mode.C40"/>, <see cref="Mode.Text"/> or <see cref="Mode.X12"/>.</param>
    /// <param name="value">The message value, 0 to 255.</param>
    /// <param name="values">The buffer of at least four values that receives the values.</param>
    /// <returns>The number of values written, or 0 when X12 mode cannot carry the message value.</returns>
    private static int TripletValues(Mode mode, int value, Span<int> values)
    {
        // X12 has one set of 40 values and no shifts: carriage return 0, asterisk 1, greater than 2, space 3, the digits 4 to 13 and the capital letters 14 to
        // 39. Any other character ends X12.
        if (mode == Mode.X12)
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

        // A character above 127 takes the Upper Shift in the Shift 2 set, then the values of the character 128 below it.
        int count = 0;
        if (value > 127)
        {
            values[0] = Shift2;
            values[1] = ShiftedUpperShift;
            count = 2;
            value -= 128;
        }

        // The basic set: space is 3 and the digits 0 to 9 are 4 to 13, so a digit is its character code less 44.
        Span<int> rest = values[count..];
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

        // The basic set holds the capital letters in C40 mode and the small letters in Text mode, as the values 14 to 39, so A is 65 - 51 and a is 97 - 83. The
        // other case moves to the Shift 3 set, where C40 puts the small letters at 1 to 26 after the grave accent at 0, and Text puts the capital letters
        // there.
        if (value is >= 'A' and <= 'Z')
        {
            if (mode == Mode.C40)
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
            if (mode == Mode.Text)
            {
                rest[0] = value - 83;
                return count + 1;
            }

            rest[0] = Shift3;
            rest[1] = value - 96;
            return count + 2;
        }

        // The rest of the characters to 127 take a shift and a value in that set. The control characters 0 to 31 are the Shift 1 set in order. The Shift 2 set
        // holds the punctuation in three runs: 33 to 47 as 0 to 14, 58 to 64 as 15 to 21 and 91 to 95 as 22 to 26. The Shift 3 set holds 96 to 127 as 0 to 31;
        // the letters of that range were handled above.
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
    /// Adds the check codewords after the data codewords. The data codewords are spread over the blocks in turn, and the check codewords of the blocks are
    /// interleaved the same way. Each block uses the generator with roots alpha^0 to alpha^(k-1).
    /// </summary>
    /// <param name="codewords">The data codewords followed by room for the check codewords.</param>
    /// <param name="dataCount">The number of data codewords.</param>
    /// <param name="blocks">The number of blocks.</param>
    /// <param name="numeric">Whether the codewords are the 5-bit codewords of an S version.</param>
    private static void AddCheckCodewords(Span<int> codewords, int dataCount, int blocks, bool numeric)
    {
        // A block takes at most 70 check codewords: version E in one block, and versions F, G and H in 2, 4 and 8.
        GaloisField field = numeric ? Field5 : Field8;
        int perBlock = (codewords.Length - dataCount) / blocks;
        InlineArray128<int> generatorStorage = default;
        InlineArray128<int> checkStorage = default;
        Span<int> generator = ((Span<int>)generatorStorage)[..perBlock];
        field.Generator(0, generator);
        field.InterleavedRemainders(codewords, dataCount, blocks, generator, ((Span<int>)checkStorage)[..perBlock]);
    }

    /// <summary>
    /// Places the modules: the centre pattern rows, the risers and the black dots first, then the codeword bricks in the cells that remain, in reading order.
    /// </summary>
    /// <param name="info">The version.</param>
    /// <param name="codewords">The data and check codewords.</param>
    /// <param name="cells">The scratch of the cells, one a module.</param>
    /// <param name="modules">The modules to write.</param>
    private static void Layout(VersionInfo info, ReadOnlySpan<int> codewords, Span<sbyte> cells, Span<bool> modules)
    {
        int rows = info.Rows;
        int columns = info.Columns;

        // Each cell is unset, -1, until a pattern takes it; the cells left unset take the data.
        cells.Fill(-1);

        ReadOnlySpan<byte> pattern = info.CentrePattern;
        int patternTop = (rows - pattern.Length) / 2;
        for (int p = 0; p < pattern.Length; p++)
        {
            WriteCentreRow((CentreRow)pattern[p], cells.Slice((patternTop + p) * columns, columns));
        }

        // A riser is a dark module and, every 12 rows, a second dark module beside it, on the top rows, with its mirror on the bottom rows.
        for (int i = 0; i < info.RiserLength; i++)
        {
            sbyte second = (sbyte)(i % 12 == 0 ? 1 : 0);
            for (int x = info.RiserOffset; x < columns; x += info.RiserInterval)
            {
                cells[(i * columns) + x] = 1;
                cells[(i * columns) + x + 1] = second;
                if (i != info.RiserLength - 1)
                {
                    int mirror = ((rows - i - 1) * columns) + columns - x - 2;
                    cells[mirror] = 1;
                    cells[mirror + 1] = second;
                }
            }
        }

        ReadOnlySpan<byte> dots = info.BlackDots;
        for (int d = 0; d < dots.Length; d += 2)
        {
            cells[(dots[d + 1] * columns) + dots[d]] = 1;
        }

        // The data cells, in reading order, are the data grid of the version row by row. A brick is two rows high: an 8-bit codeword is 4 modules wide, its
        // first four bits on top, and two 5-bit codewords make a brick 5 modules wide, the first three bits of each on top.
        int dataColumns = info.DataColumns;
        bool numeric = info.IsNumeric;
        int brickWidth = numeric ? 5 : 4;
        int bricksPerRow = dataColumns / brickWidth;
        int next = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] >= 0)
            {
                modules[i] = cells[i] == 1;
                continue;
            }

            int row = next / dataColumns;
            int column = next % dataColumns;
            next++;
            int brick = ((row / 2) * bricksPerRow) + (column / brickWidth);
            int b = column % brickWidth;
            bool top = (row & 1) == 0;
            int bit;
            if (numeric)
            {
                int first = codewords[2 * brick];
                int second = codewords[(2 * brick) + 1];
                bit = top
                    ? b < 3 ? first >> (4 - b) : second >> (7 - b)
                    : b < 2 ? first >> (1 - b) : second >> (4 - b);
            }
            else
            {
                bit = codewords[brick] >> (top ? 7 - b : 3 - b);
            }

            modules[i] = (bit & 1) == 1;
        }
    }

    /// <summary>
    /// Writes one row of the centre pattern. The <see cref="CentreRow.DarkCentre"/> and <see cref="CentreRow.LightCentre"/> rows set the middle cell alone and
    /// leave the other cells to the data.
    /// </summary>
    /// <param name="kind">The row kind.</param>
    /// <param name="row">The row of cells.</param>
    private static void WriteCentreRow(CentreRow kind, Span<sbyte> row)
    {
        int last = row.Length - 1;
        switch (kind)
        {
            case CentreRow.Light:
                row.Clear();
                break;
            case CentreRow.Dark:
                row.Fill(1);
                break;
            case CentreRow.DarkWithLightEnds:
                row.Fill(1);
                row[0] = 0;
                row[last] = 0;
                break;
            case CentreRow.LightWithDarkInner:
                row.Clear();
                row[1] = 1;
                row[last - 1] = 1;
                break;
            case CentreRow.DarkCentre:
                row[last / 2] = 1;
                break;
            case CentreRow.LightCentre:
                row[last / 2] = 0;
                break;
            case CentreRow.LightWithDarkEnds:
                row.Clear();
                row[0] = 1;
                row[last] = 1;
                break;
            default:
                row.Fill(1);
                row[1] = 0;
                row[last - 1] = 0;
                break;
        }
    }

    /// <summary>
    /// The dimensions and fixed patterns of one version, read from <see cref="VersionTable"/>.
    /// </summary>
    private readonly struct VersionInfo
    {
        /// <summary>
        /// The index of the first entry of the version in <see cref="VersionTable"/>.
        /// </summary>
        private readonly int offset;

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionInfo"/> struct.
        /// </summary>
        /// <param name="version">The version.</param>
        public VersionInfo(CodeOneVersion version) => this.offset = (int)version * VersionStride;

        /// <summary>
        /// Gets the rows.
        /// </summary>
        public int Rows => VersionTable[this.offset];

        /// <summary>
        /// Gets the columns.
        /// </summary>
        public int Columns => VersionTable[this.offset + 1];

        /// <summary>
        /// Gets the columns the codeword bricks fill.
        /// </summary>
        public int DataColumns => VersionTable[this.offset + 2];

        /// <summary>
        /// Gets the data codewords.
        /// </summary>
        public int DataCodewords => VersionTable[this.offset + 3];

        /// <summary>
        /// Gets the check codewords.
        /// </summary>
        public int CheckCodewords => VersionTable[this.offset + 4];

        /// <summary>
        /// Gets the Reed-Solomon blocks.
        /// </summary>
        public int CheckBlocks => VersionTable[this.offset + 5];

        /// <summary>
        /// Gets the column of the first riser.
        /// </summary>
        public int RiserOffset => VersionTable[this.offset + 6];

        /// <summary>
        /// Gets the columns from one riser to the next; the column count where one riser stands alone.
        /// </summary>
        public int RiserInterval => VersionTable[this.offset + 7];

        /// <summary>
        /// Gets the rows of a riser, 0 for none.
        /// </summary>
        public int RiserLength => VersionTable[this.offset + 8];

        /// <summary>
        /// Gets the row kinds of the centre pattern, top row first, as <see cref="CentreRow"/> values.
        /// </summary>
        public ReadOnlySpan<byte> CentrePattern => CentreRows.Slice(VersionTable[this.offset + 9], VersionTable[this.offset + 10]);

        /// <summary>
        /// Gets the black dots as column and row pairs.
        /// </summary>
        public ReadOnlySpan<byte> BlackDots => CodeOneEncoder.BlackDots.Slice(VersionTable[this.offset + 11] * 2, VersionTable[this.offset + 12] * 2);

        /// <summary>
        /// Gets a value indicating whether the version carries a number in 5-bit codewords, the S versions, which alone are 8 rows high.
        /// </summary>
        public bool IsNumeric => this.Rows == 8;
    }

    /// <summary>
    /// The counts of the modes the look-ahead compares, in the units of <see cref="Unit"/>.
    /// </summary>
    private struct ModeCounts
    {
        /// <summary>
        /// The ASCII count.
        /// </summary>
        public int Ascii;

        /// <summary>
        /// The C40 count.
        /// </summary>
        public int C40;

        /// <summary>
        /// The Text count.
        /// </summary>
        public int Text;

        /// <summary>
        /// The X12 count.
        /// </summary>
        public int X12;

        /// <summary>
        /// The Byte count.
        /// </summary>
        public int Byte;

        /// <summary>
        /// Adds the counts of each mode.
        /// </summary>
        /// <param name="left">The first counts.</param>
        /// <param name="right">The second counts.</param>
        /// <returns>The sums.</returns>
        public static ModeCounts operator +(ModeCounts left, ModeCounts right)
            => new()
            {
                Ascii = left.Ascii + right.Ascii,
                C40 = left.C40 + right.C40,
                Text = left.Text + right.Text,
                X12 = left.X12 + right.X12,
                Byte = left.Byte + right.Byte,
            };

        /// <summary>
        /// Subtracts the counts of each mode.
        /// </summary>
        /// <param name="left">The counts.</param>
        /// <param name="right">The counts to subtract.</param>
        /// <returns>The differences.</returns>
        public static ModeCounts operator -(ModeCounts left, ModeCounts right)
            => new()
            {
                Ascii = left.Ascii - right.Ascii,
                C40 = left.C40 - right.C40,
                Text = left.Text - right.Text,
                X12 = left.X12 - right.X12,
                Byte = left.Byte - right.Byte,
            };
    }

    /// <summary>
    /// The look-ahead data of one position of the message, which <see cref="PrepareLookAhead"/> fills. The fields are ints only, so an int scratch buffer holds
    /// the entries in place.
    /// </summary>
    private struct LookAheadEntry
    {
        /// <summary>
        /// The digits that start at the position.
        /// </summary>
        public int Digits;

        /// <summary>
        /// The distance from the position to the next X12 terminator.
        /// </summary>
        public int Terminator;

        /// <summary>
        /// The distance from the position to the next character X12 cannot carry.
        /// </summary>
        public int NonX12;

        /// <summary>
        /// The running sums of the mode costs of the characters before the position.
        /// </summary>
        public ModeCounts Sums;

        /// <summary>
        /// Gets the size of an entry in ints.
        /// </summary>
        public static int IntCount => Unsafe.SizeOf<LookAheadEntry>() / sizeof(int);
    }

    /// <summary>
    /// The state of the encodation of versions A to H and T.
    /// </summary>
    private ref struct Encoder
    {
        /// <summary>
        /// The message, characters U+0000 to U+00FF.
        /// </summary>
        private readonly ReadOnlySpan<char> message;

        /// <summary>
        /// The requested version, or <see langword="null"/> for versions A to H.
        /// </summary>
        private readonly CodeOneVersion? version;

        /// <summary>
        /// The codewords written so far.
        /// </summary>
        private readonly Span<int> codewords;

        /// <summary>
        /// The look-ahead entries, one for each position of the message and one past its end.
        /// </summary>
        private readonly ReadOnlySpan<LookAheadEntry> table;

        /// <summary>
        /// The values a C40, Text or X12 run gathers.
        /// </summary>
        private readonly Span<int> values;

        /// <summary>
        /// The current mode.
        /// </summary>
        private Mode mode;

        /// <summary>
        /// The position in the message.
        /// </summary>
        private int position;

        /// <summary>
        /// The number of codewords written.
        /// </summary>
        private int written;

        /// <summary>
        /// Whether the data overran the largest permitted version.
        /// </summary>
        private bool overflow;

        /// <summary>
        /// The Decimal mode bits not yet written as a whole codeword.
        /// </summary>
        private int pendingBits;

        /// <summary>
        /// The number of bits in <see cref="pendingBits"/>.
        /// </summary>
        private int pendingCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="Encoder"/> struct.
        /// </summary>
        /// <param name="message">The message, characters U+0000 to U+00FF.</param>
        /// <param name="version">The version, or <see langword="null"/> for versions A to H.</param>
        /// <param name="codewords">The buffer the codewords are written to.</param>
        /// <param name="table">The look-ahead entries of <see cref="PrepareLookAhead"/>.</param>
        /// <param name="values">The buffer of the values of a C40, Text or X12 run.</param>
        public Encoder(ReadOnlySpan<char> message, CodeOneVersion? version, Span<int> codewords, ReadOnlySpan<LookAheadEntry> table, Span<int> values)
        {
            this.message = message;
            this.version = version;
            this.codewords = codewords;
            this.table = table;
            this.values = values;
            this.mode = Mode.Ascii;
        }

        /// <summary>
        /// Encodes the data in the mode each step chooses until every character is written.
        /// </summary>
        /// <returns>The number of codewords, or -1 when no permitted version holds them.</returns>
        public int Run()
        {
            while (this.position < this.message.Length && !this.overflow)
            {
                switch (this.mode)
                {
                    case Mode.Ascii:
                        this.EncodeAscii();
                        break;
                    case Mode.Decimal:
                        this.EncodeDecimal();
                        break;
                    case Mode.Byte:
                        this.EncodeByte();
                        break;
                    default:
                        this.EncodeTriplets();
                        break;
                }
            }

            return this.overflow ? -1 : this.written;
        }

        /// <summary>
        /// One step of ASCII mode: a latch into Decimal mode before a long digit run, a digit pair, a latch into the mode the look-ahead chooses, or one
        /// character.
        /// </summary>
        private void EncodeAscii()
        {
            int run = this.table[this.position].Digits;
            if (run >= 21 || (run >= 13 && this.position + run == this.message.Length))
            {
                this.pendingBits = DecimalLatch;
                this.pendingCount = 4;
                this.mode = Mode.Decimal;
                return;
            }

            if (run >= 2)
            {
                this.AddDigitPair();
                return;
            }

            Mode next = this.LookAhead();
            if (next != this.mode)
            {
                this.Add(next switch
                {
                    Mode.C40 => LatchC40,
                    Mode.Text => LatchText,
                    Mode.X12 => LatchX12,
                    _ => LatchByte,
                });

                this.mode = next;
                return;
            }

            this.AddAscii(this.message[this.position++]);
        }

        /// <summary>
        /// Encodes in C40, Text or X12 mode, three values to two codewords, until the look-ahead or the end of the data returns to ASCII mode.
        /// </summary>
        private void EncodeTriplets()
        {
            Span<int> values = this.values;
            InlineArray4<int> scratchStorage = default;
            Span<int> scratch = scratchStorage;
            int p = 0;
            while (this.position != this.message.Length)
            {
                // Values past three halves of the largest version cannot fit, and the place for the last three characters stays free.
                if (p > values.Length - 12)
                {
                    this.overflow = true;
                    return;
                }

                if (p % 3 == 0)
                {
                    int run = this.table[this.position].Digits;
                    if (run >= 12 || (run >= 8 && this.position + run == this.message.Length))
                    {
                        this.UnlatchAfter(values[..p]);
                        return;
                    }

                    if (this.mode == Mode.X12)
                    {
                        if (!IsX12(this.message[this.position]))
                        {
                            this.AddTriplets(values[..p]);

                            // Unlatch to ASCII unless one codeword is left and one ASCII character fits it.
                            if (this.Remaining(this.written) != 1 || this.message[this.position] > 127)
                            {
                                this.Add(Unlatch);
                            }

                            this.mode = Mode.Ascii;
                            return;
                        }

                        if ((this.position + 1 < this.message.Length && !IsX12(this.message[this.position + 1]))
                            || (this.position + 2 < this.message.Length && !IsX12(this.message[this.position + 2])))
                        {
                            break;
                        }
                    }
                    else if (this.LookAhead() != this.mode)
                    {
                        this.UnlatchAfter(values[..p]);
                        return;
                    }

                    if (this.message.Length - this.position <= 3 && this.TryEndTriplets(values, p))
                    {
                        return;
                    }
                }

                p += TripletValues(this.mode, this.message[this.position++], values[p..]);
            }

            // Back up to a whole triplet and return to ASCII mode.
            while (p % 3 != 0)
            {
                p -= TripletValues(this.mode, this.message[--this.position], scratch);
            }

            this.UnlatchAfter(values[..p]);
            if (this.position != this.message.Length)
            {
                // A character written in ASCII mode keeps the next step from latching straight back.
                if (this.table[this.position].Digits >= 2)
                {
                    this.AddDigitPair();
                }
                else
                {
                    this.AddAscii(this.message[this.position++]);
                }
            }
        }

        /// <summary>
        /// Ends C40, Text or X12 mode at the last three or fewer characters when they fill the last codewords: a whole triplet, or two values and a pad Shift
        /// 1, with no unlatch, or one character in ASCII mode.
        /// </summary>
        /// <param name="values">The values, the first <paramref name="p"/> already gathered.</param>
        /// <param name="p">The number of values gathered, a multiple of three.</param>
        /// <returns><see langword="true"/> when the data ends here; otherwise, <see langword="false"/>.</returns>
        private bool TryEndTriplets(Span<int> values, int p)
        {
            int remaining = this.Remaining(this.written + (p / 3 * 2));
            int tail = p;
            for (int k = this.position; k < this.message.Length; k++)
            {
                int count = TripletValues(this.mode, this.message[k], values[tail..]);
                if (count == 0)
                {
                    // A character X12 cannot carry counts as four values, which no ending below accepts.
                    values.Slice(tail, 4).Fill(-1);
                    count = 4;
                }

                tail += count;
            }

            int tailCount = tail - p;
            if (remaining == 2 && tailCount == 3)
            {
                this.AddTriplets(values[..tail]);
            }
            else if (remaining == 2 && tailCount == 2 && this.mode != Mode.X12)
            {
                values[tail] = 0;
                this.AddTriplets(values[..(tail + 1)]);
            }
            else if (remaining == 2 && tailCount == 1)
            {
                this.AddTriplets(values[..p]);
                this.Add(Unlatch);
                this.AddAscii(this.message[this.position]);
            }
            else if (remaining == 1 && tailCount == 1)
            {
                this.AddTriplets(values[..p]);
                this.AddAscii(this.message[this.position]);
            }
            else
            {
                return false;
            }

            this.mode = Mode.Ascii;
            this.position = this.message.Length;
            return true;
        }

        /// <summary>
        /// Encodes in Decimal mode: three digits as the 10-bit value plus one, then the end of the run, which fills or unlatches the last codeword. Each whole
        /// codeword is written as its eighth bit arrives.
        /// </summary>
        private void EncodeDecimal()
        {
            ReadOnlySpan<char> message = this.message;
            while (this.table[this.position].Digits >= 3)
            {
                int value = ((message[this.position] - '0') * 100) + ((message[this.position + 1] - '0') * 10) + (message[this.position + 2] - '0');
                this.AddBits(value + 1, 10);
                this.position += 3;
            }

            int free = (8 - this.pendingCount) % 8;
            int remaining = this.Remaining(this.written);
            bool lastDigit = this.position == message.Length - 1 && this.table[this.position].Digits == 1;
            bool lastPair = this.position == message.Length - 2 && this.table[this.position].Digits == 2;
            if (this.position == message.Length && ((this.Remaining(this.written - 1) == 1 && free == 0) || (remaining == 1 && free != 0)))
            {
                // The data ends in the last codeword: fill its free bits.
                if (free is 4 or 6)
                {
                    this.AddBits(0b1111, 4);
                }

                if (free is 2 or 6)
                {
                    this.AddBits(0b01, 2);
                }
            }
            else if (!((lastDigit || lastPair) && remaining == 1 && free == 0))
            {
                // Unlatch to ASCII unless the last digit fits the 4 or 6 bits left in the last codeword. A final digit or digit pair that fills the last
                // codeword goes in it as ASCII instead.
                if (!(lastDigit && remaining == 1 && free is 4 or 6))
                {
                    this.AddBits(DecimalUnlatch, 6);
                    free = (8 - this.pendingCount) % 8;
                }

                if (free is 4 or 6)
                {
                    this.AddBits(this.table[this.position].Digits >= 1 ? message[this.position++] - '0' + 1 : 0b1111, 4);
                    free -= 4;
                }

                if (free == 2)
                {
                    this.AddBits(0b01, 2);
                }
            }

            this.mode = Mode.Ascii;
        }

        /// <summary>
        /// Encodes in Byte mode while the look-ahead keeps it: a length of one codeword below 250 or two above, or 0 when the bytes fill the symbol, then the
        /// bytes.
        /// </summary>
        private void EncodeByte()
        {
            int start = this.position;
            while (this.position != this.message.Length && this.LookAhead() == Mode.Byte)
            {
                this.position++;
            }

            int count = this.position - start;
            if (this.Remaining(this.written + count) == 1 && this.position == this.message.Length)
            {
                this.Add(0);
            }
            else if (count < 250)
            {
                this.Add(count);
            }
            else
            {
                this.Add((count / 250) + 249);
                this.Add(count % 250);
            }

            foreach (char value in this.message.Slice(start, count))
            {
                this.Add(value);
            }

            this.mode = Mode.Ascii;
        }

        /// <summary>
        /// The mode look-ahead. Each mode keeps a count of the codewords it needs for the data read so far, in the units of <see cref="Unit"/>, starting with
        /// the cost of the latch to it. The count of each mode but ASCII is an offset plus its running sum, so each step reads the sums; the ASCII count
        /// follows its sum once it stands at the same part of a codeword. From the fourth character one mode can win early, and at the end of the data the
        /// rounded counts decide.
        /// </summary>
        /// <returns>The mode to continue in.</returns>
        private readonly Mode LookAhead()
        {
            // Each count starts at the cost of the latch from the current mode: from ASCII mode one codeword into C40, Text or X12 mode and two into Byte mode,
            // and from any other mode one codeword more for the unlatch. The current mode costs nothing.
            const int unit = Unit;
            ModeCounts initial = this.mode == Mode.Ascii
                ? new ModeCounts { Ascii = 0, C40 = unit, Text = unit, X12 = unit, Byte = 2 * unit }
                : new ModeCounts { Ascii = unit, C40 = 2 * unit, Text = 2 * unit, X12 = 2 * unit, Byte = 3 * unit };

            switch (this.mode)
            {
                case Mode.C40:
                    initial.C40 = 0;
                    break;
                case Mode.Text:
                    initial.Text = 0;
                    break;
                case Mode.X12:
                    initial.X12 = 0;
                    break;
                case Mode.Byte:
                    initial.Byte = 0;
                    break;
            }

            // The count of a mode at a position is its offset plus the running sum there. Stage 1 counts ASCII by hand, so its offset is set when stage 1 ends.
            ReadOnlySpan<LookAheadEntry> table = this.table;
            int length = this.message.Length;
            int start = this.position;
            ModeCounts offset = initial - table[start].Sums;

            // Stage 1 reads one character at a time, until the early test may run and the ASCII count stands at the same part of a codeword as its running sum.
            int ascii = initial.Ascii;
            int index = start;
            while (index < length)
            {
                ascii = AddAsciiCost(ascii, this.message[index]);
                index++;
                if (index - start >= MinimumLookAhead)
                {
                    ModeCounts counts = offset + table[index].Sums;
                    counts.Ascii = ascii;
                    if (this.EarlyChoice(counts, index) is Mode early)
                    {
                        return early;
                    }

                    if ((ascii - table[index].Sums.Ascii) % unit == 0)
                    {
                        break;
                    }
                }
            }

            // Stage 2 reads the rest of the data from the running sums alone.
            offset.Ascii = ascii - table[index].Sums.Ascii;
            while (index < length)
            {
                index++;
                if (this.EarlyChoice(offset + table[index].Sums, index) is Mode early)
                {
                    return early;
                }
            }

            ModeCounts final = offset + table[length].Sums;
            int a = Ceiling(final.Ascii);
            int c = Ceiling(final.C40);
            int t = Ceiling(final.Text);
            int x = Ceiling(final.X12);
            if (final.Byte <= a && final.Byte <= c && final.Byte <= t && final.Byte <= x)
            {
                return Mode.Byte;
            }

            if (final.Ascii <= c && final.Ascii <= t && final.Ascii <= x && final.Ascii <= Ceiling(final.Byte))
            {
                return Mode.Ascii;
            }

            if (c <= t && c <= x)
            {
                return Mode.C40;
            }

            return t <= x ? Mode.Text : Mode.X12;
        }

        /// <summary>
        /// The early test of the look-ahead: a mode wins when it is a whole codeword cheaper than every other mode, each other count rounded up. ASCII and Byte
        /// compare their own count as it stands, and C40, Text and X12 their rounded count. Equal C40 and X12 counts go to X12 when an X12 terminator comes
        /// before a character X12 cannot carry.
        /// </summary>
        /// <param name="counts">The counts of the modes.</param>
        /// <param name="read">The position after the characters read.</param>
        /// <returns>The winning mode, or <see langword="null"/> when no mode wins yet.</returns>
        private readonly Mode? EarlyChoice(ModeCounts counts, int read)
        {
            const int unit = Unit;
            int ascii = counts.Ascii;
            int bytes = counts.Byte;
            int a = Ceiling(ascii);
            int c = Ceiling(counts.C40);
            int t = Ceiling(counts.Text);
            int x = Ceiling(counts.X12);
            int b = Ceiling(bytes);
            if (bytes + unit <= a && bytes + unit <= c && bytes + unit <= t && bytes + unit <= x)
            {
                return Mode.Byte;
            }

            if (ascii + unit <= c && ascii + unit <= t && ascii + unit <= x && ascii + unit <= b)
            {
                return Mode.Ascii;
            }

            if (t + unit <= a && t + unit <= c && t + unit <= x && t + unit <= b)
            {
                return Mode.Text;
            }

            if (c + unit <= a && c + unit <= t)
            {
                if (c < x)
                {
                    return Mode.C40;
                }

                if (counts.C40 == counts.X12)
                {
                    return this.table[read].Terminator < this.table[read].NonX12 ? Mode.X12 : Mode.C40;
                }
            }

            if (x + unit <= a && x + unit <= c && x + unit <= t && x + unit <= b)
            {
                return Mode.X12;
            }

            return null;
        }

        /// <summary>
        /// Returns the codewords left before the end of the smallest permitted version that holds more than the given number of codewords, or
        /// <see cref="int.MaxValue"/> when none does. Every caller compares the result with 1 or 2 only, so a full largest version reads as no end in reach.
        /// </summary>
        /// <param name="used">The codewords used.</param>
        /// <returns>The codewords left.</returns>
        private readonly int Remaining(int used)
        {
            for (CodeOneVersion v = CodeOneVersion.A; v <= CodeOneVersion.T48; v++)
            {
                int capacity = new VersionInfo(v).DataCodewords;
                if (IsPermitted(this.version, v) && capacity > used)
                {
                    return capacity - used;
                }
            }

            return int.MaxValue;
        }

        /// <summary>
        /// Writes the triplets gathered so far, then the unlatch to ASCII mode.
        /// </summary>
        /// <param name="values">The values, a multiple of three.</param>
        private void UnlatchAfter(ReadOnlySpan<int> values)
        {
            this.AddTriplets(values);
            this.Add(Unlatch);
            this.mode = Mode.Ascii;
        }

        /// <summary>
        /// Writes the triplets of mode values as codewords: (1600 v1 + 40 v2 + v3 + 1) in two bytes.
        /// </summary>
        /// <param name="values">The values, a multiple of three.</param>
        private void AddTriplets(ReadOnlySpan<int> values)
        {
            for (int k = 0; k + 2 < values.Length; k += 3)
            {
                int value = (values[k] * 1600) + (values[k + 1] * 40) + values[k + 2] + 1;
                this.Add(value >> 8);
                this.Add(value & 0xFF);
            }
        }

        /// <summary>
        /// Writes the two digits at the current position as one ASCII codeword.
        /// </summary>
        private void AddDigitPair()
        {
            this.Add(DigitPairBase + ((this.message[this.position] - '0') * 10) + (this.message[this.position + 1] - '0'));
            this.position += 2;
        }

        /// <summary>
        /// Writes one character in ASCII mode: its value plus one, after an Upper Shift above 127.
        /// </summary>
        /// <param name="value">The character.</param>
        private void AddAscii(int value)
        {
            if (value > 127)
            {
                this.Add(UpperShift);
                value -= 128;
            }

            this.Add(value + 1);
        }

        /// <summary>
        /// Adds Decimal mode bits, the most significant first, and writes each codeword as it completes.
        /// </summary>
        /// <param name="value">The bits.</param>
        /// <param name="count">The number of bits, at most 10.</param>
        private void AddBits(int value, int count)
        {
            this.pendingBits = (this.pendingBits << count) | value;
            this.pendingCount += count;

            // Up to seven pending bits and ten new bits can complete two codewords.
            while (this.pendingCount >= 8)
            {
                this.pendingCount -= 8;
                this.Add((this.pendingBits >> this.pendingCount) & 0xFF);
                this.pendingBits &= (1 << this.pendingCount) - 1;
            }
        }

        /// <summary>
        /// Writes one codeword, or marks the encodation as overflowed past the largest version.
        /// </summary>
        /// <param name="codeword">The codeword.</param>
        private void Add(int codeword)
        {
            if (this.written >= MaximumCodewords)
            {
                this.overflow = true;
                return;
            }

            this.codewords[this.written++] = codeword;
        }
    }
}
