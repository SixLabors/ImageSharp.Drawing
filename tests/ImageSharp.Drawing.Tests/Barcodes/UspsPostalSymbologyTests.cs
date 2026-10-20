// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="PostnetSymbology"/> and <see cref="PlanetSymbology"/>. Each expected bar string is the sequence of full (F) and half (H) bars
/// written out from the digit table of section 708.4.2.1 of the Domestic Mail Manual. The five digit POSTNET and eleven digit PLANET strings match the
/// reference implementation's raw encoding API.
/// </summary>
public class UspsPostalSymbologyTests
{
    /// <summary>
    /// The millimetres in an inch.
    /// </summary>
    private const double Inch = 25.4;

    /// <summary>
    /// The five digit symbol 01234 is a frame bar, the digits 0 to 4, the correction digit 0, which makes the sum 10, and a frame bar: 32 bars. The nine and
    /// eleven digit symbols carry 52 and 62 bars.
    /// </summary>
    /// <param name="text">The digits to encode.</param>
    /// <param name="bars">The bars from the left, F for full and H for half.</param>
    [Theory]
    [InlineData("01234", "FFFHHHHHHFFHHFHFHHFFHHFHHFFFHHHF")]
    [InlineData("012345678", "FFFHHHHHHFFHHFHFHHFFHHFHHFHFHFHHFFHHFHHHFFHHFHHFHHFF")]
    [InlineData("01234567890", "FFFHHHHHHFFHHFHFHHFFHHFHHFHFHFHHFFHHFHHHFFHHFHFHFHHFFHHHHFHFHF")]
    public void EncodesPostnet(string text, string bars)
        => Assert.Equal(bars, Encode(new PostnetSymbology(), text));

    /// <summary>
    /// PLANET inverts the bars: the two bars of each digit that POSTNET makes full are half bars. The correction digit of 01234567890 is 5, because the digits
    /// add to 45.
    /// </summary>
    /// <param name="text">The digits to encode.</param>
    /// <param name="bars">The bars from the left, F for full and H for half.</param>
    [Theory]
    [InlineData("01234567890", "FHHFFFFFFHHFFHFHFFHHFFHFFHFHFHFFHHFFHFFFHHFFHFHFHFFHHFFFFHFHFF")]
    [InlineData("0123456789012", "FHHFFFFFFHHFFHFHFFHHFFHFFHFHFHFFHHFFHFFFHHFFHFHFHFFHHFFFFFFHHFFHFHFFHFHF")]
    public void EncodesPlanet(string text, string bars)
        => Assert.Equal(bars, Encode(new PlanetSymbology(), text));

    /// <summary>
    /// Section 708.4.2.5 of the Domestic Mail Manual: "A full bar must be 0.125 ±0.010 inch high", "A half bar must be 0.050 ±0.010 inch high", "All bars
    /// must be 0.020 ±0.005 inch wide", the "pitch (a bar and a space) must average at least 0.0416 inch but no more than 0.05 inch" and "The clear vertical
    /// space between bars must not be less than 0.012 inch or more than 0.04 inch". The half bars stand on the baseline of the full bars.
    /// </summary>
    [Fact]
    public void DrawsTheDimensionsOfSectionSevenZeroEightFourTwoFive()
        => SymbolEncoder.Encode(new PostnetSymbology(), "01234", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(63, symbol.Runs.Length);
            double unit = symbol.RunUnit * symbol.XDimension / Inch;
            for (int i = 0; i < symbol.Runs.Length; i += 2)
            {
                Assert.InRange(symbol.Runs[i] * unit, 0.015, 0.025);
            }

            for (int i = 1; i < symbol.Runs.Length; i += 2)
            {
                Assert.InRange(symbol.Runs[i] * unit, 0.012, 0.04);
                Assert.InRange((symbol.Runs[i - 1] + symbol.Runs[i]) * unit, 0.0416, 0.05);
            }

            float fullBar = MaximumBarHeight(ref symbol);
            Assert.InRange(fullBar * symbol.XDimension / Inch, 0.115, 0.135);
            for (int i = 0; i < symbol.BarHeights.Length; i++)
            {
                if (symbol.BarHeights[i] != fullBar)
                {
                    Assert.InRange(symbol.BarHeights[i] * symbol.XDimension / Inch, 0.040, 0.060);
                }

                Assert.Equal(fullBar, symbol.BarTops[i] + symbol.BarHeights[i], 1e-4F);
            }
        });

    /// <summary>
    /// Section 202.5.7 c of the Domestic Mail Manual: "The clearance between the leftmost and rightmost bars and any adjacent printing must be at least 1/8
    /// inch", and "The minimum clearance between the barcode and any information line above or below it within the address block must be at least 1/25 inch".
    /// </summary>
    [Fact]
    public void KeepsTheClearanceAroundTheBars()
        => SymbolEncoder.Encode(new PostnetSymbology(), "01234", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.True(symbol.LeadingQuietZone * symbol.XDimension >= (Inch / 8) - 1e-4);
            Assert.True(symbol.TrailingQuietZone * symbol.XDimension >= (Inch / 8) - 1e-4);
            Assert.True(symbol.TopQuietZone * symbol.XDimension >= (Inch / 25) - 1e-4);
            Assert.True(symbol.BottomQuietZone * symbol.XDimension >= (Inch / 25) - 1e-4);
        });

    /// <summary>
    /// The Domestic Mail Manual defines no text line for POSTNET or PLANET, so a font prints none.
    /// </summary>
    [Fact]
    public void PrintsNoText()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        Assert.Empty(SymbolEncoder.Read(new PostnetSymbology(), "01234", options, SymbolEncoder.Texts));
        Assert.Empty(SymbolEncoder.Read(new PlanetSymbology(), "01234567890", options, SymbolEncoder.Texts));
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

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The bar kinds of the symbol.</returns>
    private static string Encode(BarcodeSymbology symbology, string text)
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), Kinds);

    /// <summary>
    /// Reads the bar kinds of a symbol from the bar heights: F for a full bar, the tallest bar of the symbol, and H for a half bar.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The bar kinds.</returns>
    private static string Kinds(ref BarcodeSymbol symbol)
    {
        float fullBar = MaximumBarHeight(ref symbol);
        char[] kinds = new char[symbol.BarHeights.Length];
        for (int i = 0; i < kinds.Length; i++)
        {
            kinds[i] = symbol.BarHeights[i] == fullBar ? 'F' : 'H';
        }

        return new string(kinds);
    }

    /// <summary>
    /// Returns the height of the tallest bar of a symbol, in modules. The frame bars of POSTNET and PLANET are always full bars.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The full bar height in modules.</returns>
    private static float MaximumBarHeight(ref BarcodeSymbol symbol)
    {
        float height = 0F;
        foreach (float bar in symbol.BarHeights)
        {
            height = MathF.Max(height, bar);
        }

        return height;
    }
}
