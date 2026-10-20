// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Interleaved2Of5Symbology"/>. The patterns are Table 5-23 of the GS1 General Specifications.
/// </summary>
public class Interleaved2Of5SymbologyTests
{
    /// <summary>
    /// Section 5.3.2.1.1 of the GS1 General Specifications pairs the digits, so an odd count takes a leading zero.
    /// </summary>
    [Fact]
    public void PadsAnOddCountWithALeadingZero()
        => Assert.Equal(Encode("0123"), Encode("123"));

    /// <summary>
    /// The encoder validates a supplied check digit against the data and carries it once, so the symbol is the one the calculated check digit produces. A
    /// wrong check digit throws, and so does a check digit with no data.
    /// </summary>
    [Fact]
    public void ValidatesASuppliedCheckDigit()
    {
        string validated = SymbolEncoder.Read(
            new Interleaved2Of5Symbology(CheckCharacterMode.Validate), "123457", new BarcodeOptions(), SymbolEncoder.RunString);

        string computed = SymbolEncoder.Read(new Interleaved2Of5Symbology(CheckCharacterMode.Compute), "12345", new BarcodeOptions(), SymbolEncoder.RunString);

        Assert.Equal(computed, validated);

        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Interleaved2Of5Symbology(CheckCharacterMode.Validate), "123456", new BarcodeOptions()));

        // A check digit alone has no data to check.
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Interleaved2Of5Symbology(CheckCharacterMode.Validate), "0", new BarcodeOptions()));
    }

    /// <summary>
    /// Appendix D of AIM USS-I 2/5 prints "all numeric characters in the code including leading zeroes", so the leading zero always prints and the check digit
    /// prints by default. A caller can still keep the check digit off the printed line.
    /// </summary>
    /// <param name="checkDigit">The check digit mode.</param>
    /// <param name="printCheckDigit">Whether the check digit is part of the printed line.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData(CheckCharacterMode.None, false, "123", "0123")]
    [InlineData(CheckCharacterMode.Compute, false, "12345", "12345")]
    [InlineData(CheckCharacterMode.Compute, true, "12345", "123457")]
    [InlineData(CheckCharacterMode.Compute, true, "1234", "012348")]
    public void PrintsTheDigitsTheSymbolCarries(CheckCharacterMode checkDigit, bool printCheckDigit, string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Interleaved2Of5Symbology(checkDigit, printCheckDigit), text, options, SymbolEncoder.Texts);

        Assert.Equal(expected, Assert.Single(texts));
    }

    /// <summary>
    /// Section 3.3 of AIM USS-I 2/5 measures a symbol as <c>(P(4N+6)+6+N)X</c> before its quiet zones, where P is the number of digit pairs and N the wide to
    /// narrow ratio, which this library draws as 3.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="pairs">The number of digit pairs the symbol carries.</param>
    [Theory]
    [InlineData("1234", 2)]
    [InlineData("123", 2)]
    [InlineData("0123456789", 5)]
    public void SpansTheWidthOfSectionThreeThree(string text, int pairs)
        => Assert.Equal((pairs * ((4 * 3) + 6)) + 3 + 6, SymbolEncoder.Read(new Interleaved2Of5Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunSum));

    /// <summary>
    /// Section 3.2 of AIM USS-I 2/5: the narrow element width "has a minimum standard value of 0.0075 inches (0.191 mm)". At 1000 pixels a millimetre the
    /// smallest whole number of pixels at or over 0.1905 mm is 191 pixels, so an X dimension of 0.1 mm draws at 0.191 mm.
    /// </summary>
    [Fact]
    public void ClampsAnXDimensionUnderTheMinimumStandardValue()
        => SymbolEncoder.Encode(
            new Interleaved2Of5Symbology(),
            "1234",
            new BarcodeOptions { Dpi = SymbolEncoder.MicrometreDpi, XDimension = 0.1 },
            static (ref symbol) => Assert.Equal(0.191, symbol.XDimension, 1e-9));

    /// <summary>
    /// Section 3.2 of AIM USS-I 2/5: "The minimum quiet zone width is ten times the X dimension or 0.10 inch (2.54 mm), whichever is greater." At the 0.191 mm
    /// X dimension of that section the 2.54 mm is wider than ten modules.
    /// </summary>
    [Fact]
    public void KeepsTheQuietZoneOfSectionThreeTwo()
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MicrometreDpi, XDimension = 0.191 };

        SymbolEncoder.Encode(new Interleaved2Of5Symbology(), "1234", options, static (ref symbol) =>
        {
            Assert.Equal((float)(2.54 / 0.191), symbol.LeadingQuietZone, 1e-4F);
            Assert.Equal((float)(2.54 / 0.191), symbol.TrailingQuietZone, 1e-4F);
        });
    }

    /// <summary>
    /// Section 3.2 of AIM USS-I 2/5: "the minimum bar height should be 0.25 inches (6.35 mm) or 15 percent of the bar code symbol length, whichever is
    /// greater", and Appendix A defines the symbol length as "The distance between the outside edges of the quiet zones". Eight digits take 4 x 18 + 9 = 81
    /// modules, and the two 10 module quiet zones make 101, so the bars are 15.15 modules high, more than the 6.35 mm, 12.7 modules, at an X dimension of
    /// 0.5 mm.
    /// </summary>
    [Fact]
    public void DrawsTheBarHeightOfSectionThreeTwo()
        => SymbolEncoder.Encode(
            new Interleaved2Of5Symbology(),
            "12345678",
            new BarcodeOptions { XDimension = 0.5 },
            static (ref symbol) => Assert.All(symbol.BarHeights.ToArray(), height => Assert.Equal(0.15F * 101F, height, 1e-4F)));

    /// <summary>
    /// Interleaved 2 of 5 carries the digits 0 to 9 only. The symbology rejects empty input, a letter, a space and full width digits.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("12A4")]
    [InlineData("12 34")]
    [InlineData("１２３４")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new Interleaved2Of5Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
