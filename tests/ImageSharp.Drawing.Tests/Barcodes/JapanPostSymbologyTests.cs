// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="JapanPostSymbology"/>. Each expected bar string is the sequence of bar states, A for an ascender, D for a descender, F for a
/// full bar and T for a tracker. The expected strings are from the reference implementation's raw encoding API unless the test states otherwise.
/// </summary>
public class JapanPostSymbologyTests
{
    /// <summary>
    /// A symbol is the start code F D, twenty codes of three bars with CC4, T D A, as the filler, the check code and the stop code D F.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="bars">The bar states from the left.</param>
    [Theory]
    [InlineData("1234567", "FDFFTFDADFAFADFTFDAFAFDTDATDATDATDATDATDATDATDATDATDATDATDATDAFFFDF")]
    [InlineData("15400233-16-4-205", "FDFFTFTFFADFTTFTTFDADFADFATFTFFTDAFTFTFADTFTFDAFTTFTFTDATDATDADAFDF")]
    public void EncodesJapanPost(string text, string bars)
        => Assert.Equal(bars, Encode(text));

    /// <summary>
    /// A letter is CC1, CC2 or CC3 followed by a digit: A is CC1 and 0, K is CC2 and 0, Z is CC3 and 5. The sum of the codes of 1234567890-ABC and the fillers
    /// is 133, a multiple of 19, so the check code is 0, the pattern F T T.
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
    /// Page 12 of the manual gives every size as a multiple of a/10 for a size of a points, "8≦ａ≦11.5": a long bar of 3.40 to 3.60, a timing bar of 1.05 to
    /// 1.35, a bar pitch of 0.95 to 1.30, a bar width of 0.50 to 0.70 and a bar space of 0.45 to 0.60 millimetres. The bar width is 0.6 × a/10 at the base
    /// size, so the drawn bar width gives a. Page 13: "カスタマバーコードの上下左右には、2mm以上の空白を設けるものとします".
    /// </summary>
    [Fact]
    public void DrawsTheDimensionsOfTheManual()
        => SymbolEncoder.Encode(new JapanPostSymbology(), "1234567", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(133, symbol.Runs.Length);

            double unit = symbol.RunUnit * symbol.XDimension;
            double size = symbol.Runs[0] * unit / 0.6;

            Assert.InRange(size * 10, 8, 11.5);

            for (int i = 0; i < symbol.Runs.Length; i += 2)
            {
                Assert.InRange(symbol.Runs[i] * unit / size, 0.50, 0.70);
            }

            for (int i = 1; i < symbol.Runs.Length; i += 2)
            {
                Assert.InRange(symbol.Runs[i] * unit / size, 0.45, 0.60);
                Assert.InRange((symbol.Runs[i - 1] + symbol.Runs[i]) * unit / size, 0.95, 1.30);
            }

            string states = SymbolEncoder.FourStates(ref symbol);
            int timing = states.IndexOf('T');

            Assert.InRange(symbol.BarHeights[timing] * symbol.XDimension / size, 1.05, 1.35);
            Assert.InRange(SymbolEncoder.BarBottom(ref symbol) * symbol.XDimension / size, 3.40, 3.60);

            Assert.True(symbol.LeadingQuietZone * symbol.XDimension >= 2 - 1e-4);
            Assert.True(symbol.TrailingQuietZone * symbol.XDimension >= 2 - 1e-4);
            Assert.True(symbol.TopQuietZone * symbol.XDimension >= 2 - 1e-4);
            Assert.True(symbol.BottomQuietZone * symbol.XDimension >= 2 - 1e-4);
        });

    /// <summary>
    /// Page 13 of the Japan Post manual prints the barcode alone, "単独で", and defines no text line, so a font prints none.
    /// </summary>
    [Fact]
    public void PrintsNoText()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        Assert.Empty(SymbolEncoder.Read(new JapanPostSymbology(), "15400233-16-4-205", options, SymbolEncoder.Texts));
    }

    /// <summary>
    /// The data fills at most twenty codes, and a letter takes two codes. The symbology rejects empty input, data longer than twenty codes, lowercase
    /// letters, a space and full width digits.
    /// </summary>
    /// <param name="text">The text to encode.</param>
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
        => SymbolEncoder.Read(new JapanPostSymbology(), text, new BarcodeOptions(), SymbolEncoder.FourStates);
}
