// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="AustraliaPostSymbology"/>. Every expected bar string is in the bar value notation of the Australia Post Customer Barcoding
/// Technical Specifications, 0 for a full bar, 1 for an ascender, 2 for a descender and 3 for a tracker, and is a barcode the specification prints in one of
/// its diagrams unless the test states another source.
/// </summary>
public class AustraliaPostSymbologyTests
{
    /// <summary>
    /// Diagram 6, the Standard Customer Barcode of sorting code 54516251; Diagram 7, Customer Barcode 2 with an empty customer information field and Customer
    /// Barcode 3 with "ABC123"; and Diagram 10, the Standard Customer Barcode of sorting code 39549554 with its error correction bars.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="bars">The bar values from the left.</param>
    [Theory]
    [InlineData("1154516251", "1301011211120120021201303030220222213")]
    [InlineData("5954516251", "1312301211120120021201333333333333333310031000312313")]
    [InlineData("6254516251ABC123", "1320021211120120021201000001002300301302333333333333313133002021313")]
    [InlineData("1139549554", "1301011030121130121211331210131132213")]
    public void EncodesTheDiagramsOfTheSpecification(string text, string bars)
        => Assert.Equal(bars, Encode(text, AustraliaPostEncodingTable.Character));

    /// <summary>
    /// The N Encoding Table in the customer information field, and the C Encoding Table with small letters, the space and the number sign. The expected bars
    /// are from the reference implementation's raw encoding API.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="table">The encoding table of the customer information.</param>
    /// <param name="bars">The bar values from the left.</param>
    [Theory]
    [InlineData("595451625112345678", AustraliaPostEncodingTable.Numeric, "1312301211120120021201010210111220212203310011000313")]
    [InlineData("6254516251123456789012345", AustraliaPostEncodingTable.Numeric, "1320021211120120021201010210111220212230000102101112313021022012113")]
    [InlineData("5954516251ABC12", AustraliaPostEncodingTable.Character, "1312301211120120021201000001002300301330233311111013")]
    [InlineData("6254516251ABC123 #xy", AustraliaPostEncodingTable.Character, "1320021211120120021201000001002300301302003013331332322122322001113")]
    public void EncodesBothCustomerInformationTables(string text, AustraliaPostEncodingTable table, string bars)
        => Assert.Equal(bars, Encode(text, table));

    /// <summary>
    /// Diagram 10: the information symbols 4 20 49 37 49 38 23 give the parity symbols 54 17 53 58.
    /// </summary>
    [Fact]
    public void CalculatesTheParitySymbolsOfDiagramTen()
    {
        int[] parity = new int[4];

        AustraliaPostEncoder.Parity([4, 20, 49, 37, 49, 38, 23], parity);

        Assert.Equal([54, 17, 53, 58], parity);
    }

    /// <summary>
    /// The specification lists the customer format control codes 00, 11, 59 and 62. Codes 45 and 92 "must not be used by customers unless authorised by
    /// Australia Post" and have no bar count, and 87 is not defined.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("4554516251")]
    [InlineData("8754516251")]
    [InlineData("9254516251")]
    public void RejectsFormatControlCodesWithoutACustomerFormat(string text)
        => Assert.Throws<ArgumentException>(() => Encode(text, AustraliaPostEncodingTable.Character));

    /// <summary>
    /// The text representation "should appear above the barcode in 8 point type or less".
    /// </summary>
    [Fact]
    public void LimitsTheTextToEightPoints()
        => SymbolEncoder.Encode(
            new AustraliaPostSymbology(),
            "1196184209",
            new BarcodeOptions { Font = BarcodeFonts.OcrB.CreateFont(12F) },
            static (ref symbol) => Assert.Equal(8F, symbol.TextMaximumSize));

    /// <summary>
    /// Diagram 14: the text representation of the barcode of 1196184209 is "11 96184209 32 57 38 54", above the bars and outside the 2 mm quiet zone.
    /// </summary>
    [Fact]
    public void PrintsTheTextRepresentationOfDiagramFourteen()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        SymbolEncoder.Encode(new AustraliaPostSymbology(), "1196184209", options, static (ref symbol) =>
        {
            BarcodeTextPlacement placement = Assert.Single(symbol.Text.ToArray());
            Assert.Equal("11 96184209 32 57 38 54", symbol.TextOf(placement).ToString());
            Assert.Equal(BarcodeTextSide.AboveBars, placement.Side);
            Assert.True(-placement.TextEdge * symbol.XDimension >= 2 - 1e-4);
        });

        string[] withCustomer = SymbolEncoder.Read(new AustraliaPostSymbology(), "6254516251ABC123", options, SymbolEncoder.Texts);

        Assert.StartsWith("62 54516251 ABC123 ", Assert.Single(withCustomer));
    }

    /// <summary>
    /// The bar type table of the dimensions section: T 1.0 to 1.6 mm, A and D 2.6 to 3.7 mm, H 4.2 to 5.8 mm, a bar gap of 0.4 to 0.7 mm and a bar width of
    /// 0.4 to 0.6 mm. Bar density: "a bar density of 22 to 25 bars per 25.4mm", and a 37 bar barcode 37.0 to 42.2 mm long. Quiet zones: "the smallest
    /// allowable Quiet Zone on the left and right of a barcode is 6 mm" and "the smallest allowable Quiet Zone above and below the barcode is 2mm".
    /// </summary>
    [Fact]
    public void DrawsTheDimensionsOfTheSpecification()
        => SymbolEncoder.Encode(new AustraliaPostSymbology(AustraliaPostEncodingTable.Character), "1154516251", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(73, symbol.Runs.Length);
            double unit = symbol.RunUnit * symbol.XDimension;
            int leadToLead = 0;
            for (int i = 0; i < symbol.Runs.Length - 1; i++)
            {
                Assert.InRange(symbol.Runs[i] * unit, 0.4, (i & 1) == 0 ? 0.6 : 0.7);
                leadToLead += symbol.Runs[i];
            }

            Assert.InRange(symbol.Runs[^1] * unit, 0.4, 0.6);
            Assert.InRange(36 * 25.4 / (leadToLead * unit), 22, 25);
            Assert.InRange((leadToLead + symbol.Runs[^1]) * unit, 37.0, 42.2);

            string values = Values(ref symbol);
            for (int i = 0; i < values.Length; i++)
            {
                (double minimum, double maximum) = values[i] switch
                {
                    '3' => (1.0, 1.6),
                    '0' => (4.2, 5.8),
                    _ => (2.6, 3.7),
                };

                Assert.InRange(symbol.BarHeights[i] * symbol.XDimension, minimum, maximum);
            }

            Assert.True(symbol.LeadingQuietZone * symbol.XDimension >= 6 - 1e-4);
            Assert.True(symbol.TrailingQuietZone * symbol.XDimension >= 6 - 1e-4);
            Assert.True(symbol.TopQuietZone * symbol.XDimension >= 2 - 1e-4);
            Assert.True(symbol.BottomQuietZone * symbol.XDimension >= 2 - 1e-4);
        });

    /// <summary>
    /// The encoder rejects text that does not fit a customer format. This includes an empty text, a short sorting code, an undefined format control code
    /// and a letter in the sorting code. It also includes customer information that is too long for its format or is not in its encoding table.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="table">The encoding table of the customer information.</param>
    [Theory]
    [InlineData("", AustraliaPostEncodingTable.Character)]
    [InlineData("115451625", AustraliaPostEncodingTable.Character)]
    [InlineData("1254516251", AustraliaPostEncodingTable.Character)]
    [InlineData("11545A6251", AustraliaPostEncodingTable.Character)]
    [InlineData("1154516251A", AustraliaPostEncodingTable.Character)]
    [InlineData("5954516251ABCDEF", AustraliaPostEncodingTable.Character)]
    [InlineData("5954516251A-C", AustraliaPostEncodingTable.Character)]
    [InlineData("5954516251ABC", AustraliaPostEncodingTable.Numeric)]
    [InlineData("5954516251123456789", AustraliaPostEncodingTable.Numeric)]
    [InlineData("6254516251ABCDEFGHIJK", AustraliaPostEncodingTable.Character)]
    [InlineData("62545162511234567890123456", AustraliaPostEncodingTable.Numeric)]
    public void RejectsMalformedInput(string text, AustraliaPostEncodingTable table)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text, table));

    /// <summary>
    /// A character outside the C Encoding Table is reported as the one character it is, also when it takes a surrogate pair.
    /// </summary>
    [Fact]
    public void ReportsACharacterOutsideTheTableByItsCodePoint()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => Encode("5954516251A😀", AustraliaPostEncodingTable.Character));

        Assert.Contains("U+1F600", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="table">The table that encodes the customer information field.</param>
    /// <returns>The bar values of the symbol.</returns>
    private static string Encode(string text, AustraliaPostEncodingTable table)
        => SymbolEncoder.Read(new AustraliaPostSymbology(table), text, new BarcodeOptions(), Values);

    /// <summary>
    /// Reads the bar values of a symbol from its bar states, with the values of the bar type table: 0 for a full bar H, 1 for an ascender A, 2 for a descender
    /// D and 3 for a tracker T.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The bar values.</returns>
    private static string Values(ref BarcodeSymbol symbol)
    {
        char[] values = SymbolEncoder.FourStates(ref symbol).ToCharArray();
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = values[i] switch
            {
                'F' => '0',
                'A' => '1',
                'D' => '2',
                _ => '3',
            };
        }

        return new string(values);
    }
}
