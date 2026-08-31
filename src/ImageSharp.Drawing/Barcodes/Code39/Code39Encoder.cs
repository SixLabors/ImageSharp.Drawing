// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Shared encodation for the Code 39 symbology family, defined by ISO/IEC 16388. Section 4.1 c) gives
/// every symbol character nine elements, "5 bars and 4 spaces" of which "3 wide and 6 narrow", and
/// section 4.2 separates the characters within the symbol with an inter-character gap. Section 4.2 also
/// gives the structure: a quiet zone, the start character, the data and its optional check character,
/// the stop character and a quiet zone.
/// </summary>
internal static class Code39Encoder
{
    /// <summary>
    /// The character set, in check character value order, so the value of a character is its index. This
    /// is the order Table 1 of ISO/IEC 16388 lists the assignments in.
    /// </summary>
    public const string Characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

    /// <summary>
    /// The largest number of characters this library encodes in one symbol. Section 4.1 e) makes the
    /// data string length variable, so the symbology sets no maximum of its own.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The quiet zone in modules on each side. Section 4.4 d): "Minimum width of quiet zone: 10 X".
    /// </summary>
    public const int QuietZone = 10;

    /// <summary>
    /// The element count of a symbol character and the gap that follows it.
    /// </summary>
    private const int ElementsPerCharacter = 10;

    /// <summary>
    /// The pattern index of the start and stop character, which carries no data.
    /// </summary>
    private const int StartStop = 43;

    /// <summary>
    /// The width of a wide element in modules. Section 4.4 b) allows a wide to narrow ratio of "2,0 : 1
    /// to 3,0 : 1", and this is the widest of that range, so a symbol character and its gap take the
    /// sixteen modules at the top of the range section 4.1 h) gives.
    /// </summary>
    private const int WideElement = 3;

    /// <summary>
    /// The bar height as a fraction of the symbol width, quiet zones excluded, when the caller sets no
    /// height. Section 4.4 e) recommends a minimum of "5,0 mm or 15 % of symbol width excluding quiet
    /// zones, whichever is greater". The millimetre floor needs a print resolution the encoder does not
    /// have, so only the proportional rule applies here.
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <summary>
    /// Gets the element widths of every symbol character, one bit per element, most significant element
    /// first: a set bit is a wide element and a clear bit a narrow one. The first nine bits are the
    /// pattern Table 1 of ISO/IEC 16388 gives in bar and space order, and the tenth is the
    /// inter-character gap, which section 4.4 c) gives a minimum width of one narrow element. The last
    /// entry is the start and stop character.
    /// </summary>
    private static ReadOnlySpan<ushort> Patterns =>
    [
        0x068, 0x242, 0x0C2, 0x2C0, 0x062, 0x260, 0x0E0, 0x04A,
        0x248, 0x0C8, 0x212, 0x092, 0x290, 0x032, 0x230, 0x0B0,
        0x01A, 0x218, 0x098, 0x038, 0x206, 0x086, 0x284, 0x026,
        0x224, 0x0A4, 0x00E, 0x20C, 0x08C, 0x02C, 0x302, 0x182,
        0x380, 0x122, 0x320, 0x1A0, 0x10A, 0x308, 0x188, 0x150,
        0x144, 0x114, 0x054, 0x128,
    ];

    /// <summary>
    /// Gets the check character value of the given character, or a negative number when the character is
    /// outside the Code 39 character set.
    /// </summary>
    /// <param name="character">The character to value.</param>
    /// <returns>The value, or a negative number.</returns>
    public static int Value(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'A' and <= 'Z' => character - 'A' + 10,
        '-' => 36,
        '.' => 37,
        ' ' => 38,
        '$' => 39,
        '/' => 40,
        '+' => 41,
        '%' => 42,
        _ => -1,
    };

    /// <summary>
    /// Works out the check character over the given data, the sum of the character values modulo the size
    /// of the character set. Section 4.1 g) makes the check character optional and leaves its calculation
    /// to Annex A, which is not in the copy of the standard this was written against, so the calculation
    /// follows the BWIPP reference implementation.
    /// </summary>
    /// <param name="data">The data the check character covers.</param>
    /// <returns>The check character.</returns>
    public static char CheckCharacter(ReadOnlySpan<char> data)
    {
        int sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            sum += Value(data[i]);
        }

        return Characters[sum % Characters.Length];
    }

    /// <summary>
    /// Validates the given text against the Code 39 character set.
    /// </summary>
    /// <param name="text">The text to validate.</param>
    /// <exception cref="ArgumentException">The text carries a character outside the set.</exception>
    public static void Validate(ReadOnlySpan<char> text)
    {
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        for (int i = 0; i < text.Length; i++)
        {
            if (Value(text[i]) < 0)
            {
                throw new ArgumentException(
                    $"Code 39 carries only digits, capital letters, spaces and the symbols -.$/+%; got '{text[i]}'.",
                    nameof(text));
            }
        }
    }

    /// <summary>
    /// Encodes text into the alternating bar and space run widths the renderer draws, starting with a bar.
    /// The runs carry the start character, the data, an optional check character and the stop character,
    /// each followed by its inter-character gap. Section 4.2 separates the characters "within the symbol"
    /// with that gap, and section 4.3.3 puts the stop character at the right end, so no gap follows it
    /// and the runs end on a bar.
    /// </summary>
    /// <param name="text">The text to encode, already validated against the character set.</param>
    /// <param name="check">The check character to carry behind the data, or <see langword="null"/>.</param>
    /// <returns>The run widths in modules.</returns>
    public static int[] Encode(ReadOnlySpan<char> text, char? check)
    {
        int characters = text.Length + (check is null ? 2 : 3);
        int[] runs = new int[(characters * ElementsPerCharacter) - 1];
        int written = 0;

        AppendPattern(runs, ref written, StartStop);
        for (int i = 0; i < text.Length; i++)
        {
            AppendPattern(runs, ref written, Value(text[i]));
        }

        if (check is not null)
        {
            AppendPattern(runs, ref written, Value(check.Value));
        }

        AppendElements(runs, ref written, StartStop, ElementsPerCharacter - 1);
        return runs;
    }

    /// <summary>
    /// Builds the symbol from encoded run widths. Code 39 carries no guard bars, so every bar runs the
    /// full height, and the human readable interpretation sits below the symbol.
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

        float barHeight = EanUpcEncoder.ResolveBarHeight(options, widthInModules * NominalBarHeightFraction);
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
    /// Writes the ten element widths of one symbol character and the gap that follows it.
    /// </summary>
    /// <param name="runs">The buffer the widths are written to.</param>
    /// <param name="written">The write position, advanced by the element count.</param>
    /// <param name="index">The pattern index, which is the character value.</param>
    private static void AppendPattern(Span<int> runs, ref int written, int index)
        => AppendElements(runs, ref written, index, ElementsPerCharacter);

    /// <summary>
    /// Writes the leading element widths of one symbol character.
    /// </summary>
    /// <param name="runs">The buffer the widths are written to.</param>
    /// <param name="written">The write position, advanced by the element count.</param>
    /// <param name="index">The pattern index, which is the character value.</param>
    /// <param name="count">The number of elements to write.</param>
    private static void AppendElements(Span<int> runs, ref int written, int index, int count)
    {
        ushort pattern = Patterns[index];
        for (int element = ElementsPerCharacter - 1; element >= ElementsPerCharacter - count; element--)
        {
            runs[written++] = ((pattern >> element) & 1) == 1 ? WideElement : 1;
        }
    }
}
