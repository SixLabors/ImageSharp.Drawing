// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1DataBarExpandedSymbology"/>. Each expected value is the modules of the symbol, 1 for dark, taken from an independent
/// reference implementation through its raw encoding API and confirmed by a second one. The inputs cover every encodation method of the compressed data field,
/// the numeric, alphanumeric and ISO/IEC 646 modes of the general purpose field, and a single digit left at the end of the field.
/// </summary>
public class Gs1DataBarExpandedSymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="Gs1DataBarExpanded_MatchesReferenceModules"/>.
    /// </summary>
    public static TheoryData<string, string> Gs1DataBarExpanded_MatchesReferenceModulesData { get; } = new()
    {
        {
            "(01)09521234543213(3103)000123",
            "01001110000011001010111111110000101110010000101100011010000100000110101111110000111000010101100011110011110001011100100011111100001010010110" +
            "001110000100001000110001101011111111001110011010011100001101"
        },
        {
            "(01)90614141000015(3202)000150",
            "01011000110011000010111111110000101001000100001111011100111000101000101111000000111001110101111110110101000001000001100011111100001010000001" +
            "00011010010"
        },
        {
            "(01)98898765432106(3103)012233",
            "01011000100011100010111111110000101000100111000011011011111011000100101111000000111011110100110001110111001000110011100011111100001011111111" +
            "01011011010"
        },
        {
            "(01)98898765432106(3103)099999",
            "01000110100001100110111111110000101110110001000010011010000001100010101111110000111010011100000010010100111110111001100011111100001011111000" +
            "010100110110010001000111101011111111001110011001111010000101"
        },
        {
            "(01)98898765432106(3203)022767",
            "01010011000001001110111111110000101000011110001010011011111011000100101111000000111011110100110001110111001100011100100011111100001011011111" +
            "01110111010"
        },
        {
            "(01)98898765432106(3105)012345",
            "01000110110110000010111111110000101110110001000010011010000001100010101111110000111010011100000010010100111110111001100011111100001010110011" +
            "100001110100001011000111101011111111001110011001111010000101"
        },
        {
            "(01)98898765432106(3202)012345(15)991231",
            "01001000011000110110111111110000101110000110010100011010000001100010101111110000111010011100000010010100111110111001100011111100001011101100" +
            "000100100100011110010110001011111111001110001101111010000101"
        },
        {
            "(01)98898765432106(3102)001750(11)100503",
            "01001100001100010110111111110000101110110001000010011010000001100010101111110000111010011100000010010100111110111001100011111100001011110010" +
            "000010100101001110111110001011111111001110101000001000011101"
        },
        {
            "(01)98898765432106(3922)795(3103)000123",
            "01000011101100010010111111110000101100010000110110011010000001100010101111100000011010011100000010010100111110111001100011111100001010011110" +
            "1110100001101110001100001010111111110011100001011101111101001010001000000110001111000000101000011001110001010"
        },
        {
            "(01)98898765432106(3932)9781234(21)ABC(3103)000123",
            "01001111101100100010111111110000101011000001001110011010000001100010101111100000011010011100000010010100111110111001100011111100001011110001" +
            "01101111010011111000101110101111111100111000100110011110010100111001100000100111111000001011001000111101100101111011000010001011111111100110" +
            "1111100011100101010000110100000010011000000000101010000011000100010"
        },
        {
            "(91)ABC123(92)abc!%",
            "01000010100001111010111111110000101100100001000001010111111001111010101111100000011000111011111001010000100000110010100011111100001010111100" +
            "1110000101101001110111111010111111110011101001000111000111011001111011001110001111000000101101001111101110010"
        },
        {
            "(01)09521234543213(10)12345678901234567890",
            "01011000100110011110111111110000101110010000101100011010000100000110101111100000011000010101100011110011110001011100100011111100001011000110" +
            "01100001010101100111100000101111111100111000111011100010010001100010111110100011000000001010111101000111000111100011001000101011111111100110" +
            "011110000110100101"
        },
        { "(90)1", "010100001000110000101111111100001010001000100000110110111110101100001011110000000010101111100001011101" },
        { "(90)12", "010100000001001100101111111100001010001000100000110101111100101110001011110000000010101111100001011101" },
        { "(90)123", "010110001001000000101111111100001010001000100000110100011111010111001011110000000010000011000110100101" },
        { "(90)1234", "010011000010000010101111111100001010001000100000110100111110001011101011110000000010001101111001011101" },
        {
            "(91)A1(92)Z",
            "01011110010000100010111111110000101100100001000001010111111011110100101111000000111001100010001100010001011000101111100011111100001011010001" +
            "11001110010"
        },
        {
            "(01)09521234543213(10)ABCDEF-12.34/5*6(21)ab%&",
            "01011110110111000110111111110000101000111011001000011010000100000110101111000000001000010101100011110011110001011100100011111100001011100011" +
            "00100001010011101111110110101111000000111010111101110011110111100010000010100011110000001011001111101101100100100000000101001011111100001110" +
            "10111110110011110110000010010011100011000000001011000110001110110111000010000101001011111000000110100111011111000101111110110110001001111110" +
            "0000101011001111011110010"
        },
        {
            "(90)ABC1234567(91)xyz",
            "01000010100001101110111111110000101000100010000011010111111001111010101111100000011000111011111001010000100000100110100011111100001011001011" +
            "0111100001000101010000000010111111110011101110111111011001011100100000110010001111000000101110011110111001010"
        },
    };

    [Theory]
    [MemberData(nameof(Gs1DataBarExpanded_MatchesReferenceModulesData))]
    public void Gs1DataBarExpanded_MatchesReferenceModules(string text, string expected)
        => Encode(text, (ref symbol) => Assert.Equal(expected, SymbolEncoder.ModuleString(symbol.Runs)));

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
    /// The compressed methods carry the GTIN of Application Identifier (01) without its check digit, so a wrong check digit is refused. A character outside the
    /// general purpose field and data longer than 21 data characters are refused too. Section 5.5.2.3.3 of the GS1 General Specifications: "when the data
    /// includes AI (01), it SHALL be the first element string encoded", so (01) after (10) is refused.
    /// </summary>
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
    /// Encodes the text with the default options and checks the symbol.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="check">The check to run on the encoded symbol.</param>
    private static void Encode(string text, SymbolEncoder.Check check)
        => SymbolEncoder.Encode(new Gs1DataBarExpandedSymbology(), text, new BarcodeOptions(), check);
}
