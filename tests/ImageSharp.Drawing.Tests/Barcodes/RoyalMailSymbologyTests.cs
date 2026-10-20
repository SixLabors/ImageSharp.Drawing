// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="RoyalMailSymbology"/>, <see cref="KixSymbology"/> and <see cref="DaftSymbology"/>. Each expected bar string is the sequence of
/// bar states, A for an ascender, D for a descender, F for a full bar and T for a tracker, from the reference implementation's raw encoding API.
/// </summary>
public class RoyalMailSymbologyTests
{
    /// <summary>
    /// A symbol is a start bar with an ascender, four bars per character, four for the check character and a full stop bar.
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
    /// The check character of the checksum calculation table. The reference symbol carries it in the four bars before the stop bar: T D A T for LE28HS9Z, which
    /// is the pattern of 9, F D T F for B31HQ, the pattern of W, and A D D A for SN34RD1A, the pattern of K.
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
    /// The Royal Mail 4-state bars share the dimensions of Table 11 of the Mailmark barcode definition document. DAFT carries no clear zone.
    /// </summary>
    [Fact]
    public void DrawsTheDimensionsOfTableEleven()
    {
        SymbolEncoder.Encode(new KixSymbology(), "2500GG30", new BarcodeOptions(), FourStateMailmarkSymbologyTests.AssertTableEleven);

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
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), SymbolEncoder.FourStates);
}
