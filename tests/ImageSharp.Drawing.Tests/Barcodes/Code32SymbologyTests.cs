// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Code32Symbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, taken from an independent reference implementation through
/// its raw encoding API, less the gap it emits after the stop character. The check digit, the base 32
/// alphabet and the printed line come from Allegato A of the Italian decree of 18 July 2014.
/// </summary>
public class Code32SymbologyTests
{
    [Theory]
    [InlineData("01234567", "1311313111111331311131311311111131113311113111331111311311311111331131131131311")]
    [InlineData("012345676", "1311313111111331311131311311111131113311113111331111311311311111331131131131311")]
    [InlineData("00000000", "1311313111111331311111133131111113313111111331311111133131111113313111131131311")]
    [InlineData("09999999", "1311313111113311113113313111113131131111113111331111131131311111313311131131311")]
    public void MatchesReferenceRuns(string text, string expected)
    {
        LinearBarcodeSymbol symbol = Encode(text);
        Assert.Equal(expected, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// Section 3 doubles the second, fourth, sixth and eighth digits and sums the quotient and remainder
    /// of each product divided by ten. For 01234567 those products are 2, 6, 10 and 14, which give 2, 6, 1
    /// and 5. The first, third, fifth and seventh digits sum to 12, so the total is 26 and the check digit
    /// is 6.
    /// </summary>
    [Fact]
    public void CalculatesTheCheckDigitOfSectionThree()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Code32Symbology().Encode("01234567", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("A012345676", placement.Text);
    }

    [Fact]
    public void RejectsWrongCheckDigit()
        => Assert.Throws<ArgumentException>(() => Encode("012345675"));

    /// <summary>
    /// The AIC code is eight digits and an optional check digit, so anything else is rejected, as is a
    /// non-digit.
    /// </summary>
    [Theory]
    [InlineData("0123456")]
    [InlineData("0123456789")]
    [InlineData("0123456A")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Table 1 gives the base 32 alphabet as the digits and the English letters without A, E, I and O. The
    /// symbol carries six of those characters. The lowest AIC code carries six zeros. The highest is
    /// 099999993, whose base 32 places are 2, 31, 11, 24, 7 and 25.
    /// </summary>
    [Theory]
    [InlineData("00000000", "000000")]
    [InlineData("09999999", "2ZCS7T")]
    public void CarriesSixBase32Characters(string text, string expected)
    {
        LinearBarcodeSymbol symbol = Encode(text);

        Assert.Equal(
            string.Concat(new Code39Symbology().Encode(expected, new BarcodeOptions()) is LinearBarcodeSymbol code39
                ? code39.RunWidths
                : []),
            string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// Area 3 prints the letter A and then the nine digits. That letter is the field identifier for
    /// automatic reading equipment. The symbol carries the six base 32 characters only.
    /// </summary>
    [Fact]
    public void PrintsTheFieldIdentifierAndTheNineDigits()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Code32Symbology().Encode("012345676", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("A012345676", placement.Text);
    }

    /// <summary>
    /// The symbol carries six characters between the start and stop characters, and Code 39 adds no check
    /// character of its own.
    /// </summary>
    [Fact]
    public void AddsNoCode39CheckCharacter()
    {
        LinearBarcodeSymbol symbol = Encode("012345676");
        int widthInModules = 0;
        foreach (int run in symbol.RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal(((6 + 2) * 16) - 1, widthInModules);
    }

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new Code32Symbology().Encode(text, new BarcodeOptions());
}
