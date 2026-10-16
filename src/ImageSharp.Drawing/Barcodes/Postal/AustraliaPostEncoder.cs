// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Encodation for the Australia Post 4-state customer barcode, from the Australia Post Customer Barcoding
/// Technical Specifications. A bar has a value of 0 for a full bar, 1 for an ascender, 2 for a descender
/// and 3 for a tracker. A symbol is the start bars, the format control code, the sorting code, the
/// customer information field with its filler bars, the Reed Solomon error correction bars and the stop
/// bars.
/// </summary>
internal static class AustraliaPostEncoder
{
    /// <summary>
    /// The characters the C Encoding Table encodes, in table order.
    /// </summary>
    public const string CharacterSet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyz #";

    /// <summary>
    /// The number of digits in the format control code and the sorting code together.
    /// </summary>
    public const int FixedDigits = 10;

    /// <summary>
    /// The number of error correction symbols, which the specification calls "the four parity check
    /// symbols of a (n,k) Reed Solomon code over Galois Field GF(64)".
    /// </summary>
    public const int ParitySymbols = 4;

    /// <summary>
    /// The number of bars from the start of the symbol to the customer information field: two start bars,
    /// four format control code bars and sixteen sorting code bars.
    /// </summary>
    private const int CustomerFieldStart = 22;

    /// <summary>
    /// The number of bars after the customer information field: twelve error correction bars and two
    /// stop bars.
    /// </summary>
    private const int TrailingBars = 14;

    /// <summary>
    /// The dimensions the specification gives as ranges, taken at the middle of each range: a bar width of
    /// 0.5 mm from "0.4" to "0.6", a tracker of 1.3 mm from "1.0" to "1.6", an ascender or descender bar of
    /// 3.15 mm from "2.6" to "3.7", so an extender of 1.85 mm, and a bar density of 23.5 bars per 25.4 mm
    /// from "22 to 25 bars per 25.4mm", so a pitch of 1.08 mm. A run unit of 0.01 mm is 1/50 module, a bar
    /// is 50 units and a space 58, inside the bar gap of "0.4" to "0.7". Diagram 14 gives a quiet zone of
    /// 6 mm at each end and 2 mm above and below, and the text representation "should appear above the
    /// barcode" and "must be outside of the barcode's minimum Quiet Zone".
    /// </summary>
    public static readonly FourStateMetrics Metrics = new(
        0.5,
        1F / 50F,
        50,
        58,
        (float)(1.85 / 0.5),
        (float)(1.3 / 0.5),
        (float)(1.85 / 0.5),
        (float)(6 / 0.5),
        BarcodeTextSide.AboveBars,
        (float)(2 / 0.5));

    /// <summary>
    /// The field of the parity symbols: GF(64) with the primitive polynomial 1 + x + x^6.
    /// </summary>
    private static readonly GaloisField Field = new(64, 67);

    /// <summary>
    /// Gets the N Encoding Table: the two bar values of every digit.
    /// </summary>
    private static ReadOnlySpan<byte> NumericTable => [0, 0, 0, 1, 0, 2, 1, 0, 1, 1, 1, 2, 2, 0, 2, 1, 2, 2, 3, 0];

    /// <summary>
    /// Gets the C Encoding Table: the three bar values of every character of <see cref="CharacterSet"/>.
    /// </summary>
    private static ReadOnlySpan<byte> CharacterTable =>
    [
        0, 0, 0, 0, 0, 1, 0, 0, 2, 0, 1, 0, 0, 1, 1, 0, 1, 2, 0, 2, 0, 0, 2, 1, 0, 2, 2, 1, 0, 0, 1, 0, 1, 1, 0, 2, 1, 1, 0,
        1, 1, 1, 1, 1, 2, 1, 2, 0, 1, 2, 1, 1, 2, 2, 2, 0, 0, 2, 0, 1, 2, 0, 2, 2, 1, 0, 2, 1, 1, 2, 1, 2, 2, 2, 0, 2, 2, 1,
        2, 2, 2, 3, 0, 0, 3, 0, 1, 3, 0, 2, 3, 1, 0, 3, 1, 1, 3, 1, 2, 3, 2, 0, 3, 2, 1, 3, 2, 2,
        0, 2, 3, 0, 3, 0, 0, 3, 1, 0, 3, 2, 0, 3, 3, 1, 0, 3, 1, 1, 3, 1, 2, 3, 1, 3, 0, 1, 3, 1, 1, 3, 2, 1, 3, 3, 2, 0, 3,
        2, 1, 3, 2, 2, 3, 2, 3, 0, 2, 3, 1, 2, 3, 2, 2, 3, 3, 3, 0, 3, 3, 1, 3, 3, 2, 3, 3, 3, 0, 3, 3, 1, 3, 3, 2, 3, 3, 3,
        0, 0, 3, 0, 1, 3,
    ];

    /// <summary>
    /// Returns the number of bars in the format a format control code selects, or 0 when the code is not
    /// one of the customer barcode formats.
    /// </summary>
    /// <param name="formatControlCode">The two digits of the format control code.</param>
    /// <returns>37 for the Standard Customer Barcode and the Null Customer Barcode, 52 for Customer Barcode 2, 67 for Customer Barcode 3.</returns>
    public static int BarCount(ReadOnlySpan<char> formatControlCode) => formatControlCode switch
    {
        "00" or "11" or "45" or "87" or "92" => 37,
        "59" => 52,
        "62" => 67,
        _ => 0,
    };

    /// <summary>
    /// Encodes the text into a symbol.
    /// </summary>
    /// <param name="text">The format control code, the sorting code and the customer information.</param>
    /// <param name="table">The encoding table of the customer information.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void Encode(string text, AustraliaPostEncodingTable table, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (text.Length < FixedDigits)
        {
            throw new ArgumentException(
                $"An Australia Post barcode starts with a 2-digit format control code and an 8-digit sorting code; got {text.Length} characters.",
                nameof(text));
        }

        ReadOnlySpan<char> formatControlCode = text.AsSpan(0, 2);
        int barCount = BarCount(formatControlCode);
        if (barCount == 0)
        {
            throw new ArgumentException($"The format control code is 00, 11, 45, 59, 62, 87 or 92; got {text[..2]}.", nameof(text));
        }

        ReadOnlySpan<char> sortingCode = text.AsSpan(2, 8);
        BarcodeSymbology.ValidateDigits(sortingCode);
        if (formatControlCode is "00" && sortingCode is not "00000000")
        {
            throw new ArgumentException($"The Null Customer Barcode is only valid with the sorting code 00000000; got {text.Substring(2, 8)}.", nameof(text));
        }

        ReadOnlySpan<char> customer = text.AsSpan(FixedDigits);
        int barsPerCharacter = table == AustraliaPostEncodingTable.Numeric ? 2 : 3;
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
        values[0] = 1;
        values[1] = 3;
        int bar = 2;
        for (int i = 0; i < FixedDigits; i++)
        {
            bar = AppendNumeric(text[i], values, bar);
        }

        if (table == AustraliaPostEncodingTable.Numeric)
        {
            BarcodeSymbology.ValidateDigits(customer);
            for (int i = 0; i < customer.Length; i++)
            {
                bar = AppendNumeric(customer[i], values, bar);
            }
        }
        else
        {
            // Walking code points reports a surrogate pair, or a lone surrogate, as the one character it is.
            SpanCodePointEnumerator codePoints = customer.EnumerateCodePoints();
            while (codePoints.MoveNext())
            {
                CodePoint current = codePoints.Current;
                int index = current.IsAscii ? CharacterSet.IndexOf((char)current.Value) : -1;
                if (index < 0)
                {
                    throw new ArgumentException($"The C Encoding Table has no character {current.ToDisplayString()}.", nameof(text));
                }

                CharacterTable.Slice(index * 3, 3).CopyTo(values[bar..]);
                bar += 3;
            }
        }

        int parityStart = barCount - TrailingBars;
        values[bar..parityStart].Fill(3);

        int dataSymbols = (parityStart - 2) / 3;
        InlineArray24<int> symbolStorage = default;
        Span<int> symbols = ((Span<int>)symbolStorage)[..dataSymbols];
        for (int i = 0; i < dataSymbols; i++)
        {
            symbols[i] = SymbolValue(values, 2 + (i * 3));
        }

        InlineArray4<int> parityStorage = default;
        Span<int> parity = parityStorage;
        Parity(symbols, parity);
        for (int i = 0; i < ParitySymbols; i++)
        {
            int at = parityStart + (i * 3);
            values[at] = (byte)((parity[i] >> 4) & 3);
            values[at + 1] = (byte)((parity[i] >> 2) & 3);
            values[at + 2] = (byte)(parity[i] & 3);
        }

        values[barCount - 2] = 1;
        values[barCount - 1] = 3;

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

        // The representation holds the ten digits, at most fifteen customer characters, the four parity
        // values of at most two digits and the spaces between them.
        InlineArray64<char> lineStorage = default;
        Span<char> line = lineStorage;
        int lineLength = options.Font is null ? 0 : TextRepresentation(text, parity, line);
        FourStateEncoder.BuildSymbol(states, Metrics, line[..lineLength], options, ref symbol);
    }

    /// <summary>
    /// Calculates the four parity symbols of the Reed Solomon code: the remainder of the data polynomial,
    /// with the first symbol as its highest term, multiplied by x^4 and divided by the generator
    /// polynomial "(x-α)(x-α²)(x-α³)(x-α⁴)". The field is GF(64) with the primitive polynomial 1 + x + x^6
    /// and the generating element α = 000010.
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
    /// Converts three bars to their decimal value by the Bar to Decimal Conversion Table, in which the
    /// bar values are the digits of a base 4 number.
    /// </summary>
    /// <param name="values">The bar values.</param>
    /// <param name="start">The index of the first of the three bars.</param>
    /// <returns>The decimal value.</returns>
    public static int SymbolValue(ReadOnlySpan<byte> values, int start)
        => (values[start] * 16) + (values[start + 1] * 4) + values[start + 2];

    /// <summary>
    /// Builds the text representation, which Diagram 14 shows as the format control code, the sorting
    /// code and the four error correction symbols as decimal values, separated by spaces. The customer
    /// information, when present, stands as given between the sorting code and the error correction
    /// symbols.
    /// </summary>
    /// <param name="text">The text as given.</param>
    /// <param name="parity">The parity symbols.</param>
    /// <param name="line">The buffer that receives the text representation.</param>
    /// <returns>The length of the text representation.</returns>
    private static int TextRepresentation(string text, ReadOnlySpan<int> parity, Span<char> line)
    {
        ValueStringBuilder builder = new(line);
        builder.Append(text.AsSpan(0, 2));
        builder.Append(' ');
        builder.Append(text.AsSpan(2, 8));
        if (text.Length > FixedDigits)
        {
            builder.Append(' ');
            builder.Append(text.AsSpan(FixedDigits));
        }

        for (int i = 0; i < parity.Length; i++)
        {
            builder.Append(' ');
            int value = parity[i];
            if (value >= 10)
            {
                builder.Append((char)('0' + (value / 10)));
            }

            builder.Append((char)('0' + (value % 10)));
        }

        return builder.Length;
    }

    /// <summary>
    /// Writes the two bar values of a digit by the N Encoding Table.
    /// </summary>
    /// <param name="digit">The digit.</param>
    /// <param name="values">The bar values.</param>
    /// <param name="bar">The index of the first of the two bars.</param>
    /// <returns>The index after the two bars.</returns>
    private static int AppendNumeric(char digit, Span<byte> values, int bar)
    {
        NumericTable.Slice((digit - '0') * 2, 2).CopyTo(values[bar..]);
        return bar + 2;
    }
}
