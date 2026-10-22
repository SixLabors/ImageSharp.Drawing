// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1DataBarExpandedSymbology"/>. The tests check the dimensions of the GS1 General Specifications, the human readable
/// interpretation and the input that the symbology rejects.
/// </summary>
public class Gs1DataBarExpandedSymbologyTests
{
    /// <summary>
    /// Section 5.5.2.3.1 of the GS1 General Specifications gives Figure 5-47, which encodes (01)90614141000015(3202)000150, as 38.35 millimetres wide at an X
    /// dimension of 0.254 millimetre, 151 modules, and 34X high. No quiet zone is required.
    /// </summary>
    [Fact]
    public void Gs1DataBarExpanded_MatchesTheDimensionsOfFigure547()
        => Encode("(01)90614141000015(3202)000150", static (ref symbol) =>
        {
            Assert.Equal(151F, symbol.WidthInModules);
            Assert.Equal(34F, symbol.HeightInModules);
            Assert.Equal(0F, symbol.LeadingQuietZone);
            Assert.Equal(0F, symbol.TrailingQuietZone);
        });

    /// <summary>
    /// Section 5.5.2.3.1: a symbol has 4 to 22 symbol characters, "a minimum of 102X wide".
    /// </summary>
    [Fact]
    public void Gs1DataBarExpanded_IsAtLeastFourSymbolCharactersWide()
        => Encode("(90)1", static (ref symbol) => Assert.Equal(102F, symbol.WidthInModules));

    /// <summary>
    /// The human readable interpretation is the element strings as given, below the bars.
    /// </summary>
    [Fact]
    public void Gs1DataBarExpanded_TextPrintsTheElementStrings()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        SymbolEncoder.Encode(new Gs1DataBarExpandedSymbology(), "(01)90614141000015(3202)000150", options, static (ref symbol) =>
        {
            BarcodeTextPlacement placement = Assert.Single(symbol.Text.ToArray());
            Assert.Equal("(01)90614141000015(3202)000150", symbol.TextOf(placement).ToString());
            Assert.Equal(symbol.HeightInModules + BarcodeTextPlacement.Clearance, placement.TextEdge);
        });
    }

    /// <summary>
    /// Section 4.14 rule 3b of the GS1 General Specifications: when Application Identifier (8200) appears, its URL does not appear in the human readable
    /// interpretation, in either form of the symbol.
    /// </summary>
    [Fact]
    public void Gs1DataBarExpanded_LeavesTheUrlOfApplicationIdentifier8200OutOfTheText()
    {
        const string text = "(01)09520123456788(8200)http://www.abc.net";
        const string expected = "(01)09520123456788";
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        Assert.Equal(expected, Assert.Single(SymbolEncoder.Read(new Gs1DataBarExpandedSymbology(), text, options, SymbolEncoder.Texts)));
        Assert.Equal(expected, Assert.Single(SymbolEncoder.Read(new Gs1DataBarExpandedStackedSymbology(), text, options, SymbolEncoder.Texts)));
    }

    /// <summary>
    /// The compressed methods carry the GTIN of Application Identifier (01) without its check digit, so both forms of the symbol reject a wrong check digit.
    /// They also reject a character outside the general purpose field, data longer than 21 data characters and text without the element string syntax.
    /// Section 5.5.2.3.3 of the GS1 General Specifications: "when the data includes AI (01), it SHALL be the first element string encoded", so both forms
    /// reject (01) after (10).
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("(01)90614141000016(3202)000150")]
    [InlineData("(01)09521234543214")]
    [InlineData("(21)AB#")]
    [InlineData("(21)ABCDEFGHIJKLMNOPQRST(22)ABCDEFGHIJKLMNOPQRST(240)ABCDEFGHIJ")]
    [InlineData("01095212345432")]
    [InlineData("(10)ABC123(01)09521234543213")]
    public void Gs1DataBarExpanded_RejectsInvalidInput(string text)
    {
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Gs1DataBarExpandedSymbology(), text, new BarcodeOptions()));
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Gs1DataBarExpandedStackedSymbology(), text, new BarcodeOptions()));
    }

    /// <summary>
    /// Table 5-56 of the GS1 General Specifications gives GS1 DataBar Expanded a minimum symbol height of 11.22 millimetres at the target X dimension of 0.330
    /// millimetre, and GS1 DataBar Expanded Stacked 23.43 millimetres for two rows, two rows of 11.22 mm and the 3X separator. "The symbol height SHALL NOT be
    /// truncated below the minimum." The bar height sets every row: a bar height of 11.21 mm draws at 11.22 mm.
    /// </summary>
    [Fact]
    public void Gs1DataBarExpanded_DrawsABarHeightUnderTheMinimumAtTheMinimum()
    {
        Assert.Equal(Height(new Gs1DataBarExpandedSymbology(), 11.22), Height(new Gs1DataBarExpandedSymbology(), 11.21));
        Assert.Equal(Height(new Gs1DataBarExpandedStackedSymbology(), 11.22), Height(new Gs1DataBarExpandedStackedSymbology(), 11.21));

        static float Height(BarcodeSymbology symbology, double barHeight)
            => SymbolEncoder.Read(
                symbology,
                "(01)90614141000015(3202)000150",
                new BarcodeOptions { Dpi = SymbolEncoder.MicrometreDpi, XDimension = 0.330, BarHeight = barHeight },
                static (ref symbol) => symbol.HeightInModules);
    }

    /// <summary>
    /// Encodes the text with the default options and checks the symbol.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="check">The check to run on the encoded symbol.</param>
    private static void Encode(string text, SymbolEncoder.Check check)
        => SymbolEncoder.Encode(new Gs1DataBarExpandedSymbology(), text, new BarcodeOptions(), check);
}
