// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="RoyalMailSymbology"/>, <see cref="KixSymbology"/> and
/// <see cref="DaftSymbology"/>. Each expected bar string is the sequence of bar states, A for an
/// ascender, D for a descender, F for a full bar and T for a tracker, from the reference
/// implementation's raw encoding API.
/// </summary>
public class RoyalMailSymbologyTests
{
    /// <summary>
    /// A symbol is a start bar with an ascender, four bars per character, four for the check character
    /// and a full stop bar.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="bars">The bar states from the left.</param>
    [Theory]
    [InlineData("LE28HS9Z", "AFTTFTFFTTDFATFDADFATFTFTDATFFFTTDATFF")]
    [InlineData("B31HQ", "ADFTADTAFTDAFDFATADFTAFDTF")]
    [InlineData("SN34RD1A", "AFTFTFDTADTAFDTFAFTADTFADTDAFDADAADDAF")]
    public void EncodesRoyalMail(string text, string bars)
        => Assert.Equal(bars, Encode(new RoyalMailSymbology(), text));

    /// <summary>
    /// KIX is the same characters without the start bar, the check character and the stop bar.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="bars">The bar states from the left.</param>
    [Theory]
    [InlineData("1231FZ13XHS", "TDAFTDFADTAFTDAFDAADFFTTTDAFDTAFFATDDFATFTFT")]
    [InlineData("2500GG30", "TDFADDAATTFFTTFFDAFTDAFTDTAFTTFF")]
    public void EncodesKix(string text, string bars)
        => Assert.Equal(bars, Encode(new KixSymbology(), text));

    [Fact]
    public void EncodesDaftAsGiven()
        => Assert.Equal("FATDAFTDAD", Encode(new DaftSymbology(), "FATDAFTDAD"));

    /// <summary>
    /// The check character of the checksum calculation table. The reference symbol carries it in the four
    /// bars before the stop bar: T D A T for LE28HS9Z, which is the pattern of 9, F D T F for B31HQ, the
    /// pattern of W, and A D D A for SN34RD1A, the pattern of K.
    /// </summary>
    /// <param name="text">The data.</param>
    /// <param name="expected">The check character.</param>
    [Theory]
    [InlineData("LE28HS9Z", '9')]
    [InlineData("B31HQ", 'W')]
    [InlineData("SN34RD1A", 'K')]
    public void CalculatesTheCheckCharacter(string text, char expected)
        => Assert.Equal(expected, RoyalMailEncoder.CheckCharacter(text));

    /// <summary>
    /// Table 11 of the Mailmark barcode definition document at the 0.54 mm module: bars of 54 run units
    /// of 0.01 mm, spaces of 66, a tracker of 1.30 mm, ascenders and descenders of 1.90 mm, a full bar of
    /// 5.10 mm and a clear zone of 2 mm. A tracker stands 1.90 mm down, a descender starts there too, and
    /// an ascender and a full bar start at the top.
    /// </summary>
    [Fact]
    public void DrawsTheDimensionsOfTableEleven()
    {
        SymbolEncoder.Encode(new KixSymbology(), "2500GG30", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(1F / 54F, symbol.RunUnit);
            for (int i = 0; i < symbol.Runs.Length; i++)
            {
                Assert.Equal((i & 1) == 0 ? 54 : 66, symbol.Runs[i]);
            }

            float ascender = (float)(1.90 / 0.54);
            float tracker = (float)(1.30 / 0.54);
            string states = States(ref symbol);
            for (int i = 0; i < states.Length; i++)
            {
                float expectedTop = states[i] is 'A' or 'F' ? 0F : ascender;
                float expectedHeight = states[i] switch
                {
                    'T' => tracker,
                    'F' => ascender + tracker + ascender,
                    _ => ascender + tracker,
                };

                Assert.Equal(expectedTop, symbol.BarTops[i]);
                Assert.Equal(expectedHeight, symbol.BarHeights[i]);
            }

            Assert.Equal((float)(2 / 0.54), symbol.LeadingQuietZone);
            Assert.Equal((float)(2 / 0.54), symbol.TrailingQuietZone);
        });

        Assert.Equal(0F, SymbolEncoder.Read(new DaftSymbology(), "FT", new BarcodeOptions(), static (ref symbol) => symbol.LeadingQuietZone));
    }

    [Fact]
    public void PrintsTheDataAsGiven()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        string[] texts = SymbolEncoder.Read(new RoyalMailSymbology(), "LE28HS9Z", options, SymbolEncoder.Texts);
        Assert.Equal("LE28HS9Z", Assert.Single(texts));

        Assert.Empty(SymbolEncoder.Read(new DaftSymbology(), "FATD", options, SymbolEncoder.Texts));
    }

    [Theory]
    [InlineData("")]
    [InlineData("le28hs9z")]
    [InlineData("LE2 8HS")]
    [InlineData("LE-28")]
    [InlineData("ＬＥ２８")]
    public void RejectsMalformedInput(string text)
    {
        Assert.ThrowsAny<ArgumentException>(() => Encode(new RoyalMailSymbology(), text));
        Assert.ThrowsAny<ArgumentException>(() => Encode(new KixSymbology(), text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("FATX")]
    [InlineData("fatd")]
    public void RejectsMalformedDaftInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new DaftSymbology(), text));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The bar states of the symbol.</returns>
    private static string Encode(BarcodeSymbology symbology, string text)
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), States);

    /// <summary>
    /// Reads the bar states of a symbol from the bar tops and heights: F for a full bar, A for an
    /// ascender, D for a descender and T for a tracker.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The bar states.</returns>
    private static string States(ref BarcodeSymbol symbol)
    {
        float ascender = (float)(1.90 / 0.54);
        float tracker = (float)(1.30 / 0.54);
        char[] states = new char[symbol.BarHeights.Length];
        for (int i = 0; i < states.Length; i++)
        {
            bool up = symbol.BarTops[i] == 0F;
            bool down = symbol.BarTops[i] + symbol.BarHeights[i] > ascender + tracker + 0.001F;
            states[i] = up && down ? 'F' : up ? 'A' : down ? 'D' : 'T';
        }

        return new string(states);
    }
}
