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
        => Assert.Equal(bars, Encode(text));

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
        Assert.Equal("FDFFTFDADFAFADFTFDAFAFDADFTFFFTTTFTDATFTTDATFFTDATFDATDATDATDAFTTDF", Encode("1234567890-ABC"));
    }

    /// <summary>
    /// The dimensions of page 12 of the manual at the 0.6 mm module: bars and spaces of one module, a
    /// timing bar of 1.2 mm, extenders of 1.2 mm, a long bar of 3.6 mm and a clear space of 2 mm.
    /// </summary>
    [Fact]
    public void DrawsTheDimensionsOfTheManual()
        => SymbolEncoder.Encode(new JapanPostSymbology(), "1234567", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(1F, symbol.RunUnit);
            Assert.Equal(133, symbol.Runs.Length);
            Assert.All(symbol.Runs.ToArray(), width => Assert.Equal(1, width));

            string states = States(ref symbol);
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

            Assert.Equal((float)(2 / 0.6), symbol.LeadingQuietZone);
            Assert.Equal((float)(2 / 0.6), symbol.TrailingQuietZone);
        });

    [Fact]
    public void PrintsTheTextAsGivenBelowTheClearSpace()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        SymbolEncoder.Encode(new JapanPostSymbology(), "15400233-16-4-205", options, static (ref symbol) =>
        {
            BarcodeTextPlacement placement = Assert.Single(symbol.Text.ToArray());
            Assert.Equal("15400233-16-4-205", symbol.TextOf(placement).ToString());
            Assert.Equal(BarcodeTextSide.BelowBars, placement.Side);
            Assert.Equal(6F + (float)(2 / 0.6), placement.TextEdge, new ApproximateFloatComparer(1e-4F));
        });
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

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The bar states of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(new JapanPostSymbology(), text, new BarcodeOptions(), States);

    /// <summary>
    /// Reads the bar states of a symbol from the bar tops and heights: F for a full bar, A for an
    /// ascender, D for a descender and T for a tracker.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The bar states.</returns>
    private static string States(ref BarcodeSymbol symbol)
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
