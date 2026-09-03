// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Itf14Symbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, taken verbatim from an independent reference implementation
/// through its raw encoding API. That implementation draws a wide element two modules wide and emits a
/// narrow space after the stop pattern, so the test widens every wide element to the three modules this
/// library draws and drops the trailing space before it compares.
/// </summary>
public class Itf14SymbologyTests
{
    private const string Figure532 = "111122111211211111221221121121211212112111221221111221211111221212222111112111";

    private const float BearerBarThickness = 4.83F / 1.016F;

    /// <summary>
    /// Figure 5-32 of the GS1 General Specifications shows the number 15400141288763, whose check digit
    /// section 7.9 calculates as 3. Thirteen digits take the calculated check digit, fourteen carry their
    /// own, and spaces in the input stay out of the symbol, as section 4.14 rule 2.a.iv requires.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="reference">The reference run string.</param>
    [Theory]
    [InlineData("1540014128876", Figure532)]
    [InlineData("15400141288763", Figure532)]
    [InlineData("1 54 00141 28876 3", Figure532)]
    [InlineData("0123456789012", "111112112121121222111121121122112111212112122112112211121121211212211112212111")]
    public void MatchesReferenceRuns(string text, string reference)
        => Assert.Equal(Widen(reference), string.Concat(Encode(text).RunWidths));

    [Fact]
    public void RejectsAWrongCheckDigit()
        => Assert.Throws<ArgumentException>(() => Encode("15400141288764"));

    /// <summary>
    /// Figure 5-32 prints the number both as "1 54 00141 28876 3" and as "15400141288763". Section 4.14
    /// rule 2.a.v permits the spaces, so the printed line keeps the spaces of the input, and a calculated
    /// check digit follows a space only when the input has spaces.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData("1540014128876", "15400141288763")]
    [InlineData("15400141288763", "15400141288763")]
    [InlineData("1 54 00141 28876", "1 54 00141 28876 3")]
    [InlineData("1 54 00141 28876 3", "1 54 00141 28876 3")]
    public void PrintsTheFourteenDigits(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Itf14Symbology().Encode(text, options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal(expected, placement.Text);
        Assert.Equal(symbol.HeightInModules, placement.BarEdge);
    }

    /// <summary>
    /// Section 5.3.2.4 of the GS1 General Specifications gives the bearer bar a thickness of 4.83
    /// millimetres, which is 4.83 / 1.016 modules at the target X of section 5.3.2.2, and butts it against
    /// the top and bottom of the bars. The bars therefore start one thickness below the symbol top, and
    /// the symbol height is the 31.75 millimetre bar height of section 5.12.3.2 plus two thicknesses.
    /// </summary>
    [Fact]
    public void FramesTheBarsWithTheBearerBarOfSectionFiveThreeTwoFour()
    {
        LinearBarcodeSymbol symbol = Encode("15400141288763");

        Assert.Equal(BearerBarThickness, symbol.BearerBarThickness);
        Assert.All(symbol.BarTops, top => Assert.Equal(BearerBarThickness, top));
        Assert.Equal((31.75F / 1.016F) + BearerBarThickness + BearerBarThickness, symbol.HeightInModules, 3);
    }

    /// <summary>
    /// Section 5.3.2.2 of the GS1 General Specifications measures a symbol as <c>(P(4N+6)+N+6)X</c>
    /// before its quiet zones, where P is the seven character pairs and N the wide to narrow ratio, which
    /// this library draws as 3.
    /// </summary>
    [Fact]
    public void SpansTheWidthOfSectionFiveThreeTwoTwo()
    {
        int widthInModules = 0;
        foreach (int run in Encode("15400141288763").RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal((7 * ((4 * 3) + 6)) + 3 + 6, widthInModules);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("123456789012345")]
    [InlineData("154001412887A")]
    [InlineData("１５４００１４１２８８７６")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new Itf14Symbology().Encode(text, new BarcodeOptions());

    private static string Widen(string reference)
        => reference[..^1].Replace('2', '3');
}
