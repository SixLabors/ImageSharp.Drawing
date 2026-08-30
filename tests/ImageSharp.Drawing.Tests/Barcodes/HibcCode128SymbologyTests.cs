// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="HibcCode128Symbology"/>. Each expected run string is the alternating bar
/// and space module widths, starting with a bar, taken from the BWIPP reference implementation through its
/// raw encoding API. A HIBC Code 128 is a Code 128 symbol carrying the HIBC flag character, the data and
/// the modulo 43 check character.
/// </summary>
public class HibcCode128SymbologyTests
{
    [Theory]
    [InlineData("A123BJC5D6E71", "2112142312121113231232212232112211321311231121331313212132121123132231121321133121311232212113132321212331112")]
    [InlineData("$$52001510X3G", "2112142312121213221213221131412133112122221132222213121141313311212211322113131123131341112331112")]
    [InlineData("12345", "2112142312121232211131413121311131231141311123131124122331112")]
    [InlineData("A99912345/$$52001510X3", "2112142312121113231131411131414121213121311131231141311132221213221213221131412133112122221132222213121141313311212211322211322131312331112")]
    [InlineData("+A123BJC5D6E71", "2112142312122312121113231232212232112211321311231121331313212132121123132231121321133121311232211321131212232331112")]
    public void MatchesReferenceRuns(string text, string expected)
    {
        LinearBarcodeSymbol symbol = Encode(new HibcCode128Symbology(), text);
        Assert.Equal(expected, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// A caller who has already worked out the check character supplies it, and the symbol carries it
    /// once, so the runs match those of the same data with the check character left off.
    /// </summary>
    [Fact]
    public void ValidatesASuppliedCheckCharacter()
    {
        HibcCode128Symbology symbology = new(true);

        Assert.Equal(
            string.Concat(Encode(new HibcCode128Symbology(), "A123BJC5D6E71").RunWidths),
            string.Concat(Encode(symbology, "A123BJC5D6E71G").RunWidths));
    }

    [Fact]
    public void RejectsWrongCheckCharacter()
    {
        HibcCode128Symbology symbology = new(true);
        Assert.Throws<ArgumentException>(() => Encode(symbology, "A123BJC5D6E71H"));
    }

    /// <summary>
    /// HIBC carries the Code 39 character set, so anything outside it is rejected, as is empty data and
    /// data beyond the length the symbology allows.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("a123")]
    [InlineData("A*123")]
    [InlineData("A,123")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new HibcCode128Symbology(), text));

    [Fact]
    public void RejectsDataBeyondTheMaximumLength()
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new HibcCode128Symbology(), new string('A', 501)));

    /// <summary>
    /// The human readable interpretation is the encoded data between delimiters, so it shows the flag
    /// character and the check character the caller did not type.
    /// </summary>
    [Fact]
    public void PrintsTheFlagAndCheckCharacters()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new HibcCode128Symbology().Encode("A123BJC5D6E71", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("*+A123BJC5D6E71G*", placement.Text);
    }

    /// <summary>
    /// A check character that lands on a space is printed as an underscore, which can be read back.
    /// </summary>
    [Fact]
    public void PrintsASpaceCheckCharacterAsAnUnderscore()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new HibcCode128Symbology().Encode("/", options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal("*+/_*", placement.Text);
    }

    private static LinearBarcodeSymbol Encode(HibcCode128Symbology symbology, string text)
        => (LinearBarcodeSymbol)symbology.Encode(text, new BarcodeOptions());
}
