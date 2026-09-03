// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="AustraliaPostSymbology"/>. Every expected bar string is in the bar value
/// notation of the Australia Post Customer Barcoding Technical Specifications, 0 for a full bar, 1 for an
/// ascender, 2 for a descender and 3 for a tracker, and is a barcode the specification prints in one of
/// its diagrams unless the test states another source.
/// </summary>
public class AustraliaPostSymbologyTests
{
    /// <summary>
    /// Diagram 6, the Standard Customer Barcode of sorting code 54516251; Diagram 7, Customer Barcode 2
    /// with an empty customer information field and Customer Barcode 3 with "ABC123"; and Diagram 10, the
    /// Standard Customer Barcode of sorting code 39549554 with its error correction bars.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="bars">The bar values from the left.</param>
    [Theory]
    [InlineData("1154516251", "1301011211120120021201303030220222213")]
    [InlineData("5954516251", "1312301211120120021201333333333333333310031000312313")]
    [InlineData("6254516251ABC123", "1320021211120120021201000001002300301302333333333333313133002021313")]
    [InlineData("1139549554", "1301011030121130121211331210131132213")]
    public void EncodesTheDiagramsOfTheSpecification(string text, string bars)
        => Assert.Equal(bars, Values(Encode(text, AustraliaPostEncodingTable.Character)));

    /// <summary>
    /// The N Encoding Table in the customer information field, and the C Encoding Table with small
    /// letters, the space and the number sign. The expected bars are from the reference implementation's
    /// raw encoding API.
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
        => Assert.Equal(bars, Values(Encode(text, table)));

    /// <summary>
    /// Diagram 10: the information symbols 4 20 49 37 49 38 23 give the parity symbols 54 17 53 58.
    /// </summary>
    [Fact]
    public void CalculatesTheParitySymbolsOfDiagramTen()
    {
        byte[] parity = new byte[4];
        AustraliaPostEncoder.Parity([4, 20, 49, 37, 49, 38, 23], parity);
        Assert.Equal([54, 17, 53, 58], parity);
    }

    /// <summary>
    /// The other 37-bar format control codes encode like the Standard Customer Barcode. The expected bars
    /// are from the reference implementation's raw encoding API.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="bars">The bar values from the left.</param>
    [Theory]
    [InlineData("4554516251", "1311121211120120021201320132012223013")]
    [InlineData("8754516251", "1322211211120120021201320300031333213")]
    [InlineData("9254516251", "1330021211120120021201330203101201213")]
    public void EncodesTheOtherStandardFormatControlCodes(string text, string bars)
        => Assert.Equal(bars, Values(Encode(text, AustraliaPostEncodingTable.Character)));

    /// <summary>
    /// Diagram 14: the text representation of the barcode of 1196184209 is "11 96184209 32 57 38 54",
    /// above the bars and outside the 2 mm quiet zone.
    /// </summary>
    [Fact]
    public void PrintsTheTextRepresentationOfDiagramFourteen()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new AustraliaPostSymbology().Encode("1196184209", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("11 96184209 32 57 38 54", placement.Text);
        Assert.Equal(BarcodeTextSide.AboveBars, placement.Side);
        Assert.Equal(-4F, placement.TextEdge);

        LinearBarcodeSymbol withCustomer = (LinearBarcodeSymbol)new AustraliaPostSymbology().Encode("6254516251ABC123", options);
        Assert.StartsWith("62 54516251 ABC123 ", Assert.Single(withCustomer.Text).Text);
    }

    /// <summary>
    /// The dimensions at the 0.5 mm module: bars of 50 run units of 0.01 mm, spaces of 58, a tracker of
    /// 1.3 mm, extenders of 1.85 mm, a full bar of 5 mm and a quiet zone of 6 mm at each end.
    /// </summary>
    [Fact]
    public void DrawsTheDimensionsOfTheSpecification()
    {
        LinearBarcodeSymbol symbol = Encode("1154516251", AustraliaPostEncodingTable.Character);

        Assert.Equal(1F / 50F, symbol.RunUnit);
        Assert.Equal(73, symbol.RunWidths.Length);
        for (int i = 0; i < symbol.RunWidths.Length; i++)
        {
            Assert.Equal((i & 1) == 0 ? 50 : 58, symbol.RunWidths[i]);
        }

        float extender = 1.85F / 0.5F;
        float tracker = 1.3F / 0.5F;
        string values = Values(symbol);
        for (int i = 0; i < values.Length; i++)
        {
            float expectedTop = values[i] is '0' or '1' ? 0F : extender;
            float expectedHeight = values[i] switch
            {
                '3' => tracker,
                '0' => extender + tracker + extender,
                _ => extender + tracker,
            };

            Assert.Equal(expectedTop, symbol.BarTops[i], 0.0001F);
            Assert.Equal(expectedHeight, symbol.BarHeights[i], 0.0001F);
        }

        Assert.Equal(12F, symbol.LeadingQuietZone);
        Assert.Equal(12F, symbol.TrailingQuietZone);
    }

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

    private static LinearBarcodeSymbol Encode(string text, AustraliaPostEncodingTable table)
        => (LinearBarcodeSymbol)new AustraliaPostSymbology(table).Encode(text, new BarcodeOptions());

    private static string Values(LinearBarcodeSymbol symbol)
    {
        float extender = 1.85F / 0.5F;
        float tracker = 1.3F / 0.5F;
        char[] values = new char[symbol.BarHeights.Length];
        for (int i = 0; i < values.Length; i++)
        {
            bool up = symbol.BarTops[i] == 0F;
            bool down = symbol.BarTops[i] + symbol.BarHeights[i] > extender + tracker + 0.001F;
            values[i] = up && down ? '0' : up ? '1' : down ? '2' : '3';
        }

        return new string(values);
    }
}
