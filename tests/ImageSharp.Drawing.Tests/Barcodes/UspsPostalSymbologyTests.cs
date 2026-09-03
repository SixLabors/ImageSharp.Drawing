// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="PostnetSymbology"/> and <see cref="PlanetSymbology"/>. Each expected bar
/// string is the sequence of full (F) and half (H) bars written out from the digit table of section
/// 708.4.2.1 of the Domestic Mail Manual. The five digit POSTNET and eleven digit PLANET strings match
/// the reference implementation's raw encoding API.
/// </summary>
public class UspsPostalSymbologyTests
{
    /// <summary>
    /// The five digit symbol 01234 is a frame bar, the digits 0 to 4, the correction digit 0, which makes
    /// the sum 10, and a frame bar: 32 bars. The nine and eleven digit symbols carry 52 and 62 bars.
    /// </summary>
    /// <param name="text">The digits to encode.</param>
    /// <param name="bars">The bars from the left, F for full and H for half.</param>
    [Theory]
    [InlineData("01234", "FFFHHHHHHFFHHFHFHHFFHHFHHFFFHHHF")]
    [InlineData("012345678", "FFFHHHHHHFFHHFHFHHFFHHFHHFHFHFHHFFHHFHHHFFHHFHHFHHFF")]
    [InlineData("01234567890", "FFFHHHHHHFFHHFHFHHFFHHFHHFHFHFHHFFHHFHHHFFHHFHFHFHHFFHHHHFHFHF")]
    public void EncodesPostnet(string text, string bars)
        => Assert.Equal(bars, Kinds(Encode(new PostnetSymbology(), text)));

    /// <summary>
    /// PLANET inverts the bars: the two bars of each digit that POSTNET makes full are half bars. The
    /// correction digit of 01234567890 is 5, because the digits add to 45.
    /// </summary>
    /// <param name="text">The digits to encode.</param>
    /// <param name="bars">The bars from the left, F for full and H for half.</param>
    [Theory]
    [InlineData("01234567890", "FHHFFFFFFHHFFHFHFFHHFFHFFHFHFHFFHHFFHFFFHHFFHFHFHFFHHFFFFHFHFF")]
    [InlineData("0123456789012", "FHHFFFFFFHHFFHFHFFHHFFHFFHFHFHFFHHFFHFFFHHFFHFHFHFFHHFFFFFFHHFFHFHFFHFHF")]
    public void EncodesPlanet(string text, string bars)
        => Assert.Equal(bars, Kinds(Encode(new PlanetSymbology(), text)));

    /// <summary>
    /// Section 708.4.2.5 of the Domestic Mail Manual: bars 0.020 inch wide at 22 bars per inch, full bars
    /// 0.125 inch high and half bars 0.050 inch high. At the 0.020 inch module a bar is 11 run units, a
    /// space is 14, the run unit is 1/11 module, a full bar is 6.25 modules and a half bar 2.5, with the
    /// half bars standing on the baseline.
    /// </summary>
    [Fact]
    public void DrawsTheDimensionsOfSectionSevenZeroEightFourTwoFive()
    {
        LinearBarcodeSymbol symbol = Encode(new PostnetSymbology(), "01234");

        Assert.Equal(1F / 11F, symbol.RunUnit);
        Assert.Equal(63, symbol.RunWidths.Length);
        for (int i = 0; i < symbol.RunWidths.Length; i++)
        {
            Assert.Equal((i & 1) == 0 ? 11 : 14, symbol.RunWidths[i]);
        }

        for (int i = 0; i < symbol.BarHeights.Length; i++)
        {
            bool full = symbol.BarHeights[i] == 6.25F;
            Assert.True(full || symbol.BarHeights[i] == 2.5F);
            Assert.Equal(full ? 0F : 3.75F, symbol.BarTops[i]);
        }

        Assert.Equal(6.25F, symbol.LeadingQuietZone);
        Assert.Equal(6.25F, symbol.TrailingQuietZone);
    }

    [Fact]
    public void PrintsTheDigitsAsGiven()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new PostnetSymbology().Encode("01234", options);
        Assert.Equal("01234", Assert.Single(symbol.Text).Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123")]
    [InlineData("012345")]
    [InlineData("0123456789")]
    [InlineData("012345678901")]
    [InlineData("0123A")]
    [InlineData("０１２３４")]
    public void RejectsMalformedPostnetInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new PostnetSymbology(), text));

    [Theory]
    [InlineData("")]
    [InlineData("01234")]
    [InlineData("012345678901")]
    [InlineData("01234567890123")]
    [InlineData("0123456789A")]
    public void RejectsMalformedPlanetInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new PlanetSymbology(), text));

    private static LinearBarcodeSymbol Encode(BarcodeSymbology symbology, string text)
        => (LinearBarcodeSymbol)symbology.Encode(text, new BarcodeOptions());

    private static string Kinds(LinearBarcodeSymbol symbol)
    {
        char[] kinds = new char[symbol.BarHeights.Length];
        for (int i = 0; i < kinds.Length; i++)
        {
            kinds[i] = symbol.BarHeights[i] == 6.25F ? 'F' : 'H';
        }

        return new string(kinds);
    }
}
