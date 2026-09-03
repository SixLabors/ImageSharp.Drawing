// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Bc412Symbology"/>. Each expected run string is the alternating bar and
/// space module widths, starting with a bar, from the reference implementation's raw encoding API in its
/// SEMI form. The reference ends on the last bar of the stop character, so the strings compare directly.
/// </summary>
public class Bc412SymbologyTests
{
    private const string Sample = "121113131113111212111411121111151111111214111113131113121211131311111";

    [Theory]
    [InlineData("BC412AB", Sample)]
    [InlineData("1234567", "121111121413121211111113131111141211111511111211141112121311121312111")]
    [InlineData("ZZZZZZZ", "121511111111141112151111111511111115111111151111111511111115111111111")]
    [InlineData("0123456789ABCDEFGH", "1211111115111312121111121411111313111114121111151111121114111212131112131211121411111311131113121211131311111411121114121111151111121111141211121312111312111")]
    public void MatchesReferenceRuns(string text, string reference)
        => Assert.Equal(reference, string.Concat(Encode(text).RunWidths));

    /// <summary>
    /// For BC412AB the symbol is B, the check character counted as 0, C, 4, 1, 2, A and B, whose values
    /// are 25, 0, 20, 11, 15, 17, 7 and 25. The odd positions add to 67, which leaves 32, the even
    /// positions add to 53, which leaves 18 and doubles to 36, and 68 leaves 33. Then 33 times 17 is 561,
    /// which leaves 1, and the character with value 1 is R. The printed line shows it after the first
    /// data character.
    /// </summary>
    [Fact]
    public void CalculatesTheCheckCharacterAfterTheFirstDataCharacter()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Bc412Symbology().Encode("BC412AB", options);
        Assert.Equal("BRC412AB", Assert.Single(symbol.Text).Text);
    }

    /// <summary>
    /// A supplied check character in the second position is validated against the data and carried
    /// once, so the symbol is the one the calculated check character produces.
    /// </summary>
    [Fact]
    public void ValidatesASuppliedCheckCharacter()
    {
        LinearBarcodeSymbol validated = (LinearBarcodeSymbol)new Bc412Symbology(true).Encode("BRC412AB", new BarcodeOptions());

        Assert.Equal(Sample, string.Concat(validated.RunWidths));
        Assert.Throws<ArgumentException>(() => new Bc412Symbology(true).Encode("BAC412AB", new BarcodeOptions()));
    }

    /// <summary>
    /// The start character is three modules, every character is twelve, and the stop character is
    /// three, so a symbol of N data characters and its check character spans 12(N + 1) + 6 modules
    /// before its quiet zones.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("BC412AB")]
    [InlineData("0123456789ABCDEFGH")]
    public void SpansTwelveModulesPerCharacter(string text)
    {
        int widthInModules = 0;
        foreach (int run in Encode(text).RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal((12 * (text.Length + 1)) + 6, widthInModules);
    }

    [Theory]
    [InlineData("")]
    [InlineData("BC412A")]
    [InlineData("0123456789ABCDEFGHI")]
    [InlineData("BC412AO")]
    [InlineData("bc412ab")]
    [InlineData("BC412A ")]
    [InlineData("ＢＣ412AB")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new Bc412Symbology().Encode(text, new BarcodeOptions());
}
