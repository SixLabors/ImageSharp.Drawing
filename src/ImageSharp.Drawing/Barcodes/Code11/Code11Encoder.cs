// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the Code 11 symbology, also called USD-8. Every character has five elements, three
/// bars and two spaces, of which one or two are wide, and a narrow inter-character gap follows it. A
/// symbol is a quiet zone, the start character, the data, one or two optional check characters, the
/// stop character and a quiet zone.
/// </summary>
internal static class Code11Encoder
{
    /// <summary>
    /// The characters the symbology encodes, in pattern order.
    /// </summary>
    public const string Characters = "0123456789-";

    /// <summary>
    /// The largest number of characters a symbol carries. The symbology sets no maximum.
    /// </summary>
    public const int MaximumLength = 500;

    /// <summary>
    /// The quiet zone in modules on each side: the 10X of Code 39 and Codabar, since no document gives
    /// one for Code 11.
    /// </summary>
    public const int QuietZone = 10;

    /// <summary>
    /// The number of data characters from which a symbol carries two check characters. Below it a symbol
    /// carries one.
    /// </summary>
    public const int TwoCheckCharactersFrom = 10;

    /// <summary>
    /// The number of elements in a symbol character, its inter-character gap excluded.
    /// </summary>
    private const int ElementsPerCharacter = 5;

    /// <summary>
    /// The pattern index of the start and stop character, which carries no data.
    /// </summary>
    private const int StartStop = 11;

    /// <summary>
    /// The width of a wide element in modules: the top of the wide to narrow ratio range of 2.0:1 to
    /// 3.0:1.
    /// </summary>
    private const int WideElement = 3;

    /// <summary>
    /// The bar height as a fraction of the symbol width, quiet zones excluded, when the caller sets no
    /// height: the 15 per cent of Code 39 and Codabar, since no document gives one for Code 11.
    /// </summary>
    private const float NominalBarHeightFraction = 0.15F;

    /// <summary>
    /// Gets the element widths of every character, one bit per element, most significant element first,
    /// in bar and space order: a set bit is a wide element and a clear bit a narrow one. The gap that
    /// follows the character is not in the pattern. The entries are in the order of
    /// <see cref="Characters"/>, and the last entry is the start and stop character.
    /// </summary>
    private static ReadOnlySpan<byte> Patterns =>
    [
        0x01, 0x11, 0x09, 0x18, 0x05, 0x14, 0x0C, 0x03, 0x12, 0x10, 0x04, 0x06,
    ];

    /// <summary>
    /// Returns the value of a character, which is its index in <see cref="Characters"/>.
    /// </summary>
    /// <param name="codePoint">The code point to value.</param>
    /// <returns>The value, or -1 when the character is not in the set.</returns>
    public static int Value(int codePoint) => codePoint switch
    {
        >= '0' and <= '9' => codePoint - '0',
        '-' => 10,
        _ => -1,
    };

    /// <summary>
    /// Calculates the C check character over the data. Each character is weighted by its position from
    /// the right, 1 for the rightmost and up to 10, after which the weights start again at 1. The check
    /// value is the weighted sum modulo 11.
    /// </summary>
    /// <param name="data">The data the check character covers.</param>
    /// <returns>The value of the check character.</returns>
    public static int CheckC(ReadOnlySpan<char> data)
    {
        int sum = 0;
        int weight = 1;
        for (int i = data.Length - 1; i >= 0; i--)
        {
            sum += weight * Value(data[i]);
            weight = weight == 10 ? 1 : weight + 1;
        }

        return sum % 11;
    }

    /// <summary>
    /// Calculates the K check character over the data and the C check character, which is the rightmost
    /// character it covers. Each character is weighted by its position from the right, 1 for the C check
    /// character and up to 9, after which the weights start again at 1. The check value is the weighted
    /// sum modulo 11.
    /// </summary>
    /// <param name="data">The data the check character covers.</param>
    /// <param name="checkC">The value of the C check character.</param>
    /// <returns>The value of the check character.</returns>
    public static int CheckK(ReadOnlySpan<char> data, int checkC)
    {
        int sum = checkC;
        int weight = 2;
        for (int i = data.Length - 1; i >= 0; i--)
        {
            sum += weight * Value(data[i]);
            weight = weight == 9 ? 1 : weight + 1;
        }

        return sum % 11;
    }

    /// <summary>
    /// Encodes data into the alternating bar and space run widths the renderer draws, starting with a
    /// bar. The runs carry the start character, the data, the check characters and the stop character,
    /// each followed by its inter-character gap except the stop character, so the runs end on a bar.
    /// </summary>
    /// <param name="data">The data to encode, already validated.</param>
    /// <param name="checks">The check characters to carry after the data, none, one or two.</param>
    /// <returns>The run widths in modules.</returns>
    public static int[] Encode(ReadOnlySpan<char> data, ReadOnlySpan<char> checks)
    {
        int characters = data.Length + checks.Length + 2;
        int[] runs = new int[(characters * (ElementsPerCharacter + 1)) - 1];
        int written = 0;

        Append(runs, ref written, StartStop, true);
        for (int i = 0; i < data.Length; i++)
        {
            Append(runs, ref written, Value(data[i]), true);
        }

        for (int i = 0; i < checks.Length; i++)
        {
            Append(runs, ref written, Value(checks[i]), true);
        }

        Append(runs, ref written, StartStop, false);
        return runs;
    }

    /// <summary>
    /// Builds the symbol from encoded run widths. Code 11 carries no guard bars, so every bar runs the
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

        float barHeight = EanUpcEncoder.ResolveBarHeight(options, BarcodeSymbology.PointXDimension, widthInModules * NominalBarHeightFraction);
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

        return new LinearBarcodeSymbol(runs, heights, tops, placements, QuietZone, QuietZone);
    }

    /// <summary>
    /// Writes the five element widths of one character and, when one follows, its inter-character gap.
    /// </summary>
    /// <param name="runs">The buffer the widths are written to.</param>
    /// <param name="written">The write position, advanced by the element count.</param>
    /// <param name="value">The character value, which is the pattern index.</param>
    /// <param name="gap">Whether a narrow inter-character gap follows the character.</param>
    private static void Append(Span<int> runs, ref int written, int value, bool gap)
    {
        byte pattern = Patterns[value];
        for (int element = ElementsPerCharacter - 1; element >= 0; element--)
        {
            runs[written++] = ((pattern >> element) & 1) == 1 ? WideElement : 1;
        }

        if (gap)
        {
            runs[written++] = 1;
        }
    }
}
