// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code32Symbology"/>. The check digit, the base 32 alphabet and the printed line come from Allegato A of the Italian decree of
/// 18 July 2014. At 96 dots per inch, the 0.25 mm module draws at 1 pixel. The wide element of 2.5 modules rounds half up to 3 pixels.
/// </summary>
public class Code32SymbologyTests
{
    /// <summary>
    /// Section 3 doubles the second, fourth, sixth and eighth digits and sums the quotient and remainder of each product divided by ten. For 01234567 those
    /// products are 2, 6, 10 and 14, which give 2, 6, 1 and 5. The first, third, fifth and seventh digits sum to 12, so the total is 26 and the check digit is
    /// 6.
    /// </summary>
    [Fact]
    public void CalculatesTheCheckDigitOfSectionThree()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Code32Symbology(), "01234567", options, SymbolEncoder.Texts);

        Assert.Equal("A012345676", Assert.Single(texts));
    }

    /// <summary>
    /// The encoder rejects a supplied check digit that does not match the section 3 calculation. The check digit of 01234567 is 6, not 5.
    /// </summary>
    [Fact]
    public void RejectsWrongCheckDigit()
        => Assert.Throws<ArgumentException>(() => Encode("012345675"));

    /// <summary>
    /// The AIC code is eight digits and an optional check digit. The encoder rejects any other length and any character that is not a digit.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("0123456")]
    [InlineData("0123456789")]
    [InlineData("0123456A")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Table 1 gives the base 32 alphabet as the digits and the English letters without A, E, I and O. The symbol carries six of those characters. The lowest
    /// AIC code carries six zeros. The highest is 099999993, whose base 32 places are 2, 31, 11, 24, 7 and 25. A Code 39 symbol of those characters draws its
    /// wide elements three modules wide, as a Code 32 symbol does at 1 pixel a module.
    /// </summary>
    /// <param name="text">The AIC code to encode.</param>
    /// <param name="expected">The six base 32 characters that the symbol carries.</param>
    [Theory]
    [InlineData("00000000", "000000")]
    [InlineData("09999999", "2ZCS7T")]
    public void CarriesSixBase32Characters(string text, string expected)
        => Assert.Equal(SymbolEncoder.Read(new Code39Symbology(), expected, new BarcodeOptions { Dpi = 96 }, SymbolEncoder.RunString), Encode(text));

    /// <summary>
    /// Area 3 prints the letter A and then the nine digits. That letter is the field identifier for automatic reading equipment. The symbol carries the six
    /// base 32 characters only.
    /// </summary>
    [Fact]
    public void PrintsTheFieldIdentifierAndTheNineDigits()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Code32Symbology(), "012345676", options, SymbolEncoder.Texts);

        Assert.Equal("A012345676", Assert.Single(texts));
    }

    /// <summary>
    /// The symbol carries six characters between the start and stop characters, and Code 39 adds no check character of its own. At 96 dots per inch, a
    /// module is 1 pixel and the wide element is 3 modules. Thus a character and its gap are 3 x 3 + 6 + 1 = 16 modules. The stop character has no gap.
    /// </summary>
    [Fact]
    public void AddsNoCode39CheckCharacter()
        => Assert.Equal(
            ((6 + 2) * 16) - 1,
            SymbolEncoder.Read(new Code32Symbology(), "012345676", new BarcodeOptions { Dpi = 96 }, static (ref symbol) => symbol.WidthInModules));

    /// <summary>
    /// Allegato A: "lo spessore nominale delle barre e degli spazi stretti è di mm 0,250", so a symbol without an X dimension of the caller draws its modules
    /// at 0.25 mm. At the default 300 dots per inch, 0.25 mm is 2.95 pixels. Thus the module draws at 3 pixels, 3 x 25.4 / 300 mm.
    /// </summary>
    [Fact]
    public void DefaultsTheModuleWidthToTheNominalWidth()
        => Assert.Equal(
            3 * 25.4 / 300,
            SymbolEncoder.Read(new Code32Symbology(), "012345676", new BarcodeOptions(), static (ref symbol) => symbol.XDimension),
            1e-12);

    /// <summary>
    /// Section 4.4 e) of ISO/IEC 16388 recommends a bar height of at least 15 % of the symbol width. The wide element rounds to a whole pixel, so the drawn
    /// width can differ from the nominal ratio of 2.5. At 0.42 mm and 300 dots per inch, a module is 5 pixels and a wide element is 13 pixels, a ratio of 2.6.
    /// The bars must still be at least 15 % of the width that the symbol draws at.
    /// </summary>
    [Fact]
    public void DrawsTheBarsAtLeastFifteenPercentOfTheDrawnWidth()
        => SymbolEncoder.Encode(new Code32Symbology(), "012345676", new BarcodeOptions { XDimension = 0.42 }, static (ref symbol) =>
            Assert.True(symbol.BarHeights[0] >= 0.15F * symbol.WidthInModules, $"{symbol.BarHeights[0]} < 0.15 x {symbol.WidthInModules}"));

    /// <summary>
    /// The symbology calculates the check digit of section 3 when the input has eight digits, so the eight digits give the same bars as the nine.
    /// </summary>
    [Fact]
    public void EncodesEightDigitsAsTheNineWithTheirCheckDigit()
        => Assert.Equal(Encode("012345676"), Encode("01234567"));

    /// <summary>
    /// Encodes the text at 96 dots per inch, where a module draws at 1 pixel.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new Code32Symbology(), text, new BarcodeOptions { Dpi = 96 }, SymbolEncoder.RunString);
}
