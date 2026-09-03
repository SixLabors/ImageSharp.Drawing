// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="JapanPostSymbology"/>. Each expected bar string is the sequence of bar
/// states, A for an ascender, D for a descender, F for a full bar and T for a tracker. The expected
/// strings are from the reference implementation's raw encoding API unless the test states otherwise.
/// </summary>
public class JapanPostSymbologyTests
{
    /// <summary>
    /// A symbol is the start code F D, twenty codes of three bars with CC4, T D A, as the filler, the
    /// check code and the stop code D F.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="bars">The bar states from the left.</param>
    [Theory]
    [InlineData("1234567", "FDFFTFDADFAFADFTFDAFAFDTDATDATDATDATDATDATDATDATDATDATDATDATDAFFFDF")]
    [InlineData("15400233-16-4-205", "FDFFTFTFFADFTTFTTFDADFADFATFTFFTDAFTFTFADTFTFDAFTTFTFTDATDATDADAFDF")]
    public void EncodesJapanPost(string text, string bars)
        => Assert.Equal(bars, States(Encode(text)));

    /// <summary>
    /// A letter is CC1, CC2 or CC3 followed by a digit: A is CC1 and 0, K is CC2 and 0, Z is CC3 and 5.
    /// The sum of the codes of 1234567890-ABC and the fillers is 133, a multiple of 19, so the check code
    /// is 0, the pattern F T T.
    /// </summary>
    [Fact]
    public void EncodesLettersAndAZeroCheckCode()
    {
        byte[] codes = new byte[20];
        JapanPostEncoder.Codes("AKZ", codes);
        Assert.Equal([11, 0, 12, 0, 13, 5, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14], codes);

        JapanPostEncoder.Codes("1234567890-ABC", codes);
        Assert.Equal(0, JapanPostEncoder.CheckCode(codes));
        Assert.Equal("FDFFTFDADFAFADFTFDAFAFDADFTFFFTTTFTDATFTTDATFFTDATFDATDATDATDAFTTDF", States(Encode("1234567890-ABC")));
    }

    /// <summary>
    /// The dimensions of page 12 of the manual at the 0.6 mm module: bars and spaces of one module, a
    /// timing bar of 1.2 mm, extenders of 1.2 mm, a long bar of 3.6 mm and a clear space of 2 mm.
    /// </summary>
    [Fact]
    public void DrawsTheDimensionsOfTheManual()
    {
        LinearBarcodeSymbol symbol = Encode("1234567");

        Assert.Equal(1F, symbol.RunUnit);
        Assert.Equal(133, symbol.RunWidths.Length);
        Assert.All(symbol.RunWidths, width => Assert.Equal(1, width));

        string states = States(symbol);
        for (int i = 0; i < states.Length; i++)
        {
            float expectedTop = states[i] is 'A' or 'F' ? 0F : 2F;
            float expectedHeight = states[i] switch
            {
                'T' => 2F,
                'F' => 6F,
                _ => 4F,
            };

            Assert.Equal(expectedTop, symbol.BarTops[i]);
            Assert.Equal(expectedHeight, symbol.BarHeights[i]);
        }

        Assert.Equal(2F / 0.6F, symbol.LeadingQuietZone);
        Assert.Equal(2F / 0.6F, symbol.TrailingQuietZone);
    }

    [Fact]
    public void PrintsTheTextAsGivenBelowTheClearSpace()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new JapanPostSymbology().Encode("15400233-16-4-205", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("15400233-16-4-205", placement.Text);
        Assert.Equal(BarcodeTextSide.BelowBars, placement.Side);
        Assert.Equal(6F + (2F / 0.6F), placement.TextEdge, 0.0001F);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456789012345678901")]
    [InlineData("1234567890123456789A")]
    [InlineData("abc")]
    [InlineData("12 34")]
    [InlineData("１２３")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new JapanPostSymbology().Encode(text, new BarcodeOptions());

    private static string States(LinearBarcodeSymbol symbol)
    {
        char[] states = new char[symbol.BarHeights.Length];
        for (int i = 0; i < states.Length; i++)
        {
            bool up = symbol.BarTops[i] == 0F;
            bool down = symbol.BarTops[i] + symbol.BarHeights[i] > 4.001F;
            states[i] = up && down ? 'F' : up ? 'A' : down ? 'D' : 'T';
        }

        return new string(states);
    }
}
