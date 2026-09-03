// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Interleaved2Of5Symbology"/>. Each expected run string is the alternating
/// bar and space module widths, starting with a bar, taken verbatim from an independent reference
/// implementation through its raw encoding API. That implementation draws a wide element two modules
/// wide and emits a narrow space after the stop pattern, so the test widens every wide element to the
/// three modules this library draws and drops the trailing space before it compares. The patterns
/// themselves are Table 5-23 of the GS1 General Specifications.
/// </summary>
public class Interleaved2Of5SymbologyTests
{
    [Theory]
    [InlineData("1234", "1111211211112221211211122111")]
    [InlineData("0123456789", "1111121121211212221111211211221121112121121221121122112111")]
    public void MatchesReferenceRuns(string text, string reference)
    {
        LinearBarcodeSymbol symbol = Encode(text);
        Assert.Equal(Widen(reference), string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// Section 5.3.2.1.1 of the GS1 General Specifications pairs the digits, so an odd count takes a
    /// leading zero. The reference vector is the one it emits for the same odd input.
    /// </summary>
    [Fact]
    public void PadsAnOddCountWithALeadingZero()
    {
        Assert.Equal(string.Concat(Encode("0123").RunWidths), string.Concat(Encode("123").RunWidths));
        Assert.Equal(Widen("1111121121211212221111212111"), string.Concat(Encode("123").RunWidths));
    }

    /// <summary>
    /// Section 7.9 of the GS1 General Specifications weights the digits 3 and 1 alternately from the
    /// right, starting with 3, and the check digit lifts the sum to the next multiple of ten. For 12345
    /// the sum is 15 + 4 + 9 + 2 + 3 = 33, so the check digit is 7. For 1234 it is 12 + 3 + 6 + 1 = 22,
    /// so the check digit is 8, and the six digits then need a leading zero. The reference vectors are
    /// the ones it emits with its check digit turned on.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="reference">The reference run string with the check digit carried.</param>
    [Theory]
    [InlineData("12345", "11112112111122212112111221112112122111")]
    [InlineData("1234", "11111211212112122211112112112112212111")]
    public void CalculatesTheCheckDigitOfSectionSevenNine(string text, string reference)
    {
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Interleaved2Of5Symbology(CheckCharacterMode.Compute).Encode(text, new BarcodeOptions());
        Assert.Equal(Widen(reference), string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// A supplied check digit is validated against the data and carried once, so the symbol is the one
    /// the calculated check digit produces.
    /// </summary>
    [Fact]
    public void ValidatesASuppliedCheckDigit()
    {
        LinearBarcodeSymbol validated = (LinearBarcodeSymbol)new Interleaved2Of5Symbology(CheckCharacterMode.Validate).Encode("123457", new BarcodeOptions());
        LinearBarcodeSymbol computed = (LinearBarcodeSymbol)new Interleaved2Of5Symbology(CheckCharacterMode.Compute).Encode("12345", new BarcodeOptions());

        Assert.Equal(string.Concat(computed.RunWidths), string.Concat(validated.RunWidths));
        Assert.Throws<ArgumentException>(() => new Interleaved2Of5Symbology(CheckCharacterMode.Validate).Encode("123456", new BarcodeOptions()));
    }

    /// <summary>
    /// Appendix D of AIM USS-I 2/5 prints "all numeric characters in the code including leading zeroes",
    /// so the leading zero always prints and the check digit prints by default. A caller can still keep
    /// the check digit off the printed line.
    /// </summary>
    [Theory]
    [InlineData(CheckCharacterMode.None, false, "123", "0123")]
    [InlineData(CheckCharacterMode.Compute, false, "12345", "12345")]
    [InlineData(CheckCharacterMode.Compute, true, "12345", "123457")]
    [InlineData(CheckCharacterMode.Compute, true, "1234", "012348")]
    public void PrintsTheDigitsTheSymbolCarries(CheckCharacterMode checkDigit, bool printCheckDigit, string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Interleaved2Of5Symbology(checkDigit, printCheckDigit).Encode(text, options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal(expected, placement.Text);
    }

    /// <summary>
    /// Section 5.3.2.2 of the GS1 General Specifications measures a symbol as <c>(P(4N+6)+N+6)X</c>
    /// before its quiet zones, where P is the number of digit pairs and N the wide to narrow ratio, which
    /// this library draws as 3.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="pairs">The number of digit pairs the symbol carries.</param>
    [Theory]
    [InlineData("1234", 2)]
    [InlineData("123", 2)]
    [InlineData("0123456789", 5)]
    public void SpansTheWidthOfSectionFiveThreeTwoTwo(string text, int pairs)
    {
        int widthInModules = 0;
        foreach (int run in Encode(text).RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal((pairs * ((4 * 3) + 6)) + 3 + 6, widthInModules);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12A4")]
    [InlineData("12 34")]
    [InlineData("１２３４")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new Interleaved2Of5Symbology().Encode(text, new BarcodeOptions());

    private static string Widen(string reference)
        => reference[..^1].Replace('2', '3');
}
