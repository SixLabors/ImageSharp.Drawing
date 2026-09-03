// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the PosiCode symbology, which AIM ITS/02-001 defines. Every character is three bars of
/// one module and three spaces, and the data lies in the positions of the bars, whose centres are 2G to
/// 9G apart in version A and 3G to 10G apart in version B, where G is the bar width. A symbol is a
/// quiet zone, a start character, the data, a check character and a stop character. The data characters
/// come from three character sets with latches and shifts between them, characters above 127 are
/// announced by the function 4 character, and the check character maps a cyclic redundancy check value
/// to six bar positions.
/// </summary>
internal static class PosiCodeEncoder
{
    /// <summary>
    /// The largest number of characters a symbol carries. The symbology sets no maximum.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The number of characters a caller stack allocates to build symbol data in. Longer data grows into
    /// a pooled array.
    /// </summary>
    public const int StackBufferLength = 64;

    /// <summary>
    /// The number of data codewords in the standard character sets.
    /// </summary>
    private const int Codewords = 46;

    /// <summary>
    /// The number of data codewords in the limited character set.
    /// </summary>
    private const int LimitedCodewords = 38;

    /// <summary>
    /// The number of runs in one data character: three bars and three spaces.
    /// </summary>
    private const int RunsPerCharacter = 6;

    /// <summary>
    /// The number of runs in the check character: six bars and six spaces.
    /// </summary>
    private const int CheckRuns = 12;

    /// <summary>
    /// The sum of the six check distances before the version B offset.
    /// </summary>
    private const int CheckDistanceSum = 20;

    /// <summary>
    /// The polynomial the check divides by, applied bit by bit to the six bit codewords.
    /// </summary>
    private const int CheckPolynomial = 7682;

    /// <summary>
    /// The sentinel codewords of the control functions, which no character value can equal.
    /// </summary>
    private const int Latch0 = -1;
    private const int Latch1 = -2;
    private const int Shift0 = -4;
    private const int Shift1 = -5;
    private const int Shift2 = -6;
    private const int Function1 = -7;
    private const int Function2 = -8;
    private const int Function3 = -9;
    private const int Function4 = -10;

    /// <summary>
    /// Gets the run widths of the codewords of version A, six characters each in bar and space order,
    /// followed by the start and stop characters. A character above 9 is a width of its value minus 48,
    /// so &lt; is 12 and ; is 11.
    /// </summary>
    private static readonly string[] PatternsA =
    [
        "141112", "131212", "121312", "111412", "131113", "121213", "111313", "121114", "111214", "111115",
        "181111", "171211", "161311", "151411", "141511", "131611", "121711", "111811", "171112", "161212",
        "151312", "141412", "131512", "121612", "111712", "161113", "151213", "141313", "131413", "121513",
        "111613", "151114", "141214", "131314", "121414", "111514", "141115", "131215", "121315", "111415",
        "131116", "121216", "111316", "121117", "111217", "111118", "1<111112", "111111111;1",
    ];

    /// <summary>
    /// Gets the run widths of the codewords of version B, in the layout of <see cref="PatternsA"/>.
    /// </summary>
    private static readonly string[] PatternsB =
    [
        "151213", "141313", "131413", "121513", "141214", "131314", "121414", "131215", "121315", "121216",
        "191212", "181312", "171412", "161512", "151612", "141712", "131812", "121912", "181213", "171313",
        "161413", "151513", "141613", "131713", "121813", "171214", "161314", "151414", "141514", "131614",
        "121714", "161215", "151315", "141415", "131515", "121615", "151216", "141316", "131416", "121516",
        "141217", "131317", "121417", "131218", "121318", "121219", "1<121312", "121212121<1",
    ];

    /// <summary>
    /// Gets the run widths of the codewords of Limited version A, in the layout of
    /// <see cref="PatternsA"/> with the 38 limited codewords.
    /// </summary>
    private static readonly string[] PatternsLimitedA =
    [
        "111411", "111312", "111213", "111114", "121311", "121212", "121113", "141111", "131211", "131112",
        "171111", "161211", "151311", "141411", "131511", "121611", "111711", "161112", "151212", "141312",
        "131412", "121512", "111612", "151113", "141213", "131313", "121413", "111513", "141114", "131214",
        "121314", "111414", "131115", "121215", "111315", "121116", "111216", "111117", "151111", "1",
    ];

    /// <summary>
    /// Gets the run widths of the codewords of Limited version B, in the layout of
    /// <see cref="PatternsLimitedA"/>.
    /// </summary>
    private static readonly string[] PatternsLimitedB =
    [
        "121512", "121413", "121314", "121215", "131412", "131313", "131214", "151212", "141312", "141213",
        "181212", "171312", "161412", "151512", "141612", "131712", "121812", "171213", "161313", "151413",
        "141513", "131613", "121713", "161214", "151314", "141414", "131514", "121614", "151215", "141315",
        "131415", "121515", "141216", "131316", "121416", "131217", "121317", "121218", "141212", "1",
    ];

    /// <summary>
    /// Gets the characters of set 0, indexed by codeword: the digits, the capital letters, the symbols
    /// space - . $ / + %, then the latch to set 1 and the shifts to sets 1 and 2.
    /// </summary>
    private static ReadOnlySpan<int> Set0 =>
    [
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
        'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
        '-', '.', ' ', '$', '/', '+', '%', Latch1, Shift1, Shift2,
    ];

    /// <summary>
    /// Gets the characters of set 1, indexed by codeword: the symbols ^ ; &lt; = &gt; ? @ [ \ ], the
    /// small letters, the symbols _ ` DEL { | } ~, then the latch to set 0 and the shifts to sets 0 and 2.
    /// </summary>
    private static ReadOnlySpan<int> Set1 =>
    [
        '^', ';', '<', '=', '>', '?', '@', '[', '\\', ']',
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
        'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
        '_', '`', 127, '{', '|', '}', '~', Latch0, Shift0, Shift2,
    ];

    /// <summary>
    /// Gets the characters of set 2, indexed by codeword: the apostrophe, the control characters 27 to
    /// 31, the symbols ! " # &amp;, the control characters 1 to 26, the symbols ( ) NUL * , :, then the
    /// four function characters.
    /// </summary>
    private static ReadOnlySpan<int> Set2 =>
    [
        '\'', 27, 28, 29, 30, 31, '!', '"', '#', '&',
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13,
        14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26,
        '(', ')', 0, '*', ',', ':', Function1, Function2, Function3, Function4,
    ];

    /// <summary>
    /// Gets the table that maps a check value to six bar distances. Row r holds the number of ways the
    /// remaining distances can be arranged when distance r grows by one more, and the mapping walks the
    /// rows until the running sum reaches the check value.
    /// </summary>
    private static ReadOnlySpan<int> CheckWeights =>
    [
        495, 330, 210, 126, 70, 35, 15, 5,
        165, 120, 84, 56, 35, 20, 10, 4,
        45, 36, 28, 21, 15, 10, 6, 3,
        9, 8, 7, 6, 5, 4, 3, 2,
        1, 1, 1, 1, 1, 1, 1, 1,
    ];

    /// <summary>
    /// Returns the quiet zone in modules on each side of a version: "12G on either side", and 13G for
    /// Limited PosiCode B.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>The quiet zone in modules.</returns>
    public static int QuietZone(PosiCodeVersion version) => version == PosiCodeVersion.LimitedB ? 13 : 12;

    /// <summary>
    /// Returns whether a version carries only the limited character set.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>Whether the version is limited.</returns>
    public static bool IsLimited(PosiCodeVersion version) => version is PosiCodeVersion.LimitedA or PosiCodeVersion.LimitedB;

    /// <summary>
    /// Returns the codeword of a character in the limited character set, which is the first 38 entries
    /// of set 0.
    /// </summary>
    /// <param name="codePoint">The code point to value.</param>
    /// <returns>The codeword, or -1 when the character is not in the set.</returns>
    public static int LimitedValue(int codePoint)
    {
        int index = Set0[..LimitedCodewords].IndexOf(codePoint);
        return index;
    }

    /// <summary>
    /// Converts character values to the codewords the symbol carries. Characters at or above 128 drop
    /// their high bit and are announced by the function 4 character: once for a run shorter than five,
    /// or shorter than three at the end of the data, and twice, which switches the mode, for a longer
    /// run. Every character then encodes in the current set, through a shift to set 2, through a latch
    /// when the next character is not in the current set either, or through a shift to the other set.
    /// </summary>
    /// <param name="values">The character values, 0 to 255.</param>
    /// <param name="codewords">The buffer that receives the codewords, at least four per value.</param>
    /// <returns>The number of codewords written.</returns>
    public static int ToCodewords(ReadOnlySpan<int> values, Span<int> codewords)
    {
        // The extended switching looks ahead at how many characters of the other mode follow, so the
        // announced sequence is built first.
        Span<int> message = values.Length * 3 <= 256 ? stackalloc int[values.Length * 3] : new int[values.Length * 3];
        int length = 0;
        bool extended = false;
        for (int i = 0; i < values.Length; i++)
        {
            int value = values[i];
            bool high = value >= 128;
            if (extended != high)
            {
                int run = 0;
                while (i + run < values.Length && (values[i + run] >= 128) == high)
                {
                    run++;
                }

                int limit = i + run == values.Length ? 3 : 5;
                if (run < limit)
                {
                    message[length++] = Function4;
                }
                else
                {
                    extended = !extended;
                    message[length++] = Function4;
                    message[length++] = Function4;
                }
            }

            message[length++] = value & 127;
        }

        int written = 0;
        ReadOnlySpan<int> current = Set0;
        bool inSet0 = true;
        int position = 0;
        while (position < length)
        {
            int first = message[position];
            int next = position + 1 < length ? message[position + 1] : -99;
            int index = current.IndexOf(first);
            if (index >= 0)
            {
                codewords[written++] = index;
                position++;
                continue;
            }

            int set2 = Set2.IndexOf(first);
            if (set2 >= 0)
            {
                codewords[written++] = current.IndexOf(Shift2);
                codewords[written++] = set2;
                position++;
                continue;
            }

            ReadOnlySpan<int> other = inSet0 ? Set1 : Set0;
            if (current.IndexOf(next) < 0)
            {
                codewords[written++] = current.IndexOf(inSet0 ? Latch1 : Latch0);
                current = other;
                inSet0 = !inSet0;
                continue;
            }

            codewords[written++] = current.IndexOf(inSet0 ? Shift1 : Shift0);
            codewords[written++] = other.IndexOf(first);
            position++;
        }

        return written;
    }

    /// <summary>
    /// Encodes codewords into the alternating bar and space run widths the renderer draws, starting with
    /// the first bar of the start character and ending on the last bar of the stop character. The runs
    /// carry the start character, the codewords, the check character and the stop character.
    /// </summary>
    /// <param name="codewords">The codewords the symbol carries.</param>
    /// <param name="version">The version.</param>
    /// <returns>The run widths in modules.</returns>
    public static int[] Encode(ReadOnlySpan<int> codewords, PosiCodeVersion version)
    {
        ReadOnlySpan<string> patterns = version switch
        {
            PosiCodeVersion.A => PatternsA,
            PosiCodeVersion.B => PatternsB,
            PosiCodeVersion.LimitedA => PatternsLimitedA,
            _ => PatternsLimitedB,
        };

        string start = patterns[^2];
        string stop = patterns[^1];
        int[] runs = new int[start.Length + (codewords.Length * RunsPerCharacter) + CheckRuns + stop.Length];
        int written = 0;
        for (int i = 0; i < start.Length; i++)
        {
            runs[written++] = start[i] - '0';
        }

        for (int i = 0; i < codewords.Length; i++)
        {
            string pattern = patterns[codewords[i]];
            for (int j = 0; j < pattern.Length; j++)
            {
                runs[written++] = pattern[j] - '0';
            }
        }

        AppendCheck(codewords, version, runs.AsSpan(written, CheckRuns));
        written += CheckRuns;
        for (int i = 0; i < stop.Length; i++)
        {
            runs[written++] = stop[i] - '0';
        }

        return runs;
    }

    /// <summary>
    /// Builds the symbol from encoded run widths. PosiCode carries no guard bars, so every bar runs the
    /// full height, and the human readable interpretation sits below the symbol.
    /// </summary>
    /// <param name="runs">The alternating bar and space run widths in modules.</param>
    /// <param name="text">The human readable interpretation.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="version">The version, which sets the quiet zone.</param>
    /// <returns>The encoded symbol.</returns>
    public static LinearBarcodeSymbol BuildSymbol(int[] runs, string text, BarcodeOptions options, PosiCodeVersion version)
    {
        int widthInModules = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            widthInModules += runs[i];
        }

        // The 15 per cent of Code 39 and Codabar; no document gives a height for PosiCode.
        float barHeight = EanUpcEncoder.ResolveBarHeight(options, BarcodeSymbology.PointXDimension, widthInModules * 0.15F);
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
            placements = [new BarcodeTextPlacement(text, 0F, widthInModules, BarcodeTextSide.BelowBars, barHeight + BarcodeTextPlacement.Clearance)];
        }

        int quietZone = QuietZone(version);
        return new LinearBarcodeSymbol(runs, heights, tops, placements, quietZone, quietZone);
    }

    /// <summary>
    /// Writes the twelve runs of the check character. The codewords are divided bit by bit by the check
    /// polynomial, and the ten bit remainder, offset by 45 in the standard versions and moved past a
    /// reserved range in the limited versions, is mapped to six distances that add to twenty. Version B
    /// adds one to every distance. The character is six bars of one module whose spaces are the distances
    /// less one, written from the last distance to the first.
    /// </summary>
    /// <param name="codewords">The codewords the check covers.</param>
    /// <param name="version">The version.</param>
    /// <param name="runs">The twelve runs of the check character.</param>
    private static void AppendCheck(ReadOnlySpan<int> codewords, PosiCodeVersion version, Span<int> runs)
    {
        int value = 0;
        for (int i = 0; i < codewords.Length; i++)
        {
            int codeword = codewords[i];
            for (int bit = 0; bit < 6; bit++)
            {
                if (((codeword ^ value) & 1) != 0)
                {
                    value ^= CheckPolynomial;
                }

                value >>= 1;
                codeword >>= 1;
            }
        }

        if (IsLimited(version))
        {
            value &= 1023;
            if (value > 824 && value < 853)
            {
                value += 292;
            }
        }
        else
        {
            value = (value & 1023) + 45;
        }

        Span<int> distances = stackalloc int[6];
        distances.Fill(2);
        int row = 0;
        int column = 0;
        int width = 0;
        int sum = 0;
        while (sum != value)
        {
            int total = sum + CheckWeights[(row * 8) + column];
            if (total == value)
            {
                width++;
                distances[row] = width + 2;
                sum = total;
            }

            if (total > value)
            {
                distances[row] = width + 2;
                row++;
                width = 0;
            }

            if (total < value)
            {
                column++;
                width++;
                sum = total;
            }
        }

        distances[5] = CheckDistanceSum - distances[0] - distances[1] - distances[2] - distances[3] - distances[4];
        int offset = version is PosiCodeVersion.B or PosiCodeVersion.LimitedB ? 1 : 0;
        for (int i = 0; i < 6; i++)
        {
            runs[i * 2] = 1;
            runs[(i * 2) + 1] = distances[5 - i] + offset - 1;
        }
    }
}
