// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="HibcCode39Symbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, taken from an independent reference implementation through
/// its raw encoding API, less the gap it emits after the stop character. A HIBC Code 39 carries the same
/// data a HIBC Code 128 does, drawn by Code 39, so these also prove the shared data layer.
/// </summary>
public class HibcCode39SymbologyTests
{
    [Theory]
    [InlineData("A123BJC5D6E71", "1311313111131113131131111311313113111131113311113131331111111131131131111133311131311311113113311111111133113111333111113111331111111311313131131111311111133131131131311")]
    [InlineData("12345", "13113131111311131311311311113111331111313133111111111331113131133111111111331131131131311")]
    [InlineData("$$52001510X3", "131131311113111313111313131111131313111131133111111133111131111331311111133131113113111131311331111131131111311113313111131131113131331111111313111311131131311")]
    [InlineData("/", "1311313111131113131113131113111331113111131131311")]
    public void MatchesReferenceRuns(string text, string expected)
    {
        LinearBarcodeSymbol symbol = Encode(new HibcCode39Symbology(), text);
        Assert.Equal(expected, string.Concat(symbol.RunWidths));
    }

    /// <summary>
    /// A caller who has already calculated the check character supplies it, and the symbol carries it
    /// once, so the runs match those of the same data with the check character left off.
    /// </summary>
    [Fact]
    public void ValidatesASuppliedCheckCharacter()
        => Assert.Equal(
            string.Concat(Encode(new HibcCode39Symbology(), "A123BJC5D6E71").RunWidths),
            string.Concat(Encode(new HibcCode39Symbology(true), "A123BJC5D6E71G").RunWidths));

    [Fact]
    public void RejectsWrongCheckCharacter()
        => Assert.Throws<ArgumentException>(() => Encode(new HibcCode39Symbology(true), "A123BJC5D6E71H"));

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
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new HibcCode39Symbology(), text));

    [Fact]
    public void RejectsDataBeyondTheMaximumLength()
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new HibcCode39Symbology(), new string('A', 501)));

    /// <summary>
    /// The interpretation is the encoded data between delimiters, so it shows the flag character and the
    /// check character the caller did not type, and a check character that is a space shows as an
    /// underscore.
    /// </summary>
    [Theory]
    [InlineData("A123BJC5D6E71", "*+A123BJC5D6E71G*")]
    [InlineData("/", "*+/_*")]
    public void PrintsTheFlagAndCheckCharacters(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new HibcCode39Symbology().Encode(text, options);

        BarcodeTextPlacement placement = Assert.Single(symbol.Text);
        Assert.Equal(expected, placement.Text);
    }

    /// <summary>
    /// The data already carries the HIBC check character, so Code 39 adds none of its own: the symbol is
    /// the flag character, the data and the one check character between the start and stop characters.
    /// </summary>
    [Fact]
    public void AddsNoCode39CheckCharacter()
    {
        LinearBarcodeSymbol symbol = Encode(new HibcCode39Symbology(), "12345");
        int widthInModules = 0;
        foreach (int run in symbol.RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal(((1 + 5 + 1 + 2) * 16) - 1, widthInModules);
    }

    private static LinearBarcodeSymbol Encode(HibcCode39Symbology symbology, string text)
        => (LinearBarcodeSymbol)symbology.Encode(text, new BarcodeOptions());
}
