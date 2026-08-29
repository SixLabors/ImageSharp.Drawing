// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.Fonts.Unicode;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the Code 128 symbology family. Section 5.4 of the GS1 General Specifications
/// describes the symbology and names ISO/IEC 15417 as its full definition. Every symbol character is six
/// elements wide, three bars and three spaces of one to four modules, and carries eleven modules. The
/// stop character is the one exception: seven elements over thirteen modules.
/// <para>
/// Code set selection follows Table 5-25 of the GS1 General Specifications: start in the set that reaches
/// the furthest before it must change, switch to code set C for any run of four or more digits, and shift
/// for a single character rather than latch when the following character returns to the current set.
/// </para>
/// </summary>
internal static class Code128Encoder
{
    /// <summary>
    /// The quiet zone in modules on each side. Section 5.4.4.2 requires ten times the X-dimension.
    /// </summary>
    public const int QuietZone = 10;

    /// <summary>
    /// The symbol character value of the Function 1 character. Section 5.4.2 places it directly after the
    /// start character to mark a GS1-128 symbol, and section 5.4.3.4.2 uses it as a field separator.
    /// </summary>
    public const int FunctionOne = 102;

    private const int StartA = 103;
    private const int StartB = 104;
    private const int StartC = 105;
    private const int Stop = 106;
    private const int LatchA = 101;
    private const int LatchB = 100;
    private const int LatchC = 99;
    private const int Shift = 98;

    /// <summary>
    /// The longest input that encodes without renting. Section 5.4.1 caps a GS1-128 symbol at 48 data
    /// characters, so every conforming symbol stays on the stack.
    /// </summary>
    private const int StackLimit = 64;

    /// <summary>
    /// The nominal bar height in modules. Section 5.4.7.1 sets the symbol height at 32 millimetres for the
    /// 0.495 millimetre X-dimension of the general distribution environment, which is 64.6 modules.
    /// </summary>
    private const float NominalBarHeight = 64.6F;

    /// <summary>
    /// Gets the element widths of every symbol character, packed four bits per element, most significant
    /// element first. Table 5-25 of the GS1 General Specifications lists them: six elements each, three
    /// bars and three spaces of one to four modules, except the stop character which takes seven. The
    /// leading nibble records the element count and is never read as an element.
    /// </summary>
    private static ReadOnlySpan<uint> Patterns =>
    [
        0x6212222, 0x6222122, 0x6222221, 0x6121223, 0x6121322, 0x6131222, 0x6122213, 0x6122312,
        0x6132212, 0x6221213, 0x6221312, 0x6231212, 0x6112232, 0x6122132, 0x6122231, 0x6113222,
        0x6123122, 0x6123221, 0x6223211, 0x6221132, 0x6221231, 0x6213212, 0x6223112, 0x6312131,
        0x6311222, 0x6321122, 0x6321221, 0x6312212, 0x6322112, 0x6322211, 0x6212123, 0x6212321,
        0x6232121, 0x6111323, 0x6131123, 0x6131321, 0x6112313, 0x6132113, 0x6132311, 0x6211313,
        0x6231113, 0x6231311, 0x6112133, 0x6112331, 0x6132131, 0x6113123, 0x6113321, 0x6133121,
        0x6313121, 0x6211331, 0x6231131, 0x6213113, 0x6213311, 0x6213131, 0x6311123, 0x6311321,
        0x6331121, 0x6312113, 0x6312311, 0x6332111, 0x6314111, 0x6221411, 0x6431111, 0x6111224,
        0x6111422, 0x6121124, 0x6121421, 0x6141122, 0x6141221, 0x6112214, 0x6112412, 0x6122114,
        0x6122411, 0x6142112, 0x6142211, 0x6241211, 0x6221114, 0x6413111, 0x6241112, 0x6134111,
        0x6111242, 0x6121142, 0x6121241, 0x6114212, 0x6124112, 0x6124211, 0x6411212, 0x6421112,
        0x6421211, 0x6212141, 0x6214121, 0x6412121, 0x6111143, 0x6111341, 0x6131141, 0x6114113,
        0x6114311, 0x6411113, 0x6411311, 0x6113141, 0x6114131, 0x6311141, 0x6411131, 0x6211412,
        0x6211214, 0x6211232, 0x72331112,
    ];

    /// <summary>
    /// Encodes text into the alternating bar and space run widths the renderer draws, starting with a bar.
    /// The runs carry the start character, the code set switches, the check character of section 5.4.3.6
    /// and the stop character, whose seven elements end the list on a bar.
    /// </summary>
    /// <param name="text">The text to encode. Every character must be ASCII 0 to 127.</param>
    /// <param name="leadingFunctionOne">Whether a Function 1 character follows the start character, which
    /// is what marks a GS1-128 symbol.</param>
    /// <param name="symbologyName">The symbology name used in error messages.</param>
    /// <returns>The run widths in modules.</returns>
    /// <exception cref="ArgumentException">The text carries a character the symbology cannot encode.</exception>
    public static int[] Encode(string text, bool leadingFunctionOne, string symbologyName)
    {
        // Section 5.4.3.3 gives the three code sets ASCII 0 to 127 between them, so anything above that is
        // rejected. Walking code points rather than UTF-16 units reports a surrogate pair as the one
        // character it is.
        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            if (current.Value > 127)
            {
                throw new ArgumentException(
                    $"{symbologyName} encodes ASCII 0 to 127; U+{current.Value:X4} is outside that range.",
                    nameof(text));
            }
        }

        // A shift costs one extra symbol character, so two per input character bounds the run, and the
        // start, the optional Function 1, the check and the stop add four more.
        int capacity = (text.Length * 2) + 4;
        int lookahead = text.Length + 1;
        byte[]? rented = null;
        Span<int> values = capacity <= StackLimit * 2 ? stackalloc int[StackLimit * 2] : new int[capacity];
        Span<byte> distances = lookahead <= StackLimit
            ? stackalloc byte[StackLimit * 2]
            : (rented = ArrayPool<byte>.Shared.Rent(lookahead * 2)).AsSpan(0, lookahead * 2);

        try
        {
            Span<byte> nextOnlyInA = distances[..lookahead];
            Span<byte> nextOnlyInB = distances.Slice(lookahead, lookahead);
            int count = Encode(text, leadingFunctionOne, values, nextOnlyInA, nextOnlyInB);
            return ToRuns(values[..count]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Builds the symbol from encoded run widths. Every symbology in the family shares this step, so only
    /// the encodation differs between them. Code 128 carries no guard bars, so every bar runs the full
    /// height, and section 5.4.7.3 prints the human readable interpretation below the symbol.
    /// </summary>
    /// <param name="runs">The alternating bar and space run widths in modules.</param>
    /// <param name="text">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <returns>The encoded symbol.</returns>
    public static LinearBarcodeSymbol BuildSymbol(int[] runs, string text, BarcodeOptions options)
    {
        int widthInModules = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            widthInModules += runs[i];
        }

        float barHeight = EanUpcEncoder.ResolveBarHeight(options, NominalBarHeight);
        int barCount = (runs.Length + 1) / 2;
        float[] heights = new float[barCount];
        float[] tops = new float[barCount];
        for (int i = 0; i < barCount; i++)
        {
            heights[i] = barHeight;
        }

        BarcodeTextPlacement[] placements = [];
        if (options.Font is not null && text.Length > 0)
        {
            placements = [new BarcodeTextPlacement(text, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight)];
        }

        return new LinearBarcodeSymbol(runs, heights, tops, placements, QuietZone, QuietZone);
    }

    /// <summary>
    /// Encodes text into symbol character values.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="leadingFunctionOne">Whether a Function 1 character follows the start character.</param>
    /// <param name="values">The buffer the symbol character values are written to.</param>
    /// <param name="nextOnlyInA">Scratch for the distance to the next character only code set A carries.</param>
    /// <param name="nextOnlyInB">Scratch for the distance to the next character only code set B carries.</param>
    /// <returns>The number of symbol character values written.</returns>
    private static int Encode(
        string text,
        bool leadingFunctionOne,
        Span<int> values,
        Span<byte> nextOnlyInA,
        Span<byte> nextOnlyInB)
    {
        // The distance to the next character that only one of code sets A and B can carry. Whichever comes
        // first decides which set to start in and which to latch back to. The walk runs backwards, so the
        // distances have to be held; everything else the encoder needs is one step ahead of the write.
        // Both saturate at the buffer length, which is further than any comparison can reach.
        nextOnlyInA[text.Length] = byte.MaxValue;
        nextOnlyInB[text.Length] = byte.MaxValue;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            nextOnlyInA[i] = OnlyInSetA(text[i]) ? (byte)0 : SaturatingIncrement(nextOnlyInA[i + 1]);
            nextOnlyInB[i] = OnlyInSetB(text[i]) ? (byte)0 : SaturatingIncrement(nextOnlyInB[i + 1]);
        }

        int written = 0;
        Code128CodeSet set = ChooseStartSet(text, nextOnlyInA, nextOnlyInB, values, ref written);
        if (leadingFunctionOne)
        {
            values[written++] = FunctionOne;
        }

        int position = 0;
        while (position < text.Length)
        {
            int digits = DigitRun(text, position);
            if (set != Code128CodeSet.C && digits >= 4)
            {
                if ((digits & 1) == 0)
                {
                    values[written++] = LatchC;
                    set = Code128CodeSet.C;
                    continue;
                }

                // An odd run starts one character late, so the current set takes the first digit and code
                // set C takes the even remainder.
                values[written++] = ValueIn(set, text[position++]);
                if (DigitRun(text, position) >= 4)
                {
                    values[written++] = LatchC;
                    set = Code128CodeSet.C;
                    continue;
                }
            }

            char character = text[position];
            if (set == Code128CodeSet.B && OnlyInSetA(character))
            {
                if (position < text.Length - 1 && nextOnlyInB[position + 1] < nextOnlyInA[position + 1])
                {
                    values[written++] = Shift;
                    values[written++] = ValueIn(Code128CodeSet.A, character);
                    position++;
                    continue;
                }

                values[written++] = LatchA;
                set = Code128CodeSet.A;
                continue;
            }

            if (set == Code128CodeSet.A && OnlyInSetB(character))
            {
                if (position < text.Length - 1 && nextOnlyInA[position + 1] < nextOnlyInB[position + 1])
                {
                    values[written++] = Shift;
                    values[written++] = ValueIn(Code128CodeSet.B, character);
                    position++;
                    continue;
                }

                values[written++] = LatchB;
                set = Code128CodeSet.B;
                continue;
            }

            if (set == Code128CodeSet.C && digits < 2)
            {
                bool intoA = nextOnlyInA[position] < nextOnlyInB[position];
                values[written++] = intoA ? LatchA : LatchB;
                set = intoA ? Code128CodeSet.A : Code128CodeSet.B;
                continue;
            }

            if (set == Code128CodeSet.C)
            {
                values[written++] = ((text[position] - '0') * 10) + (text[position + 1] - '0');
                position += 2;
                continue;
            }

            values[written++] = ValueIn(set, character);
            position++;
        }

        values[written] = ComputeCheckCharacter(values[..written]);
        written++;
        values[written++] = Stop;
        return written;
    }

    /// <summary>
    /// Counts the digits from a position, in pairs. Code set C takes two digits per symbol character, so
    /// only an even run encodes whole, and a Function 1 counts as a pair because code set C carries it.
    /// </summary>
    /// <param name="text">The text being encoded.</param>
    /// <param name="position">The position to count from.</param>
    /// <returns>The digit run length.</returns>
    private static int DigitRun(string text, int position)
    {
        int run = 0;
        for (int i = position; i < text.Length && text[i] is >= '0' and <= '9'; i++)
        {
            run++;
        }

        return run;
    }

    /// <summary>
    /// Increments a saturating distance, so a run longer than a byte still compares as further away.
    /// </summary>
    /// <param name="value">The distance to increment.</param>
    /// <returns>The incremented distance, held at the maximum.</returns>
    private static byte SaturatingIncrement(byte value) => value == byte.MaxValue ? value : (byte)(value + 1);

    /// <summary>
    /// Converts symbol character values into the run lengths the renderer draws, starting with a bar.
    /// </summary>
    /// <param name="values">The symbol character values, including the start, check and stop characters.</param>
    /// <returns>The alternating bar and space run widths in modules.</returns>
    private static int[] ToRuns(ReadOnlySpan<int> values)
    {
        // Section 5.4.1 gives every symbol character six elements. Only the stop character differs, with
        // the four bars and three spaces that close the symbol.
        ReadOnlySpan<uint> patterns = Patterns;
        int[] runs = new int[((values.Length - 1) * 6) + 7];
        int index = 0;
        for (int i = 0; i < values.Length; i++)
        {
            uint pattern = patterns[values[i]];
            int elements = values[i] == Stop ? 7 : 6;
            for (int shift = (elements - 1) * 4; shift >= 0; shift -= 4)
            {
                runs[index++] = (int)((pattern >> shift) & 0xF);
            }
        }

        return runs;
    }

    /// <summary>
    /// Chooses the code set the symbol starts in and writes its start character.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="nextOnlyInA">The distance to the next character only code set A carries.</param>
    /// <param name="nextOnlyInB">The distance to the next character only code set B carries.</param>
    /// <param name="values">The buffer the start character is written to.</param>
    /// <param name="written">The write position, advanced by one.</param>
    /// <returns>The code set the symbol starts in.</returns>
    private static Code128CodeSet ChooseStartSet(
        string text,
        ReadOnlySpan<byte> nextOnlyInA,
        ReadOnlySpan<byte> nextOnlyInB,
        Span<int> values,
        ref int written)
    {
        if (text.Length == 0)
        {
            values[written++] = StartB;
            return Code128CodeSet.B;
        }

        int digits = DigitRun(text, 0);
        if ((text.Length == 2 && digits == 2) || digits >= 4)
        {
            values[written++] = StartC;
            return Code128CodeSet.C;
        }

        if (nextOnlyInA[0] < nextOnlyInB[0])
        {
            values[written++] = StartA;
            return Code128CodeSet.A;
        }

        values[written++] = StartB;
        return Code128CodeSet.B;
    }

    /// <summary>
    /// Computes the symbol check character. Section 5.4.3.6 weights the start character by one and each
    /// following character by its position, sums the products and takes the remainder modulo 103.
    /// </summary>
    /// <param name="values">The symbol character values, starting with the start character.</param>
    /// <returns>The symbol check character value.</returns>
    private static int ComputeCheckCharacter(ReadOnlySpan<int> values)
    {
        long sum = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            sum += (long)values[i] * i;
        }

        return (int)(sum % 103);
    }

    /// <summary>
    /// Determines whether only code set A carries the character. Section 5.4.3.3.1 gives code set A the
    /// control characters, which code set B replaces with the lower case letters.
    /// </summary>
    /// <param name="character">The character to classify.</param>
    /// <returns><see langword="true"/> when only code set A carries it.</returns>
    private static bool OnlyInSetA(char character) => character < 32;

    /// <summary>
    /// Determines whether only code set B carries the character. Section 5.4.3.3.2 gives code set B the
    /// lower case letters and the characters above them, which code set A replaces with control codes.
    /// </summary>
    /// <param name="character">The character to classify.</param>
    /// <returns><see langword="true"/> when only code set B carries it.</returns>
    private static bool OnlyInSetB(char character) => character >= 96;

    /// <summary>
    /// Returns the symbol character value of a character in the given code set. Code set A runs from the
    /// space at value zero through to the control characters, and code set B from the space through to the
    /// delete character.
    /// </summary>
    /// <param name="set">The code set to encode in.</param>
    /// <param name="character">The character to encode.</param>
    /// <returns>The symbol character value.</returns>
    private static int ValueIn(Code128CodeSet set, char character)
    {
        if (set == Code128CodeSet.A && character < 32)
        {
            return character + 64;
        }

        return character - 32;
    }
}
