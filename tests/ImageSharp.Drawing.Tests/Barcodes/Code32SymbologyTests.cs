// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code32Symbology"/>. Each expected run string is the alternating bar and space module widths, starting with a bar, taken from an
/// independent reference implementation through its raw encoding API, less the gap it emits after the stop character. The check digit, the base 32 alphabet and
/// the printed line come from Allegato A of the Italian decree of 18 July 2014. The reference draws a wide element three modules wide. At the default 96 dots
/// per inch the 0.25 mm module draws at 1 pixel, and the wide element of 2.5 modules rounds half up to 3 pixels, so the runs are the reference runs.
/// </summary>
public class Code32SymbologyTests
{
    /// <summary>
    /// The bar and space widths match the reference implementation. Eight digits and the same digits with their check digit give the same symbol.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The reference run string.</param>
    [Theory]
    [InlineData("01234567", "1311313111111331311131311311111131113311113111331111311311311111331131131131311")]
    [InlineData("012345676", "1311313111111331311131311311111131113311113111331111311311311111331131131131311")]
    [InlineData("00000000", "1311313111111331311111133131111113313111111331311111133131111113313111131131311")]
    [InlineData("09999999", "1311313111113311113113313111113131131111113111331111131131311111313311131131311")]
    public void MatchesReferenceRuns(string text, string expected)
        => Assert.Equal(expected, Encode(text));

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
        => Assert.Equal(SymbolEncoder.Read(new Code39Symbology(), expected, new BarcodeOptions(), SymbolEncoder.RunString), Encode(text));

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
    /// The symbol carries six characters between the start and stop characters, and Code 39 adds no check character of its own. At 1 pixel a module the
    /// wide element is 3 modules, so a character and its gap are 3 x 3 + 6 + 1 = 16 modules, and the stop character has no gap.
    /// </summary>
    [Fact]
    public void AddsNoCode39CheckCharacter()
        => Assert.Equal(
            ((6 + 2) * 16) - 1,
            SymbolEncoder.Read(new Code32Symbology(), "012345676", new BarcodeOptions(), static (ref symbol) => symbol.WidthInModules));

    /// <summary>
    /// Allegato A: "lo spessore nominale delle barre e degli spazi stretti è di mm 0,250", so a symbol without an X dimension of the caller draws its modules
    /// at 0.25 mm. At the default 96 dots per inch 0.25 mm is 0.94 pixels, so the module draws at 1 pixel, 25.4 / 96 mm.
    /// </summary>
    [Fact]
    public void DefaultsTheModuleWidthToTheNominalWidth()
        => Assert.Equal(
            25.4 / 96,
            SymbolEncoder.Read(new Code32Symbology(), "012345676", new BarcodeOptions(), static (ref symbol) => symbol.XDimension),
            1e-12);

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new Code32Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
