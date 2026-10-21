// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the Australia Post 4-state customer barcode, from the Australia Post Customer Barcoding Technical Specifications. A bar has a value of 0 for
/// a full bar, 1 for an ascender, 2 for a descender and 3 for a tracker. A symbol is the start bars, the format control code, the sorting code, the customer
/// information field with its filler bars, the Reed Solomon error correction bars and the stop bars.
/// </summary>
internal static class AustraliaPostEncoder
{
    /// <summary>
    /// The largest size of the text representation in points: it "should appear above the barcode in 8 point type or less".
    /// </summary>
    private const float MaximumTextSize = 8F;

    /// <summary>
    /// The number of digits in the format control code and the sorting code together.
    /// </summary>
    private const int FixedDigits = 10;

    /// <summary>
    /// The number of error correction symbols, which the specification calls "the four parity check symbols of a (n,k) Reed Solomon code over Galois Field
    /// GF(64)".
    /// </summary>
    private const int ParitySymbols = 4;

    /// <summary>
    /// The number of bars from the start of the symbol to the customer information field: two start bars, four format control code bars and sixteen sorting
    /// code bars.
    /// </summary>
    private const int CustomerFieldStart = 22;

    /// <summary>
    /// The number of bars after the customer information field: twelve error correction bars and two stop bars.
    /// </summary>
    private const int TrailingBars = 14;

    /// <summary>
    /// The number of bars of one Reed Solomon symbol and of one character of the C Encoding Table: three bars of base 4 values.
    /// </summary>
    private const int BarsPerSymbol = 3;

    /// <summary>
    /// The number of bars of one digit of the N Encoding Table.
    /// </summary>
    private const int BarsPerDigit = 2;

    /// <summary>
    /// The value of the filler bars that fill the rest of the customer information field: "filler bars (value 3 bars)".
    /// </summary>
    private const byte FillerBar = 3;

    /// <summary>
    /// The dimensions that the specification gives as ranges, at the middle of each range:
    /// <list type="bullet">
    /// <item><description>A bar width of 0.5 mm, from "0.4" to "0.6".</description></item>
    /// <item><description>A tracker of 1.3 mm, from "1.0" to "1.6".</description></item>
    /// <item><description>An ascender or descender bar of 3.15 mm, from "2.6" to "3.7". The extender is thus 1.85 mm.</description></item>
    /// <item><description>A bar density of 23.5 bars per 25.4 mm, from "22 to 25 bars per 25.4mm". The pitch is thus 1.08 mm.</description></item>
    /// </list>
    /// The space is the rest of the pitch, 0.58 mm, inside the bar gap of "0.4" to "0.7". Diagram 14 gives a quiet zone of
    /// 6 mm at each end and 2 mm above and below. The text representation "should appear above the barcode" and "must be outside of the barcode's minimum
    /// Quiet Zone". The table on page 12 gives the bar width of "0.4" to "0.6" mm, and the code clamps the drawn bar width to that range. The code clamps the
    /// bars per inch to the 22 to 25 of the bar density on page 13. The code clamps the full bar to the "4.2" to "5.8" mm of bar type H in the same table. At
    /// each end of that range, the tracker and the ascender and descender bars stay inside their own ranges.
    /// </summary>
    private static readonly FourStateMetrics Metrics = new(
        0.5,
        0.58 / 0.5,
        (float)(1.85 / 0.5),
        (float)(1.3 / 0.5),
        (float)(1.85 / 0.5),
        (float)(6 / 0.5),
        (float)(2 / 0.5),
        BarcodeTextSide.AboveBars,
        (float)(2 / 0.5),
        BarcodeTextAlignment.Centered)
    {
        MinimumXDimension = 0.4,
        MaximumXDimension = 0.6,
        MinimumBarsPerInch = 22,
        MaximumBarsPerInch = 25,
        MinimumFullBar = (float)(4.2 / 0.5),
        MaximumFullBar = (float)(5.8 / 0.5),
    };

    /// <summary>
    /// The field of the parity symbols: GF(64) with the primitive polynomial 1 + x + x^6.
    /// </summary>
    private static readonly GaloisField Field = new(64, 67);

    /// <summary>
    /// Gets the N Encoding Table: the two bar values of every digit, as one base 4 number, the first bar in the high digit.
    /// </summary>
    private static ReadOnlySpan<byte> NumericTable => [0, 1, 2, 4, 5, 6, 8, 9, 10, 12];

    /// <summary>
    /// Gets the C Encoding Table: the three bar values of every character, in the order of <see cref="CharacterIndex"/>, as one base 4 number, the first bar
    /// in the high digit.
    /// </summary>
    private static ReadOnlySpan<byte> CharacterTable =>
    [
        0, 1, 2, 4, 5, 6, 8, 9, 10, 16, 17, 18, 20, 21, 22, 24,
        25, 26, 32, 33, 34, 36, 37, 38, 40, 41, 42, 48, 49, 50, 52, 53,
        54, 56, 57, 58, 11, 12, 13, 14, 15, 19, 23, 27, 28, 29, 30, 31,
        35, 39, 43, 44, 45, 46, 47, 51, 55, 59, 60, 61, 62, 63, 3, 7,
    ];

    /// <summary>
    /// Gets the two start bars, which are also the two stop bars. The specification: "These bars always have the bar values of `1' and `3', respectively".
    /// </summary>
    private static ReadOnlySpan<byte> StartStopBars => [1, 3];

    /// <summary>
    /// Encodes the text into a symbol.
    /// </summary>
    /// <param name="text">The format control code, the sorting code and the customer information.</param>
    /// <param name="table">The encoding table of the customer information.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void Encode(ReadOnlySpan<char> text, AustraliaPostEncodingTable table, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (text.Length < FixedDigits)
        {
            throw new ArgumentException(
                $"An Australia Post barcode starts with a 2-digit format control code and an 8-digit sorting code; got {text.Length} characters.",
                nameof(text));
        }

        ReadOnlySpan<char> formatControlCode = text.Slice(0, 2);
        int barCount = BarCount(formatControlCode);
        if (barCount == 0)
        {
            throw new ArgumentException($"The format control code is 00, 11, 59 or 62; got {formatControlCode}.", nameof(text));
        }

        ReadOnlySpan<char> sortingCode = text.Slice(2, 8);
        BarcodeSymbology.ValidateDigits(sortingCode);
        if (formatControlCode is "00" && sortingCode is not "00000000")
        {
            throw new ArgumentException($"The Null Customer Barcode is only valid with the sorting code 00000000; got {sortingCode}.", nameof(text));
        }

        ReadOnlySpan<char> customer = text.Slice(FixedDigits);
        int barsPerCharacter = table == AustraliaPostEncodingTable.Numeric ? BarsPerDigit : BarsPerSymbol;
        int customerBars = barCount - CustomerFieldStart - TrailingBars;
        if (customer.Length * barsPerCharacter > customerBars)
        {
            throw new ArgumentException(
                $"The customer information field of format control code {text[..2]} holds {customerBars / barsPerCharacter} characters; got {customer.Length}.",
                nameof(text));
        }

        // The longest barcode, format control code 62, has 67 bars.
        InlineArray128<byte> valueStorage = default;
        Span<byte> values = ((Span<byte>)valueStorage)[..barCount];
        StartStopBars.CopyTo(values);
        int bar = StartStopBars.Length;
        foreach (char digit in text.Slice(0, FixedDigits))
        {
            bar = AppendBars(NumericTable[digit - '0'], BarsPerDigit, values, bar);
        }

        if (table == AustraliaPostEncodingTable.Numeric)
        {
            BarcodeSymbology.ValidateDigits(customer);
            foreach (char digit in customer)
            {
                bar = AppendBars(NumericTable[digit - '0'], BarsPerDigit, values, bar);
            }
        }
        else
        {
            // Walking code points reports a surrogate pair, or a lone surrogate, as the one character it is.
            SpanCodePointEnumerator codePoints = customer.EnumerateCodePoints();
            while (codePoints.MoveNext())
            {
                CodePoint current = codePoints.Current;
                int index = CharacterIndex(current.Value);
                if (index < 0)
                {
                    throw new ArgumentException($"The C Encoding Table has no character {current.ToDisplayString()}.", nameof(text));
                }

                bar = AppendBars(CharacterTable[index], BarsPerSymbol, values, bar);
            }
        }

        int parityStart = barCount - TrailingBars;
        values[bar..parityStart].Fill(FillerBar);

        // The information symbols are the triples of bars after the start bars, read as base 4 numbers, whatever field each bar belongs to.
        int dataSymbols = (parityStart - StartStopBars.Length) / BarsPerSymbol;
        InlineArray24<int> symbolStorage = default;
        Span<int> symbols = ((Span<int>)symbolStorage)[..dataSymbols];
        for (int i = 0; i < dataSymbols; i++)
        {
            symbols[i] = SymbolValue(values, StartStopBars.Length + (i * BarsPerSymbol));
        }

        InlineArray4<int> parityStorage = default;
        Span<int> parity = parityStorage;
        Parity(symbols, parity);
        bar = parityStart;
        foreach (int value in parity)
        {
            bar = AppendBars(value, BarsPerSymbol, values, bar);
        }

        StartStopBars.CopyTo(values[bar..]);

        InlineArray128<FourState> stateStorage = default;
        Span<FourState> states = ((Span<FourState>)stateStorage)[..barCount];
        for (int i = 0; i < barCount; i++)
        {
            states[i] = values[i] switch
            {
                0 => FourState.Full,
                1 => FourState.Ascender,
                2 => FourState.Descender,
                _ => FourState.Tracker,
            };
        }

        // The representation holds the ten digits, at most fifteen customer characters, the four parity values of at most two digits and the spaces between
        // them.
        InlineArray64<char> lineStorage = default;
        Span<char> line = lineStorage;
        int lineLength = options.Font is null ? 0 : TextRepresentation(text, parity, line);
        FourStateEncoder.BuildSymbol(states, Metrics, line[..lineLength], options, ref symbol);

        // The text representation "should appear above the barcode in 8 point type or less".
        symbol.SetTextSize(0F, MaximumTextSize);
    }

    /// <summary>
    /// Calculates the four parity symbols of the Reed Solomon code: the remainder of the data polynomial, with the first symbol as its highest term, multiplied
    /// by x^4 and divided by the generator polynomial "(x-α)(x-α²)(x-α³)(x-α⁴)". The field is GF(64) with the primitive polynomial 1 + x + x^6 and the
    /// generating element α = 000010.
    /// </summary>
    /// <param name="data">The data symbols, each a value of 0 to 63, the first symbol first.</param>
    /// <param name="parity">The buffer that receives the parity symbols C3, C2, C1 and C0.</param>
    public static void Parity(ReadOnlySpan<int> data, Span<int> parity)
    {
        // The roots start at the first power of α.
        InlineArray4<int> generatorStorage = default;
        Span<int> generator = generatorStorage;
        Field.Generator(1, generator);
        Field.Remainder(data, 1, data.Length, generator, parity);
    }

    /// <summary>
    /// Returns the number of bars in the format a format control code selects, or 0 when the code is not one of the customer barcode formats.
    /// </summary>
    /// <param name="formatControlCode">The two digits of the format control code.</param>
    /// <returns>37 for the Standard Customer Barcode and the Null Customer Barcode, 52 for Customer Barcode 2, 67 for Customer Barcode 3.</returns>
    private static int BarCount(ReadOnlySpan<char> formatControlCode) => formatControlCode switch
    {
        "00" or "11" => 37,
        "59" => 52,
        "62" => 67,
        _ => 0,
    };

    /// <summary>
    /// Converts three bars to their decimal value by the Bar to Decimal Conversion Table, in which the bar values are the digits of a base 4 number.
    /// </summary>
    /// <param name="values">The bar values.</param>
    /// <param name="start">The index of the first of the three bars.</param>
    /// <returns>The decimal value.</returns>
    private static int SymbolValue(ReadOnlySpan<byte> values, int start)
        => (values[start] * 16) + (values[start + 1] * 4) + values[start + 2];

    /// <summary>
    /// Builds the text representation, which Diagram 14 shows as the format control code, the sorting code and the four error correction symbols as decimal
    /// values, separated by spaces. The customer information, when present, stands as given between the sorting code and the error correction symbols.
    /// </summary>
    /// <param name="text">The text as given.</param>
    /// <param name="parity">The parity symbols.</param>
    /// <param name="line">The buffer that receives the text representation.</param>
    /// <returns>The length of the text representation.</returns>
    private static int TextRepresentation(ReadOnlySpan<char> text, ReadOnlySpan<int> parity, Span<char> line)
    {
        ValueStringBuilder builder = new(line);
        builder.Append(text.Slice(0, 2));
        builder.Append(' ');
        builder.Append(text.Slice(2, 8));
        if (text.Length > FixedDigits)
        {
            builder.Append(' ');
            builder.Append(text.Slice(FixedDigits));
        }

        foreach (int value in parity)
        {
            builder.Append(' ');
            if (value >= 10)
            {
                builder.Append((char)('0' + (value / 10)));
            }

            builder.Append((char)('0' + (value % 10)));
        }

        return builder.Length;
    }

    /// <summary>
    /// Writes the bar values of a base 4 number, the high digit first. Each base 4 digit is one bar.
    /// </summary>
    /// <param name="value">The number: a table entry or a Reed Solomon symbol.</param>
    /// <param name="count">The number of bars.</param>
    /// <param name="values">The bar values.</param>
    /// <param name="bar">The index of the first bar.</param>
    /// <returns>The index after the bars.</returns>
    private static int AppendBars(int value, int count, Span<byte> values, int bar)
    {
        for (int shift = 2 * (count - 1); shift >= 0; shift -= 2)
        {
            values[bar++] = (byte)((value >> shift) & 3);
        }

        return bar;
    }

    /// <summary>
    /// Returns the row of a character in the C Encoding Table, which lists the capital letters, the digits, the small letters, the space and the number sign.
    /// </summary>
    /// <param name="codePoint">The code point of the character.</param>
    /// <returns>The row, from 0 to 63, or -1 when the table has no such character.</returns>
    private static int CharacterIndex(int codePoint) => codePoint switch
    {
        >= 'A' and <= 'Z' => codePoint - 'A',
        >= '0' and <= '9' => codePoint - '0' + 26,
        >= 'a' and <= 'z' => codePoint - 'a' + 36,
        ' ' => 62,
        '#' => 63,
        _ => -1,
    };
}
